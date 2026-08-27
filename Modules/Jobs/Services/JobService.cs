using RM_CMS.Modules.Care.Data;
using RM_CMS.Modules.Care.Domain;
using RM_CMS.Modules.Identity.Data;
using RM_CMS.Modules.Identity.Domain;
using RM_CMS.Modules.Identity.Services;
using RM_CMS.Modules.Jobs.Data;
using RM_CMS.Modules.Jobs.Domain;
using RM_CMS.Modules.Notifications.Domain;
using RM_CMS.Modules.Notifications.Services;
using RM_CMS.Modules.Volunteers.Data;
using RM_CMS.Utilities;

namespace RM_CMS.Modules.Jobs.Services
{
    /// <summary>
    /// The scheduled sweeps that keep cases moving when nobody is looking at them.
    ///
    /// Every job follows the same shape: take a bounded batch, process each item
    /// independently, count what worked, and record the run in <c>job_run</c>. One bad
    /// row must never abandon the rest of the queue — a single case that cannot be
    /// assigned is not a reason to leave fifty others unassigned — so per-item failures
    /// are caught and counted rather than thrown.
    ///
    /// Jobs are idempotent by construction: each one selects only rows that still need
    /// the work, so running twice does nothing the second time.
    /// </summary>
    public interface IJobService
    {
        /// <summary>Assigns open cases with no volunteer to the least-loaded eligible one.</summary>
        Task<ApiResponse<JobReport>> AssignUnassignedAsync(int limit);

        /// <summary>Creates the next nurture contact for cases whose step has fallen due.</summary>
        Task<ApiResponse<JobReport>> AdvanceNurtureAsync(int limit);

        /// <summary>Marks pending contacts that are past their date as missed.</summary>
        Task<ApiResponse<JobReport>> MarkOverdueAsync(int limit);

        /// <summary>
        /// Chases escalations nobody has acknowledged: the team lead first, then the
        /// pastor once the threshold passes.
        /// </summary>
        Task<ApiResponse<JobReport>> ChaseEscalationsAsync(int limit);

        /// <summary>
        /// Reminds each team lead and their volunteers that the weekly huddle is today.
        /// A no-op on every other day.
        /// </summary>
        Task<ApiResponse<JobReport>> HuddleReminderAsync(int limit);

        /// <summary>
        /// Sends the queued alerts. Until this ran, every job above it recorded that
        /// somebody had been told without anybody being told.
        /// </summary>
        Task<ApiResponse<JobReport>> SendNotificationsAsync(int limit);

        Task<ApiResponse<IReadOnlyList<JobReport>>> RunAllAsync(int limit);

        Task<ApiResponse<IReadOnlyList<JobRun>>> GetHistoryAsync(string? jobName, int limit);
    }

    public sealed class JobService : IJobService
    {
        private const int DefaultBatchLimit = 200;
        private const int MaxBatchLimit = 1000;

        private readonly ICareCaseRepository _cases;
        private readonly ICareInteractionRepository _interactions;
        private readonly IEscalationRepository _escalations;
        private readonly ICareLookupRepository _lookups;
        private readonly IVolunteerRepository _volunteers;
        private readonly IJobRunRepository _runs;
        private readonly INotificationQueue _notifications;
        private readonly INotificationSender _sender;
        private readonly RM_CMS.Modules.Huddle.Data.IHuddleRepository _huddle;
        private readonly RM_CMS.Modules.Settings.Data.ISettingRepository _settings;
        private readonly ICurrentIdentity _current;
        private readonly IUserAccountRepository _accounts;
        private readonly TimeProvider _clock;
        private readonly ILogger<JobService> _logger;

        public JobService(
            ICareCaseRepository cases,
            ICareInteractionRepository interactions,
            IEscalationRepository escalations,
            ICareLookupRepository lookups,
            IVolunteerRepository volunteers,
            IJobRunRepository runs,
            INotificationQueue notifications,
            INotificationSender sender,
            RM_CMS.Modules.Huddle.Data.IHuddleRepository huddle,
            RM_CMS.Modules.Settings.Data.ISettingRepository settings,
            ICurrentIdentity current,
            IUserAccountRepository accounts,
            TimeProvider clock,
            ILogger<JobService> logger)
        {
            _cases = cases;
            _interactions = interactions;
            _escalations = escalations;
            _lookups = lookups;
            _volunteers = volunteers;
            _runs = runs;
            _notifications = notifications;
            _sender = sender;
            _huddle = huddle;
            _settings = settings;
            _current = current;
            _accounts = accounts;
            _clock = clock;
            _logger = logger;
        }

        // ==================================================================
        // 1 · Assignment queue
        // ==================================================================
        public Task<ApiResponse<JobReport>> AssignUnassignedAsync(int limit) =>
            RunAsync(JobNames.AssignUnassigned, limit, async (report, batchSize, actingUserId, now) =>
            {
                var pending = await _cases.FindUnassignedAsync(null, batchSize);

                if (pending.Count == 0)
                {
                    report.AddNote("Nothing waiting for assignment.");
                    return;
                }

                // Campuses with no eligible volunteer are noted once, not once per
                // case — otherwise one understaffed campus fills the detail column
                // and hides everything else.
                var exhaustedCampuses = new HashSet<long>();

                foreach (var careCase in pending)
                {
                    if (exhaustedCampuses.Contains(careCase.CampusId))
                    {
                        report.RecordSkipped();
                        continue;
                    }

                    try
                    {
                        var eligible = await _volunteers.FindEligibleAsync(careCase.CampusId, false, 1);
                        var pick = eligible.FirstOrDefault();

                        if (pick is null)
                        {
                            exhaustedCampuses.Add(careCase.CampusId);
                            report.RecordSkipped();
                            report.AddNote($"No volunteer with spare capacity at campus {careCase.CampusId}.");
                            continue;
                        }

                        var assigned = await _cases.AssignAsync(
                            careCase.Id, careCase.RowVersion, pick.Id, pick.TeamId,
                            AssignmentReason.Auto, now, actingUserId);

                        if (assigned)
                        {
                            report.RecordProcessed();
                        }
                        else
                        {
                            // Zero affected rows is a concurrency conflict, never a
                            // no-op: somebody assigned this case by hand mid-sweep.
                            report.RecordSkipped();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to assign case {CaseId}", careCase.Id);
                        report.RecordFailed($"Case {careCase.PublicId}: {ex.Message}");
                    }
                }
            });

        // ==================================================================
        // 2 · Nurture progression
        // ==================================================================
        public Task<ApiResponse<JobReport>> AdvanceNurtureAsync(int limit) =>
            RunAsync(JobNames.AdvanceNurture, limit, async (report, batchSize, actingUserId, now) =>
            {
                var lookaheadDays = await _lookups.GetIntSettingAsync("nurture.lookahead_days", 1);
                var horizon = now.Date.AddDays(lookaheadDays);

                var due = await _cases.FindDueForNextStepAsync(horizon, batchSize);

                if (due.Count == 0)
                {
                    report.AddNote("No nurture steps are due.");
                    return;
                }

                foreach (var careCase in due)
                {
                    try
                    {
                        var plan = careCase.NurturePlanId.HasValue
                            ? await _lookups.GetPlanByIdAsync(careCase.NurturePlanId.Value)
                            : await _lookups.GetDefaultPlanAsync(careCase.CampusId);

                        if (plan is null || plan.MaxStepNumber == 0)
                        {
                            report.RecordSkipped();
                            report.AddNote($"Case {careCase.PublicId} has no nurture plan.");
                            continue;
                        }

                        // CurrentStepNumber counts COMPLETED steps, so the one to
                        // create is the next after it.
                        var stepNumber = careCase.CurrentStepNumber + 1;
                        var step = plan.StepNumber(stepNumber);

                        if (step is null)
                        {
                            // The plan is exhausted. This happens on a case that was
                            // paused by an escalation at its last step and resumed
                            // after it closed. Left alone it would stay due forever,
                            // so hand it to a team lead rather than re-sweeping it.
                            await SendToReviewAsync(careCase, now, actingUserId);
                            report.RecordProcessed();
                            report.AddNote($"Case {careCase.PublicId} finished its plan; sent to review.");
                            continue;
                        }

                        var volunteerId = await ResolveNurtureVolunteerAsync(careCase, plan, now, actingUserId);

                        if (volunteerId is null)
                        {
                            report.RecordSkipped();
                            report.AddNote($"Case {careCase.PublicId}: no volunteer available.");
                            continue;
                        }

                        await _interactions.CreateAsync(new CareInteraction
                        {
                            PublicId = Ulid.NewUlid(),
                            CareCaseId = careCase.Id,
                            VolunteerId = volunteerId,
                            Stage = InteractionStage.Nurture,
                            SequenceNumber = stepNumber,
                            MethodCode = step.MethodCode,
                            ScheduledOn = careCase.NextStepDueOn ?? now.Date,
                            Status = InteractionStatus.Pending
                        }, actingUserId);

                        report.RecordProcessed();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to advance case {CaseId}", careCase.Id);
                        report.RecordFailed($"Case {careCase.PublicId}: {ex.Message}");
                    }
                }
            });

        /// <summary>
        /// Who runs the next step. Mirrors the plan's assignment mode, and falls back
        /// to the least-loaded eligible volunteer when the case has nobody at all.
        /// </summary>
        private async Task<long?> ResolveNurtureVolunteerAsync(
            CareCase careCase, NurturePlan plan, DateTime now, long? actingUserId)
        {
            var current = careCase.AssignedVolunteerId;

            var keepCurrent = current is not null &&
                              plan.AssignmentMode == NurtureAssignmentMode.SameVolunteer;

            if (keepCurrent) return current;

            if (current is not null && plan.AssignmentMode == NurtureAssignmentMode.SameIfAvailable)
            {
                var held = await _volunteers.GetByIdAsync(current.Value);
                if (held is not null && held.IsAvailable && held.HasSpareCapacity) return current;
            }

            var pick = (await _volunteers.FindEligibleAsync(careCase.CampusId, false, 1)).FirstOrDefault();
            if (pick is null) return current;   // nobody better; keep whoever holds it

            if (pick.Id != current)
            {
                await _cases.AssignAsync(careCase.Id, careCase.RowVersion, pick.Id, pick.TeamId,
                    AssignmentReason.Capacity, now, actingUserId);
            }

            return pick.Id;
        }

        private async Task SendToReviewAsync(CareCase careCase, DateTime now, long? actingUserId)
        {
            careCase.Stage = CaseStage.Review;
            careCase.Status = CaseStatus.InProgress;
            careCase.NextStepDueOn = null;
            careCase.NextActionOn = null;
            careCase.AwaitingReviewSince = now;

            await _cases.UpdateStateAsync(careCase, actingUserId);
        }

        // ==================================================================
        // 3 · Overdue contacts
        // ==================================================================
        public Task<ApiResponse<JobReport>> MarkOverdueAsync(int limit) =>
            RunAsync(JobNames.MarkOverdue, limit, async (report, batchSize, actingUserId, now) =>
            {
                // The grace period keeps a contact "pending" for a day or two past its
                // date, because volunteers log contacts in the evening and a step
                // marked missed at one minute past midnight would be wrong far more
                // often than it was right.
                var graceDays = await _lookups.GetIntSettingAsync("assignment.retry_delay_days", 3);
                var cutoff = now.Date.AddDays(-graceDays);

                var overdue = await _interactions.FindOverdueAsync(cutoff, batchSize);

                if (overdue.Count == 0)
                {
                    report.AddNote("No overdue contacts.");
                    return;
                }

                foreach (var interaction in overdue)
                {
                    try
                    {
                        var marked = await _interactions.MarkMissedAsync(
                            interaction.Id, interaction.RowVersion, actingUserId);

                        if (marked)
                        {
                            report.RecordProcessed();
                        }
                        else
                        {
                            // Somebody logged the contact between the query and the
                            // update. That is the good outcome, not a failure.
                            report.RecordSkipped();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to mark interaction {Id} missed", interaction.Id);
                        report.RecordFailed($"Contact {interaction.PublicId}: {ex.Message}");
                    }
                }

                // NOTE: marking a contact missed deliberately does NOT advance the
                // case. The progression engine decides from a COMPLETED interaction
                // with an outcome, and a miss has no outcome to reason from. What a
                // repeated miss should mean is a workflow decision that belongs in
                // care_progression_rule, not hard-coded here.
                report.AddNote("Cases not advanced; a miss has no outcome to route on.");
            });

        // ==================================================================
        // 4 · Escalation chase-up ladder
        // ==================================================================
        public Task<ApiResponse<JobReport>> ChaseEscalationsAsync(int limit) =>
            RunAsync(JobNames.ChaseEscalations, limit, async (report, batchSize, _, now) =>
            {
                var ackTargetHours = await _lookups.GetIntSettingAsync("escalation.ack_target_hours", 4);
                var reminderEveryHours = await _lookups.GetIntSettingAsync("escalation.reminder_every_hours", 4);
                var pastorAlertHours = await _lookups.GetIntSettingAsync("escalation.pastor_alert_hours", 12);

                // Only escalations that have already missed their acknowledgement
                // target are candidates.
                var stale = await _escalations.FindUnacknowledgedAsync(
                    now.AddHours(-ackTargetHours), batchSize);

                if (stale.Count == 0)
                {
                    report.AddNote("No unacknowledged escalations.");
                    return;
                }

                foreach (var escalation in stale)
                {
                    try
                    {
                        // Respect the reminder interval: this job may run every few
                        // minutes, but a team lead should not be pinged every few
                        // minutes.
                        if (escalation.LastReminderAt is not null &&
                            escalation.LastReminderAt.Value.AddHours(reminderEveryHours) > now)
                        {
                            report.RecordSkipped();
                            continue;
                        }

                        var waitedHours = (now - escalation.RaisedAt).TotalHours;

                        // The ladder: the pastor joins in once the escalation has gone
                        // unacknowledged for long enough. An EMERGENCY skips the wait —
                        // the whole point of the tier is that it cannot sit.
                        var alertPastor =
                            escalation.PastorAlertedAt is null &&
                            (waitedHours >= pastorAlertHours ||
                             escalation.Tier == EscalationTier.Emergency);

                        var notified = await NotifyTeamLeadAsync(escalation);

                        if (alertPastor)
                        {
                            notified += await NotifyPastorsAsync(escalation);
                        }

                        await _escalations.RecordReminderAsync(escalation.Id, now, alertPastor);

                        if (notified == 0)
                        {
                            // The chase-up ran but reached nobody. That is the failure
                            // this whole job exists to prevent, so it is counted as a
                            // failure rather than quiet success.
                            report.RecordFailed(
                                $"Escalation {escalation.PublicId}: no reachable recipient.");
                        }
                        else
                        {
                            report.RecordProcessed();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to chase escalation {Id}", escalation.Id);
                        report.RecordFailed($"Escalation {escalation.PublicId}: {ex.Message}");
                    }
                }
            });

        private async Task<int> NotifyTeamLeadAsync(Escalation escalation)
        {
            var assignee = await _notifications.FindAssigneeAsync(escalation.AssignedToUserId);

            var recipients = assignee is not null
                ? new List<NotificationRecipient> { assignee }
                // Unassigned escalations are the worst case — nobody owns them — so
                // they go to every team lead at the campus rather than nowhere.
                : (await _notifications.FindByRoleAsync(RoleCodes.TeamLead, escalation.CampusId)).ToList();

            if (recipients.Count == 0) return 0;

            return await _notifications.QueueAsync(
                recipients,
                NotificationType.EscalationUnacknowledged,
                RelatedEntityType.Escalation,
                escalation.Id);
        }

        private async Task<int> NotifyPastorsAsync(Escalation escalation)
        {
            var pastors = await _notifications.FindByRoleAsync(RoleCodes.Pastor, escalation.CampusId);

            if (pastors.Count == 0) return 0;

            return await _notifications.QueueAsync(
                pastors,
                NotificationType.EscalationPastorAlert,
                RelatedEntityType.Escalation,
                escalation.Id);
        }

        // ==================================================================
        // 5 · Huddle reminder
        // ==================================================================
        public Task<ApiResponse<JobReport>> HuddleReminderAsync(int limit) =>
            RunAsync(JobNames.HuddleReminder, limit, async (report, _, _, now) =>
            {
                var huddleDay = await _settings.GetIntAsync("huddle.day_of_week", 6);

                // The app's convention is 1 = Monday ... 7 = Sunday, matching what the
                // church says aloud and what the MVP's system_config held. .NET's
                // DayOfWeek starts at 0 = Sunday.
                var todayIso = now.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)now.DayOfWeek;

                if (todayIso != huddleDay)
                {
                    report.AddNote($"Not the huddle day (today {todayIso}, huddle {huddleDay}).");
                    return;
                }

                // Guard against a scheduler that fires hourly: one reminder per team
                // per day, not one per sweep.
                var sinceMidnight = now.Date;

                var remindVolunteers = await _settings.GetBoolAsync("huddle.remind_volunteers", true);
                var teams = await _huddle.GetLedTeamsAsync();

                if (teams.Count == 0)
                {
                    report.AddNote("No active team has a lead.");
                    return;
                }

                foreach (var team in teams)
                {
                    try
                    {
                        if (await _notifications.AlreadyQueuedSinceAsync(
                                NotificationType.HuddleReminder, RelatedEntityType.Team,
                                team.TeamId, sinceMidnight))
                        {
                            report.RecordSkipped();
                            continue;
                        }

                        var recipients = new List<NotificationRecipient>();

                        var lead = await _notifications.FindAssigneeAsync(team.LeadUserId);
                        if (lead is not null) recipients.Add(lead);

                        if (remindVolunteers)
                        {
                            // The volunteers are the point: a huddle the lead remembers
                            // and nobody attends is not a huddle.
                            var members = await _huddle.GetTeamMembersAsync(new[] { team.TeamId });

                            foreach (var member in members)
                            {
                                var recipient = await _notifications.FindAssigneeAsync(member.UserAccountId);
                                if (recipient is not null) recipients.Add(recipient);
                            }
                        }

                        if (recipients.Count == 0)
                        {
                            report.RecordSkipped();
                            report.AddNote($"{team.TeamName}: nobody to remind.");
                            continue;
                        }

                        var reachable = await _notifications.QueueAsync(
                            recipients, NotificationType.HuddleReminder,
                            RelatedEntityType.Team, team.TeamId);

                        if (reachable == 0)
                        {
                            report.RecordFailed($"{team.TeamName}: no reachable recipient.");
                        }
                        else
                        {
                            report.RecordProcessed();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to remind team {TeamId}", team.TeamId);
                        report.RecordFailed($"{team.TeamName}: {ex.Message}");
                    }
                }
            });

        // ==================================================================
        // 6 · Notification sender
        // ==================================================================
        public Task<ApiResponse<JobReport>> SendNotificationsAsync(int limit) =>
            RunAsync(JobNames.SendNotifications, limit, async (report, batchSize, _, _) =>
            {
                var result = await _sender.DrainAsync(batchSize);

                if (result.Halted is not null)
                {
                    // The queue was left untouched, so this is a failure of the job
                    // rather than of any item — nothing was even attempted.
                    report.RecordFailed(result.Halted);
                    return;
                }

                for (var i = 0; i < result.Sent; i++) report.RecordProcessed();
                for (var i = 0; i < result.Skipped; i++) report.RecordSkipped();

                foreach (var note in result.Notes) report.AddNote(note);

                // A failed send is counted as a failed item, which puts the run at
                // PARTIAL or FAILED. An alert nobody received is exactly the thing
                // this job exists to make visible, so it must not read as success.
                // The sender has already noted why each one failed.
                for (var i = 0; i < result.Failed; i++) report.RecordFailed();
            });

        // ==================================================================
        // Run all
        // ==================================================================
        public async Task<ApiResponse<IReadOnlyList<JobReport>>> RunAllAsync(int limit)
        {
            var reports = new List<JobReport>();

            // Order matters. Assign first so a newly assigned case can have its first
            // contact created; advance nurture next; mark misses after that so a step
            // created moments ago is not immediately swept; chase escalations after
            // those, because the earlier jobs can raise new ones; and send last of
            // all, so alerts queued by this very sweep go out in it rather than
            // waiting for the next one.
            foreach (var run in new Func<int, Task<ApiResponse<JobReport>>>[]
                     {
                         AssignUnassignedAsync,
                         AdvanceNurtureAsync,
                         MarkOverdueAsync,
                         ChaseEscalationsAsync,
                         HuddleReminderAsync,
                         SendNotificationsAsync
                     })
            {
                var result = await run(limit);
                if (result.Data is not null) reports.Add(result.Data);
            }

            var failed = reports.Count(r => r.Status == JobStatus.Failed);

            return failed == 0
                ? Ok<IReadOnlyList<JobReport>>(reports, "All jobs ran.")
                : Ok<IReadOnlyList<JobReport>>(reports, $"{failed} job(s) failed. See the reports.");
        }

        public async Task<ApiResponse<IReadOnlyList<JobRun>>> GetHistoryAsync(string? jobName, int limit)
        {
            if (jobName is not null && !JobNames.IsKnown(jobName))
                return Warn<IReadOnlyList<JobRun>>("Unknown job name.");

            var history = await _runs.GetRecentAsync(jobName, Math.Clamp(limit, 1, 200));

            return Ok<IReadOnlyList<JobRun>>(history, $"{history.Count} run(s).");
        }

        // ==================================================================
        // Shared run wrapper — audit, bounding, and failure containment
        // ==================================================================
        private async Task<ApiResponse<JobReport>> RunAsync(
            string jobName, int limit,
            Func<JobReport, int, long?, DateTime, Task> body)
        {
            var batchSize = limit <= 0 ? DefaultBatchLimit : Math.Min(limit, MaxBatchLimit);
            var now = _clock.GetUtcNow().UtcDateTime;

            var actingUserId = await ResolveActingUserIdAsync();

            // A machine caller (X-Service-Key) has no identity, so overlapping runs
            // are guarded by the ledger rather than by a lock. One hour is well past
            // any healthy run of a bounded batch.
            if (await _runs.IsRunningAsync(jobName, now.AddHours(-1)))
            {
                _logger.LogWarning("{JobName} is already running; this trigger was ignored.", jobName);

                return Warn<JobReport>($"{jobName} is already running.");
            }

            var runId = await _runs.StartAsync(jobName, actingUserId, now);
            var report = new JobReport(jobName);

            try
            {
                await body(report, batchSize, actingUserId, now);
            }
            catch (Exception ex)
            {
                // Only a failure of the job itself reaches here; per-item failures are
                // caught inside each body and counted.
                _logger.LogError(ex, "{JobName} failed", jobName);

                report.RecordFailed(ex.Message);

                await _runs.FinishAsync(runId, JobStatus.Failed, report.Processed, report.Failed,
                    report.Detail, _clock.GetUtcNow().UtcDateTime);

                return Fail<JobReport>($"{jobName} failed.");
            }

            await _runs.FinishAsync(runId, report.Status, report.Processed, report.Failed,
                report.Detail, _clock.GetUtcNow().UtcDateTime);

            _logger.LogInformation(
                "{JobName} finished: {Status}, {Processed} processed, {Skipped} skipped, {Failed} failed.",
                jobName, report.Status, report.Processed, report.Skipped, report.Failed);

            return Ok(report,
                $"{report.Processed} processed, {report.Skipped} skipped, {report.Failed} failed.");
        }

        private static ApiResponse<T> Ok<T>(T data, string message) => new(ResponseType.Success, message, data);
        private static ApiResponse<T> Warn<T>(string message) => new(ResponseType.Warning, message, default!);
        private static ApiResponse<T> Fail<T>(string message) => new(ResponseType.Error, message, default!);

        /// <summary>
        /// The account that triggered this run, for the audit columns. Null when the
        /// scheduler called with a service key — a machine has no user identity, and
        /// inventing one would put a misleading name on every audited row.
        /// </summary>
        private async Task<long?> ResolveActingUserIdAsync()
        {
            var accountPublicId = _current.AccountId;

            if (string.IsNullOrWhiteSpace(accountPublicId)) return null;

            var account = await _accounts.GetByPublicIdAsync(accountPublicId);
            return account?.Id;
        }
    }
}
