using Dapper;
using RM_CMS.Data;
using RM_CMS.Data.DTO.Nurture;
using RM_CMS.Data.DTO.TeamLeads;
using RM_CMS.Utilities;
using System.Data;

namespace RM_CMS.DAL.TeamLeads
{
    public interface ICheckInDAL
    {
        Task<ApiResponse<string>> CreateCheckInAsync(CreateCheckInDTO dto);
        Task<ApiResponse<HuddleNurtureReviewDto>> GetHuddleNurtureReviewAsync(string teamLeadId);
    }
    public class CheckInDAL : ICheckInDAL
    {
        private readonly IDbConnectionFactory _dbConnectionFactory;
        public CheckInDAL(IDbConnectionFactory dbConnectionFactory)
        {
            _dbConnectionFactory = dbConnectionFactory;
        }

        public async Task<ApiResponse<string>> CreateCheckInAsync(CreateCheckInDTO dto)
        {
            using var connection = _dbConnectionFactory.GetConnection();
            IDbTransaction transaction = null;

            try
            {
                if (connection is System.Data.Common.DbConnection dbConn)
                    await dbConn.OpenAsync();
                else
                    connection.Open();

                transaction = connection.BeginTransaction();

                // 🔹 1. Generate Check-In ID (CI001)
                const string seqQuery = @"
            SELECT IFNULL(MAX(CAST(SUBSTRING(check_in_id, 3) AS UNSIGNED)), 0)
            FROM check_ins;
        ";

                // ✅ FIXED: pass null for params, transaction as 3rd argument
                var seqResult = await connection.ExecuteScalarAsync<int>(
                    seqQuery,
                    null,
                    transaction
                );

                var nextNum = seqResult + 1;
                var checkInId = $"CI{nextNum.ToString().PadLeft(3, '0')}";

                // 🔹 2. Insert WITH check_in_id
                const string insertQuery = @"
        INSERT INTO check_ins (
            check_in_id,
            volunteer_id, team_lead_id,
            check_in_date, duration_min, meeting_type,
            emotional_tone, capacity_adjustment, new_capacity_band,
            concerns_noted, follow_up_needed,
            completion_rate_discussed, boundary_issues,
            training_needs, action_items, next_check_in_date,
            created_at
        ) VALUES (
            @CheckInId,
            @VolunteerId, @TeamLeadId,
            @CheckInDate, @DurationMin, @MeetingType,
            @EmotionalTone, @CapacityAdjustment, @NewCapacityBand,
            @ConcernsNoted, @FollowUpNeeded,
            @CompletionRateDiscussed, @BoundaryIssues,
            @TrainingNeeds, @ActionItems, @NextCheckInDate,
            NOW()
        );";

                var checkInDate = DateTime.UtcNow;

                var nextCheckInDate = dto.NextCheckInDate
                    ?? checkInDate.AddDays(30);
            

                await connection.ExecuteAsync(insertQuery, new
                {
                    CheckInId = checkInId,
                    dto.VolunteerId,
                    dto.TeamLeadId,

                    // ✅ always today's date
                    CheckInDate = checkInDate,

                    dto.DurationMin,
                    MeetingType = dto.MeetingType ?? "Monthly",
                    dto.EmotionalTone,
                    dto.CapacityAdjustment,
                    dto.NewCapacityBand,
                    dto.ConcernsNoted,
                    dto.FollowUpNeeded,
                    dto.CompletionRateDiscussed,
                    dto.BoundaryIssues,
                    dto.TrainingNeeds,
                    dto.ActionItems,

                    // ✅ if null => today + 30 days
                    NextCheckInDate = nextCheckInDate

                }, transaction);

               // if (dto.CapacityAdjustment)
               // {  // 🔹 3. Update volunteer
                    const string updateVolunteerQuery = @"
                                                            UPDATE volunteers SET
                                                                last_check_in = @CheckInDate,
                                                                next_check_in = @NextCheckInDate,
                                                                emotional_tone = @EmotionalTone,
                                                                capacity_band=@NewCapacityBand,
                                                                capacity_min=@capacityMin,
                                                                capacity_max=@capacityMax
                                                                WHERE volunteer_id = @VolunteerId";

                    await connection.ExecuteAsync(updateVolunteerQuery, new
                    {
                        CheckInDate = dto.CheckInDate ?? DateTime.UtcNow,
                        nextCheckInDate,
                        dto.NewCapacityBand,
                        dto.CapacityMin,
                        dto.CapacityMax,
                        dto.EmotionalTone,
                        dto.VolunteerId
                    }, transaction);

              //  }
              

                // 🔹 4. Commit
                transaction.Commit();

                return new ApiResponse<string>(
                    ResponseType.Success,
                    "Check-in created successfully",
                    checkInId
                );
            }
            catch (Exception ex)
            {
                // 🔥 IMPORTANT: rollback safely
                transaction?.Rollback();

                return new ApiResponse<string>(
                    ResponseType.Error,
                    $"Error creating check-in: {ex.Message}",
                    null
                );
            }
        }

        // ─────────────────────────────────────────────────────────────
        // HUDDLE NURTURE REVIEW
        // Called when TL opens the check-in / team huddle screen.
        // Returns active sequences + sequences awaiting final decision.
        // ─────────────────────────────────────────────────────────────
        public async Task<ApiResponse<HuddleNurtureReviewDto>> GetHuddleNurtureReviewAsync(string teamLeadId)
        {
            try
            {
                using var connection = _dbConnectionFactory.GetConnection();

                // Active sequences with next step info
                var active = (await connection.QueryAsync<NurtureSequenceSummaryDto>(@"
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
                    JOIN people     p ON p.person_id    = nseq.person_id
                    JOIN volunteers v ON v.volunteer_id = nseq.volunteer_id
                    LEFT JOIN nurture_steps ns ON ns.sequence_id  = nseq.sequence_id
                                              AND ns.step_number  = nseq.current_step
                                              AND ns.status       = 'Pending'
                    WHERE nseq.team_lead_id = @TeamLeadId
                      AND nseq.status = 'Active'
                    ORDER BY ns.scheduled_date ASC",
                    new { TeamLeadId = teamLeadId })).ToList();

                // Sequences awaiting TL final decision
                var awaitingReview = (await connection.QueryAsync<NurtureSequenceSummaryDto>(@"
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
                    JOIN people     p ON p.person_id    = nseq.person_id
                    JOIN volunteers v ON v.volunteer_id = nseq.volunteer_id
                    WHERE nseq.team_lead_id = @TeamLeadId
                      AND nseq.status = 'InReview'
                    ORDER BY nseq.updated_at ASC",
                    new { TeamLeadId = teamLeadId })).ToList();

                var dto = new HuddleNurtureReviewDto
                {
                    TotalActive            = active.Count,
                    OverdueSteps           = active.Count(s => s.NextStepStatus == "Overdue"),
                    AwaitingFinalDecision  = awaitingReview.Count,
                    ActiveSequences        = active,
                    AwaitingReview         = awaitingReview
                };

                return new ApiResponse<HuddleNurtureReviewDto>(ResponseType.Success, "OK", dto);
            }
            catch (Exception ex)
            {
                return new ApiResponse<HuddleNurtureReviewDto>(
                    ResponseType.Error,
                    $"Error fetching huddle nurture review: {ex.Message}",
                    null
                );
            }
        }

    }
}
