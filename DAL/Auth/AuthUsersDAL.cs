using System.Data;
using Dapper;
using RM_CMS.Data;
using RM_CMS.Data.Models.Auth;

namespace RM_CMS.DAL.Auth
{
    /// <summary>
    /// Data access for <c>auth_users</c>, <c>auth_user_roles</c> and <c>auth_password_history</c>.
    ///
    /// Every statement is parameterised — no string concatenation of user input anywhere,
    /// so there is no SQL injection surface. Mutating statements use optimistic concurrency
    /// (<c>row_version</c>) and return the affected row count so the BLL can detect a
    /// lost update instead of silently clobbering one.
    /// </summary>
    public interface IAuthUsersDAL
    {
        Task<AuthUser?> GetByUsernameAsync(string normalizedUsername);
        Task<AuthUser?> GetByIdAsync(string userId);
        Task<IReadOnlyList<AuthUser>> ListAsync(int skip, int take, string? search);
        Task<int> CountAsync(string? search);
        Task<bool> UsernameExistsAsync(string normalizedUsername);
        Task<bool> AnyAdminExistsAsync();

        Task<bool> CreateAsync(AuthUser user, IEnumerable<string> roles, string? createdBy);

        Task<bool> RecordLoginSuccessAsync(string userId, long rowVersion, DateTime nowUtc);
        Task<bool> RecordLoginFailureAsync(string userId, long rowVersion, int failedCount, DateTime? lockoutEndUtc);

        Task<bool> UpdatePasswordAsync(
            string userId, long rowVersion, string passwordHash, string newSecurityStamp,
            bool mustChangePassword, DateTime nowUtc);

        /// <summary>Silent hash upgrade after a successful login — does not rotate the stamp.</summary>
        Task<bool> UpgradePasswordHashAsync(string userId, string passwordHash);

        Task<bool> SetActiveAsync(string userId, long rowVersion, bool isActive, string newSecurityStamp);
        Task<bool> ReplaceRolesAsync(string userId, long rowVersion, IEnumerable<string> roles, string newSecurityStamp, string? assignedBy);

        Task AddPasswordHistoryAsync(string userId, string passwordHash, DateTime nowUtc, int keepCount);
        Task<IReadOnlyList<string>> GetRecentPasswordHashesAsync(string userId, int count);
    }

    public sealed class AuthUsersDAL : IAuthUsersDAL
    {
        private readonly IDbConnectionFactory _dbFactory;

        // Explicit column list: SELECT * would leak new columns into the model implicitly
        // and breaks if column order changes.
        private const string UserColumns = @"
            user_id, username, normalized_username, email, mobile_number, display_name,
            password_hash, security_stamp, token_version,
            is_active, must_change_password, access_failed_count,
            lockout_end_utc, last_login_utc, password_changed_utc,
            volunteer_id, team_lead_id, person_id,
            row_version, created_at, updated_at";

        public AuthUsersDAL(IDbConnectionFactory dbFactory) => _dbFactory = dbFactory;

        public async Task<AuthUser?> GetByUsernameAsync(string normalizedUsername)
        {
            const string sql = $@"
                SELECT {UserColumns}
                FROM auth_users
                WHERE normalized_username = @NormalizedUsername
                LIMIT 1;";

            using var connection = _dbFactory.GetConnection();
            var user = await connection.QueryFirstOrDefaultAsync<AuthUser>(
                sql, new { NormalizedUsername = normalizedUsername });

            if (user is not null)
                user.Roles = await LoadRolesAsync(connection, user.UserId);

            return user;
        }

        public async Task<AuthUser?> GetByIdAsync(string userId)
        {
            const string sql = $@"
                SELECT {UserColumns}
                FROM auth_users
                WHERE user_id = @UserId
                LIMIT 1;";

            using var connection = _dbFactory.GetConnection();
            var user = await connection.QueryFirstOrDefaultAsync<AuthUser>(sql, new { UserId = userId });

            if (user is not null)
                user.Roles = await LoadRolesAsync(connection, user.UserId);

            return user;
        }

        public async Task<IReadOnlyList<AuthUser>> ListAsync(int skip, int take, string? search)
        {
            const string sql = $@"
                SELECT {UserColumns}
                FROM auth_users
                WHERE (@Search IS NULL
                       OR display_name   LIKE CONCAT('%', @Search, '%')
                       OR username       LIKE CONCAT('%', @Search, '%')
                       OR mobile_number  LIKE CONCAT('%', @Search, '%'))
                ORDER BY display_name
                LIMIT @Take OFFSET @Skip;";

            using var connection = _dbFactory.GetConnection();
            var users = (await connection.QueryAsync<AuthUser>(
                sql,
                new { Skip = skip, Take = take, Search = string.IsNullOrWhiteSpace(search) ? null : search.Trim() }))
                .ToList();

            if (users.Count == 0) return users;

            // One round trip for all roles rather than N.
            const string roleSql = @"
                SELECT user_id, role_name
                FROM auth_user_roles
                WHERE user_id IN @UserIds;";

            var roleRows = await connection.QueryAsync<(string UserId, string RoleName)>(
                roleSql, new { UserIds = users.Select(u => u.UserId).ToArray() });

            var byUser = roleRows
                .GroupBy(r => r.UserId, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Select(r => r.RoleName).ToList(), StringComparer.Ordinal);

            foreach (var user in users)
                user.Roles = byUser.TryGetValue(user.UserId, out var roles) ? roles : new List<string>();

            return users;
        }

        public async Task<int> CountAsync(string? search)
        {
            const string sql = @"
                SELECT COUNT(1)
                FROM auth_users
                WHERE (@Search IS NULL
                       OR display_name   LIKE CONCAT('%', @Search, '%')
                       OR username       LIKE CONCAT('%', @Search, '%')
                       OR mobile_number  LIKE CONCAT('%', @Search, '%'));";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteScalarAsync<int>(
                sql, new { Search = string.IsNullOrWhiteSpace(search) ? null : search.Trim() });
        }

        public async Task<bool> UsernameExistsAsync(string normalizedUsername)
        {
            const string sql = @"
                SELECT COUNT(1) FROM auth_users WHERE normalized_username = @NormalizedUsername;";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteScalarAsync<int>(
                sql, new { NormalizedUsername = normalizedUsername }) > 0;
        }

        public async Task<bool> AnyAdminExistsAsync()
        {
            const string sql = @"
                SELECT COUNT(1)
                FROM auth_user_roles r
                JOIN auth_users u ON u.user_id = r.user_id
                WHERE r.role_name = 'Admin' AND u.is_active = 1;";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteScalarAsync<int>(sql) > 0;
        }

        public async Task<bool> CreateAsync(AuthUser user, IEnumerable<string> roles, string? createdBy)
        {
            const string insertUser = @"
                INSERT INTO auth_users
                    (user_id, username, normalized_username, email, mobile_number, display_name,
                     password_hash, security_stamp, token_version,
                     is_active, must_change_password, access_failed_count,
                     volunteer_id, team_lead_id, person_id, row_version, created_by)
                VALUES
                    (@UserId, @Username, @NormalizedUsername, @Email, @MobileNumber, @DisplayName,
                     @PasswordHash, @SecurityStamp, @TokenVersion,
                     @IsActive, @MustChangePassword, 0,
                     @VolunteerId, @TeamLeadId, @PersonId, 1, @CreatedBy);";

            const string insertRole = @"
                INSERT IGNORE INTO auth_user_roles (user_id, role_name, assigned_by)
                VALUES (@UserId, @RoleName, @AssignedBy);";

            using var connection = _dbFactory.GetConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                await connection.ExecuteAsync(insertUser, new
                {
                    user.UserId,
                    user.Username,
                    user.NormalizedUsername,
                    user.Email,
                    user.MobileNumber,
                    user.DisplayName,
                    user.PasswordHash,
                    user.SecurityStamp,
                    user.TokenVersion,
                    user.IsActive,
                    user.MustChangePassword,
                    user.VolunteerId,
                    user.TeamLeadId,
                    user.PersonId,
                    CreatedBy = createdBy
                }, transaction);

                foreach (var role in roles.Distinct(StringComparer.Ordinal))
                {
                    await connection.ExecuteAsync(insertRole,
                        new { user.UserId, RoleName = role, AssignedBy = createdBy }, transaction);
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

        public async Task<bool> RecordLoginSuccessAsync(string userId, long rowVersion, DateTime nowUtc)
        {
            const string sql = @"
                UPDATE auth_users
                SET last_login_utc      = @NowUtc,
                    access_failed_count = 0,
                    lockout_end_utc     = NULL,
                    row_version         = row_version + 1
                WHERE user_id = @UserId AND row_version = @RowVersion;";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteAsync(sql, new { UserId = userId, RowVersion = rowVersion, NowUtc = nowUtc }) == 1;
        }

        public async Task<bool> RecordLoginFailureAsync(string userId, long rowVersion, int failedCount, DateTime? lockoutEndUtc)
        {
            const string sql = @"
                UPDATE auth_users
                SET access_failed_count = @FailedCount,
                    lockout_end_utc     = @LockoutEndUtc,
                    row_version         = row_version + 1
                WHERE user_id = @UserId AND row_version = @RowVersion;";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteAsync(sql, new
            {
                UserId = userId,
                RowVersion = rowVersion,
                FailedCount = failedCount,
                LockoutEndUtc = lockoutEndUtc
            }) == 1;
        }

        public async Task<bool> UpdatePasswordAsync(
            string userId, long rowVersion, string passwordHash, string newSecurityStamp,
            bool mustChangePassword, DateTime nowUtc)
        {
            // Rotating the security stamp invalidates every outstanding access token for
            // this user (checked on each request); bumping token_version is a second,
            // coarser kill-switch that survives a stamp collision.
            const string sql = @"
                UPDATE auth_users
                SET password_hash        = @PasswordHash,
                    security_stamp       = @SecurityStamp,
                    token_version        = token_version + 1,
                    must_change_password = @MustChangePassword,
                    password_changed_utc = @NowUtc,
                    access_failed_count  = 0,
                    lockout_end_utc      = NULL,
                    row_version          = row_version + 1
                WHERE user_id = @UserId AND row_version = @RowVersion;";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteAsync(sql, new
            {
                UserId = userId,
                RowVersion = rowVersion,
                PasswordHash = passwordHash,
                SecurityStamp = newSecurityStamp,
                MustChangePassword = mustChangePassword,
                NowUtc = nowUtc
            }) == 1;
        }

        public async Task<bool> UpgradePasswordHashAsync(string userId, string passwordHash)
        {
            // Deliberately no row_version guard and no stamp rotation: this is an
            // idempotent re-encoding of the *same* password after a successful login.
            const string sql = @"
                UPDATE auth_users
                SET password_hash = @PasswordHash,
                    row_version   = row_version + 1
                WHERE user_id = @UserId;";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteAsync(sql, new { UserId = userId, PasswordHash = passwordHash }) == 1;
        }

        public async Task<bool> SetActiveAsync(string userId, long rowVersion, bool isActive, string newSecurityStamp)
        {
            const string sql = @"
                UPDATE auth_users
                SET is_active      = @IsActive,
                    security_stamp = @SecurityStamp,
                    token_version  = token_version + 1,
                    row_version    = row_version + 1
                WHERE user_id = @UserId AND row_version = @RowVersion;";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteAsync(sql, new
            {
                UserId = userId,
                RowVersion = rowVersion,
                IsActive = isActive,
                SecurityStamp = newSecurityStamp
            }) == 1;
        }

        public async Task<bool> ReplaceRolesAsync(
            string userId, long rowVersion, IEnumerable<string> roles, string newSecurityStamp, string? assignedBy)
        {
            const string bumpUser = @"
                UPDATE auth_users
                SET security_stamp = @SecurityStamp,
                    token_version  = token_version + 1,
                    row_version    = row_version + 1
                WHERE user_id = @UserId AND row_version = @RowVersion;";

            const string deleteRoles = @"DELETE FROM auth_user_roles WHERE user_id = @UserId;";

            const string insertRole = @"
                INSERT IGNORE INTO auth_user_roles (user_id, role_name, assigned_by)
                VALUES (@UserId, @RoleName, @AssignedBy);";

            using var connection = _dbFactory.GetConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                var updated = await connection.ExecuteAsync(bumpUser, new
                {
                    UserId = userId,
                    RowVersion = rowVersion,
                    SecurityStamp = newSecurityStamp
                }, transaction);

                if (updated != 1)
                {
                    transaction.Rollback();
                    return false; // concurrency conflict — caller re-reads and retries
                }

                await connection.ExecuteAsync(deleteRoles, new { UserId = userId }, transaction);

                foreach (var role in roles.Distinct(StringComparer.Ordinal))
                {
                    await connection.ExecuteAsync(insertRole,
                        new { UserId = userId, RoleName = role, AssignedBy = assignedBy }, transaction);
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

        public async Task AddPasswordHistoryAsync(string userId, string passwordHash, DateTime nowUtc, int keepCount)
        {
            const string insert = @"
                INSERT INTO auth_password_history (user_id, password_hash, created_utc)
                VALUES (@UserId, @PasswordHash, @NowUtc);";

            // Keep the table bounded: drop everything older than the newest `keepCount` rows.
            const string trim = @"
                DELETE FROM auth_password_history
                WHERE user_id = @UserId
                  AND history_id NOT IN (
                      SELECT history_id FROM (
                          SELECT history_id
                          FROM auth_password_history
                          WHERE user_id = @UserId
                          ORDER BY created_utc DESC
                          LIMIT @KeepCount
                      ) AS keepers
                  );";

            using var connection = _dbFactory.GetConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                await connection.ExecuteAsync(insert,
                    new { UserId = userId, PasswordHash = passwordHash, NowUtc = nowUtc }, transaction);

                if (keepCount > 0)
                {
                    await connection.ExecuteAsync(trim,
                        new { UserId = userId, KeepCount = keepCount }, transaction);
                }

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public async Task<IReadOnlyList<string>> GetRecentPasswordHashesAsync(string userId, int count)
        {
            if (count <= 0) return Array.Empty<string>();

            const string sql = @"
                SELECT password_hash
                FROM auth_password_history
                WHERE user_id = @UserId
                ORDER BY created_utc DESC
                LIMIT @Count;";

            using var connection = _dbFactory.GetConnection();
            return (await connection.QueryAsync<string>(sql, new { UserId = userId, Count = count })).ToList();
        }

        private static async Task<List<string>> LoadRolesAsync(IDbConnection connection, string userId)
        {
            const string sql = @"SELECT role_name FROM auth_user_roles WHERE user_id = @UserId;";
            return (await connection.QueryAsync<string>(sql, new { UserId = userId })).ToList();
        }
    }
}
