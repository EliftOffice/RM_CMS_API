using Dapper;
using RM_CMS.Data;
using RM_CMS.Modules.Identity.Domain;

namespace RM_CMS.Modules.Identity.Data
{
    /// <summary>
    /// Data access for <c>login_challenge</c>.
    /// </summary>
    /// <remarks>
    /// Every state change here is a CONDITIONAL update — the WHERE names the status
    /// the row must currently be in, and the caller checks the affected count. That is
    /// what makes a challenge good for exactly one session: two browsers polling at
    /// once, or a button tapped twice, both resolve to a single winner in the database
    /// rather than in application code that could interleave.
    /// </remarks>
    public interface ILoginChallengeRepository
    {
        Task<long> CreateAsync(LoginChallenge challenge);

        Task<LoginChallenge?> GetByPublicIdAsync(string publicId);

        /// <summary>The one the Telegram button names. Looked up by hash, never by token.</summary>
        Task<LoginChallenge?> GetByTokenHashAsync(string tokenHash);

        /// <summary>
        /// PENDING to APPROVED, only while it is still in date. Returns false when
        /// somebody already approved it, it was declined, or it has run out of time.
        /// </summary>
        Task<bool> ApproveAsync(long id, DateTime nowUtc);

        /// <summary>PENDING to DECLINED — "this was not me".</summary>
        Task<bool> DeclineAsync(long id, DateTime nowUtc);

        /// <summary>
        /// APPROVED to CONSUMED. The single most important statement in this file:
        /// whoever wins this update gets the session, and there is exactly one winner.
        /// </summary>
        Task<bool> ConsumeAsync(long id, DateTime nowUtc);

        Task<bool> RecordSendAsync(long id);

        /// <summary>
        /// Replaces the token a challenge will accept.
        /// </summary>
        /// <remarks>
        /// Only reached when the instance sending the prompt is not the one that
        /// created the challenge, which behind more than one instance is routine. The
        /// old token was never sent to anybody, so replacing it invalidates nothing a
        /// person is holding.
        /// </remarks>
        Task<bool> RehashAsync(long id, string newTokenHash, DateTime nowUtc);

        /// <summary>
        /// Retires any challenge still open for this account. Called before a new one
        /// is created, so a person who starts signing in twice cannot leave an older
        /// prompt live on their phone.
        /// </summary>
        Task<int> ExpireOutstandingForAccountAsync(long userAccountId, DateTime nowUtc);

        /// <summary>
        /// Closes rows whose time ran out. Housekeeping — nothing depends on it, since
        /// every read checks the expiry itself, but a table of PENDING rows that will
        /// never be anything else misrepresents what is actually outstanding.
        /// </summary>
        Task<int> ExpireStaleAsync(DateTime nowUtc);
    }

    public sealed class LoginChallengeRepository : ILoginChallengeRepository
    {
        private readonly IDbConnectionFactory _dbFactory;

        public LoginChallengeRepository(IDbConnectionFactory dbFactory) => _dbFactory = dbFactory;

        private const string SelectChallenge = @"
            SELECT
                id              AS Id,
                public_id       AS PublicId,
                user_account_id AS UserAccountId,
                token_hash      AS TokenHash,
                chat_id         AS ChatId,
                status          AS Status,
                send_count      AS SendCount,
                request_ip      AS RequestIp,
                user_agent      AS UserAgent,
                created_at      AS CreatedAt,
                expires_at      AS ExpiresAt,
                approved_at     AS ApprovedAt,
                consumed_at     AS ConsumedAt
            FROM login_challenge";

        public async Task<long> CreateAsync(LoginChallenge challenge)
        {
            // created_at is passed in, NOT left to the column default. CURRENT_TIMESTAMP
            // stamps the MySQL server's LOCAL time, and every instant this application
            // writes is UTC — on a server running in IST that puts created_at five and a
            // half hours ahead of expires_at, and ck_login_challenge_window rejects the
            // row. The same trap is documented on church_event.published_at.
            const string sql = @"
                INSERT INTO login_challenge
                    (public_id, user_account_id, token_hash, chat_id, status,
                     request_ip, user_agent, created_at, expires_at)
                VALUES
                    (@PublicId, @UserAccountId, @TokenHash, @ChatId, @Status,
                     @RequestIp, @UserAgent, @CreatedAt, @ExpiresAt);
                SELECT LAST_INSERT_ID();";

            using var connection = _dbFactory.GetConnection();

            return await connection.ExecuteScalarAsync<long>(sql, new
            {
                challenge.PublicId,
                challenge.UserAccountId,
                challenge.TokenHash,
                challenge.ChatId,
                challenge.Status,
                challenge.RequestIp,
                challenge.UserAgent,
                challenge.CreatedAt,
                challenge.ExpiresAt
            });
        }

        public async Task<LoginChallenge?> GetByPublicIdAsync(string publicId)
        {
            const string sql = SelectChallenge + " WHERE public_id = @PublicId LIMIT 1;";

            using var connection = _dbFactory.GetConnection();

            return await connection.QueryFirstOrDefaultAsync<LoginChallenge>(sql, new { PublicId = publicId });
        }

        public async Task<LoginChallenge?> GetByTokenHashAsync(string tokenHash)
        {
            const string sql = SelectChallenge + " WHERE token_hash = @TokenHash LIMIT 1;";

            using var connection = _dbFactory.GetConnection();

            return await connection.QueryFirstOrDefaultAsync<LoginChallenge>(sql, new { TokenHash = tokenHash });
        }

        public async Task<bool> ApproveAsync(long id, DateTime nowUtc)
        {
            // The expiry is re-checked here rather than trusted from the read. Between
            // the webhook reading the row and updating it, the deadline may have passed.
            const string sql = @"
                UPDATE login_challenge
                SET status = 'APPROVED', approved_at = @NowUtc
                WHERE id = @Id AND status = 'PENDING' AND expires_at > @NowUtc;";

            using var connection = _dbFactory.GetConnection();

            return await connection.ExecuteAsync(sql, new { Id = id, NowUtc = nowUtc }) == 1;
        }

        public async Task<bool> DeclineAsync(long id, DateTime nowUtc)
        {
            // Allowed after expiry on purpose. Somebody tapping "this was not me" ten
            // minutes late is still telling us something worth recording.
            const string sql = @"
                UPDATE login_challenge
                SET status = 'DECLINED'
                WHERE id = @Id AND status IN ('PENDING','APPROVED');";

            using var connection = _dbFactory.GetConnection();

            return await connection.ExecuteAsync(sql, new { Id = id }) == 1;
        }

        public async Task<bool> ConsumeAsync(long id, DateTime nowUtc)
        {
            const string sql = @"
                UPDATE login_challenge
                SET status = 'CONSUMED', consumed_at = @NowUtc
                WHERE id = @Id AND status = 'APPROVED' AND expires_at > @NowUtc;";

            using var connection = _dbFactory.GetConnection();

            return await connection.ExecuteAsync(sql, new { Id = id, NowUtc = nowUtc }) == 1;
        }

        public async Task<bool> RecordSendAsync(long id)
        {
            const string sql = @"
                UPDATE login_challenge
                SET send_count = send_count + 1
                WHERE id = @Id AND status = 'PENDING';";

            using var connection = _dbFactory.GetConnection();

            return await connection.ExecuteAsync(sql, new { Id = id }) == 1;
        }

        public async Task<bool> RehashAsync(long id, string newTokenHash, DateTime nowUtc)
        {
            const string sql = @"
                UPDATE login_challenge
                SET token_hash = @TokenHash
                WHERE id = @Id AND status = 'PENDING' AND expires_at > @NowUtc;";

            using var connection = _dbFactory.GetConnection();

            return await connection.ExecuteAsync(
                sql, new { Id = id, TokenHash = newTokenHash, NowUtc = nowUtc }) == 1;
        }

        public async Task<int> ExpireOutstandingForAccountAsync(long userAccountId, DateTime nowUtc)
        {
            const string sql = @"
                UPDATE login_challenge
                SET status = 'EXPIRED'
                WHERE user_account_id = @UserAccountId AND status IN ('PENDING','APPROVED');";

            using var connection = _dbFactory.GetConnection();

            return await connection.ExecuteAsync(sql, new { UserAccountId = userAccountId });
        }

        public async Task<int> ExpireStaleAsync(DateTime nowUtc)
        {
            const string sql = @"
                UPDATE login_challenge
                SET status = 'EXPIRED'
                WHERE status IN ('PENDING','APPROVED') AND expires_at <= @NowUtc;";

            using var connection = _dbFactory.GetConnection();

            return await connection.ExecuteAsync(sql, new { NowUtc = nowUtc });
        }
    }
}
