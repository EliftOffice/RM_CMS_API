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

        /// <summary>
        /// The visitor's profile, or null when they do not exist, are staff, are
        /// soft-deleted, or fall outside the caller's scope.
        ///
        /// The scope goes into the SQL, exactly as it does for the list. A detail
        /// endpoint that fetched first and checked afterwards would be a way to read
        /// any person's pastoral record by guessing an id.
        /// </summary>
        Task<(long Id, VisitorProfile Profile)?> GetVisitorAsync(
            string personPublicId, IReadOnlyList<long>? teamIds, long? campusId);

        Task<IReadOnlyList<JourneyCase>> GetCasesAsync(long personId);
        Task<IReadOnlyList<JourneyEvent>> GetEventsAsync(long personId);
        Task<JourneyStats> GetStatsAsync(long personId, DateTime today);
    }

    public sealed class PipelineRepository : IPipelineRepository
    {
        private readonly IDbConnectionFactory _dbFactory;

        public PipelineRepository(IDbConnectionFactory dbFactory) => _dbFactory = dbFactory;

        /// <summary>
        /// The pipeline is about VISITORS — how somebody moves from first walking in to
        /// being cared for. Staff are <c>person</c> rows too, so without this every
        /// volunteer, team lead, pastor and intake operator appeared in the funnel as a
        /// person who had "not started", which is meaningless: they are not on a journey,
        /// they are running it. On the live data that was 54 of 178 rows.
        ///
        /// Staff is defined exactly as <c>CampusRepository</c> defines it — a login OR a
        /// volunteer record — so "visitors" means the same number on both screens. A
        /// volunteer with no login still counts as staff; that is the whole point of the
        /// second clause.
        ///
        /// Someone who becomes staff while still holding an open case drops off this
        /// screen. Their case is NOT lost: it stays on the team lead dashboard and the
        /// assignment screens, which read <c>care_case</c> rather than <c>person</c>.
        /// </summary>
        private const string VisitorsOnly = @"
            AND NOT EXISTS (SELECT 1 FROM user_account ua WHERE ua.person_id = p.id)
            AND NOT EXISTS (SELECT 1 FROM volunteer   sv WHERE sv.person_id = p.id) ";

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
                {VisitorsOnly}
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
                {VisitorsOnly}
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
                {VisitorsOnly}
                {ScopeClause(teamIds, campusId)}
                {(teamIds is not null ? " AND cc.id IS NOT NULL " : string.Empty)};";

            using var connection = _dbFactory.GetConnection();

            return await connection.QueryFirstOrDefaultAsync<PipelineSummary>(
                sql, new { TeamIds = teamIds, CampusId = campusId }) ?? new PipelineSummary();
        }

        // ==================================================================
        // One visitor's whole journey
        // ==================================================================

        public async Task<(long Id, VisitorProfile Profile)?> GetVisitorAsync(
            string personPublicId, IReadOnlyList<long>? teamIds, long? campusId)
        {
            if (teamIds is { Count: 0 }) return null;

            // The scope test mirrors the list's: a team lead may open somebody only if
            // one of their teams has held a case for them. EXISTS rather than a join,
            // so a person with three cases does not come back three times.
            var scope = teamIds is not null
                ? @" AND EXISTS (SELECT 1 FROM care_case sc
                                  WHERE sc.person_id = p.id AND sc.team_id IN @TeamIds) "
                : campusId is not null
                    ? @" AND (p.campus_id = @CampusId
                             OR EXISTS (SELECT 1 FROM care_case sc
                                         WHERE sc.person_id = p.id AND sc.campus_id = @CampusId)) "
                    : string.Empty;

            var sql = $@"
                SELECT
                    -- CAST because person.id is BIGINT UNSIGNED: the driver hands back a
                    -- UInt64, and Dapper's multi-map cannot narrow that to the long this
                    -- splits into. The ordinary object mapper copes, the primitive one
                    -- does not, so the cast belongs here rather than in C#.
                    CAST(p.id AS SIGNED)  AS Id,
                    p.public_id           AS PersonId,
                    p.reference_code      AS ReferenceCode,
                    p.full_name           AS FullName,
                    p.age_band            AS AgeBand,
                    p.gender              AS Gender,
                    p.household_type      AS HouseholdType,
                    (SELECT pc.value FROM person_contact pc
                      WHERE pc.person_id = p.id AND pc.contact_type = 'MOBILE'
                      ORDER BY pc.is_primary DESC, pc.id LIMIT 1)  AS Mobile,
                    (SELECT pc.value FROM person_contact pc
                      WHERE pc.person_id = p.id AND pc.contact_type = 'EMAIL'
                      ORDER BY pc.is_primary DESC, pc.id LIMIT 1)  AS Email,
                    EXISTS (SELECT 1 FROM person_contact pc
                             WHERE pc.person_id = p.id
                               AND pc.contact_type = 'TELEGRAM'
                               AND pc.is_verified = 1
                               AND pc.opted_out_at IS NULL)        AS HasTelegram,
                    p.address_line        AS AddressLine,
                    ar.name               AS AreaName,
                    p.locality            AS Locality,
                    p.postal_code         AS PostalCode,
                    p.is_local            AS IsLocal,
                    cam.name              AS CampusName,
                    p.lifecycle_status    AS LifecycleStatus,
                    p.became_member_on    AS BecameMemberOn,
                    p.do_not_contact      AS DoNotContact,
                    p.do_not_contact_at   AS DoNotContactAt,
                    p.do_not_contact_note AS DoNotContactNote,
                    p.notes               AS Notes,
                    p.created_at          AS RecordedAt,
                    rb.full_name          AS RecordedBy
                FROM person p
                LEFT JOIN area   ar  ON ar.id  = p.area_id
                LEFT JOIN campus cam ON cam.id = p.campus_id
                LEFT JOIN user_account rua ON rua.id = p.created_by
                LEFT JOIN person rb  ON rb.id  = rua.person_id
                WHERE p.public_id = @PublicId
                  AND p.deleted_at IS NULL
                  {VisitorsOnly}
                  {scope}
                LIMIT 1;";

            using var connection = _dbFactory.GetConnection();

            // Split-mapped rather than read into a subclass of VisitorProfile: that
            // model is sealed like every other DTO here, and unsealing a domain type to
            // suit one query is the wrong trade. The internal id is the first column and
            // PersonId begins the profile, so splitOn cuts exactly there.
            var rows = await connection.QueryAsync<long, VisitorProfile, (long Id, VisitorProfile Profile)>(
                sql,
                (id, profile) => (id, profile),
                new { PublicId = personPublicId, TeamIds = teamIds, CampusId = campusId },
                splitOn: "PersonId");

            var row = rows.FirstOrDefault();

            return row.Profile is null ? null : row;
        }

        public async Task<IReadOnlyList<JourneyCase>> GetCasesAsync(long personId)
        {
            const string sql = @"
                SELECT
                    cc.public_id           AS CaseId,
                    cc.reference_code      AS ReferenceCode,
                    cc.stage               AS Stage,
                    cc.status              AS Status,
                    cc.priority            AS Priority,
                    vp.full_name           AS VolunteerName,
                    t.name                 AS TeamName,
                    cc.current_step_number AS CurrentStepNumber,
                    (SELECT COUNT(*) FROM nurture_plan_step s
                      WHERE s.nurture_plan_id = cc.nurture_plan_id AND s.is_active = 1) AS PlanStepCount,
                    np.name                AS PlanName,
                    cc.next_step_due_on    AS NextStepDueOn,
                    cc.first_visit_on      AS FirstVisitOn,
                    cc.opened_at           AS OpenedAt,
                    cc.first_contact_at    AS FirstContactAt,
                    cc.last_contact_at     AS LastContactAt,
                    cc.closed_at           AS ClosedAt,
                    cc.close_reason        AS CloseReason,
                    co.label               AS CloseReasonLabel,
                    cc.contact_attempt_count AS ContactAttemptCount
                FROM care_case cc
                LEFT JOIN volunteer v      ON v.id = cc.assigned_volunteer_id
                LEFT JOIN person vp        ON vp.id = v.person_id
                LEFT JOIN team t           ON t.id = cc.team_id
                LEFT JOIN nurture_plan np  ON np.id = cc.nurture_plan_id
                LEFT JOIN care_outcome co  ON co.code = cc.close_reason
                WHERE cc.person_id = @PersonId
                ORDER BY cc.opened_at DESC;";

            using var connection = _dbFactory.GetConnection();
            return (await connection.QueryAsync<JourneyCase>(sql, new { PersonId = personId })).ToList();
        }

        public async Task<JourneyStats> GetStatsAsync(long personId, DateTime today)
        {
            const string sql = @"
                SELECT
                    (SELECT COUNT(*) FROM care_case cc
                      WHERE cc.person_id = @PersonId)                        AS CaseCount,
                    (SELECT COUNT(*) FROM care_interaction i
                      JOIN care_case cc ON cc.id = i.care_case_id
                      WHERE cc.person_id = @PersonId
                        AND i.status = 'COMPLETED')                          AS ContactsLogged,
                    -- Same rule as the timeline: the outcome decides, the flag is only
                    -- the fallback. Counting these two differently is how a header that
                    -- reads '14 of 14 reached' ends up above a list showing 'No answer'.
                    (SELECT COUNT(*) FROM care_interaction i
                      JOIN care_case cc ON cc.id = i.care_case_id
                      LEFT JOIN care_outcome o ON o.code = i.outcome_code
                      WHERE cc.person_id = @PersonId
                        AND i.status = 'COMPLETED'
                        AND COALESCE(o.contact_made, i.made_contact) = 1)    AS ContactsMade,
                    (SELECT COUNT(*) FROM care_interaction i
                      JOIN care_case cc ON cc.id = i.care_case_id
                      WHERE cc.person_id = @PersonId
                        AND i.status = 'MISSED')                             AS ContactsMissed,
                    (SELECT COUNT(*) FROM escalation e
                      JOIN care_case cc ON cc.id = e.care_case_id
                      WHERE cc.person_id = @PersonId)                        AS EscalationsRaised,
                    (SELECT COUNT(*) FROM escalation e
                      JOIN care_case cc ON cc.id = e.care_case_id
                      WHERE cc.person_id = @PersonId
                        AND e.status NOT IN ('RESOLVED','CLOSED','REFERRED_OUT')) AS OpenEscalations,
                    (SELECT MIN(COALESCE(cc.first_visit_on, DATE(cc.opened_at)))
                       FROM care_case cc WHERE cc.person_id = @PersonId)     AS FirstSeenOn,
                    (SELECT MAX(cc.last_contact_at)
                       FROM care_case cc WHERE cc.person_id = @PersonId)     AS LastContactAt;";

            using var connection = _dbFactory.GetConnection();

            var stats = await connection.QueryFirstOrDefaultAsync<JourneyStats>(
                sql, new { PersonId = personId }) ?? new JourneyStats();

            if (stats.LastContactAt is not null)
                stats.DaysSinceContact = (int)(today.Date - stats.LastContactAt.Value.Date).TotalDays;

            if (stats.FirstSeenOn is not null)
                stats.DaysInJourney = (int)(today.Date - stats.FirstSeenOn.Value.Date).TotalDays;

            return stats;
        }

        /// <summary>
        /// The timeline, assembled from five sources.
        ///
        /// Deliberately separate queries merged in C# rather than one UNION: the sources
        /// have genuinely different shapes, and a UNION would need every column padded
        /// with NULLs in four of the five branches — which is where labels get mismatched
        /// and nobody notices, because the result still renders. The volume here is one
        /// person's history, not a page of a table.
        /// </summary>
        public async Task<IReadOnlyList<JourneyEvent>> GetEventsAsync(long personId)
        {
            using var connection = _dbFactory.GetConnection();

            var events = new List<JourneyEvent>();
            var args = new { PersonId = personId };

            // ---- cases opened and closed ----
            const string caseSql = @"
                SELECT cc.reference_code AS CaseRef, cc.opened_at AS OpenedAt,
                       cc.closed_at AS ClosedAt, cc.close_reason AS CloseReason,
                       co.label AS CloseLabel, cc.close_notes AS CloseNotes,
                       cc.priority AS Priority, ob.full_name AS OpenedBy
                FROM care_case cc
                LEFT JOIN care_outcome co  ON co.code = cc.close_reason
                LEFT JOIN user_account oua ON oua.id = cc.created_by
                LEFT JOIN person ob        ON ob.id = oua.person_id
                WHERE cc.person_id = @PersonId;";

            foreach (var c in await connection.QueryAsync<CaseEventRow>(caseSql, args))
            {
                events.Add(new JourneyEvent
                {
                    OccurredAt = c.OpenedAt,
                    Kind = "CASE_OPENED",
                    Title = "Follow-up opened",
                    Detail = string.IsNullOrEmpty(c.Priority) || c.Priority == "NORMAL"
                        ? null
                        : $"Priority: {Pretty(c.Priority)}",
                    Actor = c.OpenedBy,
                    CaseReference = c.CaseRef,
                    Tone = "NEUTRAL"
                });

                if (c.ClosedAt is not null)
                {
                    events.Add(new JourneyEvent
                    {
                        OccurredAt = c.ClosedAt.Value,
                        Kind = "CASE_CLOSED",
                        Title = "Follow-up closed" +
                                (string.IsNullOrEmpty(c.CloseLabel) ? string.Empty : $" — {c.CloseLabel}"),
                        Detail = c.CloseNotes,
                        CaseReference = c.CaseRef,
                        Tone = c.CloseReason == "BECAME_MEMBER" ? "POSITIVE" : "NEUTRAL"
                    });
                }
            }

            // ---- contacts ----
            const string contactSql = @"
                SELECT cc.reference_code AS CaseRef, i.occurred_at AS OccurredAt,
                       i.scheduled_on AS ScheduledOn, i.status AS Status,
                       -- The OUTCOME decides whether they were reached, not the
                       -- made_contact flag. 43 rows in the live data disagree with each
                       -- other (a migration artifact), and care_outcome.contact_made is
                       -- the definition of what the outcome means: NO_ANSWER cannot be a
                       -- conversation. Taking the flag produced 'Spoke with them' tagged
                       -- 'No answer' on the same line. Falls back to the flag when no
                       -- outcome was recorded.
                       COALESCE(o.contact_made, i.made_contact) AS MadeContact,
                       i.notes AS Notes,
                       i.duration_minutes AS DurationMinutes,
                       cm.label AS MethodLabel, o.label AS OutcomeLabel, vi.label AS IntentLabel,
                       vp.full_name AS VolunteerName
                FROM care_interaction i
                JOIN care_case cc            ON cc.id = i.care_case_id
                LEFT JOIN contact_method cm  ON cm.code = i.method_code
                LEFT JOIN care_outcome o     ON o.code = i.outcome_code
                LEFT JOIN visitor_intent vi  ON vi.code = i.intent_code
                LEFT JOIN volunteer v        ON v.id = i.volunteer_id
                LEFT JOIN person vp          ON vp.id = v.person_id
                WHERE cc.person_id = @PersonId
                  AND i.status IN ('COMPLETED','MISSED');";

            foreach (var i in await connection.QueryAsync<ContactEventRow>(contactSql, args))
            {
                var missed = i.Status == "MISSED";
                var made = i.MadeContact == true;

                var tags = new List<string>();
                if (!string.IsNullOrEmpty(i.MethodLabel)) tags.Add(i.MethodLabel!);
                if (!string.IsNullOrEmpty(i.OutcomeLabel)) tags.Add(i.OutcomeLabel!);
                if (!string.IsNullOrEmpty(i.IntentLabel)) tags.Add(i.IntentLabel!);
                if (i.DurationMinutes is > 0) tags.Add($"{i.DurationMinutes} min");

                events.Add(new JourneyEvent
                {
                    // A missed contact has no occurred_at — it never happened. Its place
                    // on the timeline is the day it was due.
                    OccurredAt = i.OccurredAt ?? i.ScheduledOn ?? default,
                    Kind = "CONTACT",
                    Title = missed ? "Contact missed" : made ? "Spoke with them" : "Tried to reach them",
                    Detail = i.Notes,
                    Actor = i.VolunteerName,
                    CaseReference = i.CaseRef,
                    Tone = made ? "POSITIVE" : "NEGATIVE",
                    Tags = tags
                });
            }

            // ---- escalations ----
            const string escalationSql = @"
                SELECT cc.reference_code AS CaseRef, e.raised_at AS RaisedAt,
                       e.resolved_at AS ResolvedAt, e.tier AS Tier,
                       e.description AS Description, e.resolution_notes AS ResolutionNotes,
                       er.label AS ReasonLabel, eo.label AS OutcomeLabel,
                       rb.full_name AS RaisedBy, ab.full_name AS AssignedTo
                FROM escalation e
                JOIN care_case cc                ON cc.id = e.care_case_id
                LEFT JOIN escalation_reason er   ON er.code = e.reason_code
                LEFT JOIN escalation_outcome eo  ON eo.code = e.outcome_code
                LEFT JOIN volunteer rv           ON rv.id = e.raised_by_volunteer_id
                LEFT JOIN person rb              ON rb.id = rv.person_id
                LEFT JOIN user_account aua       ON aua.id = e.assigned_to_user_id
                LEFT JOIN person ab              ON ab.id = aua.person_id
                WHERE cc.person_id = @PersonId;";

            foreach (var e in await connection.QueryAsync<EscalationEventRow>(escalationSql, args))
            {
                var tags = new List<string>();
                if (!string.IsNullOrEmpty(e.Tier)) tags.Add(Pretty(e.Tier!));
                if (!string.IsNullOrEmpty(e.ReasonLabel)) tags.Add(e.ReasonLabel!);

                events.Add(new JourneyEvent
                {
                    OccurredAt = e.RaisedAt,
                    Kind = "ESCALATION",
                    Title = "Concern raised" +
                            (string.IsNullOrEmpty(e.ReasonLabel) ? string.Empty : $" — {e.ReasonLabel}"),
                    Detail = e.Description,
                    Actor = e.RaisedBy,
                    CaseReference = e.CaseRef,
                    Tone = "ALERT",
                    Tags = tags
                });

                if (e.ResolvedAt is not null)
                {
                    events.Add(new JourneyEvent
                    {
                        OccurredAt = e.ResolvedAt.Value,
                        Kind = "ESCALATION",
                        Title = "Concern resolved" +
                                (string.IsNullOrEmpty(e.OutcomeLabel) ? string.Empty : $" — {e.OutcomeLabel}"),
                        Detail = e.ResolutionNotes,
                        Actor = e.AssignedTo,
                        CaseReference = e.CaseRef,
                        Tone = "POSITIVE"
                    });
                }
            }

            // ---- assignments ----
            const string assignmentSql = @"
                SELECT cc.reference_code AS CaseRef, a.assigned_at AS AssignedAt,
                       a.reason AS Reason, vp.full_name AS VolunteerName,
                       ab.full_name AS AssignedBy
                FROM care_case_assignment a
                JOIN care_case cc          ON cc.id = a.care_case_id
                LEFT JOIN volunteer v      ON v.id = a.volunteer_id
                LEFT JOIN person vp        ON vp.id = v.person_id
                LEFT JOIN user_account aua ON aua.id = a.assigned_by
                LEFT JOIN person ab        ON ab.id = aua.person_id
                WHERE cc.person_id = @PersonId;";

            foreach (var a in await connection.QueryAsync<AssignmentEventRow>(assignmentSql, args))
            {
                events.Add(new JourneyEvent
                {
                    OccurredAt = a.AssignedAt,
                    Kind = "ASSIGNMENT",
                    Title = string.IsNullOrEmpty(a.VolunteerName)
                        ? "Assigned"
                        : $"Assigned to {a.VolunteerName}",
                    Detail = string.IsNullOrEmpty(a.Reason) ? null : Pretty(a.Reason!),
                    Actor = a.AssignedBy,
                    CaseReference = a.CaseRef,
                    Tone = "NEUTRAL"
                });
            }

            // ---- notes ----
            // Both the notes written against a CASE and those written against the
            // PERSON. Person-level notes are the ones that outlive any single case —
            // "prefers to be called in the evening" belongs to them, not to a case that
            // closed a year ago — so a history that showed only case notes would drop
            // exactly the notes worth keeping.
            //
            // Private notes are excluded from both. They are written by one person for
            // their own use, and surfacing them on a shared history screen would change
            // what "private" meant after the fact.
            const string noteSql = @"
                SELECT n.created_at AS CreatedAt, n.body AS Body, nt.label AS TypeLabel,
                       cb.full_name AS CreatedByName, cc.reference_code AS CaseRef
                FROM note n
                JOIN care_case cc          ON cc.id = n.entity_id AND n.entity_type = 'CARE_CASE'
                LEFT JOIN note_type nt     ON nt.code = n.note_type_code
                LEFT JOIN user_account cua ON cua.id = n.created_by
                LEFT JOIN person cb        ON cb.id = cua.person_id
                WHERE cc.person_id = @PersonId
                  AND n.is_private = 0

                UNION ALL

                SELECT n.created_at, n.body, nt.label,
                       cb.full_name, NULL
                FROM note n
                LEFT JOIN note_type nt     ON nt.code = n.note_type_code
                LEFT JOIN user_account cua ON cua.id = n.created_by
                LEFT JOIN person cb        ON cb.id = cua.person_id
                WHERE n.entity_type = 'PERSON'
                  AND n.entity_id = @PersonId
                  AND n.is_private = 0;";

            foreach (var n in await connection.QueryAsync<NoteEventRow>(noteSql, args))
            {
                events.Add(new JourneyEvent
                {
                    OccurredAt = n.CreatedAt,
                    Kind = "NOTE",
                    Title = string.IsNullOrEmpty(n.TypeLabel) ? "Note" : $"Note — {n.TypeLabel}",
                    Detail = n.Body,
                    Actor = n.CreatedByName,
                    CaseReference = n.CaseRef,
                    Tone = "NEUTRAL"
                });
            }

            // Newest first: the most recent thing that happened is what somebody opening
            // this record wants to read first.
            return events.OrderByDescending(e => e.OccurredAt).ToList();
        }

        // Row shapes for the timeline queries. Explicit classes rather than `dynamic`,
        // so a renamed column is a compile-time or mapping failure rather than a
        // silently null property that renders as a blank line.
        private sealed class CaseEventRow
        {
            public string? CaseRef { get; set; }
            public DateTime OpenedAt { get; set; }
            public DateTime? ClosedAt { get; set; }
            public string? CloseReason { get; set; }
            public string? CloseLabel { get; set; }
            public string? CloseNotes { get; set; }
            public string? Priority { get; set; }
            public string? OpenedBy { get; set; }
        }

        private sealed class ContactEventRow
        {
            public string? CaseRef { get; set; }
            public DateTime? OccurredAt { get; set; }
            public DateTime? ScheduledOn { get; set; }
            public string? Status { get; set; }
            public bool? MadeContact { get; set; }
            public string? Notes { get; set; }
            public int? DurationMinutes { get; set; }
            public string? MethodLabel { get; set; }
            public string? OutcomeLabel { get; set; }
            public string? IntentLabel { get; set; }
            public string? VolunteerName { get; set; }
        }

        private sealed class EscalationEventRow
        {
            public string? CaseRef { get; set; }
            public DateTime RaisedAt { get; set; }
            public DateTime? ResolvedAt { get; set; }
            public string? Tier { get; set; }
            public string? Description { get; set; }
            public string? ResolutionNotes { get; set; }
            public string? ReasonLabel { get; set; }
            public string? OutcomeLabel { get; set; }
            public string? RaisedBy { get; set; }
            public string? AssignedTo { get; set; }
        }

        private sealed class AssignmentEventRow
        {
            public string? CaseRef { get; set; }
            public DateTime AssignedAt { get; set; }
            public string? Reason { get; set; }
            public string? VolunteerName { get; set; }
            public string? AssignedBy { get; set; }
        }

        private sealed class NoteEventRow
        {
            public string? CaseRef { get; set; }
            public DateTime CreatedAt { get; set; }
            public string? Body { get; set; }
            public string? TypeLabel { get; set; }
            public string? CreatedByName { get; set; }
        }

        /// <summary>SCHEDULE_RETRY -> "Schedule retry". Codes are not for reading.</summary>
        private static string Pretty(string code) =>
            string.IsNullOrEmpty(code)
                ? code
                : char.ToUpperInvariant(code[0]) + code[1..].ToLowerInvariant().Replace('_', ' ');
    }
}
