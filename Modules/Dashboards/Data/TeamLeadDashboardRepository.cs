using Dapper;
using RM_CMS.Data;
using RM_CMS.Modules.Dashboards.Domain;

namespace RM_CMS.Modules.Dashboards.Data
{
    /// <summary>
    /// The team lead dashboard's read model.
    ///
    /// These are purpose-built aggregate queries rather than calls into the Care and
    /// Volunteers repositories. A dashboard wants five counts, not five lists it then
    /// counts in memory — fetching every open case to call <c>.Count()</c> on it is how
    /// a page that is fine with ten cases falls over with a thousand. The schema is
    /// indexed for exactly this shape (<c>ix_care_case_team (team_id, status)</c>).
    ///
    /// Every method takes the team ids the caller may see. The service resolves those
    /// from the signed-in account; nothing here trusts a caller-supplied team.
    /// </summary>
    public interface ITeamLeadDashboardRepository
    {
        /// <summary>Teams led by this account. The authorization boundary for the page.</summary>
        Task<IReadOnlyList<TeamSummary>> GetLedTeamsAsync(long leadUserAccountId);

        /// <summary>Internal team ids for a lead, used to scope every other query.</summary>
        Task<IReadOnlyList<long>> GetLedTeamIdsAsync(long leadUserAccountId);

        Task<EscalationSummary> GetEscalationsAsync(long leadUserAccountId, IReadOnlyList<long> teamIds,
                                                    DateTime nowUtc, int urgentLimit);

        Task<CaseSummary> GetCasesAsync(IReadOnlyList<long> teamIds, DateTime nowUtc, int reviewLimit);

        Task<ContactSummary> GetContactsAsync(IReadOnlyList<long> teamIds, DateTime nowUtc, int graceDays);

        /// <summary>Cases actively in a nurture plan, and how far each has reached.</summary>
        Task<NurtureSummary> GetNurtureAsync(IReadOnlyList<long> teamIds, DateTime today, int limit);

        /// <summary>
        /// Each active member with their load and their completion trend over the
        /// three most recent complete weeks.
        /// </summary>
        Task<IReadOnlyList<VolunteerLoad>> GetVolunteerLoadAsync(
            IReadOnlyList<long> teamIds, DateTime weekStart);
    }

    public sealed class TeamLeadDashboardRepository : ITeamLeadDashboardRepository
    {
        private readonly IDbConnectionFactory _dbFactory;

        public TeamLeadDashboardRepository(IDbConnectionFactory dbFactory) => _dbFactory = dbFactory;

        public async Task<IReadOnlyList<TeamSummary>> GetLedTeamsAsync(long leadUserAccountId)
        {
            // Member count is a correlated subquery for the same reason TeamRepository
            // uses one: a maintained counter is one more thing to drift.
            const string sql = @"
                SELECT
                    t.public_id   AS PublicId,
                    t.name        AS Name,
                    c.name        AS CampusName,
                    t.max_members AS MaxMembers,
                    (SELECT COUNT(*) FROM volunteer v
                      WHERE v.team_id = t.id AND v.status = 'ACTIVE') AS MemberCount
                FROM team t
                JOIN campus c ON c.id = t.campus_id
                WHERE t.lead_user_id = @LeadUserAccountId
                  AND t.is_active = 1
                ORDER BY t.name;";

            using var connection = _dbFactory.GetConnection();

            return (await connection.QueryAsync<TeamSummary>(
                sql, new { LeadUserAccountId = leadUserAccountId })).ToList();
        }

        public async Task<IReadOnlyList<long>> GetLedTeamIdsAsync(long leadUserAccountId)
        {
            const string sql = @"
                SELECT id FROM team
                WHERE lead_user_id = @LeadUserAccountId AND is_active = 1;";

            using var connection = _dbFactory.GetConnection();

            return (await connection.QueryAsync<long>(
                sql, new { LeadUserAccountId = leadUserAccountId })).ToList();
        }

        /// <summary>
        /// Escalations this lead owes an answer to.
        ///
        /// Two routes reach them: an escalation assigned to them directly, or one on a
        /// case belonging to a team they lead. The second matters because the chase-up
        /// job falls back to "every team lead at the campus" when an escalation has no
        /// assignee — those would otherwise be invisible on the page that is supposed
        /// to surface them.
        /// </summary>
        public async Task<EscalationSummary> GetEscalationsAsync(
            long leadUserAccountId, IReadOnlyList<long> teamIds, DateTime nowUtc, int urgentLimit)
        {
            var scope = @"
                (e.assigned_to_user_id = @LeadUserAccountId
                 OR (@HasTeams = 1 AND cc.team_id IN @TeamIds))
                AND e.status NOT IN ('RESOLVED','CLOSED','REFERRED_OUT')";

            var countSql = $@"
                SELECT
                    COUNT(*)                                                     AS Open,
                    SUM(CASE WHEN e.acknowledged_at IS NULL THEN 1 ELSE 0 END)   AS Unacknowledged,
                    MIN(CASE WHEN e.acknowledged_at IS NULL THEN e.raised_at END) AS OldestUnacknowledgedAt
                FROM escalation e
                JOIN care_case cc ON cc.id = e.care_case_id
                WHERE {scope};";

            var listSql = $@"
                SELECT
                    e.public_id       AS PublicId,
                    e.reference_code  AS ReferenceCode,
                    p.full_name       AS PersonName,
                    e.reason_code     AS ReasonCode,
                    er.label          AS ReasonLabel,
                    er.requires_protocol AS RequiresProtocol,
                    e.tier            AS Tier,
                    e.status          AS Status,
                    e.raised_at       AS RaisedAt,
                    e.acknowledged_at AS AcknowledgedAt,
                    rvp.full_name     AS RaisedByName
                FROM escalation e
                JOIN care_case cc       ON cc.id = e.care_case_id
                JOIN person p           ON p.id = cc.person_id
                JOIN escalation_reason er ON er.code = e.reason_code
                LEFT JOIN volunteer rv  ON rv.id = e.raised_by_volunteer_id
                LEFT JOIN person rvp    ON rvp.id = rv.person_id
                WHERE {scope}
                -- Unacknowledged first, then by tier, then oldest. This is the order a
                -- lead should work the list in.
                ORDER BY
                    (e.acknowledged_at IS NULL) DESC,
                    FIELD(e.tier,'EMERGENCY','URGENT','STANDARD'),
                    e.raised_at
                LIMIT @UrgentLimit;";

            // Dapper cannot expand an empty IN list, so a lead with no teams gets a
            // guard flag and a dummy value rather than invalid SQL.
            var parameters = new
            {
                LeadUserAccountId = leadUserAccountId,
                TeamIds = teamIds.Count > 0 ? teamIds : new List<long> { 0 },
                HasTeams = teamIds.Count > 0 ? 1 : 0,
                UrgentLimit = urgentLimit
            };

            using var connection = _dbFactory.GetConnection();

            var counts = await connection.QueryFirstOrDefaultAsync<EscalationCountRow>(countSql, parameters);
            var urgent = (await connection.QueryAsync<EscalationItem>(listSql, parameters)).ToList();

            foreach (var item in urgent)
            {
                item.WaitingHours = Math.Round((nowUtc - item.RaisedAt).TotalHours, 1);
            }

            return new EscalationSummary
            {
                Open = counts?.Open ?? 0,
                Unacknowledged = counts?.Unacknowledged ?? 0,
                OldestUnacknowledgedHours = counts?.OldestUnacknowledgedAt is { } oldest
                    ? Math.Round((nowUtc - oldest).TotalHours, 1)
                    : null,
                Urgent = urgent
            };
        }

        private sealed class EscalationCountRow
        {
            public int Open { get; set; }
            public int Unacknowledged { get; set; }
            public DateTime? OldestUnacknowledgedAt { get; set; }
        }

        public async Task<CaseSummary> GetCasesAsync(
            IReadOnlyList<long> teamIds, DateTime nowUtc, int reviewLimit)
        {
            if (teamIds.Count == 0) return new CaseSummary();

            // One pass over the team's open cases, bucketed. Four separate COUNT queries
            // would read the same index four times to answer one question.
            const string countSql = @"
                SELECT
                    SUM(CASE WHEN status = 'AWAITING_ASSIGNMENT' THEN 1 ELSE 0 END) AS AwaitingAssignment,
                    SUM(CASE WHEN status = 'IN_PROGRESS'         THEN 1 ELSE 0 END) AS InProgress,
                    SUM(CASE WHEN status = 'ESCALATED'           THEN 1 ELSE 0 END) AS Escalated,
                    SUM(CASE WHEN stage  = 'REVIEW'
                              AND status <> 'CLOSED'             THEN 1 ELSE 0 END) AS AwaitingReview
                FROM care_case
                WHERE team_id IN @TeamIds
                  AND status <> 'CLOSED';";

            const string reviewSql = @"
                SELECT
                    cc.public_id             AS PublicId,
                    cc.reference_code        AS ReferenceCode,
                    p.full_name              AS PersonName,
                    vp.full_name             AS VolunteerName,
                    cc.awaiting_review_since AS AwaitingReviewSince
                FROM care_case cc
                JOIN person p          ON p.id = cc.person_id
                LEFT JOIN volunteer v  ON v.id = cc.assigned_volunteer_id
                LEFT JOIN person vp    ON vp.id = v.person_id
                WHERE cc.team_id IN @TeamIds
                  AND cc.stage = 'REVIEW'
                  AND cc.status <> 'CLOSED'
                ORDER BY cc.awaiting_review_since IS NULL, cc.awaiting_review_since
                LIMIT @ReviewLimit;";

            using var connection = _dbFactory.GetConnection();

            var summary = await connection.QueryFirstOrDefaultAsync<CaseSummary>(
                countSql, new { TeamIds = teamIds }) ?? new CaseSummary();

            var reviews = (await connection.QueryAsync<ReviewItem>(
                reviewSql, new { TeamIds = teamIds, ReviewLimit = reviewLimit })).ToList();

            foreach (var review in reviews)
            {
                review.WaitingDays = review.AwaitingReviewSince is { } since
                    ? Math.Round((nowUtc - since).TotalDays, 1)
                    : null;
            }

            summary.AwaitingReviewItems = reviews;
            return summary;
        }

        /// <summary>
        /// Follow-ups owed. "Overdue" uses the same grace period as the mark-overdue
        /// job, passed in by the service, so the page and the job cannot disagree about
        /// which contacts are late.
        /// </summary>
        public async Task<ContactSummary> GetContactsAsync(
            IReadOnlyList<long> teamIds, DateTime nowUtc, int graceDays)
        {
            if (teamIds.Count == 0) return new ContactSummary();

            const string sql = @"
                SELECT
                    SUM(CASE WHEN ci.status = 'PENDING'
                              AND ci.scheduled_on = @Today            THEN 1 ELSE 0 END) AS DueToday,
                    SUM(CASE WHEN ci.status = 'PENDING'
                              AND ci.scheduled_on < @OverdueCutoff    THEN 1 ELSE 0 END) AS Overdue,
                    SUM(CASE WHEN ci.status = 'MISSED'
                              AND ci.updated_at >= @SevenDaysAgo      THEN 1 ELSE 0 END) AS MissedLast7Days
                FROM care_interaction ci
                JOIN care_case cc ON cc.id = ci.care_case_id
                WHERE cc.team_id IN @TeamIds;";

            using var connection = _dbFactory.GetConnection();

            return await connection.QueryFirstOrDefaultAsync<ContactSummary>(sql, new
            {
                TeamIds = teamIds,
                Today = nowUtc.Date,
                OverdueCutoff = nowUtc.Date.AddDays(-graceDays),
                SevenDaysAgo = nowUtc.AddDays(-7)
            }) ?? new ContactSummary();
        }

        /// <summary>
        /// Each active team member and what they are carrying.
        ///
        /// ACTIVE only, matching the member count on <see cref="GetLedTeamsAsync"/> —
        /// a page that says "2 members" above three rows is a page nobody trusts. It is
        /// also the right list on its own terms: this answers "who can take another
        /// one?", and somebody on leave cannot.
        /// </summary>
        /// <summary>
        /// The team's live nurture journeys.
        ///
        /// A case paused by an escalation is still IN the plan — it just is not
        /// moving — so it is counted and marked rather than hidden. A lead looking at
        /// "6 active" needs to know that two of them are stuck behind a concern they
        /// have not yet resolved.
        /// </summary>
        public async Task<NurtureSummary> GetNurtureAsync(
            IReadOnlyList<long> teamIds, DateTime today, int limit)
        {
            if (teamIds.Count == 0) return new NurtureSummary();

            const string countSql = @"
                SELECT
                    COUNT(*)                                                   AS Active,
                    SUM(cc.next_step_due_on IS NOT NULL
                        AND cc.next_step_due_on < @Today)                      AS Overdue,
                    SUM(cc.status = 'ESCALATED')                               AS Paused
                FROM care_case cc
                WHERE cc.team_id IN @TeamIds
                  AND cc.stage = 'NURTURE'
                  AND cc.status <> 'CLOSED';";

            const string listSql = @"
                SELECT
                    cc.public_id          AS CaseId,
                    cc.reference_code     AS CaseReference,
                    p.full_name           AS PersonName,
                    vp.full_name          AS VolunteerName,
                    cc.current_step_number AS CurrentStepNumber,
                    (SELECT COUNT(*) FROM nurture_plan_step s
                      WHERE s.nurture_plan_id = cc.nurture_plan_id AND s.is_active = 1) AS PlanStepCount,
                    cc.next_step_due_on   AS NextStepDueOn,
                    (cc.status = 'ESCALATED') AS IsPaused,
                    CASE WHEN cc.next_step_due_on IS NOT NULL AND cc.next_step_due_on < @Today
                         THEN DATEDIFF(@Today, cc.next_step_due_on) END AS DaysOverdue
                FROM care_case cc
                JOIN person p          ON p.id = cc.person_id
                LEFT JOIN volunteer v  ON v.id = cc.assigned_volunteer_id
                LEFT JOIN person vp    ON vp.id = v.person_id
                WHERE cc.team_id IN @TeamIds
                  AND cc.stage = 'NURTURE'
                  AND cc.status <> 'CLOSED'
                -- Most overdue first: the point of the card is to surface the journeys
                -- that have quietly stalled, not to list them alphabetically.
                ORDER BY cc.next_step_due_on IS NULL, cc.next_step_due_on
                LIMIT @Limit;";

            using var connection = _dbFactory.GetConnection();

            var summary = await connection.QueryFirstOrDefaultAsync<NurtureSummary>(
                countSql, new { TeamIds = teamIds, Today = today.Date }) ?? new NurtureSummary();

            summary.Items = (await connection.QueryAsync<NurtureItem>(
                listSql, new { TeamIds = teamIds, Today = today.Date, Limit = limit })).ToList();

            return summary;
        }

        public async Task<IReadOnlyList<VolunteerLoad>> GetVolunteerLoadAsync(
            IReadOnlyList<long> teamIds, DateTime weekStart)
        {
            if (teamIds.Count == 0) return Array.Empty<VolunteerLoad>();

            const string sql = @"
                SELECT
                    v.public_id           AS PublicId,
                    v.reference_code      AS ReferenceCode,
                    p.full_name           AS Name,
                    v.status              AS Status,
                    v.capacity_band_code  AS CapacityBandCode,
                    cb.label              AS CapacityBandLabel,
                    v.current_case_load   AS CurrentCaseLoad,
                    cb.max_per_week       AS CapacityMaxPerWeek,
                    (SELECT COUNT(*) FROM escalation e
                      WHERE e.raised_by_volunteer_id = v.id
                        AND e.status NOT IN ('RESOLVED','CLOSED','REFERRED_OUT')) AS OpenEscalations,

                    -- Completion over the three most recent COMPLETE weeks. Counts,
                    -- not percentages: a week with nothing scheduled is a week with
                    -- no information, and the difference between that and 0% is the
                    -- difference between a quiet rota and a struggling volunteer.
                    --
                    -- 'Done' is a contact the volunteer actually closed out — logged
                    -- as COMPLETED, or one that raised an escalation. Both are the
                    -- volunteer doing their job with what they heard. PENDING and
                    -- MISSED sit in the denominator only, which is the whole point.
                    (SELECT COUNT(*) FROM care_interaction ci
                      WHERE ci.volunteer_id = v.id
                        AND ci.scheduled_on >= @Week1Start AND ci.scheduled_on < @Week1End) AS Week1Total,
                    (SELECT COUNT(*) FROM care_interaction ci
                      WHERE ci.volunteer_id = v.id
                        AND ci.scheduled_on >= @Week1Start AND ci.scheduled_on < @Week1End
                        AND (ci.status = 'COMPLETED'
                             OR EXISTS (SELECT 1 FROM escalation e2
                                         WHERE e2.care_interaction_id = ci.id))) AS Week1Done,

                    (SELECT COUNT(*) FROM care_interaction ci
                      WHERE ci.volunteer_id = v.id
                        AND ci.scheduled_on >= @Week2Start AND ci.scheduled_on < @Week1Start) AS Week2Total,
                    (SELECT COUNT(*) FROM care_interaction ci
                      WHERE ci.volunteer_id = v.id
                        AND ci.scheduled_on >= @Week2Start AND ci.scheduled_on < @Week1Start
                        AND (ci.status = 'COMPLETED'
                             OR EXISTS (SELECT 1 FROM escalation e2
                                         WHERE e2.care_interaction_id = ci.id))) AS Week2Done,

                    (SELECT COUNT(*) FROM care_interaction ci
                      WHERE ci.volunteer_id = v.id
                        AND ci.scheduled_on >= @Week3Start AND ci.scheduled_on < @Week2Start) AS Week3Total,
                    (SELECT COUNT(*) FROM care_interaction ci
                      WHERE ci.volunteer_id = v.id
                        AND ci.scheduled_on >= @Week3Start AND ci.scheduled_on < @Week2Start
                        AND (ci.status = 'COMPLETED'
                             OR EXISTS (SELECT 1 FROM escalation e2
                                         WHERE e2.care_interaction_id = ci.id))) AS Week3Done
                FROM volunteer v
                JOIN person p        ON p.id = v.person_id
                JOIN capacity_band cb ON cb.code = v.capacity_band_code
                WHERE v.team_id IN @TeamIds
                  AND v.status = 'ACTIVE'
                  AND p.deleted_at IS NULL
                -- Busiest first: the lead is looking for who to take work off, and the
                -- people with spare capacity are the easy half of that question.
                --
                -- CAST TO SIGNED IS LOAD-BEARING. Both columns are UNSIGNED, so when a
                -- volunteer is OVER capacity the subtraction underflows and MySQL
                -- raises 'BIGINT UNSIGNED value is out of range' — the whole dashboard
                -- 500s. Over capacity is not a hypothetical: a team can exceed its
                -- ceiling through manual assignment or a band being lowered, and the
                -- lead looking at that team is exactly who needs this page to load.
                ORDER BY (CAST(cb.max_per_week AS SIGNED) - CAST(v.current_case_load AS SIGNED)),
                         p.full_name;";

            using var connection = _dbFactory.GetConnection();

            // weekStart is the Monday of the CURRENT week, so week 1 is the last
            // complete one. Counting a half-finished week would show every volunteer
            // falling every Monday morning.
            return (await connection.QueryAsync<VolunteerLoad>(sql, new
            {
                TeamIds = teamIds,
                Week1End = weekStart,
                Week1Start = weekStart.AddDays(-7),
                Week2Start = weekStart.AddDays(-14),
                Week3Start = weekStart.AddDays(-21)
            })).ToList();
        }
    }
}
