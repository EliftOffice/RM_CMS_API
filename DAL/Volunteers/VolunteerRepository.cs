using RM_CMS.Data.Models;
using RM_CMS.Data;
using RM_CMS.Utilities;
using Dapper;
using System.Data;

namespace RM_CMS.DAL.Volunteers
{
    public interface IVolunteerRepository
    {
        Task<ApiResponse<Volunteer>> GetAvailableVolunteerAsync(string campus);
        Task<ApiResponse<bool>> UpdateCurrentAssignmentsAsync(string volunteerId);
    }

    public class VolunteerRepository : IVolunteerRepository
    {
        private readonly IDbConnectionFactory _dbConnectionFactory;

        public VolunteerRepository(IDbConnectionFactory dbConnectionFactory)
        {
            _dbConnectionFactory = dbConnectionFactory;
        }

        public async Task<ApiResponse<Volunteer>> GetAvailableVolunteerAsync(string campus)
        {
            try
            {
                using (var connection = _dbConnectionFactory.GetConnection())
                {
                    const string query = @"
                        SELECT v.volunteer_id VolunteerId, v.first_name, v.last_name, 
                               v.email, v.phone, v.capacity_max, v.current_assignments,
                               v.status, v.level, v.start_date, v.end_date, v.capacity_band,
                               v.capacity_min, v.total_completed, v.total_assigned,
                               v.completion_rate, v.avg_response_time, v.last_check_in,
                               v.next_check_in, v.emotional_tone, v.vnps_score,
                               v.burnout_risk, v.team_lead, v.campus, v.level_0_complete,
                               v.crisis_trained, v.confidentiality_signed, v.background_check,
                               v.boundary_violations, v.last_violation_date, v.created_at, v.updated_at
                        FROM volunteers v
                        WHERE LOWER(v.status) = 'active'
                          AND v.current_assignments < v.capacity_max
                          AND v.campus = @Campus
                        ORDER BY v.current_assignments ASC, RAND()
                        LIMIT 1";

                    var volunteer = await connection.QueryFirstOrDefaultAsync<Volunteer>(
                        query,
                        new { Campus = campus }
                    );

                    if (volunteer == null)
                    {
                        return new ApiResponse<Volunteer>(
                            ResponseType.Warning,
                            "No available volunteers with capacity in this campus",
                            null
                        );
                    }

                    return new ApiResponse<Volunteer>(
                        ResponseType.Success,
                        "Available volunteer found",
                        volunteer
                    );
                }
            }
            catch (Exception ex)
            {
                return new ApiResponse<Volunteer>(
                    ResponseType.Error,
                    $"Error retrieving available volunteer: {ex.Message}",
                    null
                );
            }
        }

        public async Task<ApiResponse<bool>> UpdateCurrentAssignmentsAsync(string volunteerId)
        {
            try
            {
                using (var connection = _dbConnectionFactory.GetConnection())
                {
                    const string query = @"
                        UPDATE volunteers SET
                            current_assignments = current_assignments + 1,
                            updated_at = @UpdatedAt
                        WHERE volunteer_id = @VolunteerId";

                    var parameters = new
                    {
                        UpdatedAt = DateTime.UtcNow,
                        VolunteerId = volunteerId
                    };

                    var rowsAffected = await connection.ExecuteAsync(query, parameters);

                    if (rowsAffected == 0)
                    {
                        return new ApiResponse<bool>(
                            ResponseType.Error,
                            "Failed to update volunteer assignments",
                            false
                        );
                    }

                    return new ApiResponse<bool>(
                        ResponseType.Success,
                        "Volunteer assignments updated successfully",
                        true
                    );
                }
            }
            catch (Exception ex)
            {
                return new ApiResponse<bool>(
                    ResponseType.Error,
                    $"Error updating volunteer assignments: {ex.Message}",
                    false
                );
            }
        }
    }
}
