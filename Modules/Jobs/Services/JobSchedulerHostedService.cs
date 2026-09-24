using RM_CMS.Modules.Jobs.Data;
using RM_CMS.Modules.Jobs.Domain;

namespace RM_CMS.Modules.Jobs.Services
{
    /// <summary>
    /// Runs the scheduled sweeps.
    /// </summary>
    /// <remarks>
    /// JobsController's comment used to read "There is no in-process scheduler: an
    /// external cron calls these endpoints." No cron was ever set up, so nothing ever
    /// ran on its own and every sweep waited for somebody to press "Run now".
    ///
    /// The objection that kept this out was real and is answered in the database
    /// rather than here: <see cref="IJobScheduleRepository.ClaimAsync"/> is a
    /// compare-and-swap on <c>next_due_at</c>, so however many instances tick at the
    /// same moment, exactly one wins the row and the rest run nothing. The manual
    /// triggers stay exactly as they were.
    ///
    /// MISSED RUNS ARE CAUGHT UP ONCE, NOT REPLAYED. A due time that passed while the
    /// application was down leaves <c>next_due_at</c> in the past, so the job runs at
    /// the next tick after startup. It runs ONCE regardless of how long the outage
    /// was, because claiming moves the row to the next occurrence after NOW rather
    /// than stepping it forward one interval at a time. A week of downtime must not
    /// produce seven sweeps and seven rounds of Telegram messages.
    /// </remarks>
    public sealed class JobSchedulerHostedService : BackgroundService
    {
        /// <summary>
        /// How often due work is looked for. The schedule screen only offers HH:mm, so
        /// checking more often than once a minute could not find anything new.
        /// </summary>
        private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);

        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<JobSchedulerHostedService> _logger;
        private readonly TimeProvider _clock;
        private readonly bool _enabled;

        public JobSchedulerHostedService(
            IServiceScopeFactory scopes,
            ILogger<JobSchedulerHostedService> logger,
            TimeProvider clock,
            IConfiguration configuration)
        {
            _scopes = scopes;
            _logger = logger;
            _clock = clock;

            // Off by default on a developer's machine. These sweeps send Telegram
            // messages to real people, and a local run pointed at a copy of production
            // data would message the congregation from somebody's laptop.
            _enabled = configuration.GetValue("Jobs:SchedulerEnabled", true);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_enabled)
            {
                _logger.LogInformation(
                    "Job scheduler is disabled (Jobs:SchedulerEnabled=false). " +
                    "Jobs can still be run by hand from the settings screen.");

                return;
            }

            await InitialiseAsync(stoppingToken);

            using var timer = new PeriodicTimer(TickInterval);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (!await timer.WaitForNextTickAsync(stoppingToken)) break;

                    await TickAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;   // shutting down
                }
                catch (Exception ex)
                {
                    // Swallowed on purpose. An exception out of ExecuteAsync ends the
                    // hosted service silently, and a scheduler that stops without
                    // saying so is worse than one that fails a single tick.
                    _logger.LogError(ex, "The job scheduler tick failed. Continuing.");
                }
            }
        }

        /// <summary>
        /// Gives every known job a row, and every enabled job a next occurrence.
        /// </summary>
        /// <remarks>
        /// The migration seeds <c>next_due_at</c> as NULL on purpose, so this is what
        /// first sets it — which means a deployment does NOT fire every enabled job
        /// the moment it starts. A null cache resolves to the next occurrence AFTER
        /// now, never to now.
        /// </remarks>
        private async Task InitialiseAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var scope = _scopes.CreateScope();

                var schedules = scope.ServiceProvider.GetRequiredService<IJobScheduleRepository>();

                foreach (var name in JobNames.All)
                    await schedules.EnsureRowAsync(name);

                var now = _clock.GetUtcNow().UtcDateTime;

                foreach (var schedule in await schedules.ListAsync())
                {
                    if (cancellationToken.IsCancellationRequested) return;

                    // A disabled job's cache is cleared so it can never be claimed, and
                    // an enabled one with a due time still ahead of it is left exactly
                    // as it is — rewriting that would silently move a run an
                    // administrator is waiting for.
                    if (!schedule.IsEnabled)
                    {
                        if (schedule.NextDueAt is not null) await schedules.SetNextDueAsync(schedule.Id, null);
                        continue;
                    }

                    if (schedule.NextDueAt is not null) continue;

                    var next = NextRunCalculator.NextOccurrence(schedule, now);

                    await schedules.SetNextDueAsync(schedule.Id, next);

                    if (next is null)
                    {
                        _logger.LogWarning(
                            "Job {JobName} is enabled but its schedule cannot produce a next run. " +
                            "Check its cadence, day and time zone on the schedule screen.",
                            schedule.JobName);
                    }
                    else
                    {
                        _logger.LogInformation(
                            "Job {JobName} is scheduled. Next run {NextRunUtc:u}.", schedule.JobName, next);
                    }
                }
            }
            catch (Exception ex)
            {
                // Not fatal. The tick loop re-reads everything anyway, so a database
                // that is not up yet costs one minute rather than the scheduler.
                _logger.LogError(ex, "Could not initialise the job schedule. The scheduler will retry on its next tick.");
            }
        }

        private async Task TickAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopes.CreateScope();

            var schedules = scope.ServiceProvider.GetRequiredService<IJobScheduleRepository>();
            var jobs = scope.ServiceProvider.GetRequiredService<IJobService>();

            var now = _clock.GetUtcNow().UtcDateTime;

            var due = await schedules.ListDueAsync(now);

            foreach (var schedule in due)
            {
                if (cancellationToken.IsCancellationRequested) return;
                if (schedule.NextDueAt is null) continue;

                // From NOW, not from the missed due time. This is what turns an outage
                // into one catch-up run instead of one run per interval missed.
                var next = NextRunCalculator.NextOccurrence(schedule, now);

                if (next is null)
                {
                    _logger.LogWarning(
                        "Job {JobName} is due but its schedule cannot produce a following run. " +
                        "Switching it off so it does not run every tick.",
                        schedule.JobName);

                    await schedules.SetNextDueAsync(schedule.Id, null);
                    continue;
                }

                if (!await schedules.ClaimAsync(schedule.Id, schedule.NextDueAt.Value, next.Value, now))
                {
                    // Another instance won it. Expected, not an error.
                    _logger.LogDebug("Job {JobName} was claimed elsewhere.", schedule.JobName);
                    continue;
                }

                await RunClaimedAsync(schedules, jobs, schedule, next.Value);
            }
        }

        private async Task RunClaimedAsync(
            IJobScheduleRepository schedules, IJobService jobs, JobSchedule schedule, DateTime next)
        {
            _logger.LogInformation("Running scheduled job {JobName}.", schedule.JobName);

            try
            {
                var response = await RunAsync(jobs, schedule.JobName);
                var report = response.Data;

                // The row is already claimed, so a failure here only decides what the
                // schedule screen shows. The run itself is recorded in job_run by the
                // job, exactly as a manual run is.
                await schedules.CompleteAsync(schedule.Id, report?.Status ?? JobStatus.Failed);

                if (report is null)
                {
                    _logger.LogWarning(
                        "Scheduled job {JobName} returned no report: {Message}",
                        schedule.JobName, response.Message);
                }
                else
                {
                    _logger.LogInformation(
                        "Scheduled job {JobName} finished {Status}: {Processed} processed, {Failed} failed. Next run {NextRunUtc:u}.",
                        schedule.JobName, report.Status, report.Processed, report.Failed, next);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scheduled job {JobName} threw.", schedule.JobName);

                await schedules.CompleteAsync(schedule.Id, JobStatus.Failed);
            }
        }

        /// <summary>
        /// Maps a job name to its method.
        /// </summary>
        /// <remarks>
        /// A switch rather than reflection, so a name that exists in
        /// <see cref="JobNames"/> with no method here is a compile-time-visible gap
        /// and a logged warning at runtime — not a job that silently never runs.
        ///
        /// The limit is left at the service default: a scheduled sweep should take the
        /// whole queue it is given, and capping it here would quietly leave work for a
        /// week without anybody being told.
        /// </remarks>
        private async Task<Utilities.ApiResponse<JobReport>> RunAsync(IJobService jobs, string jobName) =>
            jobName switch
            {
                JobNames.AssignUnassigned => await jobs.AssignUnassignedAsync(0),
                JobNames.AdvanceNurture => await jobs.AdvanceNurtureAsync(0),
                JobNames.MarkOverdue => await jobs.MarkOverdueAsync(0),
                JobNames.ChaseEscalations => await jobs.ChaseEscalationsAsync(0),
                JobNames.HuddleReminder => await jobs.HuddleReminderAsync(0),
                JobNames.SendNotifications => await jobs.SendNotificationsAsync(0),
                _ => new Utilities.ApiResponse<JobReport>(
                        Utilities.ResponseType.Warning,
                        $"'{jobName}' has a schedule but no runner.", null!)
            };
    }
}
