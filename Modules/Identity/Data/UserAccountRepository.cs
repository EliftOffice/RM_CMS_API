using System.Data;
using Dapper;
using RM_CMS.Data;
using RM_CMS.Modules.Identity.Domain;

namespace RM_CMS.Modules.Identity.Data
{
    /// <summary>
    /// Data access for <c>user_account</c>, <c>person</c>, <c>user_role</c> and
    /// <c>password_history</c>.
    ///
    /// Every statement is parameterised — no user input is ever concatenated into
    /// SQL. Mutating statements carry an optimistic-concurrency guard on
    /// <c>row_version</c> and return the affected row count, so a lost update is
    /// detectable instead of silent.
    /// </summary>
    public interface IUserAccountRepository
    {
        Task<UserAccount?> GetByUsernameAsync(string normalizedUsername);
        Task<UserAccount?> GetByPublicIdAsync(string publicId);

        /// <summary>
        /// Lookup by internal key. Used only by the refresh path, which holds an id
        /// from the token row rather than a public id.
        /// </summary>
        Task<UserAccount?> GetByIdAsync(long id);
        Task<IReadOnlyList<UserAccount>> ListAsync(int skip, int take, string? search, string? roleCode);
        Task<int> CountAsync(string? search, string? roleCode);

        Task<bool> UsernameExistsAsync(string normalizedUsername);
        Task<int> CountActiveAdminsAsync(long? excludingAccountId = null);

        /// <summary>
        /// Creates the account. The person must already exist — accounts attach to
        /// people, they do not create them.
        /// </summary>
        Task<long> CreateAsync(UserAccount account, IEnumerable<UserRoleAssignment> roles, long? actingUserId);

        Task<bool> RecordLoginSuccessAsync(long accountId, int rowVersion, DateTime nowUtc);
        Task<bool> RecordLoginFailureAsync(long accountId, int rowVersion, int failureCount, DateTime? lockoutEndsAt);

        Task<bool> UpdatePasswordAsync(
            long accountId, int rowVersion, string passwordHash, string newSecurityStamp,
            bool mustChangePassword, DateTime nowUtc);

        /// <summary>Silent hash upgrade after a successful login. Does not rotate the stamp.</summary>
        Task<bool> UpgradePasswordHashAsync(long accountId, string passwordHash);

        Task<bool> SetActiveAsync(long accountId, int rowVersion, bool isActive, string newSecurityStamp);
        Task<bool> ReplaceRolesAsync(long accountId, int rowVersion, IEnumerable<UserRoleAssignment> roles,
                                     string newSecurityStamp, long? actingUserId);

        Task AddPasswordHistoryAsync(long accountId, string passwordHash, DateTime nowUtc, int keepCount);
        Task<IReadOnlyList<string>> GetRecentPasswordHashesAsync(long accountId, int count);

        /// <summary>Resolves a campus public id to its internal key. Null when unknown.</summary>
        Task<long?> ResolveCampusIdAsync(string? campusPublicId);

        /// <summary>
        /// Resolves a person's public id to its internal key, excluding soft-deleted
        /// rows. Null when unknown — accounts attach to existing people only.
        /// </summary>
        Task<long?> ResolvePersonIdAsync(string personPublicId);
    }

    public sealed class UserAccountRepository : IUserAccountRepository
    {
        private readonly IDbConnectionFactory _dbFactory;

        public UserAccountRepository(IDbConnectionFactory dbFactory) => _dbFactory = dbFactory;

        /// <summary>
        /// The authentication read model. Joins the person (identity), the campus
        /// (tenancy), and optionally the volunteer and led team, so a login needs
        /// exactly two round trips: this, then the roles.
        /// </summary>
        private const string SelectAccount = @"
            SELECT
                ua.id                    AS Id,
                ua.public_id             AS PublicId,
                ua.username              AS Username,
                ua.normalized_username   AS NormalizedUsername,
                ua.password_hash         AS PasswordHash,
                ua.security_stamp        AS SecurityStamp,
                ua.token_version         AS TokenVersion,
                ua.is_active             AS IsActive,
                ua.must_change_password  AS MustChangePassword,
                ua.failed_access_count   AS FailedAccessCount,
                ua.lockout_ends_at       AS LockoutEndsAt,
                ua.last_login_at         AS LastLoginAt,
                ua.password_changed_at   AS PasswordChangedAt,
                ua.row_version           AS RowVersion,
                ua.created_at            AS CreatedAt,

                p.id                     AS PersonId,
                p.public_id              AS PersonPublicId,
                p.given_name             AS GivenName,
                p.family_name            AS FamilyName,
                p.full_name              AS FullName,
                p.campus_id              AS CampusId,
                c.public_id              AS CampusPublicId,

                v.public_id              AS VolunteerPublicId,

                -- The team they LEAD if they lead one, otherwise the team they
                -- BELONG to. This used to be the led team alone, so a volunteer —
                -- who leads nothing — always came back with no team, and the
                -- assignments screen showed 'N/A' where their team lead's name
                -- should be.
                COALESCE(lt.public_id, vt.public_id) AS TeamPublicId
            FROM user_account ua
            JOIN person p          ON p.id = ua.person_id
            LEFT JOIN campus c     ON c.id = p.campus_id
            LEFT JOIN volunteer v  ON v.person_id = p.id
            LEFT JOIN team lt      ON lt.lead_user_id = ua.id
            LEFT JOIN team vt      ON vt.id = v.team_id";

        public async Task<UserAccount?> GetByUsernameAsync(string normalizedUsername)
        {
            const string sql = SelectAccount + @"
            WHERE ua.normalized_username = @NormalizedUsername
              AND p.deleted_at IS NULL
            LIMIT 1;";

            using var connection = _dbFactory.GetConnection();
            var account = await connection.QueryFirstOrDefaultAsync<UserAccount>(
                sql, new { NormalizedUsername = normalizedUsername });

            if (account is not null)
                account.Roles = await LoadRolesAsync(connection, account.Id);

            return account;
        }

        public async Task<UserAccount?> GetByPublicIdAsync(string publicId)
        {
            const string sql = SelectAccount + @"
            WHERE ua.public_id = @PublicId
            LIMIT 1;";

            using var connection = _dbFactory.GetConnection();
            var account = await connection.QueryFirstOrDefaultAsync<UserAccount>(sql, new { PublicId = publicId });

            if (account is not null)
                account.Roles = await LoadRolesAsync(connection, account.Id);

            return account;
        }

        public async Task<UserAccount?> GetByIdAsync(long id)
        {
            const string sql = SelectAccount + @"
            WHERE ua.id = @Id
            LIMIT 1;";

            using var connection = _dbFactory.GetConnection();
            var account = await connection.QueryFirstOrDefaultAsync<UserAccount>(sql, new { Id = id });

            if (account is not null)
                account.Roles = await LoadRolesAsync(connection, account.Id);

            return account;
        }

        public async Task<IReadOnlyList<UserAccount>> ListAsync(int skip, int take, string? search, string? roleCode)
        {
            const string sql = SelectAccount + @"
            WHERE (@Search IS NULL
                   OR p.full_name  LIKE CONCAT('%', @Search, '%')
                   OR ua.username  LIKE CONCAT('%', @Search, '%'))
              AND (@RoleCode IS NULL
                   OR EXISTS (SELECT 1 FROM user_role ur
                               WHERE ur.user_account_id = ua.id AND ur.role_code = @RoleCode))
              AND p.deleted_at IS NULL
            ORDER BY p.full_name
            LIMIT @Take OFFSET @Skip;";

            using var connection = _dbFactory.GetConnection();
            var accounts = (await connection.QueryAsync<UserAccount>(sql, new
            {
                Skip = skip,
                Take = take,
                Search = Blank(search),
                RoleCode = Blank(roleCode)
            })).ToList();

            if (accounts.Count == 0) return accounts;

            // One round trip for every account's roles rather than N.
            const string roleSql = @"
                SELECT ur.user_account_id AS UserAccountId,
                       ur.role_code       AS RoleCode,
                       ur.campus_id       AS CampusId,
                       c.public_id        AS CampusPublicId,
                       ur.granted_at      AS GrantedAt
                FROM user_role ur
                LEFT JOIN campus c ON c.id = ur.campus_id
                WHERE ur.user_account_id IN @Ids;";

            var rows = await connection.QueryAsync<(long UserAccountId, string RoleCode, long? CampusId, string? CampusPublicId, DateTime GrantedAt)>(
                roleSql, new { Ids = accounts.Select(a => a.Id).ToArray() });

            var byAccount = rows
                .GroupBy(r => r.UserAccountId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(r => new UserRoleAssignment
                    {
                        RoleCode = r.RoleCode,
                        CampusId = r.CampusId,
                        CampusPublicId = r.CampusPublicId,
                        GrantedAt = r.GrantedAt
                    }).ToList());

            foreach (var account in accounts)
                account.Roles = byAccount.TryGetValue(account.Id, out var roles) ? roles : new List<UserRoleAssignment>();

            return accounts;
        }

        public async Task<int> CountAsync(string? search, string? roleCode)
        {
            const string sql = @"
                SELECT COUNT(1)
                FROM user_account ua
                JOIN person p ON p.id = ua.person_id
                WHERE (@Search IS NULL
                       OR p.full_name LIKE CONCAT('%', @Search, '%')
                       OR ua.username LIKE CONCAT('%', @Search, '%'))
                  AND (@RoleCode IS NULL
                       OR EXISTS (SELECT 1 FROM user_role ur
                                   WHERE ur.user_account_id = ua.id AND ur.role_code = @RoleCode))
                  AND p.deleted_at IS NULL;";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteScalarAsync<int>(sql, new { Search = Blank(search), RoleCode = Blank(roleCode) });
        }

        public async Task<bool> UsernameExistsAsync(string normalizedUsername)
        {
            const string sql = @"SELECT COUNT(1) FROM user_account WHERE normalized_username = @NormalizedUsername;";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteScalarAsync<int>(sql, new { NormalizedUsername = normalizedUsername }) > 0;
        }

        public async Task<int> CountActiveAdminsAsync(long? excludingAccountId = null)
        {
            const string sql = @"
                SELECT COUNT(DISTINCT ua.id)
                FROM user_account ua
                JOIN user_role ur ON ur.user_account_id = ua.id
                WHERE ur.role_code = 'ADMIN'
                  AND ua.is_active = 1
                  AND (@Excluding IS NULL OR ua.id <> @Excluding);";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteScalarAsync<int>(sql, new { Excluding = excludingAccountId });
        }

        public async Task<long> CreateAsync(UserAccount account, IEnumerable<UserRoleAssignment> roles, long? actingUserId)
        {
            const string insertAccount = @"
                INSERT INTO user_account
                    (public_id, person_id, username, normalized_username,
                     password_hash, security_stamp, token_version,
                     is_active, must_change_password, failed_access_count,
                     created_by, updated_by)
                VALUES
                    (@PublicId, @PersonId, @Username, @NormalizedUsername,
                     @PasswordHash, @SecurityStamp, 1,
                     @IsActive, @MustChangePassword, 0,
                     @ActingUserId, @ActingUserId);
                SELECT LAST_INSERT_ID();";

            // ON DUPLICATE KEY, not INSERT IGNORE: both make a repeated grant a no-op,
            // but IGNORE also downgrades a FOREIGN KEY violation to a warning, so a
            // role_code with no app_role row was skipped silently and the account was
            // created with no role at all.
            const string insertRole = @"
                INSERT INTO user_role (user_account_id, role_code, campus_id, granted_by)
                VALUES (@AccountId, @RoleCode, @CampusId, @ActingUserId)
                ON DUPLICATE KEY UPDATE user_account_id = user_account_id;";

            using var connection = _dbFactory.GetConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                var accountId = await connection.ExecuteScalarAsync<long>(insertAccount, new
                {
                    account.PublicId,
                    account.PersonId,
                    account.Username,
                    account.NormalizedUsername,
                    account.PasswordHash,
                    account.SecurityStamp,
                    account.IsActive,
                    account.MustChangePassword,
                    ActingUserId = actingUserId
                }, transaction);

                foreach (var role in roles.DistinctBy(r => r.RoleCode, StringComparer.Ordinal))
                {
                    await connection.ExecuteAsync(insertRole, new
                    {
                        AccountId = accountId,
                        role.RoleCode,
                        role.CampusId,
                        ActingUserId = actingUserId
                    }, transaction);
                }

                transaction.Commit();
                return accountId;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public async Task<bool> RecordLoginSuccessAsync(long accountId, int rowVersion, DateTime nowUtc)
        {
            const string sql = @"
                UPDATE user_account
                SET last_login_at       = @NowUtc,
                    failed_access_count = 0,
                    lockout_ends_at     = NULL,
                    row_version         = row_version + 1
                WHERE id = @Id AND row_version = @RowVersion;";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteAsync(sql, new { Id = accountId, RowVersion = rowVersion, NowUtc = nowUtc }) == 1;
        }

        public async Task<bool> RecordLoginFailureAsync(long accountId, int rowVersion, int failureCount, DateTime? lockoutEndsAt)
        {
            const string sql = @"
                UPDATE user_account
                SET failed_access_count = @FailureCount,
                    lockout_ends_at     = @LockoutEndsAt,
                    row_version         = row_version + 1
                WHERE id = @Id AND row_version = @RowVersion;";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteAsync(sql, new
            {
                Id = accountId,
                RowVersion = rowVersion,
                FailureCount = failureCount,
                LockoutEndsAt = lockoutEndsAt
            }) == 1;
        }

        public async Task<bool> UpdatePasswordAsync(
            long accountId, int rowVersion, string passwordHash, string newSecurityStamp,
            bool mustChangePassword, DateTime nowUtc)
        {
            // Rotating the stamp invalidates every outstanding access token for this
            // account (it is re-checked per request); bumping token_version is a
            // second, coarser kill switch.
            const string sql = @"
                UPDATE user_account
                SET password_hash        = @PasswordHash,
                    security_stamp       = @SecurityStamp,
                    token_version        = token_version + 1,
                    must_change_password = @MustChangePassword,
                    password_changed_at  = @NowUtc,
                    failed_access_count  = 0,
                    lockout_ends_at      = NULL,
                    row_version          = row_version + 1
                WHERE id = @Id AND row_version = @RowVersion;";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteAsync(sql, new
            {
                Id = accountId,
                RowVersion = rowVersion,
                PasswordHash = passwordHash,
                SecurityStamp = newSecurityStamp,
                MustChangePassword = mustChangePassword,
                NowUtc = nowUtc
            }) == 1;
        }

        public async Task<bool> UpgradePasswordHashAsync(long accountId, string passwordHash)
        {
            // No row_version guard and no stamp rotation: this re-encodes the SAME
            // password after a successful verification, so it must not log anyone out.
            const string sql = @"
                UPDATE user_account
                SET password_hash = @PasswordHash,
                    row_version   = row_version + 1
                WHERE id = @Id;";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteAsync(sql, new { Id = accountId, PasswordHash = passwordHash }) == 1;
        }

        public async Task<bool> SetActiveAsync(long accountId, int rowVersion, bool isActive, string newSecurityStamp)
        {
            const string sql = @"
                UPDATE user_account
                SET is_active      = @IsActive,
                    security_stamp = @SecurityStamp,
                    token_version  = token_version + 1,
                    row_version    = row_version + 1
                WHERE id = @Id AND row_version = @RowVersion;";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteAsync(sql, new
            {
                Id = accountId,
                RowVersion = rowVersion,
                IsActive = isActive,
                SecurityStamp = newSecurityStamp
            }) == 1;
        }

        public async Task<bool> ReplaceRolesAsync(
            long accountId, int rowVersion, IEnumerable<UserRoleAssignment> roles,
            string newSecurityStamp, long? actingUserId)
        {
            const string bumpAccount = @"
                UPDATE user_account
                SET security_stamp = @SecurityStamp,
                    token_version  = token_version + 1,
                    row_version    = row_version + 1
                WHERE id = @Id AND row_version = @RowVersion;";

            const string deleteRoles = @"DELETE FROM user_role WHERE user_account_id = @Id;";

            // ON DUPLICATE KEY, not INSERT IGNORE: both make a repeated grant a no-op,
            // but IGNORE also downgrades a FOREIGN KEY violation to a warning, so a
            // role_code with no app_role row was skipped silently and the account was
            // created with no role at all.
            const string insertRole = @"
                INSERT INTO user_role (user_account_id, role_code, campus_id, granted_by)
                VALUES (@Id, @RoleCode, @CampusId, @ActingUserId)
                ON DUPLICATE KEY UPDATE user_account_id = user_account_id;";

            using var connection = _dbFactory.GetConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                var updated = await connection.ExecuteAsync(bumpAccount, new
                {
                    Id = accountId,
                    RowVersion = rowVersion,
                    SecurityStamp = newSecurityStamp
                }, transaction);

                if (updated != 1)
                {
                    transaction.Rollback();
                    return false; // concurrency conflict — the caller re-reads and retries
                }

                await connection.ExecuteAsync(deleteRoles, new { Id = accountId }, transaction);

                foreach (var role in roles.DistinctBy(r => r.RoleCode, StringComparer.Ordinal))
                {
                    await connection.ExecuteAsync(insertRole, new
                    {
                        Id = accountId,
                        role.RoleCode,
                        role.CampusId,
                        ActingUserId = actingUserId
                    }, transaction);
                }

                transaction.Commit();
                return true;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public async Task AddPasswordHistoryAsync(long accountId, string passwordHash, DateTime nowUtc, int keepCount)
        {
            const string insert = @"
                INSERT INTO password_history (user_account_id, password_hash, created_at)
                VALUES (@Id, @PasswordHash, @NowUtc);";

            // Keep the table bounded: drop everything older than the newest `keepCount`.
            const string trim = @"
                DELETE FROM password_history
                WHERE user_account_id = @Id
                  AND id NOT IN (
                      SELECT id FROM (
                          SELECT id FROM password_history
                          WHERE user_account_id = @Id
                          ORDER BY created_at DESC
                          LIMIT @KeepCount
                      ) AS keepers
                  );";

            using var connection = _dbFactory.GetConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                await connection.ExecuteAsync(insert,
                    new { Id = accountId, PasswordHash = passwordHash, NowUtc = nowUtc }, transaction);

                if (keepCount > 0)
                    await connection.ExecuteAsync(trim, new { Id = accountId, KeepCount = keepCount }, transaction);

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public async Task<IReadOnlyList<string>> GetRecentPasswordHashesAsync(long accountId, int count)
        {
            if (count <= 0) return Array.Empty<string>();

            const string sql = @"
                SELECT password_hash
                FROM password_history
                WHERE user_account_id = @Id
                ORDER BY created_at DESC
                LIMIT @Count;";

            using var connection = _dbFactory.GetConnection();
            return (await connection.QueryAsync<string>(sql, new { Id = accountId, Count = count })).ToList();
        }

        public async Task<long?> ResolveCampusIdAsync(string? campusPublicId)
        {
            if (string.IsNullOrWhiteSpace(campusPublicId)) return null;

            const string sql = @"SELECT id FROM campus WHERE public_id = @PublicId LIMIT 1;";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteScalarAsync<long?>(sql, new { PublicId = campusPublicId });
        }

        public async Task<long?> ResolvePersonIdAsync(string personPublicId)
        {
            if (string.IsNullOrWhiteSpace(personPublicId)) return null;

            const string sql = @"
                SELECT id FROM person
                WHERE public_id = @PublicId AND deleted_at IS NULL
                LIMIT 1;";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteScalarAsync<long?>(sql, new { PublicId = personPublicId });
        }

        private static async Task<List<UserRoleAssignment>> LoadRolesAsync(IDbConnection connection, long accountId)
        {
            const string sql = @"
                SELECT ur.role_code  AS RoleCode,
                       ur.campus_id  AS CampusId,
                       c.public_id   AS CampusPublicId,
                       ur.granted_at AS GrantedAt
                FROM user_role ur
                LEFT JOIN campus c ON c.id = ur.campus_id
                WHERE ur.user_account_id = @Id;";

            return (await connection.QueryAsync<UserRoleAssignment>(sql, new { Id = accountId })).ToList();
        }

        private static string? Blank(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
