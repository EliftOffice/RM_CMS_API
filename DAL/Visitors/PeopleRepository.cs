using RM_CMS.Data.Models;
using RM_CMS.Data;
using RM_CMS.Utilities;
using Dapper;
using System.Data;

namespace RM_CMS.DAL.Visitors
{
    public interface IPeopleRepository
    {
        Task<ApiResponse<People>> GetPersonByIdAsync(string personId);
        Task<ApiResponse<bool>> UpdatePersonAssignmentAsync(string personId, string volunteerId, DateTime nextActionDate);
    }

    public class PeopleRepository : IPeopleRepository
    {
        private readonly IDbConnectionFactory _dbConnectionFactory;

        public PeopleRepository(IDbConnectionFactory dbConnectionFactory)
        {
            _dbConnectionFactory = dbConnectionFactory;
        }

        public async Task<ApiResponse<People>> GetPersonByIdAsync(string personId)
        {
            try
            {
                using (var connection = _dbConnectionFactory.GetConnection())
                {
                    const string query = "SELECT * FROM people WHERE person_id = @PersonId";
                    var person = await connection.QueryFirstOrDefaultAsync<People>(
                        query,
                        new { PersonId = personId }
                    );

                    if (person == null)
                    {
                        return new ApiResponse<People>(
                            ResponseType.Error,
                            $"Person with ID '{personId}' not found",
                            null
                        );
                    }

                    return new ApiResponse<People>(
                        ResponseType.Success,
                        "Person retrieved successfully",
                        person
                    );
                }
            }
            catch (Exception ex)
            {
                return new ApiResponse<People>(
                    ResponseType.Error,
                    $"Error retrieving person: {ex.Message}",
                    null
                );
            }
        }

        public async Task<ApiResponse<bool>> UpdatePersonAssignmentAsync(string personId, string volunteerId, DateTime nextActionDate)
        {
            try
            {
                using (var connection = _dbConnectionFactory.GetConnection())
                {
                    const string query = @"
                        UPDATE people SET
                            assigned_volunteer = @VolunteerId,
                            assigned_date = @AssignedDate,
                            follow_up_status = 'ASSIGNED',
                            next_action_date = @NextActionDate,
                            updated_at = @UpdatedAt
                        WHERE person_id = @PersonId";

                    var parameters = new
                    {
                        VolunteerId = volunteerId,
                        AssignedDate = DateTime.UtcNow,
                        NextActionDate = nextActionDate,
                        UpdatedAt = DateTime.UtcNow,
                        PersonId = personId
                    };

                    var rowsAffected = await connection.ExecuteAsync(query, parameters);

                    if (rowsAffected == 0)
                    {
                        return new ApiResponse<bool>(
                            ResponseType.Error,
                            "Failed to update person assignment",
                            false
                        );
                    }

                    return new ApiResponse<bool>(
                        ResponseType.Success,
                        "Person assignment updated successfully",
                        true
                    );
                }
            }
            catch (Exception ex)
            {
                return new ApiResponse<bool>(
                    ResponseType.Error,
                    $"Error updating person assignment: {ex.Message}",
                    false
                );
            }
        }
    }
}
