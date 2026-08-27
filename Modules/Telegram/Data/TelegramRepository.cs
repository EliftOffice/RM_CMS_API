using System.Data;
using Dapper;
using RM_CMS.Data;
using RM_CMS.Modules.Telegram.Domain;

namespace RM_CMS.Modules.Telegram.Data
{
    /// <summary>
    /// Data access for Telegram linking: one-time tokens, and the TELEGRAM row in
    /// <c>person_contact</c>.
    ///
    /// There is no Telegram column on <c>volunteer</c>. A Telegram account is a way of
    /// reaching a PERSON, so it lives with their other contacts — which is what makes
    /// it work for pastors and team leads, not only volunteers.
    /// </summary>
    public interface ITelegramRepository
    {
        Task<long> CreateTokenAsync(long personId, string tokenHash, DateTime issuedAt, DateTime expiresAt, long? issuedBy);

        /// <summary>
        /// Consumes a token: returns the person it names and marks it used, in one
        /// statement so the same token cannot be redeemed twice concurrently.
        /// Null when the token is unknown, expired or already spent.
        /// </summary>
        Task<long?> RedeemTokenAsync(string tokenHash, DateTime nowUtc);

        /// <summary>Retires any live tokens for a person before issuing a fresh one.</summary>
        Task<int> ExpireOutstandingTokensAsync(long personId, DateTime nowUtc);

        /// <summary>The person currently holding this Telegram chat id, if any.</summary>
        Task<long?> FindPersonByChatIdAsync(string chatId);

        /// <summary>The active TELEGRAM contact for a person, if they have one.</summary>
        Task<TelegramContactRow?> GetContactAsync(long personId);

        /// <summary>
        /// Writes the link. Reactivates a previously disconnected row rather than
        /// inserting a second one, because <c>ux_person_contact_value</c> is unique on
        /// (person, type, value) and a reconnect is the same contact returning.
        /// </summary>
        Task LinkAsync(long personId, string chatId, string? username, DateTime nowUtc, long? actingUserId);

        /// <summary>
        /// Soft disconnect: sets <c>opted_out_at</c> and clears verification, keeping
        /// the row so history and any past delivery records still make sense.
        /// </summary>
        Task<bool> DisconnectAsync(long personId, DateTime nowUtc, long? actingUserId);

        /// <summary>How many people currently have an active Telegram link.</summary>
        Task<int> CountLinkedAsync();

        Task<long?> ResolvePersonIdAsync(string personPublicId);
        Task<string?> GetPersonPublicIdAsync(long personId);
    }

    public sealed class TelegramContactRow
    {
        public long Id { get; set; }
        public string Value { get; set; } = string.Empty;
        public string? Username { get; set; }
        public bool IsVerified { get; set; }
        public DateTime? VerifiedAt { get; set; }
        public DateTime? OptedOutAt { get; set; }
    }

    public sealed class TelegramRepository : ITelegramRepository
    {
        private readonly IDbConnectionFactory _dbFactory;

        public TelegramRepository(IDbConnectionFactory dbFactory) => _dbFactory = dbFactory;

        public async Task<long> CreateTokenAsync(
            long personId, string tokenHash, DateTime issuedAt, DateTime expiresAt, long? issuedBy)
        {
            // issued_at is supplied explicitly rather than left to its
            // DEFAULT CURRENT_TIMESTAMP(3). The database server runs in local time, so
            // the default would stamp IST while expires_at arrives in UTC — putting
            // expiry 5.5 hours BEFORE issue and tripping ck_telegram_link_token_expiry.
            // Every instant in this application comes from TimeProvider in UTC.
            const string sql = @"
                INSERT INTO telegram_link_token (person_id, token_hash, issued_at, expires_at, issued_by)
                VALUES (@PersonId, @TokenHash, @IssuedAt, @ExpiresAt, @IssuedBy);

                SELECT LAST_INSERT_ID();";

            using var connection = _dbFactory.GetConnection();

            return await connection.ExecuteScalarAsync<long>(sql, new
            {
                PersonId = personId,
                TokenHash = tokenHash,
                IssuedAt = issuedAt,
                ExpiresAt = expiresAt,
                IssuedBy = issuedBy
            });
        }

        public async Task<long?> RedeemTokenAsync(string tokenHash, DateTime nowUtc)
        {
            using var connection = _dbFactory.GetConnection();
            connection.Open();

            using var transaction = connection.BeginTransaction();

            try
            {
                // Claim the token first. The UPDATE only matches an unused, unexpired
                // row, so two webhook deliveries racing on the same token produce one
                // winner and one no-op rather than two links.
                const string claimSql = @"
                    UPDATE telegram_link_token
                    SET used_at = @NowUtc
                    WHERE token_hash = @TokenHash
                      AND used_at IS NULL
                      AND expires_at > @NowUtc;";

                var claimed = await connection.ExecuteAsync(claimSql,
                    new { TokenHash = tokenHash, NowUtc = nowUtc }, transaction);

                if (claimed != 1)
                {
                    transaction.Rollback();
                    return null;
                }

                var personId = await connection.ExecuteScalarAsync<long?>(
                    "SELECT person_id FROM telegram_link_token WHERE token_hash = @TokenHash LIMIT 1;",
                    new { TokenHash = tokenHash }, transaction);

                transaction.Commit();
                return personId;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public async Task<int> ExpireOutstandingTokensAsync(long personId, DateTime nowUtc)
        {
            const string sql = @"
                UPDATE telegram_link_token
                SET expires_at = @NowUtc
                WHERE person_id = @PersonId AND used_at IS NULL AND expires_at > @NowUtc;";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteAsync(sql, new { PersonId = personId, NowUtc = nowUtc });
        }

        public async Task<long?> FindPersonByChatIdAsync(string chatId)
        {
            // Deliberately ignores opted_out_at: a disconnected row still holds the
            // chat id, and reassigning it to somebody else would be exactly the
            // cross-wiring this feature must never allow.
            const string sql = @"
                SELECT person_id
                FROM person_contact
                WHERE contact_type = 'TELEGRAM' AND normalized_value = @ChatId
                LIMIT 1;";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteScalarAsync<long?>(sql, new { ChatId = chatId });
        }

        public async Task<TelegramContactRow?> GetContactAsync(long personId)
        {
            const string sql = @"
                SELECT id AS Id, value AS Value, is_verified AS IsVerified,
                       verified_at AS VerifiedAt, opted_out_at AS OptedOutAt
                FROM person_contact
                WHERE person_id = @PersonId AND contact_type = 'TELEGRAM'
                ORDER BY opted_out_at IS NOT NULL, id
                LIMIT 1;";

            using var connection = _dbFactory.GetConnection();
            return await connection.QueryFirstOrDefaultAsync<TelegramContactRow>(sql, new { PersonId = personId });
        }

        public async Task LinkAsync(long personId, string chatId, string? username, DateTime nowUtc, long? actingUserId)
        {
            // The displayed value carries the @username when Telegram gave us one, so
            // a person can recognise their own link. The chat id stays in
            // normalized_value, which is what lookups and sending use.
            var display = string.IsNullOrWhiteSpace(username) ? chatId : "@" + username.TrimStart('@');

            const string sql = @"
                INSERT INTO person_contact
                    (person_id, contact_type, value, normalized_value,
                     is_primary, is_verified, verified_at, opted_out_at, created_by, updated_by)
                VALUES
                    (@PersonId, 'TELEGRAM', @Display, @ChatId,
                     0, 1, @NowUtc, NULL, @ActingUserId, @ActingUserId)
                ON DUPLICATE KEY UPDATE
                    value        = VALUES(value),
                    is_verified  = 1,
                    verified_at  = @NowUtc,
                    opted_out_at = NULL,
                    updated_by   = @ActingUserId;";

            using var connection = _dbFactory.GetConnection();

            await connection.ExecuteAsync(sql, new
            {
                PersonId = personId,
                Display = display,
                ChatId = chatId,
                NowUtc = nowUtc,
                ActingUserId = actingUserId
            });
        }

        public async Task<bool> DisconnectAsync(long personId, DateTime nowUtc, long? actingUserId)
        {
            const string sql = @"
                UPDATE person_contact
                SET opted_out_at = @NowUtc,
                    is_verified  = 0,
                    updated_by   = @ActingUserId
                WHERE person_id = @PersonId
                  AND contact_type = 'TELEGRAM'
                  AND opted_out_at IS NULL;";

            using var connection = _dbFactory.GetConnection();

            return await connection.ExecuteAsync(sql, new
            {
                PersonId = personId,
                NowUtc = nowUtc,
                ActingUserId = actingUserId
            }) > 0;
        }

        public async Task<int> CountLinkedAsync()
        {
            using var connection = _dbFactory.GetConnection();

            return await connection.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*) FROM person_contact
                  WHERE contact_type = 'TELEGRAM' AND is_verified = 1 AND opted_out_at IS NULL;");
        }

        public async Task<long?> ResolvePersonIdAsync(string personPublicId)
        {
            using var connection = _dbFactory.GetConnection();

            return await connection.ExecuteScalarAsync<long?>(
                "SELECT id FROM person WHERE public_id = @PublicId AND deleted_at IS NULL LIMIT 1;",
                new { PublicId = personPublicId });
        }

        public async Task<string?> GetPersonPublicIdAsync(long personId)
        {
            using var connection = _dbFactory.GetConnection();

            return await connection.ExecuteScalarAsync<string?>(
                "SELECT public_id FROM person WHERE id = @Id LIMIT 1;",
                new { Id = personId });
        }
    }
}
