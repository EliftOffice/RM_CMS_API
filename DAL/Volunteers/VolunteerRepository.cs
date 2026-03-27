using RM_CMS.Data.Models;
using RM_CMS.Data;
using Dapper;
using System.Data;

namespace RM_CMS.DAL.Volunteers
{
    public interface IVolunteerRepository
    {
        Task<IEnumerable<Volunteer>> GetAllAsync();
        Task<Volunteer?> GetByIdAsync(string volunteerId);
        Task<IEnumerable<Volunteer>> GetByStatusAsync(string status);
        Task<IEnumerable<Volunteer>> GetByTeamLeadAsync(string teamLeadId);
        Task<IEnumerable<Volunteer>> GetByCapacityBandAsync(string capacityBand);
        Task<bool> CreateAsync(Volunteer volunteer);
        Task<bool> UpdateAsync(Volunteer volunteer);
        Task<bool> DeleteAsync(string volunteerId);
        Task<int> GetTotalCountAsync();
        Task<IEnumerable<Volunteer>> GetPaginatedAsync(int pageNumber, int pageSize);
        Task<bool> UpdateStatusAsync(string volunteerId, string status);
        Task<bool> UpdateCapacityAsync(string volunteerId, string capacityBand, int min, int max);
        Task<bool> UpdatePerformanceAsync(string volunteerId, int completed, int assigned);
        Task<bool> UpdateCheckInAsync(string volunteerId, DateTime? lastCheckIn, DateTime? nextCheckIn);
        Task<int> GetActiveVolunteerCountAsync();
        Task<IEnumerable<Volunteer>> GetWithLowCompletionRateAsync(decimal threshold);
    }

    public class VolunteerRepository : IVolunteerRepository
    {
        private readonly IDbConnectionFactory _dbConnectionFactory;

        public VolunteerRepository(IDbConnectionFactory dbConnectionFactory)
        {
            _dbConnectionFactory = dbConnectionFactory;
        }

        public async Task<IEnumerable<Volunteer>> GetAllAsync()
        {
            using (var connection = _dbConnectionFactory.GetConnection())
            {
                connection.Open();
                var query = "SELECT * FROM volunteers ORDER BY created_at DESC";
                return await connection.QueryAsync<Volunteer>(query);
            }
        }

        public async Task<Volunteer?> GetByIdAsync(string volunteerId)
        {
            using (var connection = _dbConnectionFactory.GetConnection())
            {
                connection.Open();
                var query = "SELECT * FROM volunteers WHERE volunteer_id = @VolunteerId";
                return await connection.QueryFirstOrDefaultAsync<Volunteer>(query, new { VolunteerId = volunteerId });
            }
        }

        public async Task<IEnumerable<Volunteer>> GetByStatusAsync(string status)
        {
            using (var connection = _dbConnectionFactory.GetConnection())
            {
                connection.Open();
                var query = "SELECT * FROM volunteers WHERE status = @Status ORDER BY first_name ASC";
                return await connection.QueryAsync<Volunteer>(query, new { Status = status });
            }
        }

        public async Task<IEnumerable<Volunteer>> GetByTeamLeadAsync(string teamLeadId)
        {
            using (var connection = _dbConnectionFactory.GetConnection())
            {
                connection.Open();
                var query = "SELECT * FROM volunteers WHERE team_lead = @TeamLead ORDER BY first_name ASC";
                return await connection.QueryAsync<Volunteer>(query, new { TeamLead = teamLeadId });
            }
        }

        public async Task<IEnumerable<Volunteer>> GetByCapacityBandAsync(string capacityBand)
        {
            using (var connection = _dbConnectionFactory.GetConnection())
            {
                connection.Open();
                var query = "SELECT * FROM volunteers WHERE capacity_band = @CapacityBand ORDER BY first_name ASC";
                return await connection.QueryAsync<Volunteer>(query, new { CapacityBand = capacityBand });
            }
        }

        public async Task<bool> CreateAsync(Volunteer volunteer)
        {
            using (var connection = _dbConnectionFactory.GetConnection())
            {
                connection.Open();
                var query = @"
                    INSERT INTO volunteers (
                        volunteer_id, first_name, last_name, email, phone, status, level,
                        start_date, end_date, capacity_band, capacity_min, capacity_max,
                        current_assignments, total_completed, total_assigned, completion_rate,
                        avg_response_time, last_check_in, next_check_in, emotional_tone,
                        vnps_score, burnout_risk, team_lead, campus, level_0_complete,
                        crisis_trained, confidentiality_signed, background_check,
                        boundary_violations, last_violation_date, created_at, updated_at
                    ) VALUES (
                        @VolunteerId, @FirstName, @LastName, @Email, @Phone, @Status, @Level,
                        @StartDate, @EndDate, @CapacityBand, @CapacityMin, @CapacityMax,
                        @CurrentAssignments, @TotalCompleted, @TotalAssigned, @CompletionRate,
                        @AvgResponseTime, @LastCheckIn, @NextCheckIn, @EmotionalTone,
                        @VnpsScore, @BurnoutRisk, @TeamLead, @Campus, @Level0Complete,
                        @CrisisTrained, @ConfidentialitySigned, @BackgroundCheck,
                        @BoundaryViolations, @LastViolationDate, @CreatedAt, @UpdatedAt
                    )";

                var result = await connection.ExecuteAsync(query, volunteer);
                return result > 0;
            }
        }

        public async Task<bool> UpdateAsync(Volunteer volunteer)
        {
            using (var connection = _dbConnectionFactory.GetConnection())
            {
                connection.Open();
                var query = @"
                    UPDATE volunteers SET
                        first_name = @FirstName,
                        last_name = @LastName,
                        email = @Email,
                        phone = @Phone,
                        status = @Status,
                        level = @Level,
                        end_date = @EndDate,
                        capacity_band = @CapacityBand,
                        capacity_min = @CapacityMin,
                        capacity_max = @CapacityMax,
                        current_assignments = @CurrentAssignments,
                        total_completed = @TotalCompleted,
                        total_assigned = @TotalAssigned,
                        completion_rate = @CompletionRate,
                        avg_response_time = @AvgResponseTime,
                        last_check_in = @LastCheckIn,
                        next_check_in = @NextCheckIn,
                        emotional_tone = @EmotionalTone,
                        vnps_score = @VnpsScore,
                        burnout_risk = @BurnoutRisk,
                        team_lead = @TeamLead,
                        campus = @Campus,
                        level_0_complete = @Level0Complete,
                        crisis_trained = @CrisisTrained,
                        confidentiality_signed = @ConfidentialitySigned,
                        background_check = @BackgroundCheck,
                        boundary_violations = @BoundaryViolations,
                        last_violation_date = @LastViolationDate,
                        updated_at = @UpdatedAt
                    WHERE volunteer_id = @VolunteerId";

                var result = await connection.ExecuteAsync(query, volunteer);
                return result > 0;
            }
        }

        public async Task<bool> DeleteAsync(string volunteerId)
        {
            using (var connection = _dbConnectionFactory.GetConnection())
            {
                connection.Open();
                var query = "DELETE FROM volunteers WHERE volunteer_id = @VolunteerId";
                var result = await connection.ExecuteAsync(query, new { VolunteerId = volunteerId });
                return result > 0;
            }
        }

        public async Task<int> GetTotalCountAsync()
        {
            using (var connection = _dbConnectionFactory.GetConnection())
            {
                connection.Open();
                var query = "SELECT COUNT(*) FROM volunteers";
                return await connection.ExecuteScalarAsync<int>(query);
            }
        }

        public async Task<IEnumerable<Volunteer>> GetPaginatedAsync(int pageNumber, int pageSize)
        {
            using (var connection = _dbConnectionFactory.GetConnection())
            {
                connection.Open();
                var query = @"
                    SELECT * FROM volunteers 
                    ORDER BY created_at DESC 
                    LIMIT @PageSize OFFSET @Offset";
                
                var offset = (pageNumber - 1) * pageSize;
                return await connection.QueryAsync<Volunteer>(query, new { Offset = offset, PageSize = pageSize });
            }
        }

        public async Task<bool> UpdateStatusAsync(string volunteerId, string status)
        {
            using (var connection = _dbConnectionFactory.GetConnection())
            {
                connection.Open();
                var query = @"
                    UPDATE volunteers 
                    SET status = @Status, updated_at = @UpdatedAt 
                    WHERE volunteer_id = @VolunteerId";

                var result = await connection.ExecuteAsync(query, new 
                { 
                    VolunteerId = volunteerId, 
                    Status = status, 
                    UpdatedAt = DateTime.UtcNow 
                });
                return result > 0;
            }
        }

        public async Task<bool> UpdateCapacityAsync(string volunteerId, string capacityBand, int min, int max)
        {
            using (var connection = _dbConnectionFactory.GetConnection())
            {
                connection.Open();
                var query = @"
                    UPDATE volunteers 
                    SET capacity_band = @CapacityBand, capacity_min = @Min, capacity_max = @Max, updated_at = @UpdatedAt 
                    WHERE volunteer_id = @VolunteerId";

                var result = await connection.ExecuteAsync(query, new 
                { 
                    VolunteerId = volunteerId, 
                    CapacityBand = capacityBand,
                    Min = min,
                    Max = max,
                    UpdatedAt = DateTime.UtcNow 
                });
                return result > 0;
            }
        }

        public async Task<bool> UpdatePerformanceAsync(string volunteerId, int completed, int assigned)
        {
            using (var connection = _dbConnectionFactory.GetConnection())
            {
                connection.Open();
                var query = @"
                    UPDATE volunteers 
                    SET total_completed = @Completed, total_assigned = @Assigned, 
                        completion_rate = (@Completed * 100.0 / @Assigned), updated_at = @UpdatedAt 
                    WHERE volunteer_id = @VolunteerId";

                var result = await connection.ExecuteAsync(query, new 
                { 
                    VolunteerId = volunteerId, 
                    Completed = completed,
                    Assigned = assigned,
                    UpdatedAt = DateTime.UtcNow 
                });
                return result > 0;
            }
        }

        public async Task<bool> UpdateCheckInAsync(string volunteerId, DateTime? lastCheckIn, DateTime? nextCheckIn)
        {
            using (var connection = _dbConnectionFactory.GetConnection())
            {
                connection.Open();
                var query = @"
                    UPDATE volunteers 
                    SET last_check_in = @LastCheckIn, next_check_in = @NextCheckIn, updated_at = @UpdatedAt 
                    WHERE volunteer_id = @VolunteerId";

                var result = await connection.ExecuteAsync(query, new 
                { 
                    VolunteerId = volunteerId, 
                    LastCheckIn = lastCheckIn,
                    NextCheckIn = nextCheckIn,
                    UpdatedAt = DateTime.UtcNow 
                });
                return result > 0;
            }
        }

        public async Task<int> GetActiveVolunteerCountAsync()
        {
            using (var connection = _dbConnectionFactory.GetConnection())
            {
                connection.Open();
                var query = "SELECT COUNT(*) FROM volunteers WHERE status = 'Active'";
                return await connection.ExecuteScalarAsync<int>(query);
            }
        }

        public async Task<IEnumerable<Volunteer>> GetWithLowCompletionRateAsync(decimal threshold)
        {
            using (var connection = _dbConnectionFactory.GetConnection())
            {
                connection.Open();
                var query = "SELECT * FROM volunteers WHERE completion_rate < @Threshold AND status = 'Active' ORDER BY completion_rate ASC";
                return await connection.QueryAsync<Volunteer>(query, new { Threshold = threshold });
            }
        }
    }
}
