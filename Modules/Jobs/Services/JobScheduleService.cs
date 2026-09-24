using RM_CMS.Modules.Identity.Data;
using RM_CMS.Modules.Identity.Services;
using RM_CMS.Modules.Jobs.Data;
using RM_CMS.Modules.Jobs.Domain;
using RM_CMS.Utilities;

namespace RM_CMS.Modules.Jobs.Services
{
    /// <summary>
    /// Reading and editing the schedule. The running of it is
    /// <see cref="JobSchedulerHostedService"/>; this is only the administrator's side.
    /// </summary>
    public interface IJobScheduleService
    {
        Task<ApiResponse<IReadOnlyList<JobScheduleDto>>> ListAsync();

        Task<ApiResponse<JobScheduleDto>> UpdateAsync(string jobName, JobScheduleUpdateRequest request);
    }

    public sealed class JobScheduleService : IJobScheduleService
    {
        private readonly IJobScheduleRepository _schedules;
        private readonly IUserAccountRepository _accounts;
        private readonly ICurrentIdentity _current;
        private readonly TimeProvider _clock;

        public JobScheduleService(
            IJobScheduleRepository schedules,
            IUserAccountRepository accounts,
            ICurrentIdentity current,
            TimeProvider clock)
        {
            _schedules = schedules;
            _accounts = accounts;
            _current = current;
            _clock = clock;
        }

        public async Task<ApiResponse<IReadOnlyList<JobScheduleDto>>> ListAsync()
        {
            // A job added to JobNames but never migrated would otherwise be invisible
            // here — and invisible means unschedulable. Cheap: INSERT IGNORE against a
            // unique key, six rows, on a screen an administrator opens occasionally.
            foreach (var name in JobNames.All)
                await _schedules.EnsureRowAsync(name);

            var rows = await _schedules.ListAsync();

            var dtos = rows
                .Where(r => JobNames.IsKnown(r.JobName))
                .Select(ToDto)
                .ToList();

            return new ApiResponse<IReadOnlyList<JobScheduleDto>>(
                ResponseType.Success, "Job schedule", dtos);
        }

        public async Task<ApiResponse<JobScheduleDto>> UpdateAsync(
            string jobName, JobScheduleUpdateRequest request)
        {
            if (!JobNames.IsKnown(jobName))
                return Warn<JobScheduleDto>($"'{jobName}' is not a job this application runs.");

            var schedule = await _schedules.GetByNameAsync(jobName);

            if (schedule is null)
                return Warn<JobScheduleDto>("That job has no schedule row yet. Reload the screen and try again.");

            if (!JobCadence.IsKnown(request.Cadence))
                return Warn<JobScheduleDto>("Choose either a daily or a weekly schedule.");

            var weekly = string.Equals(request.Cadence, JobCadence.Weekly, StringComparison.Ordinal);

            if (weekly && request.DayOfWeek is not (>= 1 and <= 7))
                return Warn<JobScheduleDto>("A weekly schedule needs a day of the week.");

            if (!TryParseTimeOfDay(request.TimeOfDay, out var timeOfDay))
                return Warn<JobScheduleDto>("Enter the time as HH:mm, for example 06:00.");

            var timezone = string.IsNullOrWhiteSpace(request.Timezone)
                ? schedule.Timezone
                : request.Timezone.Trim();

            if (!NextRunCalculator.TryResolveZone(timezone, out _))
                return Warn<JobScheduleDto>($"'{timezone}' is not a time zone this server recognises.");

            schedule.IsEnabled = request.IsEnabled;
            schedule.Cadence = request.Cadence;

            // Cleared rather than kept, so a job switched from weekly to daily cannot
            // leave a stale day behind that the check constraint would reject.
            schedule.DayOfWeek = weekly ? request.DayOfWeek : null;

            schedule.TimeOfDay = timeOfDay;
            schedule.Timezone = timezone;
            schedule.RowVersion = request.RowVersion;

            var now = _clock.GetUtcNow().UtcDateTime;

            // Recomputed here rather than left to the scheduler's next tick. An
            // administrator who changes the time expects the screen to show the new
            // next run immediately, and a stale cache would also run the job at the
            // OLD time once more before correcting itself.
            schedule.NextDueAt = NextRunCalculator.NextOccurrence(schedule, now);

            var saved = await _schedules.UpdateAsync(schedule, await ResolveActingUserIdAsync());

            if (!saved)
            {
                return Warn<JobScheduleDto>("Somebody else changed this schedule while you were editing it. " +
                            "Reload the screen to see their version.");
            }

            var fresh = await _schedules.GetByNameAsync(jobName);

            return new ApiResponse<JobScheduleDto>(
                ResponseType.Success,
                request.IsEnabled ? "Schedule saved." : "Schedule saved. This job is switched off.",
                ToDto(fresh ?? schedule));
        }

        /// <summary>
        /// Accepts HH:mm, and HH:mm:ss for a caller that sends what the API returned.
        /// Seconds are discarded: the scheduler ticks once a minute, so keeping them
        /// would promise a precision that does not exist.
        /// </summary>
        private static bool TryParseTimeOfDay(string? value, out TimeSpan timeOfDay)
        {
            timeOfDay = default;

            if (string.IsNullOrWhiteSpace(value)) return false;

            if (!TimeSpan.TryParse(value.Trim(), out var parsed)) return false;

            if (parsed < TimeSpan.Zero || parsed >= TimeSpan.FromDays(1)) return false;

            timeOfDay = new TimeSpan(parsed.Hours, parsed.Minutes, 0);
            return true;
        }

        private static JobScheduleDto ToDto(JobSchedule row)
        {
            var entry = JobCatalog.For(row.JobName);

            return new JobScheduleDto
            {
                JobName = row.JobName,
                Title = entry.Title,
                Description = entry.Description,
                QueuesNotifications = entry.QueuesNotifications,
                IsEnabled = row.IsEnabled,
                Cadence = row.Cadence,
                DayOfWeek = row.DayOfWeek,
                TimeOfDay = $"{row.TimeOfDay.Hours:D2}:{row.TimeOfDay.Minutes:D2}",
                Timezone = row.Timezone,
                NextDueAt = row.NextDueAt,
                LastRunAt = row.LastRunAt,
                LastStatus = row.LastStatus,
                RowVersion = row.RowVersion
            };
        }

        private async Task<long?> ResolveActingUserIdAsync()
        {
            var publicId = _current.AccountId;

            if (string.IsNullOrWhiteSpace(publicId)) return null;

            return (await _accounts.GetByPublicIdAsync(publicId))?.Id;
        }

        private static ApiResponse<T> Warn<T>(string message) => new(ResponseType.Warning, message, default!);
    }
}
