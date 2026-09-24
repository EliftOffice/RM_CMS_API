using Dapper;
using RM_CMS.Data;
using RM_CMS.Modules.Jobs.Domain;

namespace RM_CMS.Modules.Jobs.Data
{
    /// <summary>
    /// Data access for <c>job_schedule</c>.
    /// </summary>
    /// <remarks>
    /// <see cref="ClaimAsync"/> is the important one and the reason this module can
    /// have an in-process scheduler at all. JobsController's own comment gives the
    /// objection: "a second application instance does not silently double every
    /// job". A compare-and-swap on <c>next_due_at</c> answers it — every instance
    /// reads the same due row, all of them try to move it forward, and the database
    /// lets exactly one succeed. The loser is told no and runs nothing.
    ///
    /// The same shape as login_challenge's ConsumeAsync, for the same reason: the
    /// winner has to be decided in the database, not in application code that could
    /// interleave between the read and the write.
    /// </remarks>
    public interface IJobScheduleRepository
    {
        Task<IReadOnlyList<JobSchedule>> ListAsync();

        Task<JobSchedule?> GetByNameAsync(string jobName);

        /// <summary>Enabled schedules whose next occurrence has arrived.</summary>
        Task<IReadOnlyList<JobSchedule>> ListDueAsync(DateTime nowUtc);

        /// <summary>
        /// Moves a due row forward, but only if it is still sitting on the occurrence
        /// the caller read. Returns false when another instance got there first, in
        /// which case this one must not run the job.
        /// </summary>
        Task<bool> ClaimAsync(long id, DateTime expectedDueAt, DateTime nextDueAt, DateTime nowUtc);

        /// <summary>Records how the claimed run turned out.</summary>
        Task CompleteAsync(long id, string status);

        /// <summary>
        /// Writes an administrator's edit, guarding on <c>row_version</c>. Returns
        /// false on a mismatch: two administrators editing the schedule must not
        /// silently overwrite each other.
        /// </summary>
        Task<bool> UpdateAsync(JobSchedule schedule, long? actingUserId);

        /// <summary>
        /// Rewrites <c>next_due_at</c> without touching the rule or the row version.
        /// Used on startup to re-derive the cache, which is not an edit and must not
        /// make somebody else's open schedule screen look stale.
        /// </summary>
        Task SetNextDueAsync(long id, DateTime? nextDueAt);

        /// <summary>
        /// Adds any job the application knows about that has no row yet, so a job
        /// added in code appears on the schedule screen without a migration.
        /// </summary>
        Task<int> EnsureRowAsync(string jobName);
    }

    public sealed class JobScheduleRepository : IJobScheduleRepository
    {
        private readonly IDbConnectionFactory _dbFactory;

        public JobScheduleRepository(IDbConnectionFactory dbFactory) => _dbFactory = dbFactory;

        private const string SelectSchedule = @"
            SELECT
                id          AS Id,
                job_name    AS JobName,
                is_enabled  AS IsEnabled,
                cadence     AS Cadence,
                day_of_week AS DayOfWeek,
                time_of_day AS TimeOfDay,
                interval_minutes AS IntervalMinutes,
                timezone    AS Timezone,
                next_due_at AS NextDueAt,
                last_run_at AS LastRunAt,
                last_status AS LastStatus,
                row_version AS RowVersion
            FROM job_schedule";

        public async Task<IReadOnlyList<JobSchedule>> ListAsync()
        {
            const string sql = SelectSchedule + " ORDER BY id;";

            using var connection = _dbFactory.GetConnection();

            return (await connection.QueryAsync<JobSchedule>(sql)).ToList();
        }

        public async Task<JobSchedule?> GetByNameAsync(string jobName)
        {
            const string sql = SelectSchedule + " WHERE job_name = @JobName LIMIT 1;";

            using var connection = _dbFactory.GetConnection();

            return await connection.QueryFirstOrDefaultAsync<JobSchedule>(sql, new { JobName = jobName });
        }

        public async Task<IReadOnlyList<JobSchedule>> ListDueAsync(DateTime nowUtc)
        {
            const string sql = SelectSchedule + @"
                WHERE is_enabled = 1
                  AND next_due_at IS NOT NULL
                  AND next_due_at <= @NowUtc
                ORDER BY next_due_at;";

            using var connection = _dbFactory.GetConnection();

            return (await connection.QueryAsync<JobSchedule>(sql, new { NowUtc = nowUtc })).ToList();
        }

        public async Task<bool> ClaimAsync(long id, DateTime expectedDueAt, DateTime nextDueAt, DateTime nowUtc)
        {
            // next_due_at in the WHERE is the whole mechanism: it is both the thing
            // being changed and the proof that nobody changed it first. is_enabled is
            // re-checked here rather than trusted from the read, because an
            // administrator may have switched the job off in the seconds between.
            const string sql = @"
                UPDATE job_schedule
                SET next_due_at = @NextDueAt,
                    last_run_at = @NowUtc,
                    last_status = 'RUNNING'
                WHERE id = @Id
                  AND is_enabled = 1
                  AND next_due_at = @ExpectedDueAt;";

            using var connection = _dbFactory.GetConnection();

            return await connection.ExecuteAsync(sql, new
            {
                Id = id,
                ExpectedDueAt = expectedDueAt,
                NextDueAt = nextDueAt,
                NowUtc = nowUtc
            }) == 1;
        }

        public async Task CompleteAsync(long id, string status)
        {
            const string sql = @"
                UPDATE job_schedule
                SET last_status = @Status
                WHERE id = @Id;";

            using var connection = _dbFactory.GetConnection();

            await connection.ExecuteAsync(sql, new { Id = id, Status = status });
        }

        public async Task<bool> UpdateAsync(JobSchedule schedule, long? actingUserId)
        {
            const string sql = @"
                UPDATE job_schedule
                SET is_enabled  = @IsEnabled,
                    cadence     = @Cadence,
                    day_of_week = @DayOfWeek,
                    time_of_day = @TimeOfDay,
                    interval_minutes = @IntervalMinutes,
                    timezone    = @Timezone,
                    next_due_at = @NextDueAt,
                    updated_by  = @ActingUserId,
                    row_version = row_version + 1
                WHERE id = @Id
                  AND row_version = @RowVersion;";

            using var connection = _dbFactory.GetConnection();

            return await connection.ExecuteAsync(sql, new
            {
                schedule.Id,
                schedule.IsEnabled,
                schedule.Cadence,
                schedule.DayOfWeek,
                schedule.TimeOfDay,
                schedule.IntervalMinutes,
                schedule.Timezone,
                schedule.NextDueAt,
                schedule.RowVersion,
                ActingUserId = actingUserId
            }) == 1;
        }

        public async Task SetNextDueAsync(long id, DateTime? nextDueAt)
        {
            const string sql = @"
                UPDATE job_schedule
                SET next_due_at = @NextDueAt
                WHERE id = @Id;";

            using var connection = _dbFactory.GetConnection();

            await connection.ExecuteAsync(sql, new { Id = id, NextDueAt = nextDueAt });
        }

        public async Task<int> EnsureRowAsync(string jobName)
        {
            // Disabled, daily, and with no next occurrence. A job that appears in code
            // must never start running on its own the moment it is deployed — it shows
            // up on the schedule screen switched off, waiting to be given a time.
            const string sql = @"
                INSERT IGNORE INTO job_schedule
                    (job_name, is_enabled, cadence, day_of_week, time_of_day, timezone)
                VALUES
                    (@JobName, 0, 'DAILY', NULL, '06:00:00', @Timezone);";

            using var connection = _dbFactory.GetConnection();

            return await connection.ExecuteAsync(sql, new
            {
                JobName = jobName,
                Timezone = JobCadence.DefaultTimezone
            });
        }
    }
}
