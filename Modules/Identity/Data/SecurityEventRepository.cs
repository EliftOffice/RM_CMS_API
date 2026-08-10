using Dapper;
using RM_CMS.Data;
using RM_CMS.Modules.Identity.Domain;

namespace RM_CMS.Modules.Identity.Data
{
    /// <summary>
    /// Append-only security audit trail.
    ///
    /// <c>security_event</c> deliberately has no foreign key on user_account_id:
    /// audit rows must survive deletion of the account they describe, which is
    /// exactly when they matter most.
    /// </summary>
    public interface ISecurityEventRepository
    {
        Task WriteAsync(SecurityEventEntry entry);

        /// <summary>
        /// Failed attempts for a username since a point in time. Backs per-account
        /// backoff independently of the per-IP rate limiter, so an attacker rotating
        /// source addresses still trips the account lockout.
        /// </summary>
        Task<int> CountRecentFailuresAsync(string usernameAttempted, DateTime sinceUtc);
    }

    public sealed class SecurityEventRepository : ISecurityEventRepository
    {
        private readonly IDbConnectionFactory _dbFactory;
        private readonly ILogger<SecurityEventRepository> _logger;

        public SecurityEventRepository(IDbConnectionFactory dbFactory, ILogger<SecurityEventRepository> logger)
        {
            _dbFactory = dbFactory;
            _logger = logger;
        }

        public async Task WriteAsync(SecurityEventEntry entry)
        {
            const string sql = @"
                INSERT INTO security_event
                    (user_account_id, username_attempted, event_type, succeeded, detail,
                     ip_address, user_agent, correlation_id)
                VALUES
                    (@UserAccountId, @UsernameAttempted, @EventType, @Succeeded, @Detail,
                     INET6_ATON(@IpAddress), @UserAgent, @CorrelationId);";

            try
            {
                using var connection = _dbFactory.GetConnection();
                await connection.ExecuteAsync(sql, new
                {
                    entry.UserAccountId,
                    UsernameAttempted = Truncate(entry.UsernameAttempted, 100),
                    entry.EventType,
                    entry.Succeeded,
                    Detail = Truncate(entry.Detail, 500),
                    entry.IpAddress,
                    UserAgent = Truncate(entry.UserAgent, 300),
                    CorrelationId = Truncate(entry.CorrelationId, 36)
                });
            }
            catch (Exception ex)
            {
                // Auditing must never break the request it is auditing — but failing to
                // record the audit trail is itself security-relevant, so it is logged loudly.
                _logger.LogError(ex,
                    "Failed to write security event {EventType} for account {AccountId}",
                    entry.EventType, entry.UserAccountId?.ToString() ?? "(unresolved)");
            }
        }

        public async Task<int> CountRecentFailuresAsync(string usernameAttempted, DateTime sinceUtc)
        {
            const string sql = @"
                SELECT COUNT(1)
                FROM security_event
                WHERE username_attempted = @Username
                  AND event_type = @EventType
                  AND occurred_at >= @SinceUtc;";

            try
            {
                using var connection = _dbFactory.GetConnection();
                return await connection.ExecuteScalarAsync<int>(sql, new
                {
                    Username = usernameAttempted,
                    EventType = SecurityEventTypes.LoginFailed,
                    SinceUtc = sinceUtc
                });
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
