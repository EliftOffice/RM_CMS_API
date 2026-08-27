using Dapper;
using RM_CMS.Data;
using RM_CMS.Modules.Jobs.Domain;

namespace RM_CMS.Modules.Jobs.Data
{
    /// <summary>
    /// Data access for <c>job_run</c> — the audit trail of scheduled work.
    ///
    /// This table is append-then-close: a row is written when a job starts and updated
    /// once when it ends. A run that never gets its <c>finished_at</c> is a crash, and
    /// that is deliberately visible rather than tidied away.
    /// </summary>
    public interface IJobRunRepository
    {
        Task<long> StartAsync(string jobName, long? triggeredBy, DateTime nowUtc);

        Task FinishAsync(long id, string status, int itemsProcessed, int itemsFailed,
                         string? detail, DateTime nowUtc);

        Task<IReadOnlyList<JobRun>> GetRecentAsync(string? jobName, int limit);

        /// <summary>
        /// True when a run of this job started recently and never finished. Used to
        /// avoid two overlapping sweeps of the same queue when a scheduler fires
        /// again before the previous run returned.
        /// </summary>
        Task<bool> IsRunningAsync(string jobName, DateTime startedAfter);
    }

    public sealed class JobRunRepository : IJobRunRepository
    {
        private readonly IDbConnectionFactory _dbFactory;

        public JobRunRepository(IDbConnectionFactory dbFactory) => _dbFactory = dbFactory;

        private const string SelectJobRun = @"
            SELECT
                id              AS Id,
                job_name        AS JobName,
                started_at      AS StartedAt,
                finished_at     AS FinishedAt,
                status          AS Status,
                items_processed AS ItemsProcessed,
                items_failed    AS ItemsFailed,
                detail          AS Detail,
                triggered_by    AS TriggeredBy
            FROM job_run";

        public async Task<long> StartAsync(string jobName, long? triggeredBy, DateTime nowUtc)
        {
            const string sql = @"
                INSERT INTO job_run (job_name, started_at, status, triggered_by)
                VALUES (@JobName, @NowUtc, 'RUNNING', @TriggeredBy);

                SELECT LAST_INSERT_ID();";

            using var connection = _dbFactory.GetConnection();

            return await connection.ExecuteScalarAsync<long>(sql, new
            {
                JobName = jobName,
                NowUtc = nowUtc,
                TriggeredBy = triggeredBy
            });
        }

        public async Task FinishAsync(long id, string status, int itemsProcessed, int itemsFailed,
                                      string? detail, DateTime nowUtc)
        {
            const string sql = @"
                UPDATE job_run
                SET finished_at     = @NowUtc,
                    status          = @Status,
                    items_processed = @ItemsProcessed,
                    items_failed    = @ItemsFailed,
                    detail          = @Detail
                WHERE id = @Id;";

            using var connection = _dbFactory.GetConnection();

            await connection.ExecuteAsync(sql, new
            {
                Id = id,
                Status = status,
                ItemsProcessed = itemsProcessed,
                ItemsFailed = itemsFailed,
                Detail = detail,
                NowUtc = nowUtc
            });
        }

        public async Task<IReadOnlyList<JobRun>> GetRecentAsync(string? jobName, int limit)
        {
            var sql = SelectJobRun + @"
            WHERE (@JobName IS NULL OR job_name = @JobName)
            ORDER BY started_at DESC
            LIMIT @Limit;";

            using var connection = _dbFactory.GetConnection();

            return (await connection.QueryAsync<JobRun>(sql, new { JobName = jobName, Limit = limit })).ToList();
        }

        public async Task<bool> IsRunningAsync(string jobName, DateTime startedAfter)
        {
            const string sql = @"
                SELECT COUNT(1)
                FROM job_run
                WHERE job_name = @JobName
                  AND status = 'RUNNING'
                  AND started_at >= @StartedAfter;";

            using var connection = _dbFactory.GetConnection();

            return await connection.ExecuteScalarAsync<int>(sql, new
            {
                JobName = jobName,
                StartedAfter = startedAfter
            }) > 0;
        }
    }
}
