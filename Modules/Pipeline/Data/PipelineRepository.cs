using Dapper;
using RM_CMS.Data;
using RM_CMS.Modules.Pipeline.Domain;

namespace RM_CMS.Modules.Pipeline.Data
{
    /// <summary>
    /// The people pipeline read model.
    ///
    /// SCOPE IS A PARAMETER, NOT A FILTER APPLIED LATER. Every query takes the team
    /// ids the caller may see, or null meaning organisation-wide, and the service
    /// decides which from the caller's role. Fetching everything and trimming it in
    /// C# would put other campuses' pastoral data one bug away from the response.
    /// </summary>
    public interface IPipelineRepository
    {
        Task<IReadOnlyList<PipelineRow>> SearchAsync(
            PipelineQuery query, IReadOnlyList<long>? teamIds, long? campusId, DateTime today);

        Task<int> CountAsync(PipelineQuery query, IReadOnlyList<long>? teamIds, long? campusId);

        Task<PipelineSummary> GetSummaryAsync(IReadOnlyList<long>? teamIds, long? campusId);
    }

    public sealed class PipelineRepository : IPipelineRepository
    {
        private readonly IDbConnectionFactory _dbFactory;

        public PipelineRepository(IDbConnectionFactory dbFactory) => _dbFactory = dbFactory;

        /// <summary>
        /// A person may have several cases over time; this takes their most recent, so
        /// the pipeline is one row per PERSON rather than one per case. Somebody who
        /// visited, lapsed and came back should appear once, at where they are now.
        /// </summary>
        private const string LatestCase = @"
            LEFT JOIN care_case cc ON cc.id = (
                SELECT c2.id FROM care_case c2
                 WHERE c2.person_id = p.id
                 ORDER BY (c2.status <> 'CLOSED') DESC, c2.opened_at DESC
                 LIMIT 1)";

        /// <summary>
        /// The scope clause. A team lead sees cases belonging to the teams they lead;
        /// a pastor sees their campus; an administrator sees everything.
        ///
        /// A person with NO case is visible only to the wider scopes — a team lead has
        /// no claim on somebody who was never assigned to their team.
        /// </summary>
        private static string ScopeClause(IReadOnlyList<long>? teamIds, long? campusId)
        {
            if (teamIds is not null)
                return " AND cc.team_id IN @TeamIds ";

            return campusId is not null
                ? " AND (cc.campus_id = @CampusId OR (cc.id IS NULL AND p.campus_id = @CampusId)) "
                : string.Empty;
        }

        private static string FilterClause(PipelineQuery q, IReadOnlyList<long>? teamIds)
        {
            var sql = string.Empty;

            if (!string.IsNullOrWhiteSpace(q.Search))
            {
                sql += @" AND (p.full_name LIKE @Search
                            OR cc.reference_code LIKE @Search
                            OR EXISTS (SELECT 1 FROM person_contact pc
                                        WHERE pc.person_id = p.id
                                          AND pc.normalized_value LIKE @Search)) ";
            }

            if (!string.IsNullOrWhiteSpace(q.Stage)) sql += " AND cc.stage = @Stage ";
            if (!string.IsNullOrWhiteSpace(q.Status)) sql += " AND cc.status = @Status ";

            // A team lead's list is inherently case-scoped, so "people with no case"
            // cannot apply to them.
            if (!q.IncludeUnstarted || teamIds is not null) sql += " AND cc.id IS NOT NULL ";

            return sql;
        }

        public async Task<IReadOnlyList<PipelineRow>> SearchAsync(
            PipelineQuery query, IReadOnlyList<long>? teamIds, long? campusId, DateTime today)
        {
            if (teamIds is { Count: 0 }) return Array.Empty<PipelineRow>();

            var sql = $@"
                SELECT
                    p.public_id           AS PersonId,
                    p.full_name           AS PersonName,
                    (SELECT pc.value FROM person_contact pc
                      WHERE pc.person_id = p.id AND pc.contact_type = 'MOBILE'
                      ORDER BY pc.is_primary DESC, pc.id LIMIT 1) AS Phone,

                    cc.public_id          AS CaseId,
                    cc.reference_code     AS CaseReference,
                    cc.stage              AS Stage,
                    cc.status             AS Status,

                    vp.full_name          AS VolunteerName,
                    t.name                AS TeamName,
                    cam.name              AS CampusName,

                    cc.current_step_number AS CurrentStepNumber,
                    (SELECT COUNT(*) FROM nurture_plan_step s
                      WHERE s.nurture_plan_id = cc.nurture_plan_id AND s.is_active = 1) AS PlanStepCount,
                    cc.next_step_due_on   AS NextStepDueOn,

                    cc.first_visit_on     AS FirstVisitOn,
                    cc.opened_at          AS OpenedAt,
                    cc.last_contact_at    AS LastContactAt,
                    cc.closed_at          AS ClosedAt,
                    cc.close_reason       AS CloseReason,
                    cc.contact_attempt_count AS ContactAttemptCount,

                    EXISTS (SELECT 1 FROM escalation e
                             WHERE e.care_case_id = cc.id
                               AND e.status NOT IN ('RESOLVED','CLOSED','REFERRED_OUT')) AS HasOpenEscalation,

                    DATEDIFF(@Today, COALESCE(cc.last_contact_at, cc.opened_at)) AS DaysSinceContact

                FROM person p
                {LatestCase}
                LEFT JOIN volunteer v  ON v.id = cc.assigned_volunteer_id
                LEFT JOIN person vp    ON vp.id = v.person_id
                LEFT JOIN team t       ON t.id = cc.team_id
                LEFT JOIN campus cam   ON cam.id = cc.campus_id
                WHERE p.deleted_at IS NULL
                {ScopeClause(teamIds, campusId)}
                {FilterClause(query, teamIds)}
                -- Open cases first, then the least recently touched: the top of this
                -- list should be whoever has been waiting longest, not whoever is
                -- alphabetically unlucky.
                ORDER BY (cc.status = 'CLOSED'), DaysSinceContact DESC, p.full_name
                LIMIT @Take OFFSET @Skip;";

            using var connection = _dbFactory.GetConnection();

            return (await connection.QueryAsync<PipelineRow>(sql, new
            {
                TeamIds = teamIds,
                CampusId = campusId,
                Search = $"%{query.Search}%",
                query.Stage,
                query.Status,
                query.Take,
                query.Skip,
                Today = today.Date
            })).ToList();
        }

        public async Task<int> CountAsync(PipelineQuery query, IReadOnlyList<long>? teamIds, long? campusId)
        {
            if (teamIds is { Count: 0 }) return 0;

            var sql = $@"
                SELECT COUNT(*)
                FROM person p
                {LatestCase}
                WHERE p.deleted_at IS NULL
                {ScopeClause(teamIds, campusId)}
                {FilterClause(query, teamIds)};";

            using var connection = _dbFactory.GetConnection();

            return await connection.ExecuteScalarAsync<int>(sql, new
            {
                TeamIds = teamIds,
                CampusId = campusId,
                Search = $"%{query.Search}%",
                query.Stage,
                query.Status
            });
        }

        public async Task<PipelineSummary> GetSummaryAsync(IReadOnlyList<long>? teamIds, long? campusId)
        {
            if (teamIds is { Count: 0 }) return new PipelineSummary();

            var sql = $@"
                SELECT
                    SUM(cc.stage = 'INTAKE'            AND cc.status <> 'CLOSED') AS Intake,
                    SUM(cc.stage = 'INITIAL_FOLLOW_UP' AND cc.status <> 'CLOSED') AS InitialFollowUp,
                    SUM(cc.stage = 'NURTURE'           AND cc.status <> 'CLOSED') AS Nurture,
                    SUM(cc.stage = 'REVIEW'            AND cc.status <> 'CLOSED') AS Review,
                    SUM(cc.status = 'CLOSED')                                     AS Closed,
                    SUM(cc.status = 'ESCALATED')                                  AS Escalated,
                    SUM(cc.id IS NULL)                                            AS NoCase,
                    COUNT(*)                                                      AS TotalPeople,
                    SUM(cc.close_reason = 'BECAME_MEMBER')                        AS BecameMembers
                FROM person p
                {LatestCase}
                WHERE p.deleted_at IS NULL
                {ScopeClause(teamIds, campusId)}
                {(teamIds is not null ? " AND cc.id IS NOT NULL " : string.Empty)};";

            using var connection = _dbFactory.GetConnection();

            return await connection.QueryFirstOrDefaultAsync<PipelineSummary>(
                sql, new { TeamIds = teamIds, CampusId = campusId }) ?? new PipelineSummary();
        }
    }
}
