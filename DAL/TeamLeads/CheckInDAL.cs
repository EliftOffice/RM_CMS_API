using Dapper;
using RM_CMS.Data;
using RM_CMS.Data.DTO.TeamLeads;
using RM_CMS.Utilities;
using System.Data;

namespace RM_CMS.DAL.TeamLeads
{
    public interface ICheckInDAL
    {
        Task<ApiResponse<string>> CreateCheckInAsync(CreateCheckInDTO dto);
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

                var checkInDate = dto.CheckInDate ?? DateTime.UtcNow;
                var nextCheckInDate = checkInDate.AddDays(30);
                await connection.ExecuteAsync(insertQuery, new
                {
                    CheckInId = checkInId,
                    dto.VolunteerId,
                    dto.TeamLeadId,
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
                    nextCheckInDate
                }, transaction);

                // 🔹 3. Update volunteer
                const string updateVolunteerQuery = @"
        UPDATE volunteers SET
            last_check_in = @CheckInDate,
            next_check_in = @NextCheckInDate,
            emotional_tone = @EmotionalTone
        WHERE volunteer_id = @VolunteerId";

                await connection.ExecuteAsync(updateVolunteerQuery, new
                {
                    CheckInDate = dto.CheckInDate ?? DateTime.UtcNow,
                    nextCheckInDate,
                    dto.EmotionalTone,
                    dto.VolunteerId
                }, transaction);

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


    }
}
