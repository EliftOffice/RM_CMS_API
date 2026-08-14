using Dapper;
using RM_CMS.Data;
using RM_CMS.Modules.Care.Domain;

namespace RM_CMS.Modules.Care.Data
{
    /// <summary>
    /// Data access for <c>care_interaction</c> — every planned or completed contact,
    /// for both the initial follow-up and each nurture step.
    /// </summary>
    public interface ICareInteractionRepository
    {
        Task<CareInteraction?> GetByPublicIdAsync(string publicId);
        Task<CareInteraction?> GetByIdAsync(long id);

        Task<IReadOnlyList<CareInteraction>> GetForCaseAsync(long caseId);

        /// <summary>The volunteer's work list: what is due, oldest first.</summary>
        Task<IReadOnlyList<CareInteraction>> GetDueForVolunteerAsync(long volunteerId, DateTime onOrBefore);

        /// <summary>The one open contact on a case, if any.</summary>
        Task<CareInteraction?> GetPendingForCaseAsync(long caseId);

        Task<long> CreateAsync(CareInteraction interaction, long? actingUserId);

        Task<bool> CompleteAsync(long id, int rowVersion, DateTime occurredAt, bool madeContact,
                                 string outcomeCode, string? intentCode, int? durationMinutes,
                                 string? notes, long? volunteerId, long? actingUserId);

        Task<bool> MarkMissedAsync(long id, int rowVersion, long? actingUserId);

        /// <summary>Highest sequence number used on a case for a stage.</summary>
        Task<int> MaxSequenceAsync(long caseId, string stage);

        /// <summary>Pending contacts past their scheduled date plus the grace period.</summary>
        Task<IReadOnlyList<CareInteraction>> FindOverdueAsync(DateTime cutoff, int limit);
    }

    public sealed class CareInteractionRepository : ICareInteractionRepository
    {
        private readonly IDbConnectionFactory _dbFactory;

        public CareInteractionRepository(IDbConnectionFactory dbFactory) => _dbFactory = dbFactory;

        private const string SelectInteraction = @"
            SELECT
                ci.id               AS Id,
                ci.public_id        AS PublicId,
                ci.care_case_id     AS CareCaseId,
                cc.public_id        AS CareCasePublicId,
                ci.volunteer_id     AS VolunteerId,
                v.public_id         AS VolunteerPublicId,
                vp.full_name        AS VolunteerName,
                ci.stage            AS Stage,
                ci.sequence_number  AS SequenceNumber,
                ci.method_code      AS MethodCode,
                ci.scheduled_on     AS ScheduledOn,
                ci.occurred_at      AS OccurredAt,
                ci.status           AS Status,
                ci.made_contact     AS MadeContact,
                ci.outcome_code     AS OutcomeCode,
                co.label            AS OutcomeLabel,
                ci.intent_code      AS IntentCode,
                vi.label            AS IntentLabel,
                ci.duration_minutes AS DurationMinutes,
                ci.notes            AS Notes,
                ci.created_at       AS CreatedAt,
                ci.row_version      AS RowVersion
            FROM care_interaction ci
            JOIN care_case cc            ON cc.id = ci.care_case_id
            LEFT JOIN volunteer v        ON v.id  = ci.volunteer_id
            LEFT JOIN person vp          ON vp.id = v.person_id
            LEFT JOIN care_outcome co    ON co.code = ci.outcome_code
            LEFT JOIN visitor_intent vi  ON vi.code = ci.intent_code";

        public async Task<CareInteraction?> GetByPublicIdAsync(string publicId)
        {
            const string sql = SelectInteraction + @" WHERE ci.public_id = @PublicId LIMIT 1;";

            using var connection = _dbFactory.GetConnection();
            return await connection.QueryFirstOrDefaultAsync<CareInteraction>(sql, new { PublicId = publicId });
        }

        public async Task<CareInteraction?> GetByIdAsync(long id)
        {
            const string sql = SelectInteraction + @" WHERE ci.id = @Id LIMIT 1;";

            using var connection = _dbFactory.GetConnection();
            return await connection.QueryFirstOrDefaultAsync<CareInteraction>(sql, new { Id = id });
        }

        public async Task<IReadOnlyList<CareInteraction>> GetForCaseAsync(long caseId)
        {
            // Chronological: this is the conversation history a team lead reads at review.
            const string sql = SelectInteraction + @"
            WHERE ci.care_case_id = @CaseId
            ORDER BY ci.stage, ci.sequence_number, ci.id;";

            using var connection = _dbFactory.GetConnection();
            return (await connection.QueryAsync<CareInteraction>(sql, new { CaseId = caseId })).ToList();
        }

        public async Task<IReadOnlyList<CareInteraction>> GetDueForVolunteerAsync(long volunteerId, DateTime onOrBefore)
        {
            const string sql = SelectInteraction + @"
            WHERE ci.volunteer_id = @VolunteerId
              AND ci.status = 'PENDING'
              AND (ci.scheduled_on IS NULL OR ci.scheduled_on <= @OnOrBefore)
              AND cc.status NOT IN ('CLOSED', 'ESCALATED', 'ON_HOLD')
            ORDER BY ci.scheduled_on ASC, ci.id ASC;";

            using var connection = _dbFactory.GetConnection();
            return (await connection.QueryAsync<CareInteraction>(sql, new
            {
                VolunteerId = volunteerId,
                OnOrBefore = onOrBefore.Date
            })).ToList();
        }

        public async Task<CareInteraction?> GetPendingForCaseAsync(long caseId)
        {
            const string sql = SelectInteraction + @"
            WHERE ci.care_case_id = @CaseId AND ci.status = 'PENDING'
            ORDER BY ci.id LIMIT 1;";

            using var connection = _dbFactory.GetConnection();
            return await connection.QueryFirstOrDefaultAsync<CareInteraction>(sql, new { CaseId = caseId });
        }

        public async Task<long> CreateAsync(CareInteraction i, long? actingUserId)
        {
            const string sql = @"
                INSERT INTO care_interaction
                    (public_id, care_case_id, volunteer_id, logged_by, stage, sequence_number,
                     method_code, scheduled_on, occurred_at, status, made_contact,
                     outcome_code, intent_code, duration_minutes, notes, created_by, updated_by)
                VALUES
                    (@PublicId, @CareCaseId, @VolunteerId, @ActingUserId, @Stage, @SequenceNumber,
                     @MethodCode, @ScheduledOn, @OccurredAt, @Status, @MadeContact,
                     @OutcomeCode, @IntentCode, @DurationMinutes, @Notes, @ActingUserId, @ActingUserId);
                SELECT LAST_INSERT_ID();";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteScalarAsync<long>(sql, new
            {
                i.PublicId,
                i.CareCaseId,
                i.VolunteerId,
                i.Stage,
                i.SequenceNumber,
                i.MethodCode,
                i.ScheduledOn,
                i.OccurredAt,
                i.Status,
                i.MadeContact,
                i.OutcomeCode,
                i.IntentCode,
                i.DurationMinutes,
                i.Notes,
                ActingUserId = actingUserId
            });
        }

        public async Task<bool> CompleteAsync(
            long id, int rowVersion, DateTime occurredAt, bool madeContact,
            string outcomeCode, string? intentCode, int? durationMinutes,
            string? notes, long? volunteerId, long? actingUserId)
        {
            // Guarded on status = 'PENDING' as well as row_version, so an interaction
            // cannot be completed twice and drive the progression rules twice.
            const string sql = @"
                UPDATE care_interaction
                SET status           = 'COMPLETED',
                    occurred_at      = @OccurredAt,
                    made_contact     = @MadeContact,
                    outcome_code     = @OutcomeCode,
                    intent_code      = @IntentCode,
                    duration_minutes = @DurationMinutes,
                    notes            = @Notes,
                    volunteer_id     = COALESCE(@VolunteerId, volunteer_id),
                    logged_by        = @ActingUserId,
                    updated_by       = @ActingUserId,
                    row_version      = row_version + 1
                WHERE id = @Id AND row_version = @RowVersion AND status = 'PENDING';";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteAsync(sql, new
            {
                Id = id,
                RowVersion = rowVersion,
                OccurredAt = occurredAt,
                MadeContact = madeContact,
                OutcomeCode = outcomeCode,
                IntentCode = intentCode,
                DurationMinutes = durationMinutes,
                Notes = notes,
                VolunteerId = volunteerId,
                ActingUserId = actingUserId
            }) == 1;
        }

        public async Task<bool> MarkMissedAsync(long id, int rowVersion, long? actingUserId)
        {
            const string sql = @"
                UPDATE care_interaction
                SET status = 'MISSED', updated_by = @ActingUserId, row_version = row_version + 1
                WHERE id = @Id AND row_version = @RowVersion AND status = 'PENDING';";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteAsync(sql, new
            {
                Id = id,
                RowVersion = rowVersion,
                ActingUserId = actingUserId
            }) == 1;
        }

        public async Task<int> MaxSequenceAsync(long caseId, string stage)
        {
            const string sql = @"
                SELECT IFNULL(MAX(sequence_number), 0)
                FROM care_interaction
                WHERE care_case_id = @CaseId AND stage = @Stage;";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteScalarAsync<int>(sql, new { CaseId = caseId, Stage = stage });
        }

        public async Task<IReadOnlyList<CareInteraction>> FindOverdueAsync(DateTime cutoff, int limit)
        {
            const string sql = SelectInteraction + @"
            WHERE ci.status = 'PENDING'
              AND ci.scheduled_on IS NOT NULL
              AND ci.scheduled_on < @Cutoff
              AND cc.status NOT IN ('CLOSED', 'ESCALATED', 'ON_HOLD')
            ORDER BY ci.scheduled_on ASC
            LIMIT @Limit;";

            using var connection = _dbFactory.GetConnection();
            return (await connection.QueryAsync<CareInteraction>(sql, new
            {
                Cutoff = cutoff.Date,
                Limit = limit
            })).ToList();
        }
    }
}
