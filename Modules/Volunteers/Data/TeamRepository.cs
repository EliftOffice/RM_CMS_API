using Dapper;
using RM_CMS.Data;
using RM_CMS.Modules.Volunteers.Domain;

namespace RM_CMS.Modules.Volunteers.Data
{
    /// <summary>
    /// Data access for <c>team</c>.
    ///
    /// A team is a first-class entity here. The MVP had none — a volunteer pointed
    /// straight at a team lead, so renaming a team, retiring it, or handing it to a
    /// new leader meant rewriting every volunteer row.
    /// </summary>
    public interface ITeamRepository
    {
        Task<Team?> GetByPublicIdAsync(string publicId);
        Task<Team?> GetByIdAsync(long id);
        Task<IReadOnlyList<Team>> ListAsync(long? campusId, bool includeInactive);

        Task<long> CreateAsync(Team team, long? actingUserId);
        Task<bool> UpdateAsync(long id, int rowVersion, string name, long? leadUserId,
                               int maxMembers, bool isActive, long? actingUserId);

        Task<bool> NameExistsAsync(long campusId, string name, long? excludingTeamId = null);
        Task<long?> ResolveLeadAccountIdAsync(string? accountPublicId);

        /// <summary>
        /// Accounts that could lead a team. Its own query rather than the user
        /// directory because that route is admin-only, and a pastor granted team
        /// management still has to be able to pick a leader.
        /// </summary>
        Task<IReadOnlyList<LeadCandidate>> ListLeadCandidatesAsync(long? campusId);
    }

    /// <summary>One account that may be set as a team's lead.</summary>
    public sealed class LeadCandidate
    {
        public string AccountId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string RoleCode { get; set; } = string.Empty;
        public string? CampusName { get; set; }

        /// <summary>The team they already lead, when they lead one.</summary>
        public string? LeadsTeam { get; set; }
    }

    public sealed class TeamRepository : ITeamRepository
    {
        private readonly IDbConnectionFactory _dbFactory;

        public TeamRepository(IDbConnectionFactory dbFactory) => _dbFactory = dbFactory;

        /// <summary>
        /// Member count is a correlated subquery rather than a stored column, because
        /// it changes whenever a volunteer moves and a maintained counter would be one
        /// more thing to drift.
        /// </summary>
        private const string SelectTeam = @"
            SELECT
                t.id            AS Id,
                t.public_id     AS PublicId,
                t.campus_id     AS CampusId,
                c.public_id     AS CampusPublicId,
                c.name          AS CampusName,
                t.name          AS Name,
                t.lead_user_id  AS LeadUserId,
                ua.public_id    AS LeadUserPublicId,
                lp.full_name    AS LeadName,
                t.max_members   AS MaxMembers,
                t.is_active     AS IsActive,
                t.created_at    AS CreatedAt,
                t.row_version   AS RowVersion,
                (SELECT COUNT(1) FROM volunteer v
                  WHERE v.team_id = t.id AND v.status = 'ACTIVE') AS MemberCount
            FROM team t
            JOIN campus c            ON c.id  = t.campus_id
            LEFT JOIN user_account ua ON ua.id = t.lead_user_id
            LEFT JOIN person lp       ON lp.id = ua.person_id";

        public async Task<Team?> GetByPublicIdAsync(string publicId)
        {
            const string sql = SelectTeam + @" WHERE t.public_id = @PublicId LIMIT 1;";

            using var connection = _dbFactory.GetConnection();
            return await connection.QueryFirstOrDefaultAsync<Team>(sql, new { PublicId = publicId });
        }

        public async Task<Team?> GetByIdAsync(long id)
        {
            const string sql = SelectTeam + @" WHERE t.id = @Id LIMIT 1;";

            using var connection = _dbFactory.GetConnection();
            return await connection.QueryFirstOrDefaultAsync<Team>(sql, new { Id = id });
        }

        public async Task<IReadOnlyList<Team>> ListAsync(long? campusId, bool includeInactive)
        {
            const string sql = SelectTeam + @"
            WHERE (@CampusId IS NULL OR t.campus_id = @CampusId)
              AND (@IncludeInactive = 1 OR t.is_active = 1)
            ORDER BY t.name;";

            using var connection = _dbFactory.GetConnection();
            return (await connection.QueryAsync<Team>(sql, new
            {
                CampusId = campusId,
                IncludeInactive = includeInactive
            })).ToList();
        }

        public async Task<long> CreateAsync(Team team, long? actingUserId)
        {
            const string sql = @"
                INSERT INTO team (public_id, campus_id, name, lead_user_id, max_members, created_by, updated_by)
                VALUES (@PublicId, @CampusId, @Name, @LeadUserId, @MaxMembers, @ActingUserId, @ActingUserId);
                SELECT LAST_INSERT_ID();";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteScalarAsync<long>(sql, new
            {
                team.PublicId,
                team.CampusId,
                team.Name,
                team.LeadUserId,
                team.MaxMembers,
                ActingUserId = actingUserId
            });
        }

        public async Task<bool> UpdateAsync(
            long id, int rowVersion, string name, long? leadUserId,
            int maxMembers, bool isActive, long? actingUserId)
        {
            const string sql = @"
                UPDATE team
                SET name         = @Name,
                    lead_user_id = @LeadUserId,
                    max_members  = @MaxMembers,
                    is_active    = @IsActive,
                    updated_by   = @ActingUserId,
                    row_version  = row_version + 1
                WHERE id = @Id AND row_version = @RowVersion;";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteAsync(sql, new
            {
                Id = id,
                RowVersion = rowVersion,
                Name = name,
                LeadUserId = leadUserId,
                MaxMembers = maxMembers,
                IsActive = isActive,
                ActingUserId = actingUserId
            }) == 1;
        }

        public async Task<bool> NameExistsAsync(long campusId, string name, long? excludingTeamId = null)
        {
            // Matches the ux_team_campus_name unique key, so the caller gets a readable
            // message instead of a duplicate-key exception.
            const string sql = @"
                SELECT COUNT(1) FROM team
                WHERE campus_id = @CampusId
                  AND name = @Name
                  AND (@Excluding IS NULL OR id <> @Excluding);";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteScalarAsync<int>(sql, new
            {
                CampusId = campusId,
                Name = name,
                Excluding = excludingTeamId
            }) > 0;
        }

        public async Task<IReadOnlyList<LeadCandidate>> ListLeadCandidatesAsync(long? campusId)
        {
            // TEAM_LEAD and PASTOR only. A volunteer cannot lead a team, and putting a
            // data-entry operator in the list would offer an assignment that hands
            // them every escalation raised on that team's people.
            //
            // DISTINCT because an account can hold both roles; the role shown is the
            // most senior, which is what MIN over this ordering gives.
            const string sql = @"
                SELECT
                    ua.public_id AS AccountId,
                    p.full_name  AS Name,
                    MIN(CASE ur.role_code WHEN 'PASTOR' THEN 'PASTOR'
                                          ELSE 'TEAM_LEAD' END) AS RoleCode,
                    c.name       AS CampusName,
                    (SELECT t.name FROM team t
                      WHERE t.lead_user_id = ua.id AND t.is_active = 1
                      ORDER BY t.name LIMIT 1) AS LeadsTeam
                FROM user_account ua
                JOIN user_role ur ON ur.user_account_id = ua.id
                JOIN person p     ON p.id = ua.person_id
                LEFT JOIN campus c ON c.id = p.campus_id
                WHERE ua.is_active = 1
                  AND p.deleted_at IS NULL
                  AND ur.role_code IN ('TEAM_LEAD', 'PASTOR')
                  AND (@CampusId IS NULL OR p.campus_id = @CampusId)
                GROUP BY ua.public_id, p.full_name, c.name, ua.id
                ORDER BY p.full_name;";

            using var connection = _dbFactory.GetConnection();

            return (await connection.QueryAsync<LeadCandidate>(
                sql, new { CampusId = campusId })).ToList();
        }

        public async Task<long?> ResolveLeadAccountIdAsync(string? accountPublicId)
        {
            if (string.IsNullOrWhiteSpace(accountPublicId)) return null;

            const string sql = @"SELECT id FROM user_account WHERE public_id = @PublicId AND is_active = 1 LIMIT 1;";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteScalarAsync<long?>(sql, new { PublicId = accountPublicId });
        }
    }
}
