using Dapper;
using RM_CMS.Data;
using RM_CMS.Modules.Huddle.Domain;

namespace RM_CMS.Modules.Huddle.Data
{
    /// <summary>
    /// The huddle agenda and the verdicts recorded against it.
    ///
    /// Every query is scoped by the team ids the caller leads, resolved upstream from
    /// their account. Nothing here trusts a caller-supplied team.
    /// </summary>
    public interface IHuddleRepository
    {
        /// <summary>Contacts in the window that still need a verdict.</summary>
        Task<IReadOnlyList<HuddleItem>> GetAgendaAsync(
            IReadOnlyList<long> teamIds, DateTime fromDate, DateTime toDate, int limit);

        /// <summary>How many unassessed contacts sit OLDER than the window.</summary>
        Task<int> CountOlderUnassessedAsync(IReadOnlyList<long> teamIds, DateTime beforeDate);

        Task<int> CountAssessedInWindowAsync(IReadOnlyList<long> teamIds, DateTime fromDate, DateTime toDate);

        /// <summary>
        /// Records verdicts. Returns how many rows were actually written — a caller
        /// that submits ten and gets eight back has had two rejected by the team scope,
        /// which is worth reporting rather than silently accepting.
        /// </summary>
        Task<int> SaveVerdictsAsync(
            IReadOnlyList<HuddleVerdict> verdicts, IReadOnlyList<long> teamIds,
            long assessedBy, DateTime nowUtc);

        /// <summary>
        /// Volunteers on these teams, with their person id, for the huddle reminder.
        /// </summary>
        Task<IReadOnlyList<(long UserAccountId, long PersonId, string Name)>> GetTeamMembersAsync(
            IReadOnlyList<long> teamIds);

        /// <summary>Every active team that has a lead, for the reminder sweep.</summary>
        Task<IReadOnlyList<(long TeamId, long LeadUserId, string TeamName)>> GetLedTeamsAsync();
    }

    public sealed class HuddleRepository : IHuddleRepository
    {
        private readonly IDbConnectionFactory _dbFactory;

        public HuddleRepository(IDbConnectionFactory dbFactory) => _dbFactory = dbFactory;

        /// <summary>
        /// Only COMPLETED contacts appear. A pending or missed one carries no judgement
        /// to assess — there was no conversation in which the volunteer could have
        /// decided anything.
        /// </summary>
        private const string AgendaScope = @"
            FROM care_interaction ci
            JOIN care_case cc   ON cc.id = ci.care_case_id
            JOIN person p       ON p.id = cc.person_id
            LEFT JOIN volunteer v ON v.id = ci.volunteer_id
            LEFT JOIN person vp ON vp.id = v.person_id
            WHERE cc.team_id IN @TeamIds
              AND ci.status = 'COMPLETED'";

        public async Task<IReadOnlyList<HuddleItem>> GetAgendaAsync(
            IReadOnlyList<long> teamIds, DateTime fromDate, DateTime toDate, int limit)
        {
            if (teamIds.Count == 0) return Array.Empty<HuddleItem>();

            // Written out in full rather than composed from AgendaScope: the agenda
            // needs two lookup joins for the labels that the COUNT queries do not,
            // and threading that difference through a shared fragment was less clear
            // than the small duplication of a FROM clause.
            const string sql = @"
            SELECT
                ci.public_id            AS InteractionId,
                cc.reference_code       AS CaseReference,
                v.public_id             AS VolunteerId,
                vp.full_name            AS VolunteerName,
                p.full_name             AS PersonName,
                ci.status               AS Status,
                ci.outcome_code         AS OutcomeCode,
                co.label                AS OutcomeLabel,
                ci.intent_code          AS IntentCode,
                vi.label                AS IntentLabel,
                ci.occurred_at          AS OccurredAt,
                ci.scheduled_on         AS ScheduledOn,
                ci.notes                AS Notes,
                ci.escalation_assessment AS EscalationAssessment,
                ci.assessment_note      AS AssessmentNote,
                EXISTS (SELECT 1 FROM escalation e
                         WHERE e.care_interaction_id = ci.id) AS RaisedEscalation
            FROM care_interaction ci
            JOIN care_case cc     ON cc.id = ci.care_case_id
            JOIN person p         ON p.id = cc.person_id
            LEFT JOIN volunteer v ON v.id = ci.volunteer_id
            LEFT JOIN person vp   ON vp.id = v.person_id
            LEFT JOIN care_outcome co   ON co.code = ci.outcome_code
            LEFT JOIN visitor_intent vi ON vi.code = ci.intent_code
            WHERE cc.team_id IN @TeamIds
              AND ci.status = 'COMPLETED'
              AND ci.escalation_assessment IS NULL
              AND ci.occurred_at >= @FromDate
              AND ci.occurred_at <  @ToDate
            -- Grouped by volunteer: the huddle works through one person's week at a
            -- time, which is also how the conversation runs in the room.
            ORDER BY vp.full_name, ci.occurred_at DESC
            LIMIT @Limit;";

            using var connection = _dbFactory.GetConnection();

            return (await connection.QueryAsync<HuddleItem>(sql, new
            {
                TeamIds = teamIds,
                FromDate = fromDate,
                ToDate = toDate,
                Limit = limit
            })).ToList();
        }

        public async Task<int> CountOlderUnassessedAsync(IReadOnlyList<long> teamIds, DateTime beforeDate)
        {
            if (teamIds.Count == 0) return 0;

            var sql = $@"
            SELECT COUNT(*)
            {AgendaScope}
              AND ci.escalation_assessment IS NULL
              AND ci.occurred_at < @BeforeDate;";

            using var connection = _dbFactory.GetConnection();

            return await connection.ExecuteScalarAsync<int>(
                sql, new { TeamIds = teamIds, BeforeDate = beforeDate });
        }

        public async Task<int> CountAssessedInWindowAsync(
            IReadOnlyList<long> teamIds, DateTime fromDate, DateTime toDate)
        {
            if (teamIds.Count == 0) return 0;

            var sql = $@"
            SELECT COUNT(*)
            {AgendaScope}
              AND ci.escalation_assessment IS NOT NULL
              AND ci.occurred_at >= @FromDate
              AND ci.occurred_at <  @ToDate;";

            using var connection = _dbFactory.GetConnection();

            return await connection.ExecuteScalarAsync<int>(
                sql, new { TeamIds = teamIds, FromDate = fromDate, ToDate = toDate });
        }

        public async Task<int> SaveVerdictsAsync(
            IReadOnlyList<HuddleVerdict> verdicts, IReadOnlyList<long> teamIds,
            long assessedBy, DateTime nowUtc)
        {
            if (verdicts.Count == 0 || teamIds.Count == 0) return 0;

            // The team scope is re-applied in the UPDATE itself, not just checked
            // beforehand: the ids arrive from a form, and a WHERE that trusts a prior
            // check is one refactor away from not having one.
            const string sql = @"
                UPDATE care_interaction ci
                JOIN care_case cc ON cc.id = ci.care_case_id
                SET ci.escalation_assessment = @Assessment,
                    ci.assessment_note       = @Note,
                    ci.assessed_by           = @AssessedBy,
                    ci.assessed_at           = @NowUtc,
                    ci.updated_by            = @AssessedBy
                WHERE ci.public_id = @InteractionId
                  AND cc.team_id IN @TeamIds
                  AND ci.status = 'COMPLETED';";

            using var connection = _dbFactory.GetConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                var written = 0;

                foreach (var v in verdicts)
                {
                    written += await connection.ExecuteAsync(sql, new
                    {
                        v.InteractionId,
                        Assessment = v.Assessment,
                        Note = Truncate(v.Note),
                        AssessedBy = assessedBy,
                        NowUtc = nowUtc,
                        TeamIds = teamIds
                    }, transaction);
                }

                transaction.Commit();
                return written;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public async Task<IReadOnlyList<(long, long, string)>> GetTeamMembersAsync(IReadOnlyList<long> teamIds)
        {
            if (teamIds.Count == 0) return Array.Empty<(long, long, string)>();

            const string sql = @"
                SELECT ua.id AS UserAccountId, p.id AS PersonId, p.full_name AS Name
                FROM volunteer v
                JOIN person p        ON p.id = v.person_id
                JOIN user_account ua ON ua.person_id = p.id AND ua.is_active = 1
                WHERE v.team_id IN @TeamIds
                  AND v.status = 'ACTIVE'
                  AND p.deleted_at IS NULL
                ORDER BY p.full_name;";

            using var connection = _dbFactory.GetConnection();

            var rows = await connection.QueryAsync<(long UserAccountId, long PersonId, string Name)>(
                sql, new { TeamIds = teamIds });

            return rows.ToList();
        }

        public async Task<IReadOnlyList<(long, long, string)>> GetLedTeamsAsync()
        {
            const string sql = @"
                SELECT t.id AS TeamId, t.lead_user_id AS LeadUserId, t.name AS TeamName
                FROM team t
                JOIN user_account ua ON ua.id = t.lead_user_id AND ua.is_active = 1
                WHERE t.is_active = 1 AND t.lead_user_id IS NOT NULL
                ORDER BY t.name;";

            using var connection = _dbFactory.GetConnection();

            var rows = await connection.QueryAsync<(long TeamId, long LeadUserId, string TeamName)>(sql);

            return rows.ToList();
        }

        /// <summary>assessment_note is VARCHAR(500); a longer note must not fail the write.</summary>
        private static string? Truncate(string? value) =>
            value is null || value.Length <= 500 ? value : value[..497] + "...";
    }
}
