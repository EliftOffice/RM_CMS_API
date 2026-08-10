using Dapper;
using RM_CMS.Data;
using RM_CMS.Modules.Identity.Domain;

namespace RM_CMS.Modules.Identity.Data
{
    /// <summary>
    /// Data access for <c>refresh_token</c>.
    ///
    /// Only SHA-256 hashes are stored. Rotation is enforced by revoking the
    /// presented token in the same transaction that issues its replacement, which
    /// is what makes a replayed token detectable.
    ///
    /// IP addresses go in as VARBINARY(16) via INET6_ATON and come back out as text
    /// via INET6_NTOA — 16 bytes instead of 45, and correct for IPv6.
    /// </summary>
    public interface IRefreshTokenRepository
    {
        Task<long> InsertAsync(RefreshTokenRecord token);
        Task<RefreshTokenRecord?> GetByHashAsync(string tokenHash);

        /// <summary>
        /// Atomically revokes the presented token and inserts its replacement.
        /// Returns false when the token was already revoked by a concurrent request —
        /// the caller must treat that as a reuse attempt.
        /// </summary>
        Task<bool> RotateAsync(long presentedTokenId, RefreshTokenRecord replacement);

        Task<int> RevokeFamilyAsync(string familyId, string reason, DateTime nowUtc);
        Task<int> RevokeAllForAccountAsync(long accountId, string reason, DateTime nowUtc);

        Task<IReadOnlyList<ActiveSession>> GetActiveSessionsAsync(long accountId, DateTime nowUtc);
        Task<int> CountActiveFamiliesAsync(long accountId, DateTime nowUtc);
        Task RevokeOldestFamiliesAsync(long accountId, int keepCount, string reason, DateTime nowUtc);

        Task<int> DeleteExpiredAsync(DateTime cutoffUtc);
    }

    public sealed class RefreshTokenRepository : IRefreshTokenRepository
    {
        private readonly IDbConnectionFactory _dbFactory;

        public RefreshTokenRepository(IDbConnectionFactory dbFactory) => _dbFactory = dbFactory;

        private const string SelectToken = @"
            SELECT  id                     AS Id,
                    public_id              AS PublicId,
                    user_account_id        AS UserAccountId,
                    family_id              AS FamilyId,
                    token_hash             AS TokenHash,
                    parent_id              AS ParentId,
                    replaced_by_id         AS ReplacedById,
                    issued_at              AS IssuedAt,
                    expires_at             AS ExpiresAt,
                    revoked_at             AS RevokedAt,
                    revoked_reason         AS RevokedReason,
                    device_label           AS DeviceLabel,
                    user_agent_hash        AS UserAgentHash,
                    INET6_NTOA(issued_ip)  AS IssuedIp
            FROM refresh_token";

        private const string InsertToken = @"
            INSERT INTO refresh_token
                (public_id, user_account_id, family_id, token_hash, parent_id,
                 issued_at, expires_at, device_label, user_agent_hash, issued_ip)
            VALUES
                (@PublicId, @UserAccountId, @FamilyId, @TokenHash, @ParentId,
                 @IssuedAt, @ExpiresAt, @DeviceLabel, @UserAgentHash, INET6_ATON(@IssuedIp));
            SELECT LAST_INSERT_ID();";

        public async Task<long> InsertAsync(RefreshTokenRecord token)
        {
            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteScalarAsync<long>(InsertToken, token);
        }

        public async Task<RefreshTokenRecord?> GetByHashAsync(string tokenHash)
        {
            const string sql = SelectToken + @" WHERE token_hash = @TokenHash LIMIT 1;";

            using var connection = _dbFactory.GetConnection();
            return await connection.QueryFirstOrDefaultAsync<RefreshTokenRecord>(sql, new { TokenHash = tokenHash });
        }

        public async Task<bool> RotateAsync(long presentedTokenId, RefreshTokenRecord replacement)
        {
            // The UPDATE is guarded on `revoked_at IS NULL`, so two concurrent
            // refreshes with the same token race on this row: exactly one rotates,
            // and the loser gets 0 rows and is reported to the caller as reuse.
            const string revokePresented = @"
                UPDATE refresh_token
                SET revoked_at     = @NowUtc,
                    revoked_reason = @Reason,
                    replaced_by_id = NULL
                WHERE id = @PresentedId
                  AND revoked_at IS NULL;";

            const string linkReplacement = @"
                UPDATE refresh_token SET replaced_by_id = @ReplacementId WHERE id = @PresentedId;";

            using var connection = _dbFactory.GetConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                var revoked = await connection.ExecuteAsync(revokePresented, new
                {
                    PresentedId = presentedTokenId,
                    Reason = RevocationReasons.Rotated,
                    NowUtc = replacement.IssuedAt
                }, transaction);

                if (revoked != 1)
                {
                    transaction.Rollback();
                    return false;
                }

                var newId = await connection.ExecuteScalarAsync<long>(InsertToken, replacement, transaction);

                await connection.ExecuteAsync(linkReplacement,
                    new { PresentedId = presentedTokenId, ReplacementId = newId }, transaction);

                transaction.Commit();
                replacement.Id = newId;
                return true;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public async Task<int> RevokeFamilyAsync(string familyId, string reason, DateTime nowUtc)
        {
            const string sql = @"
                UPDATE refresh_token
                SET revoked_at = @NowUtc, revoked_reason = @Reason
                WHERE family_id = @FamilyId AND revoked_at IS NULL;";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteAsync(sql, new { FamilyId = familyId, Reason = reason, NowUtc = nowUtc });
        }

        public async Task<int> RevokeAllForAccountAsync(long accountId, string reason, DateTime nowUtc)
        {
            const string sql = @"
                UPDATE refresh_token
                SET revoked_at = @NowUtc, revoked_reason = @Reason
                WHERE user_account_id = @AccountId AND revoked_at IS NULL;";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteAsync(sql, new { AccountId = accountId, Reason = reason, NowUtc = nowUtc });
        }

        public async Task<IReadOnlyList<ActiveSession>> GetActiveSessionsAsync(long accountId, DateTime nowUtc)
        {
            // One row per family = one device, described by its newest live token.
            const string sql = @"
                SELECT  t.family_id             AS FamilyId,
                        t.device_label          AS DeviceLabel,
                        INET6_NTOA(t.issued_ip) AS IssuedIp,
                        t.issued_at             AS IssuedAt,
                        t.expires_at            AS ExpiresAt
                FROM refresh_token t
                JOIN (
                    SELECT family_id, MAX(issued_at) AS newest
                    FROM refresh_token
                    WHERE user_account_id = @AccountId
                      AND revoked_at IS NULL
                      AND expires_at > @NowUtc
                    GROUP BY family_id
                ) latest ON latest.family_id = t.family_id AND latest.newest = t.issued_at
                WHERE t.user_account_id = @AccountId
                  AND t.revoked_at IS NULL
                ORDER BY t.issued_at DESC;";

            using var connection = _dbFactory.GetConnection();
            return (await connection.QueryAsync<ActiveSession>(sql, new { AccountId = accountId, NowUtc = nowUtc })).ToList();
        }

        public async Task<int> CountActiveFamiliesAsync(long accountId, DateTime nowUtc)
        {
            const string sql = @"
                SELECT COUNT(DISTINCT family_id)
                FROM refresh_token
                WHERE user_account_id = @AccountId
                  AND revoked_at IS NULL
                  AND expires_at > @NowUtc;";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteScalarAsync<int>(sql, new { AccountId = accountId, NowUtc = nowUtc });
        }

        public async Task RevokeOldestFamiliesAsync(long accountId, int keepCount, string reason, DateTime nowUtc)
        {
            const string sql = @"
                UPDATE refresh_token
                SET revoked_at = @NowUtc, revoked_reason = @Reason
                WHERE user_account_id = @AccountId
                  AND revoked_at IS NULL
                  AND family_id IN (
                      SELECT family_id FROM (
                          SELECT family_id
                          FROM refresh_token
                          WHERE user_account_id = @AccountId
                            AND revoked_at IS NULL
                            AND expires_at > @NowUtc
                          GROUP BY family_id
                          ORDER BY MAX(issued_at) DESC
                          LIMIT 18446744073709551615 OFFSET @KeepCount
                      ) AS stale
                  );";

            using var connection = _dbFactory.GetConnection();
            await connection.ExecuteAsync(sql, new
            {
                AccountId = accountId,
                KeepCount = keepCount,
                Reason = reason,
                NowUtc = nowUtc
            });
        }

        public async Task<int> DeleteExpiredAsync(DateTime cutoffUtc)
        {
            const string sql = @"DELETE FROM refresh_token WHERE expires_at < @CutoffUtc;";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteAsync(sql, new { CutoffUtc = cutoffUtc });
        }
    }
}
