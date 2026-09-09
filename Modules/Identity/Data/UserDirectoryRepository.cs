using Dapper;
using RM_CMS.Data;
using RM_CMS.Modules.Identity.Domain;

namespace RM_CMS.Modules.Identity.Data
{
    /// <summary>
    /// The user directory: one row per PERSON who holds any kind of standing in the
    /// system — a login, a volunteer record, or both.
    ///
    /// This is the only place that reads across <c>person</c>, <c>user_account</c>,
    /// <c>user_role</c> and <c>volunteer</c> together. It exists because "user" is not
    /// a table here: identity lives on <c>person</c>, access lives on
    /// <c>user_account</c>, authority lives on <c>user_role</c>, and the capacity to
    /// carry cases lives on <c>volunteer</c>.
    ///
    /// That split is what makes promotion safe. Promoting somebody adds a role grant
    /// and, if the new role needs one, a volunteer record — it never creates a second
    /// person. <c>ux_user_account_person</c> and <c>ux_volunteer_person</c> enforce
    /// that at the database level, so a duplicate cannot be introduced even by a bug
    /// in this file.
    /// </summary>
    public interface IUserDirectoryRepository
    {
        Task<IReadOnlyList<DirectoryRow>> ListAsync(UserDirectoryQuery query);
        Task<int> CountAsync(UserDirectoryQuery query);

        /// <summary>Everything known about one person's standing in the system.</summary>
        Task<DirectoryRow?> GetByPersonPublicIdAsync(string personPublicId);

        /// <summary>The roles currently granted to an account.</summary>
        Task<IReadOnlyList<RoleGrantRow>> GetRolesAsync(long userAccountId);

        /// <summary>
        /// Grants a role if it is not already held. Returns false when the account
        /// already had it, so a repeated promotion is a no-op rather than an error.
        /// </summary>
        Task<bool> GrantRoleAsync(long userAccountId, string roleCode, long? campusId, long? grantedBy);

        Task<bool> RevokeRoleAsync(long userAccountId, string roleCode);

        /// <summary>Puts an account in charge of a team (<c>team.lead_user_id</c>).</summary>
        Task<bool> SetTeamLeadAsync(long teamId, long userAccountId, long? actingUserId);

        Task<long?> ResolvePersonIdAsync(string personPublicId);
        Task<long?> ResolveTeamIdAsync(string? teamPublicId);
        Task<long?> ResolveCampusIdAsync(string? campusPublicId);

        /// <summary>The person's primary mobile, which is also their username by convention.</summary>
        Task<string?> GetPrimaryMobileAsync(long personId);

        /// <summary>Role grants over time, for the history view.</summary>
        Task<IReadOnlyList<RoleHistoryRow>> GetRoleHistoryAsync(long userAccountId);
    }

    public sealed class UserDirectoryQuery
    {
        public string? Search { get; init; }

        /// <summary>Filters to people holding this role. Null returns everyone.</summary>
        public string? RoleCode { get; init; }

        /// <summary>true = active accounts only, false = disabled only, null = both.</summary>
        public bool? IsActive { get; init; }

        /// <summary>When true, includes volunteers who have no login account.</summary>
        public bool IncludeAccountless { get; init; } = true;

        public int Skip { get; init; }
        public int Take { get; init; } = 25;
    }

    /// <summary>A person's standing, flattened for a list row.</summary>
    public sealed class DirectoryRow
    {
        public long PersonId { get; set; }
        public string PersonPublicId { get; set; } = string.Empty;
        public string? PersonReferenceCode { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string LifecycleStatus { get; set; } = string.Empty;
        public string? PrimaryMobile { get; set; }
        public string? PrimaryEmail { get; set; }
        public string? CampusPublicId { get; set; }
        public string? CampusName { get; set; }

        // ---- account (null when the person has no login) ----
        public long? UserAccountId { get; set; }
        public string? AccountPublicId { get; set; }
        public string? Username { get; set; }
        public bool? IsActive { get; set; }
        public bool? MustChangePassword { get; set; }
        public DateTime? LastLoginAt { get; set; }
        public int AccountRowVersion { get; set; }

        // ---- volunteer (null when they hold no volunteer record) ----
        public long? VolunteerId { get; set; }
        public string? VolunteerPublicId { get; set; }
        public string? VolunteerReferenceCode { get; set; }
        public string? VolunteerStatus { get; set; }
        public string? CapacityBandCode { get; set; }
        public int? CurrentCaseLoad { get; set; }
        public string? TeamName { get; set; }

        /// <summary>Comma-separated role codes, assembled by the query.</summary>
        public string? RoleCodes { get; set; }

        public DateTime CreatedAt { get; set; }

        /// <summary>An active, verified TELEGRAM contact exists for this person.</summary>
        public bool HasTelegram { get; set; }
    }

    public sealed class RoleGrantRow
    {
        public string RoleCode { get; set; } = string.Empty;
        public string? CampusPublicId { get; set; }
    }

    public sealed class RoleHistoryRow
    {
        public string RoleCode { get; set; } = string.Empty;
        public string? CampusName { get; set; }
        public DateTime GrantedAt { get; set; }
        public string? GrantedByName { get; set; }
    }

    public sealed class UserDirectoryRepository : IUserDirectoryRepository
    {
        private readonly IDbConnectionFactory _dbFactory;

        public UserDirectoryRepository(IDbConnectionFactory dbFactory) => _dbFactory = dbFactory;

        /// <summary>
        /// Person is the anchor, everything else is optional. Roles are aggregated with
        /// GROUP_CONCAT rather than joined, because a person with three roles must
        /// still be one row — joining user_role would multiply them.
        ///
        /// Contacts come from correlated subqueries for the same reason: contacts are
        /// rows now, and a person may hold several.
        /// </summary>
        private const string SelectRow = @"
            SELECT
                p.id                    AS PersonId,
                p.public_id             AS PersonPublicId,
                p.reference_code        AS PersonReferenceCode,
                p.full_name             AS FullName,
                p.lifecycle_status      AS LifecycleStatus,
                (SELECT pc.value FROM person_contact pc
                  WHERE pc.person_id = p.id AND pc.contact_type = 'MOBILE'
                  ORDER BY pc.is_primary DESC, pc.id LIMIT 1) AS PrimaryMobile,
                (SELECT pc.value FROM person_contact pc
                  WHERE pc.person_id = p.id AND pc.contact_type = 'EMAIL'
                  ORDER BY pc.is_primary DESC, pc.id LIMIT 1) AS PrimaryEmail,
                cam.public_id           AS CampusPublicId,
                cam.name                AS CampusName,

                ua.id                   AS UserAccountId,
                ua.public_id            AS AccountPublicId,
                ua.username             AS Username,
                ua.is_active            AS IsActive,
                ua.must_change_password AS MustChangePassword,
                ua.last_login_at        AS LastLoginAt,
                COALESCE(ua.row_version, 0) AS AccountRowVersion,

                v.id                    AS VolunteerId,
                v.public_id             AS VolunteerPublicId,
                v.reference_code        AS VolunteerReferenceCode,
                v.status                AS VolunteerStatus,
                v.capacity_band_code    AS CapacityBandCode,
                v.current_case_load     AS CurrentCaseLoad,
                t.name                  AS TeamName,

                (SELECT GROUP_CONCAT(ur.role_code ORDER BY ur.role_code)
                   FROM user_role ur WHERE ur.user_account_id = ua.id) AS RoleCodes,

                p.created_at            AS CreatedAt,

                -- Reachability, shown as an icon on the list. Active links only:
                -- a disconnected row still holds the chat id but delivers nothing.
                EXISTS (SELECT 1 FROM person_contact tg
                         WHERE tg.person_id = p.id
                           AND tg.contact_type = 'TELEGRAM'
                           AND tg.is_verified = 1
                           AND tg.opted_out_at IS NULL) AS HasTelegram

            FROM person p
            LEFT JOIN user_account ua ON ua.person_id = p.id
            LEFT JOIN volunteer    v  ON v.person_id  = p.id
            LEFT JOIN team         t  ON t.id = v.team_id
            LEFT JOIN campus       cam ON cam.id = p.campus_id";

        /// <summary>
        /// A "user" is someone with a login OR a volunteer record. A plain visitor is
        /// not a user and must not appear here — they are found through the people
        /// picker when an administrator wants to promote them.
        /// </summary>
        private const string WhereClause = @"
            WHERE p.deleted_at IS NULL
              AND (ua.id IS NOT NULL OR (@IncludeAccountless = 1 AND v.id IS NOT NULL))
              AND (@RoleCode IS NULL OR EXISTS (
                    SELECT 1 FROM user_role ur
                     WHERE ur.user_account_id = ua.id AND ur.role_code = @RoleCode))
              AND (@IsActive IS NULL OR ua.is_active = @IsActive)
              AND (@Search IS NULL OR @Search = ''
                   OR p.full_name LIKE CONCAT('%', @Search, '%')
                   OR ua.username LIKE CONCAT('%', @Search, '%')
                   OR p.reference_code LIKE CONCAT('%', @Search, '%')
                   OR v.reference_code LIKE CONCAT('%', @Search, '%'))";

        private static object Params(UserDirectoryQuery q) => new
        {
            q.Search,
            q.RoleCode,
            IsActive = q.IsActive,
            IncludeAccountless = q.IncludeAccountless ? 1 : 0,
            q.Skip,
            q.Take
        };

        public async Task<IReadOnlyList<DirectoryRow>> ListAsync(UserDirectoryQuery query)
        {
            var sql = SelectRow + WhereClause + @"
            ORDER BY p.full_name
            LIMIT @Take OFFSET @Skip;";

            using var connection = _dbFactory.GetConnection();
            return (await connection.QueryAsync<DirectoryRow>(sql, Params(query))).ToList();
        }

        public async Task<int> CountAsync(UserDirectoryQuery query)
        {
            const string countSelect = @"
                SELECT COUNT(*)
                FROM person p
                LEFT JOIN user_account ua ON ua.person_id = p.id
                LEFT JOIN volunteer    v  ON v.person_id  = p.id";

            var sql = countSelect + WhereClause + ";";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteScalarAsync<int>(sql, Params(query));
        }

        public async Task<DirectoryRow?> GetByPersonPublicIdAsync(string personPublicId)
        {
            var sql = SelectRow + @"
            WHERE p.public_id = @PublicId AND p.deleted_at IS NULL
            LIMIT 1;";

            using var connection = _dbFactory.GetConnection();
            return await connection.QueryFirstOrDefaultAsync<DirectoryRow>(sql, new { PublicId = personPublicId });
        }

        public async Task<IReadOnlyList<RoleGrantRow>> GetRolesAsync(long userAccountId)
        {
            const string sql = @"
                SELECT ur.role_code AS RoleCode, cam.public_id AS CampusPublicId
                FROM user_role ur
                LEFT JOIN campus cam ON cam.id = ur.campus_id
                WHERE ur.user_account_id = @AccountId
                ORDER BY ur.role_code;";

            using var connection = _dbFactory.GetConnection();
            return (await connection.QueryAsync<RoleGrantRow>(sql, new { AccountId = userAccountId })).ToList();
        }

        public async Task<bool> GrantRoleAsync(long userAccountId, string roleCode, long? campusId, long? grantedBy)
        {
            // ON DUPLICATE KEY rather than INSERT IGNORE. Both make a repeated grant a
            // no-op against the (user_account_id, role_code) key, which is what this
            // needs — but IGNORE also downgrades a FOREIGN KEY violation to a warning,
            // so a role_code with no app_role row was skipped in silence and the caller
            // was told the grant had worked. That produced an active account with no
            // role at all, which then fails at the login screen with "no assigned role"
            // and no trace of why.
            const string sql = @"
                INSERT INTO user_role (user_account_id, role_code, campus_id, granted_by)
                VALUES (@AccountId, @RoleCode, @CampusId, @GrantedBy)
                ON DUPLICATE KEY UPDATE user_account_id = user_account_id;";

            using var connection = _dbFactory.GetConnection();

            return await connection.ExecuteAsync(sql, new
            {
                AccountId = userAccountId,
                RoleCode = roleCode,
                CampusId = campusId,
                GrantedBy = grantedBy
            }) == 1;
        }

        public async Task<bool> RevokeRoleAsync(long userAccountId, string roleCode)
        {
            const string sql = @"
                DELETE FROM user_role
                WHERE user_account_id = @AccountId AND role_code = @RoleCode;";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteAsync(sql, new { AccountId = userAccountId, RoleCode = roleCode }) == 1;
        }

        public async Task<bool> SetTeamLeadAsync(long teamId, long userAccountId, long? actingUserId)
        {
            const string sql = @"
                UPDATE team
                SET lead_user_id = @AccountId,
                    updated_by   = @ActingUserId,
                    row_version  = row_version + 1
                WHERE id = @TeamId;";

            using var connection = _dbFactory.GetConnection();

            return await connection.ExecuteAsync(sql, new
            {
                TeamId = teamId,
                AccountId = userAccountId,
                ActingUserId = actingUserId
            }) == 1;
        }

        public async Task<long?> ResolvePersonIdAsync(string personPublicId)
        {
            using var connection = _dbFactory.GetConnection();

            return await connection.ExecuteScalarAsync<long?>(
                "SELECT id FROM person WHERE public_id = @PublicId AND deleted_at IS NULL LIMIT 1;",
                new { PublicId = personPublicId });
        }

        public async Task<long?> ResolveTeamIdAsync(string? teamPublicId)
        {
            if (string.IsNullOrWhiteSpace(teamPublicId)) return null;

            using var connection = _dbFactory.GetConnection();

            return await connection.ExecuteScalarAsync<long?>(
                "SELECT id FROM team WHERE public_id = @PublicId LIMIT 1;",
                new { PublicId = teamPublicId });
        }

        public async Task<long?> ResolveCampusIdAsync(string? campusPublicId)
        {
            using var connection = _dbFactory.GetConnection();

            if (string.IsNullOrWhiteSpace(campusPublicId))
            {
                // Fall back to the single active campus, which is the common case.
                return await connection.ExecuteScalarAsync<long?>(
                    "SELECT id FROM campus WHERE is_active = 1 ORDER BY id LIMIT 1;");
            }

            return await connection.ExecuteScalarAsync<long?>(
                "SELECT id FROM campus WHERE public_id = @PublicId LIMIT 1;",
                new { PublicId = campusPublicId });
        }

        public async Task<string?> GetPrimaryMobileAsync(long personId)
        {
            const string sql = @"
                SELECT pc.value
                FROM person_contact pc
                WHERE pc.person_id = @PersonId AND pc.contact_type = 'MOBILE'
                ORDER BY pc.is_primary DESC, pc.id
                LIMIT 1;";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteScalarAsync<string?>(sql, new { PersonId = personId });
        }

        public async Task<IReadOnlyList<RoleHistoryRow>> GetRoleHistoryAsync(long userAccountId)
        {
            // user_role holds granted_at/granted_by, so the current grants double as
            // the history. Revocations are not retained — if an audit trail of removals
            // is needed it belongs in security_event, not here.
            const string sql = @"
                SELECT ur.role_code   AS RoleCode,
                       cam.name       AS CampusName,
                       ur.granted_at  AS GrantedAt,
                       gp.full_name   AS GrantedByName
                FROM user_role ur
                LEFT JOIN campus       cam ON cam.id = ur.campus_id
                LEFT JOIN user_account gu  ON gu.id  = ur.granted_by
                LEFT JOIN person       gp  ON gp.id  = gu.person_id
                WHERE ur.user_account_id = @AccountId
                ORDER BY ur.granted_at DESC;";

            using var connection = _dbFactory.GetConnection();
            return (await connection.QueryAsync<RoleHistoryRow>(sql, new { AccountId = userAccountId })).ToList();
        }
    }
}
