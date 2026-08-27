using Dapper;
using RM_CMS.Data;
using RM_CMS.Modules.Care.Domain;

namespace RM_CMS.Modules.Care.Data
{
    /// <summary>
    /// Data access for <c>escalation</c>, plus the <c>note</c> surface.
    ///
    /// An escalation pauses its case. Raising one and pausing the case happen in a
    /// single transaction, as do closing one and releasing the case — a half-applied
    /// escalation would either stall a case forever or let contact continue on an
    /// unresolved safeguarding concern.
    /// </summary>
    public interface IEscalationRepository
    {
        Task<Escalation?> GetByPublicIdAsync(string publicId);

        /// <summary>
        /// By internal id. Used by the notification sender, which holds
        /// <c>notification_delivery.related_entity_id</c> — an internal id, because a
        /// queue row is server-side and never leaves through the API.
        /// </summary>
        Task<Escalation?> GetByIdAsync(long id);

        Task<IReadOnlyList<Escalation>> GetForCaseAsync(long caseId);

        Task<IReadOnlyList<Escalation>> SearchAsync(long? campusId, long? assignedToUserId,
                                                    string? status, int skip, int take);
        Task<int> CountAsync(long? campusId, long? assignedToUserId, string? status);

        /// <summary>Raises an escalation and pauses the case in one transaction.</summary>
        Task<long> RaiseAndPauseCaseAsync(Escalation escalation, int caseRowVersion, long? actingUserId);

        Task<bool> AcknowledgeAsync(long id, int rowVersion, long acknowledgedByUserId, DateTime nowUtc);

        /// <summary>Resolves an escalation and resumes the case in one transaction.</summary>
        Task<bool> ResolveAndResumeCaseAsync(long id, int rowVersion, string status, string outcomeCode,
                                             string? resolutionNotes, string? resourceConnected,
                                             bool? protocolFollowed, bool? authoritiesContacted,
                                             bool? volunteerDebriefed, DateTime nowUtc,
                                             DateTime? resumeNextStepOn, long? actingUserId);

        /// <summary>Open escalations that have gone unacknowledged past the threshold.</summary>
        Task<IReadOnlyList<Escalation>> FindUnacknowledgedAsync(DateTime raisedBefore, int limit);

        Task<bool> RecordReminderAsync(long id, DateTime nowUtc, bool alertedPastor);

        /// <summary>
        /// Stamps <c>notified_at</c> the first time an alert about this escalation is
        /// actually delivered. Only the first delivery counts — the column answers
        /// "when was anybody told?", and a later reminder does not change that answer.
        /// </summary>
        Task<bool> MarkNotifiedAsync(long id, DateTime nowUtc);

        Task<bool> ReasonExistsAsync(string reasonCode);
        Task<bool> OutcomeExistsAsync(string outcomeCode);
    }

    public sealed class EscalationRepository : IEscalationRepository
    {
        private readonly IDbConnectionFactory _dbFactory;

        public EscalationRepository(IDbConnectionFactory dbFactory) => _dbFactory = dbFactory;

        private const string SelectEscalation = @"
            SELECT
                e.id                       AS Id,
                e.public_id                AS PublicId,
                e.reference_code           AS ReferenceCode,
                e.care_case_id             AS CareCaseId,
                cc.public_id               AS CareCasePublicId,
                pp.full_name               AS PersonName,
                e.care_interaction_id      AS CareInteractionId,
                e.raised_by_volunteer_id   AS RaisedByVolunteerId,
                rvp.full_name              AS RaisedByName,
                e.assigned_to_user_id      AS AssignedToUserId,
                aua.public_id              AS AssignedToPublicId,
                aup.full_name              AS AssignedToName,
                e.campus_id                AS CampusId,
                cam.public_id              AS CampusPublicId,
                e.reason_code              AS ReasonCode,
                er.label                   AS ReasonLabel,
                er.requires_protocol       AS ReasonRequiresProtocol,
                e.tier                     AS Tier,
                e.status                   AS Status,
                e.description              AS Description,
                e.raised_at                AS RaisedAt,
                e.notified_at              AS NotifiedAt,
                e.acknowledged_at          AS AcknowledgedAt,
                e.resolved_at              AS ResolvedAt,
                e.outcome_code             AS OutcomeCode,
                e.resolution_notes         AS ResolutionNotes,
                e.resource_connected       AS ResourceConnected,
                e.resume_case_on_close     AS ResumeCaseOnClose,
                e.reminder_count           AS ReminderCount,
                e.last_reminder_at         AS LastReminderAt,
                e.pastor_alerted_at        AS PastorAlertedAt,
                e.protocol_followed        AS ProtocolFollowed,
                e.authorities_contacted    AS AuthoritiesContacted,
                e.volunteer_debriefed      AS VolunteerDebriefed,
                e.row_version              AS RowVersion
            FROM escalation e
            JOIN care_case cc              ON cc.id = e.care_case_id
            JOIN person pp                 ON pp.id = cc.person_id
            JOIN campus cam                ON cam.id = e.campus_id
            JOIN escalation_reason er      ON er.code = e.reason_code
            LEFT JOIN volunteer rv         ON rv.id = e.raised_by_volunteer_id
            LEFT JOIN person rvp           ON rvp.id = rv.person_id
            LEFT JOIN user_account aua     ON aua.id = e.assigned_to_user_id
            LEFT JOIN person aup           ON aup.id = aua.person_id";

        public async Task<Escalation?> GetByPublicIdAsync(string publicId)
        {
            const string sql = SelectEscalation + @" WHERE e.public_id = @PublicId LIMIT 1;";

            using var connection = _dbFactory.GetConnection();
            return await connection.QueryFirstOrDefaultAsync<Escalation>(sql, new { PublicId = publicId });
        }

        public async Task<Escalation?> GetByIdAsync(long id)
        {
            const string sql = SelectEscalation + @" WHERE e.id = @Id LIMIT 1;";

            using var connection = _dbFactory.GetConnection();
            return await connection.QueryFirstOrDefaultAsync<Escalation>(sql, new { Id = id });
        }

        public async Task<bool> MarkNotifiedAsync(long id, DateTime nowUtc)
        {
            // Deliberately does NOT touch row_version or updated_at: a delivery
            // landing is not a domain edit, and bumping the version would make the
            // team lead's open acknowledge form fail with a phantom conflict.
            const string sql = @"
                UPDATE escalation
                SET notified_at = @NowUtc
                WHERE id = @Id AND notified_at IS NULL;";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteAsync(sql, new { Id = id, NowUtc = nowUtc }) > 0;
        }

        public async Task<IReadOnlyList<Escalation>> GetForCaseAsync(long caseId)
        {
            const string sql = SelectEscalation + @" WHERE e.care_case_id = @CaseId ORDER BY e.raised_at DESC;";

            using var connection = _dbFactory.GetConnection();
            return (await connection.QueryAsync<Escalation>(sql, new { CaseId = caseId })).ToList();
        }

        public async Task<IReadOnlyList<Escalation>> SearchAsync(
            long? campusId, long? assignedToUserId, string? status, int skip, int take)
        {
            // Most urgent first, then oldest — the order a team lead should work them.
            const string sql = SelectEscalation + @"
            WHERE (@CampusId IS NULL OR e.campus_id = @CampusId)
              AND (@AssignedTo IS NULL OR e.assigned_to_user_id = @AssignedTo)
              AND (@Status IS NULL OR e.status = @Status)
            ORDER BY FIELD(e.tier, 'EMERGENCY', 'URGENT', 'STANDARD'), e.raised_at ASC
            LIMIT @Take OFFSET @Skip;";

            using var connection = _dbFactory.GetConnection();
            return (await connection.QueryAsync<Escalation>(sql, new
            {
                CampusId = campusId,
                AssignedTo = assignedToUserId,
                Status = status,
                Skip = skip,
                Take = take
            })).ToList();
        }

        public async Task<int> CountAsync(long? campusId, long? assignedToUserId, string? status)
        {
            const string sql = @"
                SELECT COUNT(1) FROM escalation e
                WHERE (@CampusId IS NULL OR e.campus_id = @CampusId)
                  AND (@AssignedTo IS NULL OR e.assigned_to_user_id = @AssignedTo)
                  AND (@Status IS NULL OR e.status = @Status);";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteScalarAsync<int>(sql, new
            {
                CampusId = campusId,
                AssignedTo = assignedToUserId,
                Status = status
            });
        }

        public async Task<long> RaiseAndPauseCaseAsync(Escalation e, int caseRowVersion, long? actingUserId)
        {
            const string insert = @"
                INSERT INTO escalation
                    (public_id, reference_code, care_case_id, care_interaction_id,
                     raised_by_volunteer_id, assigned_to_user_id, campus_id,
                     reason_code, tier, status, description, raised_at, created_by, updated_by)
                VALUES
                    (@PublicId, @ReferenceCode, @CareCaseId, @CareInteractionId,
                     @RaisedByVolunteerId, @AssignedToUserId, @CampusId,
                     @ReasonCode, @Tier, 'NEW', @Description, @RaisedAt, @ActingUserId, @ActingUserId);
                SELECT LAST_INSERT_ID();";

            // Pausing here is the whole point: the scheduler skips ESCALATED cases, so
            // no further contact is made while the concern is unresolved.
            const string pauseCase = @"
                UPDATE care_case
                SET status           = 'ESCALATED',
                    next_step_due_on = NULL,
                    updated_by       = @ActingUserId,
                    row_version      = row_version + 1
                WHERE id = @CaseId AND row_version = @CaseRowVersion;";

            using var connection = _dbFactory.GetConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                e.ReferenceCode = await connection.ExecuteScalarAsync<string>(@"
                -- LPAD TRUNCATES when the value is longer than the width, so a
                -- plain LPAD(n, 5) silently returns the first 5 characters once the
                -- sequence outgrows it. The MVP's codes carried the year
                -- (P2026149), which is seven digits, so every generated code came
                -- back as 'E2026' and the second intake collided on the unique
                -- key — no visitor could be recorded at all. GREATEST keeps the
                -- padding for small numbers and gets out of the way for large ones.
                    SELECT CONCAT('E', LPAD(
                        IFNULL(MAX(CAST(SUBSTRING(reference_code, 2) AS UNSIGNED)), 0) + 1,
                    GREATEST(5, CHAR_LENGTH(
                        IFNULL(MAX(CAST(SUBSTRING(reference_code, 2) AS UNSIGNED)), 0) + 1)),
                    '0'))
                    FROM escalation WHERE reference_code REGEXP '^E[0-9]+$' FOR UPDATE;",
                    transaction: transaction) ?? "E00001";

                var id = await connection.ExecuteScalarAsync<long>(insert, new
                {
                    e.PublicId,
                    e.ReferenceCode,
                    e.CareCaseId,
                    e.CareInteractionId,
                    e.RaisedByVolunteerId,
                    e.AssignedToUserId,
                    e.CampusId,
                    e.ReasonCode,
                    e.Tier,
                    e.Description,
                    e.RaisedAt,
                    ActingUserId = actingUserId
                }, transaction);

                var paused = await connection.ExecuteAsync(pauseCase, new
                {
                    CaseId = e.CareCaseId,
                    CaseRowVersion = caseRowVersion,
                    ActingUserId = actingUserId
                }, transaction);

                if (paused != 1)
                {
                    transaction.Rollback();
                    return 0;
                }

                transaction.Commit();
                return id;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public async Task<bool> AcknowledgeAsync(long id, int rowVersion, long acknowledgedByUserId, DateTime nowUtc)
        {
            const string sql = @"
                UPDATE escalation
                SET status              = 'ACKNOWLEDGED',
                    acknowledged_at     = @NowUtc,
                    assigned_to_user_id = COALESCE(assigned_to_user_id, @UserId),
                    updated_by          = @UserId,
                    row_version         = row_version + 1
                WHERE id = @Id AND row_version = @RowVersion AND acknowledged_at IS NULL;";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteAsync(sql, new
            {
                Id = id,
                RowVersion = rowVersion,
                UserId = acknowledgedByUserId,
                NowUtc = nowUtc
            }) == 1;
        }

        public async Task<bool> ResolveAndResumeCaseAsync(
            long id, int rowVersion, string status, string outcomeCode,
            string? resolutionNotes, string? resourceConnected,
            bool? protocolFollowed, bool? authoritiesContacted, bool? volunteerDebriefed,
            DateTime nowUtc, DateTime? resumeNextStepOn, long? actingUserId)
        {
            const string resolve = @"
                UPDATE escalation
                SET status                = @Status,
                    resolved_at           = @NowUtc,
                    outcome_code          = @OutcomeCode,
                    resolution_notes      = @ResolutionNotes,
                    resource_connected    = @ResourceConnected,
                    protocol_followed     = @ProtocolFollowed,
                    authorities_contacted = @AuthoritiesContacted,
                    volunteer_debriefed   = @VolunteerDebriefed,
                    updated_by            = @ActingUserId,
                    row_version           = row_version + 1
                WHERE id = @Id AND row_version = @RowVersion;";

            // Resume only if no OTHER escalation is still open on the case — a second
            // unresolved concern must keep it paused.
            const string resumeCase = @"
                UPDATE care_case cc
                SET cc.status           = 'IN_PROGRESS',
                    cc.next_step_due_on = @ResumeOn,
                    cc.updated_by       = @ActingUserId,
                    cc.row_version      = cc.row_version + 1
                WHERE cc.id = (SELECT care_case_id FROM escalation WHERE id = @Id)
                  AND cc.status = 'ESCALATED'
                  AND (SELECT resume_case_on_close FROM escalation WHERE id = @Id) = 1
                  AND NOT EXISTS (
                      SELECT 1 FROM escalation e2
                      WHERE e2.care_case_id = cc.id
                        AND e2.id <> @Id
                        AND e2.status NOT IN ('RESOLVED', 'CLOSED', 'REFERRED_OUT'));";

            using var connection = _dbFactory.GetConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                var resolved = await connection.ExecuteAsync(resolve, new
                {
                    Id = id,
                    RowVersion = rowVersion,
                    Status = status,
                    OutcomeCode = outcomeCode,
                    ResolutionNotes = resolutionNotes,
                    ResourceConnected = resourceConnected,
                    ProtocolFollowed = protocolFollowed,
                    AuthoritiesContacted = authoritiesContacted,
                    VolunteerDebriefed = volunteerDebriefed,
                    NowUtc = nowUtc,
                    ActingUserId = actingUserId
                }, transaction);

                if (resolved != 1)
                {
                    transaction.Rollback();
                    return false;
                }

                await connection.ExecuteAsync(resumeCase, new
                {
                    Id = id,
                    ResumeOn = resumeNextStepOn?.Date,
                    ActingUserId = actingUserId
                }, transaction);

                transaction.Commit();
                return true;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public async Task<IReadOnlyList<Escalation>> FindUnacknowledgedAsync(DateTime raisedBefore, int limit)
        {
            const string sql = SelectEscalation + @"
            WHERE e.acknowledged_at IS NULL
              AND e.status = 'NEW'
              AND e.raised_at < @RaisedBefore
            ORDER BY FIELD(e.tier, 'EMERGENCY', 'URGENT', 'STANDARD'), e.raised_at ASC
            LIMIT @Limit;";

            using var connection = _dbFactory.GetConnection();
            return (await connection.QueryAsync<Escalation>(sql, new
            {
                RaisedBefore = raisedBefore,
                Limit = limit
            })).ToList();
        }

        public async Task<bool> RecordReminderAsync(long id, DateTime nowUtc, bool alertedPastor)
        {
            const string sql = @"
                UPDATE escalation
                SET reminder_count    = reminder_count + 1,
                    last_reminder_at  = @NowUtc,
                    pastor_alerted_at = CASE WHEN @AlertedPastor = 1 AND pastor_alerted_at IS NULL
                                             THEN @NowUtc ELSE pastor_alerted_at END,
                    row_version       = row_version + 1
                WHERE id = @Id;";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteAsync(sql, new
            {
                Id = id,
                NowUtc = nowUtc,
                AlertedPastor = alertedPastor
            }) == 1;
        }

        public async Task<bool> ReasonExistsAsync(string reasonCode)
        {
            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM escalation_reason WHERE code = @Code AND is_active = 1;",
                new { Code = reasonCode }) > 0;
        }

        public async Task<bool> OutcomeExistsAsync(string outcomeCode)
        {
            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM escalation_outcome WHERE code = @Code AND is_active = 1;",
                new { Code = outcomeCode }) > 0;
        }
    }
}
