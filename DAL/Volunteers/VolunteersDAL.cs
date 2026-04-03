using Dapper;
using RM_CMS.Data;
using RM_CMS.Data.DTO.Volunteers;
using RM_CMS.Data.Models;
using RM_CMS.Utilities;

namespace RM_CMS.DAL.Volunteers
{
    public interface IVolunteersDAL
    {
        Task<ApiResponse<AssignedVolunteerDTO>> AssignToVolunteerAsync(string personId);
        Task<ApiResponse<Volunteer>> GetAvailableVolunteerAsync(string campus);
        Task<ApiResponse<Volunteer>> GetVolunteerByIdAsync(string volunteerId);
        Task<ApiResponse<IEnumerable<People>>> GetVolunteerAssignmentsAsync(string volunteerId);
    }

    public class VolunteersDAL : IVolunteersDAL
    {
        private readonly IDbConnectionFactory _dbConnectionFactory;

        public VolunteersDAL(IDbConnectionFactory dbConnectionFactory)
        {
            _dbConnectionFactory = dbConnectionFactory;
        }
        public async Task<ApiResponse<AssignedVolunteerDTO>> AssignToVolunteerAsync(string personId)
        {
            try
            {
                using (var connection = _dbConnectionFactory.GetConnection())
                {
                    // ✅ Open connection: try async if concrete type supports it
                    if (connection is System.Data.Common.DbConnection dbConn)
                    {
                        await dbConn.OpenAsync();
                    }
                    else
                    {
                        connection.Open(); // fallback for plain IDbConnection
                    }

                    // 1. Get person
                    const string personQuery = @"
                SELECT person_id, campus 
                FROM people 
                WHERE person_id = @PersonId;
            ";

                    var person = await connection.QueryFirstOrDefaultAsync<dynamic>(
                        personQuery,
                        new { PersonId = personId }
                    );

                    if (person == null)
                    {
                        return new ApiResponse<AssignedVolunteerDTO>(
                            ResponseType.Error,
                            "Person not found",
                            null
                        );
                    }

                    using (var transaction = connection.BeginTransaction())
                    {
                        // 2. Find & lock volunteer (prevents race condition)
                        const string volunteerQuery = @"
                    SELECT volunteer_id, first_name, last_name, capacity_max, current_assignments
                    FROM volunteers
                    WHERE status = 'Active'
                      AND current_assignments < capacity_max
                      AND campus = @Campus
                    ORDER BY current_assignments ASC, RAND()
                    LIMIT 1
                    FOR UPDATE;
                ";

                        var volunteer = await connection.QueryFirstOrDefaultAsync<AssignedVolunteerDTO>(
                            volunteerQuery,
                            new { Campus = person.campus },
                            transaction
                        );

                        if (volunteer == null)
                        {
                            transaction.Rollback();

                            return new ApiResponse<AssignedVolunteerDTO>(
                                ResponseType.Warning,
                                "No available volunteers",
                                null
                            );
                        }

                        // 3. Update person (use DB time)
                        const string updatePerson = @"
                    UPDATE people SET
                        assigned_volunteer = @VolunteerId,
                        assigned_date = NOW(),
                        follow_up_status = 'ASSIGNED',
                        next_action_date = NOW() + INTERVAL 48 HOUR
                    WHERE person_id = @PersonId;
                ";

                        await connection.ExecuteAsync(updatePerson, new
                        {
                            VolunteerId = volunteer.volunteer_id,
                            PersonId = personId
                        }, transaction);

                        // 4. Update volunteer
                        const string updateVolunteer = @"
                    UPDATE volunteers SET
                        current_assignments = current_assignments + 1,
                        updated_at = NOW()
                    WHERE volunteer_id = @VolunteerId;
                ";

                        await connection.ExecuteAsync(updateVolunteer,
                            new { VolunteerId = volunteer.volunteer_id },
                            transaction);

                        transaction.Commit();

                        return new ApiResponse<AssignedVolunteerDTO>(
                            ResponseType.Success,
                            "Volunteer assigned successfully",
                            volunteer
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                return new ApiResponse<AssignedVolunteerDTO>(
                    ResponseType.Error,
                    $"Error assigning volunteer: {ex.Message}",
                    null
                );
            }
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

        public async Task<ApiResponse<Volunteer>> GetVolunteerByIdAsync(string volunteerId)
        {
            try
            {
                using (var connection = _dbConnectionFactory.GetConnection())
                {
                    const string query = "SELECT * FROM volunteers WHERE volunteer_id = @VolunteerId";

                    var volunteer = await connection.QueryFirstOrDefaultAsync<Volunteer>(
                        query,
                        new { VolunteerId = volunteerId }
                    );

                    if (volunteer == null)
                    {
                        return new ApiResponse<Volunteer>(
                            ResponseType.Error,
                            $"Volunteer with ID '{volunteerId}' not found",
                            null
                        );
                    }

                    return new ApiResponse<Volunteer>(
                        ResponseType.Success,
                        "Volunteer retrieved successfully",
                        volunteer
                    );
                }
            }
            catch (Exception ex)
            {
                return new ApiResponse<Volunteer>(
                    ResponseType.Error,
                    $"Error retrieving volunteer: {ex.Message}",
                    null
                );
            }
        }

        public async Task<ApiResponse<IEnumerable<People>>> GetVolunteerAssignmentsAsync(string volunteerId)
        {
            try
            {
                using (var connection = _dbConnectionFactory.GetConnection())
                {
                    const string query = @"
                SELECT p.* FROM people p
                WHERE p.assigned_volunteer = @VolunteerId
                  AND p.follow_up_status IN ('ASSIGNED', 'RETRY PENDING')
                ORDER BY p.next_action_date";

                    var assignments = await connection.QueryAsync<People>(
                        query,
                        new { VolunteerId = volunteerId }
                    );

                    return new ApiResponse<IEnumerable<People>>(
                        ResponseType.Success,
                        "Volunteer assignments retrieved successfully",
                        assignments
                    );
                }
            }
            catch (Exception ex)
            {
                return new ApiResponse<IEnumerable<People>>(
                    ResponseType.Error,
                    $"Error retrieving assignments: {ex.Message}",
                    null
                );
            }
        }
    }
}