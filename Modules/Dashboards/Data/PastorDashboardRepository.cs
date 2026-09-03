using Dapper;
using RM_CMS.Data;
using RM_CMS.Modules.Dashboards.Domain;

namespace RM_CMS.Modules.Dashboards.Data
{
    /// <summary>
    /// The pastor dashboard's read model.
    ///
    /// Deliberately small. Cases, contacts, nurture and volunteer load are already
    /// purpose-built aggregates on <see cref="ITeamLeadDashboardRepository"/> that
    /// take a team-id list — a pastor's scope is just a longer one, so the service
    /// calls those directly rather than this class re-deriving the same SQL. What
    /// lives here is the three things only a pastor needs: escalations that were
    /// actually alerted UP to pastor level (not every escalation on every team),
    /// a per-team breakdown for the leaderboard, and per-team huddle compliance.
    /// </summary>
    public interface IPastorDashboardRepository
    {
        /// <summary>
        /// Escalations that reached pastor level — <c>pastor_alerted_at</c> is set —
        /// and are still open. Deliberately narrower than "every open escalation on
        /// every team in scope": that total already lives on <see cref="TeamHealthRow"/>,
        /// and flooding the pastor's own list with everything a lead is still within
        /// their own window to answer would bury the ones that actually escalated
        /// past them.
        /// </summary>
        Task<EscalationSummary> GetPastorAlertedEscalationsAsync(
            IReadOnlyList<long> teamIds, DateTime nowUtc, int urgentLimit);

        /// <summary>One row per team, for the leaderboard a pastor scans to find who needs them.</summary>
        Task<IReadOnlyList<TeamHealthRow>> GetTeamHealthAsync(IReadOnlyList<long> teamIds, DateTime nowUtc);

        /// <summary>
        /// One row per team: how much of the huddle backlog each lead has actually
        /// cleared. Uses the exact same definition of "assessed" / "waiting" /
        /// "older backlog" as <c>HuddleRepository</c> itself — a completed contact,
        /// windowed on when it OCCURRED, not when someone got around to judging it.
        /// </summary>
        Task<IReadOnlyList<HuddleComplianceRow>> GetHuddleComplianceAsync(
            IReadOnlyList<long> teamIds, DateTime fromDate, DateTime toDate);
    }

    public sealed class PastorDashboardRepository : IPastorDashboardRepository
    {
        private readonly IDbConnectionFactory _dbFactory;

        public PastorDashboardRepository(IDbConnectionFactory dbFactory) => _dbFactory = dbFactory;

        public async Task<EscalationSummary> GetPastorAlertedEscalationsAsync(
            IReadOnlyList<long> teamIds, DateTime nowUtc, int urgentLimit)
        {
            if (teamIds.Count == 0) return new EscalationSummary();

            const string countSql = @"
                SELECT
                    COUNT(*)                                                     AS Open,
                    SUM(CASE WHEN e.acknowledged_at IS NULL THEN 1 ELSE 0 END)   AS Unacknowledged,
                    MIN(CASE WHEN e.acknowledged_at IS NULL THEN e.raised_at END) AS OldestUnacknowledgedAt
                FROM escalation e
                JOIN care_case cc ON cc.id = e.care_case_id
                WHERE cc.team_id IN @TeamIds
                  AND e.pastor_alerted_at IS NOT NULL
                  AND e.status NOT IN ('RESOLVED','CLOSED','REFERRED_OUT');";

            const string listSql = @"
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
                WHERE cc.team_id IN @TeamIds
                  AND e.pastor_alerted_at IS NOT NULL
                  AND e.status NOT IN ('RESOLVED','CLOSED','REFERRED_OUT')
                -- Unacknowledged first, then longest-alerted: the ones a pastor has
                -- sat on longest belong at the top of their own list too.
                ORDER BY (e.acknowledged_at IS NULL) DESC, e.pastor_alerted_at
                LIMIT @UrgentLimit;";

            var parameters = new { TeamIds = teamIds, UrgentLimit = urgentLimit };

            using var connection = _dbFactory.GetConnection();

            var counts = await connection.QueryFirstOrDefaultAsync<EscalationCountRow>(countSql, parameters);
            var urgent = (await connection.QueryAsync<EscalationItem>(listSql, parameters)).ToList();

            foreach (var item in urgent)
                item.WaitingHours = Math.Round((nowUtc - item.RaisedAt).TotalHours, 1);

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

        public async Task<IReadOnlyList<TeamHealthRow>> GetTeamHealthAsync(
            IReadOnlyList<long> teamIds, DateTime nowUtc)
        {
            if (teamIds.Count == 0) return Array.Empty<TeamHealthRow>();

            // Kept as one row shape (InternalId carried alongside, never returned) so
            // the three passes below can be joined back together by a plain Dictionary
            // keyed on the INTERNAL id — team_id on escalation and care_case is the
            // internal id, never the public one, so resolving through the public id
            // would be pure overhead.
            const string teamsSql = @"
                SELECT
                    t.id           AS InternalId,
                    t.public_id    AS TeamId,
                    t.name         AS TeamName,
                    lp.full_name   AS LeadName,
                    (SELECT COUNT(*) FROM volunteer v
                      WHERE v.team_id = t.id AND v.status = 'ACTIVE') AS MemberCount
                FROM team t
                LEFT JOIN user_account lua ON lua.id = t.lead_user_id
                LEFT JOIN person lp        ON lp.id = lua.person_id
                WHERE t.id IN @TeamIds
                ORDER BY t.name;";

            const string escSql = @"
                SELECT
                    cc.team_id AS InternalId,
                    COUNT(*)   AS OpenEscalations,
                    SUM(CASE WHEN e.acknowledged_at IS NULL THEN 1 ELSE 0 END) AS UnacknowledgedEscalations
                FROM escalation e
                JOIN care_case cc ON cc.id = e.care_case_id
                WHERE cc.team_id IN @TeamIds
                  AND e.status NOT IN ('RESOLVED','CLOSED','REFERRED_OUT')
                GROUP BY cc.team_id;";

            // Two independent facts, two independent queries — deliberately NOT one
            // query joining care_case to care_interaction. A case with several
            // interactions would fan out into several rows under that join, and a
            // case-level flag (review stage) summed across those rows would count
            // that ONE case once per interaction it happens to have. Overdue contacts
            // is genuinely a COUNT of interaction rows; awaiting-review is a count of
            // cases. They do not share a FROM clause.
            const string overdueSql = @"
                SELECT
                    cc.team_id AS InternalId,
                    COUNT(*)   AS OverdueContacts
                FROM care_interaction ci
                JOIN care_case cc ON cc.id = ci.care_case_id
                WHERE cc.team_id IN @TeamIds
                  AND ci.status = 'PENDING'
                  AND ci.scheduled_on < @Today
                GROUP BY cc.team_id;";

            const string reviewSql = @"
                SELECT
                    cc.team_id AS InternalId,
                    COUNT(*)   AS AwaitingReviewCases
                FROM care_case cc
                WHERE cc.team_id IN @TeamIds
                  AND cc.stage = 'REVIEW'
                  AND cc.status <> 'CLOSED'
                GROUP BY cc.team_id;";

            var parameters = new { TeamIds = teamIds, Today = nowUtc.Date };

            using var connection = _dbFactory.GetConnection();

            var rows = (await connection.QueryAsync<TeamHealthInternalRow>(teamsSql, new { TeamIds = teamIds }))
                .ToDictionary(t => t.InternalId);

            var escRows = await connection.QueryAsync<TeamHealthEscRow>(escSql, parameters);

            foreach (var row in escRows)
            {
                if (rows.TryGetValue(row.InternalId, out var team))
                {
                    team.OpenEscalations = row.OpenEscalations;
                    team.UnacknowledgedEscalations = row.UnacknowledgedEscalations;
                }
            }

            var overdueRows = await connection.QueryAsync<(long InternalId, int OverdueContacts)>(overdueSql, parameters);

            foreach (var row in overdueRows)
            {
                if (rows.TryGetValue(row.InternalId, out var team))
                    team.OverdueContacts = row.OverdueContacts;
            }

            var reviewRows = await connection.QueryAsync<(long InternalId, int AwaitingReviewCases)>(reviewSql, parameters);

            foreach (var row in reviewRows)
            {
                if (rows.TryGetValue(row.InternalId, out var team))
                    team.AwaitingReviewCases = row.AwaitingReviewCases;
            }

            return rows.Values
                .OrderBy(t => t.TeamName, StringComparer.OrdinalIgnoreCase)
                .Select(t => new TeamHealthRow
                {
                    TeamId = t.TeamId,
                    TeamName = t.TeamName,
                    LeadName = t.LeadName,
                    MemberCount = t.MemberCount,
                    OpenEscalations = t.OpenEscalations,
                    UnacknowledgedEscalations = t.UnacknowledgedEscalations,
                    OverdueContacts = t.OverdueContacts,
                    AwaitingReviewCases = t.AwaitingReviewCases
                    // AtRiskVolunteers is filled in by the service, which already
                    // holds the full VolunteerLoad list fetched for its own card and
                    // would otherwise be counted twice.
                })
                .ToList();
        }

        private sealed class TeamHealthInternalRow
        {
            public long InternalId { get; set; }
            public string TeamId { get; set; } = string.Empty;
            public string TeamName { get; set; } = string.Empty;
            public string? LeadName { get; set; }
            public int MemberCount { get; set; }
            public int OpenEscalations { get; set; }
            public int UnacknowledgedEscalations { get; set; }
            public int OverdueContacts { get; set; }
            public int AwaitingReviewCases { get; set; }
        }

        private sealed class TeamHealthEscRow
        {
            public long InternalId { get; set; }
            public int OpenEscalations { get; set; }
            public int UnacknowledgedEscalations { get; set; }
        }


        public async Task<IReadOnlyList<HuddleComplianceRow>> GetHuddleComplianceAsync(
            IReadOnlyList<long> teamIds, DateTime fromDate, DateTime toDate)
        {
            if (teamIds.Count == 0) return Array.Empty<HuddleComplianceRow>();

            // Same base scope as HuddleRepository's own agenda: a COMPLETED contact,
            // windowed on occurred_at. "Assessed" / "waiting" is exactly the split
            // HuddleService uses to build a lead's own agenda — a pastor reading
            // "waiting" here should mean the same thing a lead sees on their screen.
            const string sql = @"
                SELECT
                    t.public_id  AS TeamId,
                    t.name       AS TeamName,
                    lp.full_name AS LeadName,

                    (SELECT COUNT(*) FROM care_interaction ci
                      JOIN care_case cc2 ON cc2.id = ci.care_case_id
                      WHERE cc2.team_id = t.id
                        AND ci.status = 'COMPLETED'
                        AND ci.escalation_assessment IS NOT NULL
                        AND ci.occurred_at >= @FromDate AND ci.occurred_at < @ToDate) AS AssessedThisWindow,

                    (SELECT COUNT(*) FROM care_interaction ci
                      JOIN care_case cc2 ON cc2.id = ci.care_case_id
                      WHERE cc2.team_id = t.id
                        AND ci.status = 'COMPLETED'
                        AND ci.escalation_assessment IS NULL
                        AND ci.occurred_at >= @FromDate AND ci.occurred_at < @ToDate) AS WaitingThisWindow,

                    (SELECT COUNT(*) FROM care_interaction ci
                      JOIN care_case cc2 ON cc2.id = ci.care_case_id
                      WHERE cc2.team_id = t.id
                        AND ci.status = 'COMPLETED'
                        AND ci.escalation_assessment IS NULL
                        AND ci.occurred_at < @FromDate) AS OlderBacklog

                FROM team t
                LEFT JOIN user_account lua ON lua.id = t.lead_user_id
                LEFT JOIN person lp        ON lp.id = lua.person_id
                WHERE t.id IN @TeamIds
                ORDER BY t.name;";

            using var connection = _dbFactory.GetConnection();

            return (await connection.QueryAsync<HuddleComplianceRow>(sql, new
            {
                TeamIds = teamIds,
                FromDate = fromDate,
                ToDate = toDate
            })).ToList();
        }
    }
}
