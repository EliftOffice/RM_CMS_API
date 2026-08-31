using System.Data;
using Dapper;
using RM_CMS.Data;
using RM_CMS.Modules.Care.Domain;

namespace RM_CMS.Modules.Care.Data
{
    /// <summary>
    /// Data access for <c>care_case</c> and <c>care_case_assignment</c>.
    ///
    /// This repository owns the volunteer case-load counters. They are MAINTAINED,
    /// not derived, and every method that changes who holds a case updates them in
    /// the same transaction. The MVP kept the same figure in a counter AND derived
    /// it from another table, and the two drifted apart.
    /// </summary>
    public interface ICareCaseRepository
    {
        Task<CareCase?> GetByPublicIdAsync(string publicId);
        Task<CareCase?> GetByIdAsync(long id);

        Task<IReadOnlyList<CareCase>> SearchAsync(CaseQuery query);
        Task<int> CountAsync(CaseQuery query);

        /// <summary>Open cases with no volunteer — the queue the assignment job drains.</summary>
        Task<IReadOnlyList<CareCase>> FindUnassignedAsync(long? campusId, int limit);

        /// <summary>Cases whose next nurture step has fallen due and are not paused.</summary>
        Task<IReadOnlyList<CareCase>> FindDueForNextStepAsync(DateTime onOrBefore, int limit);

        Task<long> OpenAsync(CareCase careCase, long? actingUserId);

        /// <summary>
        /// Assigns (or reassigns) a case. Adjusts both volunteers' counters and
        /// writes the assignment history in one transaction.
        /// </summary>
        Task<bool> AssignAsync(long caseId, int rowVersion, long volunteerId, long? teamId,
                               string reason, DateTime nowUtc, long? actingUserId);

        Task<bool> UpdateStateAsync(CareCase careCase, long? actingUserId);

        /// <summary>Closes a case and releases the volunteer's capacity.</summary>
        Task<bool> CloseAsync(long caseId, int rowVersion, string closeReason, string? closeNotes,
                              DateTime nowUtc, long? actingUserId);

        Task<IReadOnlyList<CaseAssignment>> GetAssignmentHistoryAsync(long caseId);

        Task<long?> ResolvePersonIdAsync(string personPublicId);
        Task<long?> ResolveVolunteerIdAsync(string volunteerPublicId);
        Task<long?> ResolveCampusIdAsync(string? campusPublicId);

        /// <summary>
        /// The campus a person belongs to. A case is opened at the PERSON's campus,
        /// never the operator's — the case campus decides which volunteers are
        /// eligible for it, so taking it from whoever happened to be at the keyboard
        /// would hand a visitor to a volunteer at another site.
        /// </summary>
        Task<long?> GetPersonCampusIdAsync(long personId);

        /// <summary>Public id of a campus, for the caller-scope check.</summary>
        Task<string?> GetCampusPublicIdAsync(long campusId);

        /// <summary>An open case for this person, if one exists. Prevents duplicates.</summary>
        Task<CareCase?> FindOpenCaseForPersonAsync(long personId);

        /// <summary>
        /// Records a do-not-contact request on the PERSON. Called when an interaction
        /// captures an intent flagged implies_do_not_contact, so the block outlives
        /// this particular case.
        /// </summary>
        Task SetPersonDoNotContactAsync(long personId, string note, DateTime nowUtc, long? actingUserId);

        /// <summary>True when this person has asked not to be contacted.</summary>
        Task<bool> IsPersonDoNotContactAsync(long personId);

        Task<long> AddNoteAsync(CareNote note, long? actingUserId);
        Task<IReadOnlyList<CareNote>> GetNotesAsync(string entityType, long entityId, bool includePrivate);
    }

    public sealed class CaseQuery
    {
        public string? Search { get; init; }
        public string? Stage { get; init; }
        public string? Status { get; init; }
        public long? CampusId { get; init; }
        public long? VolunteerId { get; init; }
        public long? TeamId { get; init; }
        public bool? Unassigned { get; init; }
        public int Skip { get; init; }
        public int Take { get; init; } = 25;
    }

    public sealed class CareCaseRepository : ICareCaseRepository
    {
        private readonly IDbConnectionFactory _dbFactory;

        public CareCaseRepository(IDbConnectionFactory dbFactory) => _dbFactory = dbFactory;

        private const string SelectCase = @"
            SELECT
                cc.id                    AS Id,
                cc.public_id             AS PublicId,
                cc.reference_code        AS ReferenceCode,

                cc.person_id             AS PersonId,
                p.public_id              AS PersonPublicId,
                p.full_name              AS PersonName,
                (SELECT pc.value FROM person_contact pc
                  WHERE pc.person_id = p.id AND pc.contact_type = 'MOBILE'
                  ORDER BY pc.is_primary DESC, pc.id LIMIT 1) AS PersonPhone,
                p.do_not_contact         AS PersonDoNotContact,

                cc.campus_id             AS CampusId,
                cam.public_id            AS CampusPublicId,
                cam.name                 AS CampusName,

                cc.assigned_volunteer_id AS AssignedVolunteerId,
                v.public_id              AS AssignedVolunteerPublicId,
                vp.full_name             AS AssignedVolunteerName,

                cc.team_id               AS TeamId,
                t.name                   AS TeamName,

                cc.stage                 AS Stage,
                cc.status                AS Status,
                cc.priority              AS Priority,
                cc.visit_type            AS VisitType,
                cc.connection_source_code AS ConnectionSourceCode,
                cc.first_visit_on        AS FirstVisitOn,

                cc.nurture_plan_id       AS NurturePlanId,
                cc.current_step_number   AS CurrentStepNumber,
                cc.next_step_due_on      AS NextStepDueOn,
                cc.awaiting_review_since AS AwaitingReviewSince,

                cc.opened_at             AS OpenedAt,
                cc.assigned_at           AS AssignedAt,
                cc.first_contact_at      AS FirstContactAt,
                cc.last_contact_at       AS LastContactAt,
                cc.next_action_on        AS NextActionOn,
                cc.closed_at             AS ClosedAt,
                cc.close_reason          AS CloseReason,
                cc.close_notes           AS CloseNotes,

                cc.contact_attempt_count  AS ContactAttemptCount,
                cc.consecutive_no_contact AS ConsecutiveNoContact,

                cc.created_at            AS CreatedAt,
                cc.row_version           AS RowVersion
            FROM care_case cc
            JOIN person p             ON p.id  = cc.person_id
            JOIN campus cam           ON cam.id = cc.campus_id
            LEFT JOIN volunteer v     ON v.id  = cc.assigned_volunteer_id
            LEFT JOIN person vp       ON vp.id = v.person_id
            LEFT JOIN team t          ON t.id  = cc.team_id";

        // ------------------------------------------------------------------
        // Reads
        // ------------------------------------------------------------------

        public async Task<CareCase?> GetByPublicIdAsync(string publicId)
        {
            const string sql = SelectCase + @" WHERE cc.public_id = @PublicId LIMIT 1;";

            using var connection = _dbFactory.GetConnection();
            return await connection.QueryFirstOrDefaultAsync<CareCase>(sql, new { PublicId = publicId });
        }

        public async Task<CareCase?> GetByIdAsync(long id)
        {
            const string sql = SelectCase + @" WHERE cc.id = @Id LIMIT 1;";

            using var connection = _dbFactory.GetConnection();
            return await connection.QueryFirstOrDefaultAsync<CareCase>(sql, new { Id = id });
        }

        public async Task<IReadOnlyList<CareCase>> SearchAsync(CaseQuery query)
        {
            var sql = SelectCase + WhereClause() + @"
            ORDER BY cc.opened_at DESC
            LIMIT @Take OFFSET @Skip;";

            using var connection = _dbFactory.GetConnection();
            return (await connection.QueryAsync<CareCase>(sql, Parameters(query))).ToList();
        }

        public async Task<int> CountAsync(CaseQuery query)
        {
            var sql = @"
                SELECT COUNT(1)
                FROM care_case cc
                JOIN person p         ON p.id  = cc.person_id
                JOIN campus cam       ON cam.id = cc.campus_id
                LEFT JOIN volunteer v ON v.id  = cc.assigned_volunteer_id
                LEFT JOIN person vp   ON vp.id = v.person_id
                LEFT JOIN team t      ON t.id  = cc.team_id" + WhereClause() + ";";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteScalarAsync<int>(sql, Parameters(query));
        }

        private static string WhereClause() => @"
            WHERE (@Search IS NULL
                   OR p.full_name       LIKE CONCAT('%', @Search, '%')
                   OR cc.reference_code LIKE CONCAT('%', @Search, '%'))
              AND (@Stage      IS NULL OR cc.stage  = @Stage)
              AND (@Status     IS NULL OR cc.status = @Status)
              AND (@CampusId   IS NULL OR cc.campus_id = @CampusId)
              AND (@VolunteerId IS NULL OR cc.assigned_volunteer_id = @VolunteerId)
              AND (@TeamId     IS NULL OR cc.team_id = @TeamId)
              AND (@Unassigned IS NULL
                   OR (@Unassigned = 1 AND cc.assigned_volunteer_id IS NULL)
                   OR (@Unassigned = 0 AND cc.assigned_volunteer_id IS NOT NULL))";

        private static object Parameters(CaseQuery q) => new
        {
            Search = string.IsNullOrWhiteSpace(q.Search) ? null : q.Search.Trim(),
            q.Stage,
            q.Status,
            q.CampusId,
            q.VolunteerId,
            q.TeamId,
            q.Unassigned,
            q.Skip,
            q.Take
        };

        public async Task<IReadOnlyList<CareCase>> FindUnassignedAsync(long? campusId, int limit)
        {
            const string sql = SelectCase + @"
            WHERE cc.assigned_volunteer_id IS NULL
              AND cc.status <> 'CLOSED'
              AND p.do_not_contact = 0
              AND (@CampusId IS NULL OR cc.campus_id = @CampusId)
            ORDER BY cc.priority = 'URGENT' DESC, cc.priority = 'HIGH' DESC, cc.opened_at ASC
            LIMIT @Limit;";

            using var connection = _dbFactory.GetConnection();
            return (await connection.QueryAsync<CareCase>(sql, new { CampusId = campusId, Limit = limit })).ToList();
        }

        public async Task<IReadOnlyList<CareCase>> FindDueForNextStepAsync(DateTime onOrBefore, int limit)
        {
            // An open escalation pauses the case, so ESCALATED is excluded: the
            // scheduler must not create the next contact while a concern is unresolved.
            const string sql = SelectCase + @"
            WHERE cc.status NOT IN ('CLOSED', 'ESCALATED', 'ON_HOLD')
              AND cc.stage = 'NURTURE'
              AND cc.next_step_due_on IS NOT NULL
              AND cc.next_step_due_on <= @OnOrBefore
              AND p.do_not_contact = 0
              AND NOT EXISTS (SELECT 1 FROM care_interaction ci
                               WHERE ci.care_case_id = cc.id AND ci.status = 'PENDING')
            ORDER BY cc.next_step_due_on ASC
            LIMIT @Limit;";

            using var connection = _dbFactory.GetConnection();
            return (await connection.QueryAsync<CareCase>(sql, new { OnOrBefore = onOrBefore.Date, Limit = limit })).ToList();
        }

        public async Task<CareCase?> FindOpenCaseForPersonAsync(long personId)
        {
            const string sql = SelectCase + @"
            WHERE cc.person_id = @PersonId AND cc.status <> 'CLOSED'
            ORDER BY cc.opened_at DESC LIMIT 1;";

            using var connection = _dbFactory.GetConnection();
            return await connection.QueryFirstOrDefaultAsync<CareCase>(sql, new { PersonId = personId });
        }

        // ------------------------------------------------------------------
        // Writes
        // ------------------------------------------------------------------

        public async Task<long> OpenAsync(CareCase careCase, long? actingUserId)
        {
            const string insert = @"
                INSERT INTO care_case
                    (public_id, reference_code, person_id, campus_id, stage, status, priority,
                     visit_type, connection_source_code, first_visit_on, nurture_plan_id,
                     created_by, updated_by)
                VALUES
                    (@PublicId, @ReferenceCode, @PersonId, @CampusId, @Stage, @Status, @Priority,
                     @VisitType, @ConnectionSourceCode, @FirstVisitOn, @NurturePlanId,
                     @ActingUserId, @ActingUserId);
                SELECT LAST_INSERT_ID();";

            using var connection = _dbFactory.GetConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                careCase.ReferenceCode = await NextReferenceCodeAsync(connection, transaction);

                var id = await connection.ExecuteScalarAsync<long>(insert, new
                {
                    careCase.PublicId,
                    careCase.ReferenceCode,
                    careCase.PersonId,
                    careCase.CampusId,
                    careCase.Stage,
                    careCase.Status,
                    careCase.Priority,
                    careCase.VisitType,
                    careCase.ConnectionSourceCode,
                    careCase.FirstVisitOn,
                    careCase.NurturePlanId,
                    ActingUserId = actingUserId
                }, transaction);

                transaction.Commit();
                return id;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public async Task<bool> AssignAsync(
            long caseId, int rowVersion, long volunteerId, long? teamId,
            string reason, DateTime nowUtc, long? actingUserId)
        {
            const string readCurrent = @"
                SELECT assigned_volunteer_id FROM care_case WHERE id = @Id FOR UPDATE;";

            const string updateCase = @"
                UPDATE care_case
                SET assigned_volunteer_id = @VolunteerId,
                    team_id     = COALESCE(@TeamId, team_id),
                    assigned_at = @NowUtc,
                    status      = CASE WHEN status = 'AWAITING_ASSIGNMENT' THEN 'IN_PROGRESS' ELSE status END,
                    stage       = CASE WHEN stage  = 'INTAKE' THEN 'INITIAL_FOLLOW_UP' ELSE stage END,
                    updated_by  = @ActingUserId,
                    row_version = row_version + 1
                WHERE id = @Id AND row_version = @RowVersion;";

            const string closePreviousAssignment = @"
                UPDATE care_case_assignment
                SET unassigned_at = @NowUtc
                WHERE care_case_id = @Id AND unassigned_at IS NULL;";

            const string insertAssignment = @"
                INSERT INTO care_case_assignment (care_case_id, volunteer_id, assigned_at, reason, assigned_by)
                VALUES (@Id, @VolunteerId, @NowUtc, @Reason, @ActingUserId);";

            // The counters are the authority on workload, so they move in the same
            // transaction as the assignment that causes them to change.
            const string incrementNew = @"
                UPDATE volunteer
                SET current_case_load       = current_case_load + 1,
                    lifetime_cases_assigned = lifetime_cases_assigned + 1,
                    last_assigned_at        = @NowUtc,
                    row_version             = row_version + 1
                WHERE id = @VolunteerId;";

            const string decrementPrevious = @"
                UPDATE volunteer
                SET current_case_load = GREATEST(0, current_case_load - 1),
                    row_version       = row_version + 1
                WHERE id = @PreviousId;";

            using var connection = _dbFactory.GetConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                var previousId = await connection.ExecuteScalarAsync<long?>(
                    readCurrent, new { Id = caseId }, transaction);

                if (previousId == volunteerId)
                {
                    transaction.Rollback();
                    return true; // already there; nothing to do
                }

                var updated = await connection.ExecuteAsync(updateCase, new
                {
                    Id = caseId,
                    RowVersion = rowVersion,
                    VolunteerId = volunteerId,
                    TeamId = teamId,
                    NowUtc = nowUtc,
                    ActingUserId = actingUserId
                }, transaction);

                if (updated != 1)
                {
                    transaction.Rollback();
                    return false;
                }

                await connection.ExecuteAsync(closePreviousAssignment,
                    new { Id = caseId, NowUtc = nowUtc }, transaction);

                await connection.ExecuteAsync(insertAssignment, new
                {
                    Id = caseId,
                    VolunteerId = volunteerId,
                    NowUtc = nowUtc,
                    Reason = reason,
                    ActingUserId = actingUserId
                }, transaction);

                await connection.ExecuteAsync(incrementNew,
                    new { VolunteerId = volunteerId, NowUtc = nowUtc }, transaction);

                if (previousId.HasValue)
                {
                    await connection.ExecuteAsync(decrementPrevious,
                        new { PreviousId = previousId.Value }, transaction);
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

        public async Task<bool> UpdateStateAsync(CareCase c, long? actingUserId)
        {
            const string sql = @"
                UPDATE care_case
                SET stage                  = @Stage,
                    status                 = @Status,
                    priority               = @Priority,
                    nurture_plan_id        = @NurturePlanId,
                    current_step_number    = @CurrentStepNumber,
                    next_step_due_on       = @NextStepDueOn,
                    awaiting_review_since  = @AwaitingReviewSince,
                    first_contact_at       = @FirstContactAt,
                    last_contact_at        = @LastContactAt,
                    next_action_on         = @NextActionOn,
                    contact_attempt_count  = @ContactAttemptCount,
                    consecutive_no_contact = @ConsecutiveNoContact,
                    updated_by             = @ActingUserId,
                    row_version            = row_version + 1
                WHERE id = @Id AND row_version = @RowVersion;";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteAsync(sql, new
            {
                c.Id,
                c.RowVersion,
                c.Stage,
                c.Status,
                c.Priority,
                c.NurturePlanId,
                c.CurrentStepNumber,
                c.NextStepDueOn,
                c.AwaitingReviewSince,
                c.FirstContactAt,
                c.LastContactAt,
                c.NextActionOn,
                c.ContactAttemptCount,
                c.ConsecutiveNoContact,
                ActingUserId = actingUserId
            }) == 1;
        }

        public async Task<bool> CloseAsync(
            long caseId, int rowVersion, string closeReason, string? closeNotes,
            DateTime nowUtc, long? actingUserId)
        {
            const string readCurrent = @"
                SELECT assigned_volunteer_id FROM care_case WHERE id = @Id FOR UPDATE;";

            const string closeCase = @"
                UPDATE care_case
                SET status           = 'CLOSED',
                    stage            = 'CLOSED',
                    closed_at        = @NowUtc,
                    close_reason     = @CloseReason,
                    close_notes      = @CloseNotes,
                    next_step_due_on = NULL,
                    next_action_on   = NULL,
                    updated_by       = @ActingUserId,
                    row_version      = row_version + 1
                WHERE id = @Id AND row_version = @RowVersion;";

            const string closeAssignment = @"
                UPDATE care_case_assignment
                SET unassigned_at = @NowUtc
                WHERE care_case_id = @Id AND unassigned_at IS NULL;";

            // Closing frees the volunteer's capacity. Forgetting this is how a
            // volunteer ends up permanently "full" and never offered new work.
            const string releaseVolunteer = @"
                UPDATE volunteer
                SET current_case_load     = GREATEST(0, current_case_load - 1),
                    lifetime_cases_closed = lifetime_cases_closed + 1,
                    row_version           = row_version + 1
                WHERE id = @VolunteerId;";

            const string cancelPending = @"
                UPDATE care_interaction
                SET status = 'CANCELLED', row_version = row_version + 1
                WHERE care_case_id = @Id AND status = 'PENDING';";

            using var connection = _dbFactory.GetConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                var volunteerId = await connection.ExecuteScalarAsync<long?>(
                    readCurrent, new { Id = caseId }, transaction);

                var updated = await connection.ExecuteAsync(closeCase, new
                {
                    Id = caseId,
                    RowVersion = rowVersion,
                    CloseReason = closeReason,
                    CloseNotes = closeNotes,
                    NowUtc = nowUtc,
                    ActingUserId = actingUserId
                }, transaction);

                if (updated != 1)
                {
                    transaction.Rollback();
                    return false;
                }

                await connection.ExecuteAsync(closeAssignment, new { Id = caseId, NowUtc = nowUtc }, transaction);
                await connection.ExecuteAsync(cancelPending, new { Id = caseId }, transaction);

                if (volunteerId.HasValue)
                    await connection.ExecuteAsync(releaseVolunteer, new { VolunteerId = volunteerId.Value }, transaction);

                transaction.Commit();
                return true;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public async Task<IReadOnlyList<CaseAssignment>> GetAssignmentHistoryAsync(long caseId)
        {
            const string sql = @"
                SELECT  a.id            AS Id,
                        a.care_case_id  AS CareCaseId,
                        a.volunteer_id  AS VolunteerId,
                        vp.full_name    AS VolunteerName,
                        a.assigned_at   AS AssignedAt,
                        a.unassigned_at AS UnassignedAt,
                        a.reason        AS Reason,
                        bp.full_name    AS AssignedByName
                FROM care_case_assignment a
                JOIN volunteer v          ON v.id  = a.volunteer_id
                JOIN person vp            ON vp.id = v.person_id
                LEFT JOIN user_account ua ON ua.id = a.assigned_by
                LEFT JOIN person bp       ON bp.id = ua.person_id
                WHERE a.care_case_id = @CaseId
                ORDER BY a.assigned_at DESC;";

            using var connection = _dbFactory.GetConnection();
            return (await connection.QueryAsync<CaseAssignment>(sql, new { CaseId = caseId })).ToList();
        }

        // ------------------------------------------------------------------
        // Resolvers
        // ------------------------------------------------------------------

        public async Task<long?> ResolvePersonIdAsync(string personPublicId)
        {
            const string sql = @"SELECT id FROM person WHERE public_id = @PublicId AND deleted_at IS NULL LIMIT 1;";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteScalarAsync<long?>(sql, new { PublicId = personPublicId });
        }

        public async Task<long?> ResolveVolunteerIdAsync(string volunteerPublicId)
        {
            const string sql = @"SELECT id FROM volunteer WHERE public_id = @PublicId LIMIT 1;";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteScalarAsync<long?>(sql, new { PublicId = volunteerPublicId });
        }

        public async Task<long?> ResolveCampusIdAsync(string? campusPublicId)
        {
            if (string.IsNullOrWhiteSpace(campusPublicId)) return null;

            const string sql = @"SELECT id FROM campus WHERE public_id = @PublicId AND is_active = 1 LIMIT 1;";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteScalarAsync<long?>(sql, new { PublicId = campusPublicId });
        }

        public async Task<long?> GetPersonCampusIdAsync(long personId)
        {
            const string sql = @"SELECT campus_id FROM person WHERE id = @Id LIMIT 1;";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteScalarAsync<long?>(sql, new { Id = personId });
        }

        public async Task<string?> GetCampusPublicIdAsync(long campusId)
        {
            const string sql = @"SELECT public_id FROM campus WHERE id = @Id LIMIT 1;";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteScalarAsync<string?>(sql, new { Id = campusId });
        }

        public async Task SetPersonDoNotContactAsync(long personId, string note, DateTime nowUtc, long? actingUserId)
        {
            const string sql = @"
                UPDATE person
                SET do_not_contact      = 1,
                    do_not_contact_at   = @NowUtc,
                    do_not_contact_note = @Note,
                    updated_by          = @ActingUserId,
                    row_version         = row_version + 1
                WHERE id = @PersonId AND do_not_contact = 0;";

            using var connection = _dbFactory.GetConnection();
            await connection.ExecuteAsync(sql, new
            {
                PersonId = personId,
                Note = note,
                NowUtc = nowUtc,
                ActingUserId = actingUserId
            });
        }

        public async Task<bool> IsPersonDoNotContactAsync(long personId)
        {
            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteScalarAsync<int>(
                "SELECT do_not_contact FROM person WHERE id = @Id LIMIT 1;",
                new { Id = personId }) == 1;
        }

        public async Task<long> AddNoteAsync(CareNote note, long? actingUserId)
        {
            const string sql = @"
                INSERT INTO note
                    (public_id, entity_type, entity_id, note_type_code, body, tags,
                     is_private, visible_to_role_code, created_by, updated_by)
                VALUES
                    (@PublicId, @EntityType, @EntityId, @NoteTypeCode, @Body, @Tags,
                     @IsPrivate, @VisibleToRoleCode, @ActingUserId, @ActingUserId);
                SELECT LAST_INSERT_ID();";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteScalarAsync<long>(sql, new
            {
                note.PublicId,
                note.EntityType,
                note.EntityId,
                note.NoteTypeCode,
                note.Body,
                note.Tags,
                note.IsPrivate,
                note.VisibleToRoleCode,
                ActingUserId = actingUserId
            });
        }

        public async Task<IReadOnlyList<CareNote>> GetNotesAsync(string entityType, long entityId, bool includePrivate)
        {
            // A caller without the required role simply does not see private notes —
            // filtered in SQL rather than trimmed afterwards, so they never leave the
            // database.
            const string sql = @"
                SELECT  n.id                   AS Id,
                        n.public_id            AS PublicId,
                        n.entity_type          AS EntityType,
                        n.entity_id            AS EntityId,
                        n.note_type_code       AS NoteTypeCode,
                        nt.label               AS NoteTypeLabel,
                        n.body                 AS Body,
                        n.tags                 AS Tags,
                        n.is_private           AS IsPrivate,
                        n.visible_to_role_code AS VisibleToRoleCode,
                        n.created_at           AS CreatedAt,
                        p.full_name            AS CreatedByName
                FROM note n
                LEFT JOIN note_type nt    ON nt.code = n.note_type_code
                LEFT JOIN user_account ua ON ua.id = n.created_by
                LEFT JOIN person p        ON p.id  = ua.person_id
                WHERE n.entity_type = @EntityType
                  AND n.entity_id   = @EntityId
                  AND (@IncludePrivate = 1 OR n.is_private = 0)
                ORDER BY n.created_at DESC;";

            using var connection = _dbFactory.GetConnection();
            return (await connection.QueryAsync<CareNote>(sql, new
            {
                EntityType = entityType,
                EntityId = entityId,
                IncludePrivate = includePrivate
            })).ToList();
        }

        private static async Task<string> NextReferenceCodeAsync(IDbConnection connection, IDbTransaction transaction)
        {
            const string sql = @"
                -- LPAD TRUNCATES when the value is longer than the width, so a
                -- plain LPAD(n, 5) silently returns the first 5 characters once the
                -- sequence outgrows it. The MVP's codes carried the year
                -- (P2026149), which is seven digits, so every generated code came
                -- back as 'C2026' and the second intake collided on the unique
                -- key — no visitor could be recorded at all. GREATEST keeps the
                -- padding for small numbers and gets out of the way for large ones.
                SELECT CONCAT('C', LPAD(
                    IFNULL(MAX(CAST(SUBSTRING(reference_code, 2) AS UNSIGNED)), 0) + 1,
                    GREATEST(5, CHAR_LENGTH(
                        IFNULL(MAX(CAST(SUBSTRING(reference_code, 2) AS UNSIGNED)), 0) + 1)),
                    '0'))
                FROM care_case
                WHERE reference_code REGEXP '^C[0-9]+$'
                FOR UPDATE;";

            return await connection.ExecuteScalarAsync<string>(sql, transaction: transaction) ?? "C00001";
        }
    }
}
