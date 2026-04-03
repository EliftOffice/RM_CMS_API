using Dapper;
using RM_CMS.Data;
using RM_CMS.Data.DTO.TeamLeads;
using RM_CMS.Utilities;

namespace RM_CMS.DAL.TeamLeads
{
    public interface ICheckInDAL
    {
        Task<ApiResponse<string>> CreateCheckInAsync(CreateCheckInDTO dto);
    }
    public class CheckInDAL: ICheckInDAL
    {
        private readonly IDbConnectionFactory _dbConnectionFactory;
        public CheckInDAL(IDbConnectionFactory dbConnectionFactory)
        {
            _dbConnectionFactory = dbConnectionFactory;
        }

        public async Task<ApiResponse<string>> CreateCheckInAsync(CreateCheckInDTO dto)
        {
            using var connection = _dbConnectionFactory.GetConnection();

            try
            {
                if (connection is System.Data.Common.DbConnection dbConn)
                    await dbConn.OpenAsync();
                else
                    connection.Open();

                using var transaction = connection.BeginTransaction();

                var year = DateTime.UtcNow.Year;

                // 1️⃣ Insert WITHOUT check_in_id
                const string insertQuery = @"
        INSERT INTO check_ins (
            volunteer_id, team_lead_id,
            check_in_date, duration_min, meeting_type,
            emotional_tone, capacity_adjustment, new_capacity_band,
            concerns_noted, follow_up_needed,
            completion_rate_discussed, boundary_issues,
            training_needs, action_items, next_check_in_date,
            created_at
        ) VALUES (
            @VolunteerId, @TeamLeadId,
            @CheckInDate, @DurationMin, @MeetingType,
            @EmotionalTone, @CapacityAdjustment, @NewCapacityBand,
            @ConcernsNoted, @FollowUpNeeded,
            @CompletionRateDiscussed, @BoundaryIssues,
            @TrainingNeeds, @ActionItems, @NextCheckInDate,
            NOW()
        );
        SELECT LAST_INSERT_ID();";

                var newId = await connection.ExecuteScalarAsync<long>(insertQuery, new
                {
                    dto.VolunteerId,
                    dto.TeamLeadId,
                    CheckInDate = dto.CheckInDate ?? DateTime.UtcNow,
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
                    dto.NextCheckInDate
                }, transaction);

                // 2️⃣ Generate ID → CHK2026001
                var checkInId = $"CI{newId.ToString().PadLeft(3, '0')}";

                // 3️⃣ ✅ FIX: Use id column (NOT check_in_id)
                const string updateCheckInIdQuery = @"
        UPDATE check_ins
        SET check_in_id = @CheckInId
        WHERE id = @Id";

                await connection.ExecuteAsync(updateCheckInIdQuery, new
                {
                    CheckInId = checkInId,
                    Id = newId
                }, transaction);

                // 4️⃣ Update volunteer
                const string updateVolunteerQuery = @"
        UPDATE volunteers SET
            last_check_in = @CheckInDate,
            next_check_in = @NextCheckInDate,
            emotional_tone = @EmotionalTone
        WHERE volunteer_id = @VolunteerId";

                await connection.ExecuteAsync(updateVolunteerQuery, new
                {
                    CheckInDate = dto.CheckInDate ?? DateTime.UtcNow,
                    dto.NextCheckInDate,
                    dto.EmotionalTone,
                    dto.VolunteerId
                }, transaction);

                transaction.Commit();

                return new ApiResponse<string>(
                    ResponseType.Success,
                    "Check-in created successfully",
                    checkInId
                );
            }
            catch (Exception ex)
            {
                return new ApiResponse<string>(
                    ResponseType.Error,
                    $"Error creating check-in: {ex.Message}",
                    null
                );
            }
        }


    }
}
