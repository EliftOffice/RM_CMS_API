using Dapper;
using RM_CMS.Data;
using RM_CMS.Data.DTO.Nurture;
using RM_CMS.Data.Models;
using RM_CMS.Utilities;

namespace RM_CMS.DAL.Nurture
{
    public interface INurtureDAL
    {
        Task<ApiResponse<string>> StartSequenceAsync(string personId, string volunteerId, string? teamLeadId);
        Task<ApiResponse<bool>> LogStepAsync(NurtureStepLogDto dto);
        Task<ApiResponse<bool>> CloseSequenceAsync(CloseSequenceDto dto);
        Task<ApiResponse<IEnumerable<NurtureStepDetailDto>>> GetDueStepsForVolunteerAsync(string volunteerId);
        Task<ApiResponse<IEnumerable<NurtureSequenceSummaryDto>>> GetActiveSequencesForTeamLeadAsync(string teamLeadId);
        Task<ApiResponse<IEnumerable<NurtureSequenceSummaryDto>>> GetSequencesAwaitingReviewAsync(string teamLeadId);
        Task<ApiResponse<IEnumerable<NurtureStep>>> GetStepsBySequenceAsync(string sequenceId);
        Task<ApiResponse<bool>> MarkPermanentAsync(
    string personId);
        Task<ApiResponse<bool>> MarkFailedAsync(
    string personId);
    }

    public class NurtureDAL : INurtureDAL
    {
        private readonly IDbConnectionFactory _dbConnectionFactory;

        // Step method pattern: Call, Visit, Call, Visit, Call, Visit, Call
        private static readonly string[] StepMethods = { "Call", "Visit", "Call", "Visit", "Call", "Visit", "Call" };

        public NurtureDAL(IDbConnectionFactory dbConnectionFactory)
        {
            _dbConnectionFactory = dbConnectionFactory;
        }

        // ─────────────────────────────────────────────────────────────
        // START SEQUENCE — called when volunteer closes follow-up as Normal
        // ─────────────────────────────────────────────────────────────
        public async Task<ApiResponse<string>> StartSequenceAsync(string personId, string volunteerId, string? teamLeadId)
        {
            using var connection = _dbConnectionFactory.GetConnection();
            if (connection is System.Data.Common.DbConnection dbConn)
                await dbConn.OpenAsync();
            else
                connection.Open();

            using var transaction = connection.BeginTransaction();
            try
            {
                // Guard: don't start a second sequence if already active
                var existing = await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM nurture_sequences WHERE person_id = @PersonId AND status = 'Active'",
                    new { PersonId = personId }, transaction);
                if (existing > 0)
                    return new ApiResponse<string>(ResponseType.Warning, "Active sequence already exists for this person", string.Empty);

                // Generate sequence ID
                var seqMax = await connection.ExecuteScalarAsync<int?>(
                    "SELECT MAX(CAST(SUBSTRING(sequence_id, 3) AS UNSIGNED)) FROM nurture_sequences", null, transaction);
                var sequenceId = $"NS{((seqMax ?? 0) + 1).ToString().PadLeft(4, '0')}";

                // Insert sequence record
                await connection.ExecuteAsync(@"
                    INSERT INTO nurture_sequences
                        (sequence_id, person_id, volunteer_id, team_lead_id, current_step, status, started_at)
                    VALUES
                        (@SequenceId, @PersonId, @VolunteerId, @TeamLeadId, 1, 'Active', NOW())",
                    new { SequenceId = sequenceId, PersonId = personId, VolunteerId = volunteerId, TeamLeadId = teamLeadId },
                    transaction);

                // Pre-create all 7 steps with scheduled dates (weekly apart)
                var stepMax = await connection.ExecuteScalarAsync<int?>(
                    "SELECT MAX(CAST(SUBSTRING(step_id, 4) AS UNSIGNED)) FROM nurture_steps", null, transaction);
                var stepCounter = (stepMax ?? 0) + 1;

                var startDate = DateTime.Today;
                for (int i = 0; i < 7; i++)
                {
                    var stepId = $"NST{stepCounter.ToString().PadLeft(4, '0')}";
                    var scheduledDate = startDate.AddDays(i * 7);
                    await connection.ExecuteAsync(@"
                        INSERT INTO nurture_steps
                            (step_id, sequence_id, person_id, volunteer_id, step_number, method, scheduled_date, status)
                        VALUES
                            (@StepId, @SequenceId, @PersonId, @VolunteerId, @StepNumber, @Method, @ScheduledDate, 'Pending')",
                        new
                        {
                            StepId = stepId,
                            SequenceId = sequenceId,
                            PersonId = personId,
                            VolunteerId = volunteerId,
                            StepNumber = i + 1,
                            Method = StepMethods[i],
                            ScheduledDate = scheduledDate
                        }, transaction);
                    stepCounter++;
                }

                // Update person status
                await connection.ExecuteAsync(
                    "UPDATE people SET follow_up_status = 'IN_NURTURE', next_action_date = @NextDate WHERE person_id = @PersonId",
                    new { PersonId = personId, NextDate = startDate }, transaction);

                transaction.Commit();
                return new ApiResponse<string>(ResponseType.Success, "Nurture sequence started", sequenceId);
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return new ApiResponse<string>(ResponseType.Error, $"Failed to start sequence: {ex.Message}", string.Empty);
            }
        }

        // ─────────────────────────────────────────────────────────────
        // LOG STEP — volunteer submits outcome for a nurture step
        // ─────────────────────────────────────────────────────────────
        public async Task<ApiResponse<bool>> LogStepAsync(NurtureStepLogDto dto)
        {
            using var connection = _dbConnectionFactory.GetConnection();
            if (connection is System.Data.Common.DbConnection dbConn)
                await dbConn.OpenAsync();
            else
                connection.Open();

            using var transaction = connection.BeginTransaction();
            try
            {
                // Mark step done
                await connection.ExecuteAsync(@"
                    UPDATE nurture_steps SET
                        status         = 'Done',
                        contact_status = @ContactStatus,
                        response_type  = @ResponseType,
                        notes          = @Notes,
                        completed_at   = NOW(),
                        updated_at     = NOW()
                    WHERE step_id = @StepId",
                    new { dto.StepId, dto.ContactStatus, dto.ResponseType, dto.Notes }, transaction);

                // Get current step number and total steps in this sequence
                var stepInfo = await connection.QueryFirstOrDefaultAsync<(int StepNumber, int TotalDone, string SequenceId)>(@"
                    SELECT ns.step_number,
                           (SELECT COUNT(*) FROM nurture_steps WHERE sequence_id = ns.sequence_id AND status = 'Done') AS total_done,
                           ns.sequence_id
                    FROM nurture_steps ns WHERE ns.step_id = @StepId",
                    new { dto.StepId }, transaction);

                bool isLastStep = stepInfo.StepNumber == 7;

                if (isLastStep)
                {
                    // Move to InReview — TL must make final call
                    await connection.ExecuteAsync(@"
                        UPDATE nurture_sequences SET status = 'InReview', current_step = 7, updated_at = NOW()
                        WHERE sequence_id = @SequenceId",
                        new { stepInfo.SequenceId }, transaction);

                    await connection.ExecuteAsync(
                        "UPDATE people SET follow_up_status = 'IN_REVIEW' WHERE person_id = @PersonId",
                        new { dto.PersonId }, transaction);
                }
                else
                {
                    // Advance current_step
                    await connection.ExecuteAsync(@"
                        UPDATE nurture_sequences SET current_step = @NextStep, updated_at = NOW()
                        WHERE sequence_id = @SequenceId",
                        new { NextStep = stepInfo.StepNumber + 1, stepInfo.SequenceId }, transaction);
                }

                transaction.Commit();
                return new ApiResponse<bool>(ResponseType.Success,
                    isLastStep ? "Step 7 done — sequence moved to review" : $"Step {stepInfo.StepNumber} logged", true);
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return new ApiResponse<bool>(ResponseType.Error, $"Failed to log step: {ex.Message}", false);
            }
        }

        // ─────────────────────────────────────────────────────────────
        // CLOSE SEQUENCE — Team Lead marks Permanent or Failed
        // ─────────────────────────────────────────────────────────────
        public async Task<ApiResponse<bool>> CloseSequenceAsync(CloseSequenceDto dto)
        {
            using var connection = _dbConnectionFactory.GetConnection();
            if (connection is System.Data.Common.DbConnection dbConn)
                await dbConn.OpenAsync();
            else
                connection.Open();

            using var transaction = connection.BeginTransaction();
            try
            {
                var personId = await connection.ExecuteScalarAsync<string>(
                    "SELECT person_id FROM nurture_sequences WHERE sequence_id = @SequenceId",
                    new { dto.SequenceId }, transaction);

                await connection.ExecuteAsync(@"
                    UPDATE nurture_sequences SET
                        status       = @FinalStatus,
                        final_notes  = @FinalNotes,
                        completed_at = NOW(),
                        updated_at   = NOW()
                    WHERE sequence_id = @SequenceId",
                    new { dto.FinalStatus, dto.FinalNotes, dto.SequenceId }, transaction);

                await connection.ExecuteAsync(
                    "UPDATE people SET follow_up_status = @Status, next_action_date = NULL WHERE person_id = @PersonId",
                    new { Status = dto.FinalStatus.ToUpper(), PersonId = personId }, transaction);

                transaction.Commit();
                return new ApiResponse<bool>(ResponseType.Success, $"Sequence closed as {dto.FinalStatus}", true);
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return new ApiResponse<bool>(ResponseType.Error, $"Failed to close sequence: {ex.Message}", false);
            }
        }

        // ─────────────────────────────────────────────────────────────
        // GET DUE STEPS FOR VOLUNTEER — used by cron + assignments page
        // ─────────────────────────────────────────────────────────────
        public async Task<ApiResponse<IEnumerable<NurtureStepDetailDto>>> GetDueStepsForVolunteerAsync(string volunteerId)
        {
            try
            {
                using var connection = _dbConnectionFactory.GetConnection();
                var steps = await connection.QueryAsync<NurtureStepDetailDto>(@"
                    SELECT
                        ns.step_id, ns.sequence_id, ns.person_id,
                        CONCAT(p.first_name, ' ', p.last_name) AS person_name,
                        p.phone                                  AS person_phone,
                        ns.step_number, ns.method, ns.scheduled_date, ns.status
                    FROM nurture_steps ns
                    JOIN people p ON p.person_id = ns.person_id
                    WHERE ns.volunteer_id = @VolunteerId
                      AND ns.status = 'Pending'
                      AND ns.scheduled_date <= CURDATE()
                    ORDER BY ns.scheduled_date ASC",
                    new { VolunteerId = volunteerId });

                return new ApiResponse<IEnumerable<NurtureStepDetailDto>>(ResponseType.Success, "OK", steps);
            }
            catch (Exception ex)
            {
                return new ApiResponse<IEnumerable<NurtureStepDetailDto>>(ResponseType.Error, ex.Message, null);
            }
        }

        // ─────────────────────────────────────────────────────────────
        // GET ACTIVE SEQUENCES FOR TEAM LEAD — TL dashboard + huddle
        // ─────────────────────────────────────────────────────────────
        public async Task<ApiResponse<IEnumerable<NurtureSequenceSummaryDto>>> GetActiveSequencesForTeamLeadAsync(string teamLeadId)
        {
            try
            {
                using var connection = _dbConnectionFactory.GetConnection();
                var rows = await connection.QueryAsync<NurtureSequenceSummaryDto>(@"
                    SELECT
                        nseq.sequence_id,
                        nseq.person_id,
                        CONCAT(p.first_name, ' ', p.last_name) AS person_name,
                        p.phone                                  AS person_phone,
                        nseq.volunteer_id,
                        CONCAT(v.first_name, ' ', v.last_name)  AS volunteer_name,
                        nseq.current_step,
                        nseq.status,
                        nseq.started_at,
                        ns.method                                AS next_method,
                        ns.scheduled_date                        AS next_scheduled_date,
                        CASE WHEN ns.scheduled_date < CURDATE() THEN 'Overdue' ELSE 'Pending' END AS next_step_status
                    FROM nurture_sequences nseq
                    JOIN people    p    ON p.person_id    = nseq.person_id
                    JOIN volunteers v   ON v.volunteer_id = nseq.volunteer_id
                    LEFT JOIN nurture_steps ns ON ns.sequence_id = nseq.sequence_id
                                              AND ns.step_number = nseq.current_step
                                              AND ns.status = 'Pending'
                    WHERE nseq.team_lead_id = @TeamLeadId
                      AND nseq.status = 'Active'
                    ORDER BY ns.scheduled_date ASC",
                    new { TeamLeadId = teamLeadId });

                return new ApiResponse<IEnumerable<NurtureSequenceSummaryDto>>(ResponseType.Success, "OK", rows);
            }
            catch (Exception ex)
            {
                return new ApiResponse<IEnumerable<NurtureSequenceSummaryDto>>(ResponseType.Error, ex.Message, null);
            }
        }

        // ─────────────────────────────────────────────────────────────
        // GET SEQUENCES AWAITING REVIEW — step 7 done, TL needs to decide
        // ─────────────────────────────────────────────────────────────
        public async Task<ApiResponse<IEnumerable<NurtureSequenceSummaryDto>>> GetSequencesAwaitingReviewAsync(string teamLeadId)
        {
            try
            {
                using var connection = _dbConnectionFactory.GetConnection();
                var rows = await connection.QueryAsync<NurtureSequenceSummaryDto>(@"
                    SELECT
                        nseq.sequence_id,
                        nseq.person_id,
                        CONCAT(p.first_name, ' ', p.last_name) AS person_name,
                        p.phone                                  AS person_phone,
                        nseq.volunteer_id,
                        CONCAT(v.first_name, ' ', v.last_name)  AS volunteer_name,
                        nseq.current_step,
                        nseq.status,
                        nseq.started_at,
                        '' AS next_method,
                        NULL AS next_scheduled_date,
                        '' AS next_step_status
                    FROM nurture_sequences nseq
                    JOIN people    p ON p.person_id    = nseq.person_id
                    JOIN volunteers v ON v.volunteer_id = nseq.volunteer_id
                    WHERE nseq.team_lead_id = @TeamLeadId
                      AND nseq.status = 'InReview'
                    ORDER BY nseq.updated_at ASC",
                    new { TeamLeadId = teamLeadId });

                return new ApiResponse<IEnumerable<NurtureSequenceSummaryDto>>(ResponseType.Success, "OK", rows);
            }
            catch (Exception ex)
            {
                return new ApiResponse<IEnumerable<NurtureSequenceSummaryDto>>(ResponseType.Error, ex.Message, null);
            }
        }

        // ─────────────────────────────────────────────────────────────
        // GET ALL STEPS FOR A SEQUENCE — full history view
        // ─────────────────────────────────────────────────────────────
        public async Task<ApiResponse<IEnumerable<NurtureStep>>> GetStepsBySequenceAsync(string sequenceId)
        {
            try
            {
                using var connection = _dbConnectionFactory.GetConnection();
                var steps = await connection.QueryAsync<NurtureStep>(
                    "SELECT * FROM nurture_steps WHERE sequence_id = @SequenceId ORDER BY step_number",
                    new { SequenceId = sequenceId });
                return new ApiResponse<IEnumerable<NurtureStep>>(ResponseType.Success, "OK", steps);
            }
            catch (Exception ex)
            {
                return new ApiResponse<IEnumerable<NurtureStep>>(ResponseType.Error, ex.Message, null);
            }
        }

        public async Task<ApiResponse<bool>> MarkPermanentAsync(
    string personId)
        {
            try
            {
                using var connection =
                    _dbConnectionFactory.GetConnection();

                const string query = @"

            UPDATE nurture_sequences
            SET
                status = 'Permanent',
                completed_at = NOW(),
                updated_at = NOW()
            WHERE person_id = @PersonId
              AND current_step = 8
              AND status = 'Active';

            UPDATE people
            SET
                follow_up_status = 'PERMANENT',
                next_action_date = NULL
            WHERE person_id = @PersonId;
        ";

                await connection.ExecuteAsync(
                    query,
                    new { PersonId = personId });

                return new ApiResponse<bool>(
                    ResponseType.Success,
                    "Marked as Permanent",
                    true);
            }
            catch (Exception ex)
            {
                return new ApiResponse<bool>(
                    ResponseType.Error,
                    ex.Message,
                    false);
            }
        }

        public async Task<ApiResponse<bool>> MarkFailedAsync(
    string personId)
        {
            try
            {
                using var connection =
                    _dbConnectionFactory.GetConnection();

                const string query = @"

            UPDATE nurture_sequences
            SET
                status = 'Failed',
                completed_at = NOW(),
                updated_at = NOW()
            WHERE person_id = @PersonId
              AND current_step = 8
              AND status = 'Active';

            UPDATE people
            SET
                follow_up_status = 'FAILED',
                next_action_date = NULL
            WHERE person_id = @PersonId;
        ";

                await connection.ExecuteAsync(
                    query,
                    new { PersonId = personId });

                return new ApiResponse<bool>(
                    ResponseType.Success,
                    "Marked as Failed",
                    true);
            }
            catch (Exception ex)
            {
                return new ApiResponse<bool>(
                    ResponseType.Error,
                    ex.Message,
                    false);
            }
        }
    }
}
