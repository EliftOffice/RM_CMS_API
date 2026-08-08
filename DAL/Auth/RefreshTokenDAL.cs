using Dapper;
using RM_CMS.Data;
using RM_CMS.Data.DTO.Auth;
using RM_CMS.Data.Models.Auth;

namespace RM_CMS.DAL.Auth
{
    /// <summary>
    /// Data access for <c>auth_refresh_tokens</c>.
    ///
    /// Only SHA-256 hashes are stored. A stolen database dump therefore yields no usable
    /// refresh tokens. Rotation is enforced by marking the presented token revoked in the
    /// same transaction that issues its replacement, which is what makes reuse detectable.
    /// </summary>
    public interface IRefreshTokenDAL
    {
        Task InsertAsync(RefreshTokenRecord token);
        Task<RefreshTokenRecord?> GetByHashAsync(string tokenHash);

        /// <summary>
        /// Atomically revokes the presented token and inserts its replacement.
        /// Returns false if the token was already revoked by a concurrent request —
        /// the caller must then treat it as a reuse attempt.
        /// </summary>
        Task<bool> RotateAsync(string presentedTokenId, RefreshTokenRecord replacement);

        /// <summary>Revokes every non-revoked token in a family. Used on reuse detection and logout.</summary>
        Task<int> RevokeFamilyAsync(string familyId, string reason, DateTime nowUtc);

        /// <summary>Revokes every non-revoked token for a user. Used on logout-all, password change, disable.</summary>
        Task<int> RevokeAllForUserAsync(string userId, string reason, DateTime nowUtc);

        Task<IReadOnlyList<ActiveSessionDto>> GetActiveSessionsAsync(string userId, DateTime nowUtc);
        Task<int> CountActiveFamiliesAsync(string userId, DateTime nowUtc);

        /// <summary>Revokes the least recently created active family — enforces the device cap.</summary>
        Task RevokeOldestFamiliesAsync(string userId, int keepCount, string reason, DateTime nowUtc);

        Task<int> DeleteExpiredAsync(DateTime cutoffUtc);
    }

    public sealed class RefreshTokenDAL : IRefreshTokenDAL
    {
        private readonly IDbConnectionFactory _dbFactory;

        private const string TokenColumns = @"
            token_id, user_id, family_id, token_hash, parent_token_id, replaced_by_token_id,
            expires_utc, created_utc, revoked_utc, revoked_reason,
            device_label, user_agent_hash, created_ip";

        public RefreshTokenDAL(IDbConnectionFactory dbFactory) => _dbFactory = dbFactory;

        public async Task InsertAsync(RefreshTokenRecord token)
        {
            const string sql = @"
                INSERT INTO auth_refresh_tokens
                    (token_id, user_id, family_id, token_hash, parent_token_id,
                     expires_utc, created_utc, device_label, user_agent_hash, created_ip)
                VALUES
                    (@TokenId, @UserId, @FamilyId, @TokenHash, @ParentTokenId,
                     @ExpiresUtc, @CreatedUtc, @DeviceLabel, @UserAgentHash, @CreatedIp);";

            using var connection = _dbFactory.GetConnection();
            await connection.ExecuteAsync(sql, token);
        }

        public async Task<RefreshTokenRecord?> GetByHashAsync(string tokenHash)
        {
            const string sql = $@"
                SELECT {TokenColumns}
                FROM auth_refresh_tokens
                WHERE token_hash = @TokenHash
                LIMIT 1;";

            using var connection = _dbFactory.GetConnection();
            return await connection.QueryFirstOrDefaultAsync<RefreshTokenRecord>(sql, new { TokenHash = tokenHash });
        }

        public async Task<bool> RotateAsync(string presentedTokenId, RefreshTokenRecord replacement)
        {
            // The UPDATE is guarded on `revoked_utc IS NULL`. Two concurrent refreshes with
            // the same token therefore race on this row: exactly one wins and rotates, the
            // loser gets 0 rows and is reported to the BLL as a reuse attempt.
            const string revokePresented = @"
                UPDATE auth_refresh_tokens
                SET revoked_utc          = @NowUtc,
                    revoked_reason       = @Reason,
                    replaced_by_token_id = @ReplacementId
                WHERE token_id = @PresentedTokenId
                  AND revoked_utc IS NULL;";

            const string insertReplacement = @"
                INSERT INTO auth_refresh_tokens
                    (token_id, user_id, family_id, token_hash, parent_token_id,
                     expires_utc, created_utc, device_label, user_agent_hash, created_ip)
                VALUES
                    (@TokenId, @UserId, @FamilyId, @TokenHash, @ParentTokenId,
                     @ExpiresUtc, @CreatedUtc, @DeviceLabel, @UserAgentHash, @CreatedIp);";

            using var connection = _dbFactory.GetConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                var revoked = await connection.ExecuteAsync(revokePresented, new
                {
                    PresentedTokenId = presentedTokenId,
                    ReplacementId = replacement.TokenId,
                    Reason = RevocationReasons.Rotated,
                    NowUtc = replacement.CreatedUtc
                }, transaction);

                if (revoked != 1)
                {
                    transaction.Rollback();
                    return false;
                }

                await connection.ExecuteAsync(insertReplacement, replacement, transaction);

                transaction.Commit();
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
                UPDATE auth_refresh_tokens
                SET revoked_utc    = @NowUtc,
                    revoked_reason = @Reason
                WHERE family_id = @FamilyId
                  AND revoked_utc IS NULL;";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteAsync(sql, new { FamilyId = familyId, Reason = reason, NowUtc = nowUtc });
        }

        public async Task<int> RevokeAllForUserAsync(string userId, string reason, DateTime nowUtc)
        {
            const string sql = @"
                UPDATE auth_refresh_tokens
                SET revoked_utc    = @NowUtc,
                    revoked_reason = @Reason
                WHERE user_id = @UserId
                  AND revoked_utc IS NULL;";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteAsync(sql, new { UserId = userId, Reason = reason, NowUtc = nowUtc });
        }

        public async Task<IReadOnlyList<ActiveSessionDto>> GetActiveSessionsAsync(string userId, DateTime nowUtc)
        {
            // One row per family = one device/session, described by its newest live token.
            const string sql = @"
                SELECT  t.family_id     AS FamilyId,
                        t.device_label  AS DeviceLabel,
                        t.created_ip    AS CreatedIp,
                        t.created_utc   AS CreatedUtc,
                        t.expires_utc   AS ExpiresUtc
                FROM auth_refresh_tokens t
                JOIN (
                    SELECT family_id, MAX(created_utc) AS newest
                    FROM auth_refresh_tokens
                    WHERE user_id = @UserId
                      AND revoked_utc IS NULL
                      AND expires_utc > @NowUtc
                    GROUP BY family_id
                ) latest
                  ON latest.family_id = t.family_id
                 AND latest.newest    = t.created_utc
                WHERE t.user_id = @UserId
                  AND t.revoked_utc IS NULL
                ORDER BY t.created_utc DESC;";

            using var connection = _dbFactory.GetConnection();
            return (await connection.QueryAsync<ActiveSessionDto>(sql, new { UserId = userId, NowUtc = nowUtc })).ToList();
        }

        public async Task<int> CountActiveFamiliesAsync(string userId, DateTime nowUtc)
        {
            const string sql = @"
                SELECT COUNT(DISTINCT family_id)
                FROM auth_refresh_tokens
                WHERE user_id = @UserId
                  AND revoked_utc IS NULL
                  AND expires_utc > @NowUtc;";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteScalarAsync<int>(sql, new { UserId = userId, NowUtc = nowUtc });
        }

        public async Task RevokeOldestFamiliesAsync(string userId, int keepCount, string reason, DateTime nowUtc)
        {
            const string sql = @"
                UPDATE auth_refresh_tokens
                SET revoked_utc    = @NowUtc,
                    revoked_reason = @Reason
                WHERE user_id = @UserId
                  AND revoked_utc IS NULL
                  AND family_id IN (
                      SELECT family_id FROM (
                          SELECT family_id
                          FROM auth_refresh_tokens
                          WHERE user_id = @UserId
                            AND revoked_utc IS NULL
                            AND expires_utc > @NowUtc
                          GROUP BY family_id
                          ORDER BY MAX(created_utc) DESC
                          LIMIT 18446744073709551615 OFFSET @KeepCount
                      ) AS stale
                  );";

            using var connection = _dbFactory.GetConnection();
            await connection.ExecuteAsync(sql, new
            {
                UserId = userId,
                KeepCount = keepCount,
                Reason = reason,
                NowUtc = nowUtc
            });
        }

        public async Task<int> DeleteExpiredAsync(DateTime cutoffUtc)
        {
            const string sql = @"DELETE FROM auth_refresh_tokens WHERE expires_utc < @CutoffUtc;";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteAsync(sql, new { CutoffUtc = cutoffUtc });
        }
    }
}
