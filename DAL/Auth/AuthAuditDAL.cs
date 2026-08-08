using Dapper;
using RM_CMS.Data;

namespace RM_CMS.DAL.Auth
{
    /// <summary>
    /// A single security-audit entry. Carries outcomes only — never a password,
    /// token, hash or secret.
    /// </summary>
    public sealed class AuthAuditEntry
    {
        public string? UserId { get; set; }
        public string? UsernameAttempted { get; set; }
        public string EventType { get; set; } = string.Empty;
        public bool Succeeded { get; set; }
        public string? Detail { get; set; }
        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }
        public string? CorrelationId { get; set; }
    }

    public interface IAuthAuditDAL
    {
        Task WriteAsync(AuthAuditEntry entry);

        /// <summary>
        /// Failed attempts for a username since <paramref name="sinceUtc"/>. Backs the
        /// per-account exponential backoff independently of the per-IP rate limiter.
        /// </summary>
        Task<int> CountRecentFailuresAsync(string usernameAttempted, DateTime sinceUtc);
    }

    public sealed class AuthAuditDAL : IAuthAuditDAL
    {
        private readonly IDbConnectionFactory _dbFactory;
        private readonly ILogger<AuthAuditDAL> _logger;

        public AuthAuditDAL(IDbConnectionFactory dbFactory, ILogger<AuthAuditDAL> logger)
        {
            _dbFactory = dbFactory;
            _logger = logger;
        }

        public async Task WriteAsync(AuthAuditEntry entry)
        {
            const string sql = @"
                INSERT INTO auth_login_audit
                    (user_id, username_attempted, event_type, succeeded, detail,
                     ip_address, user_agent, correlation_id)
                VALUES
                    (@UserId, @UsernameAttempted, @EventType, @Succeeded, @Detail,
                     @IpAddress, @UserAgent, @CorrelationId);";

            try
            {
                using var connection = _dbFactory.GetConnection();
                await connection.ExecuteAsync(sql, new
                {
                    entry.UserId,
                    entry.UsernameAttempted,
                    entry.EventType,
                    entry.Succeeded,
                    Detail = Truncate(entry.Detail, 300),
                    entry.IpAddress,
                    UserAgent = Truncate(entry.UserAgent, 300),
                    entry.CorrelationId
                });
            }
            catch (Exception ex)
            {
                // Auditing must never break the request it is auditing — but a failure to
                // write the audit trail is itself security-relevant, so it is logged loudly.
                _logger.LogError(ex,
                    "Failed to write security audit entry {EventType} for {UserId}",
                    entry.EventType, entry.UserId ?? "(unresolved)");
            }
        }

        public async Task<int> CountRecentFailuresAsync(string usernameAttempted, DateTime sinceUtc)
        {
            const string sql = @"
                SELECT COUNT(1)
                FROM auth_login_audit
                WHERE username_attempted = @UsernameAttempted
                  AND event_type = 'LoginFailed'
                  AND created_utc >= @SinceUtc;";

            try
            {
                using var connection = _dbFactory.GetConnection();
                return await connection.ExecuteScalarAsync<int>(
                    sql, new { UsernameAttempted = usernameAttempted, SinceUtc = sinceUtc });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to count recent login failures");
                return 0;
            }
        }

        private static string? Truncate(string? value, int max) =>
            string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max];
    }
}
