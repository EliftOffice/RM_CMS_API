using RM_CMS.Data.Models;
using RM_CMS.Data;
using Dapper;
using System.Data;

namespace RM_CMS.DAL.Visitors
{
    public interface IPeopleRepository
    {
        Task<IEnumerable<People>> GetAllAsync();
        Task<People?> GetByIdAsync(string personId);
        Task<IEnumerable<People>> GetByStatusAsync(string followUpStatus);
        Task<IEnumerable<People>> GetByAssignedVolunteerAsync(string volunteerId);
        Task<IEnumerable<People>> GetByPriorityAsync(string priority);
        Task<bool> CreateAsync(People people);
        Task<bool> UpdateAsync(People people);
        Task<bool> DeleteAsync(string personId);
        Task<int> GetTotalCountAsync();
        Task<IEnumerable<People>> GetPaginatedAsync(int pageNumber, int pageSize);
        Task<bool> UpdateFollowUpStatusAsync(string personId, string status);
        Task<bool> UpdateAssignmentAsync(string personId, string volunteerId, DateTime assignedDate);
        Task<bool> UpdateLastContactAsync(string personId, DateTime lastContactDate, DateTime? nextActionDate);
    }

    public class PeopleRepository : IPeopleRepository
    {
        private readonly IDbConnectionFactory _dbConnectionFactory;

        public PeopleRepository(IDbConnectionFactory dbConnectionFactory)
        {
            _dbConnectionFactory = dbConnectionFactory;
        }

        public async Task<IEnumerable<People>> GetAllAsync()
        {
            using (var connection = _dbConnectionFactory.GetConnection())
            {
                connection.Open();
                var query = "SELECT * FROM people ORDER BY created_at DESC";
                return await connection.QueryAsync<People>(query);
            }
        }

        public async Task<People?> GetByIdAsync(string personId)
        {
            using (var connection = _dbConnectionFactory.GetConnection())
            {
                connection.Open();
                var query = "SELECT * FROM people WHERE person_id = @PersonId";
                return await connection.QueryFirstOrDefaultAsync<People>(query, new { PersonId = personId });
            }
        }

        public async Task<IEnumerable<People>> GetByStatusAsync(string followUpStatus)
        {
            using (var connection = _dbConnectionFactory.GetConnection())
            {
                connection.Open();
                var query = "SELECT * FROM people WHERE follow_up_status = @FollowUpStatus ORDER BY follow_up_priority DESC, created_at DESC";
                return await connection.QueryAsync<People>(query, new { FollowUpStatus = followUpStatus });
            }
        }

        public async Task<IEnumerable<People>> GetByAssignedVolunteerAsync(string volunteerId)
        {
            using (var connection = _dbConnectionFactory.GetConnection())
            {
                connection.Open();
                var query = "SELECT * FROM people WHERE assigned_volunteer = @VolunteerId ORDER BY follow_up_priority DESC, assigned_date DESC";
                return await connection.QueryAsync<People>(query, new { VolunteerId = volunteerId });
            }
        }

        public async Task<IEnumerable<People>> GetByPriorityAsync(string priority)
        {
            using (var connection = _dbConnectionFactory.GetConnection())
            {
                connection.Open();
                var query = "SELECT * FROM people WHERE follow_up_priority = @Priority ORDER BY assigned_date DESC";
                return await connection.QueryAsync<People>(query, new { Priority = priority });
            }
        }

        public async Task<bool> CreateAsync(People people)
        {
            using (var connection = _dbConnectionFactory.GetConnection())
            {
                connection.Open();
                var query = @"
                    INSERT INTO people (
                        person_id, first_name, last_name, email, phone, age_range, household_type, 
                        zip_code, visit_type, first_visit_date, last_visit_date, visit_count, 
                        connection_source, campus, follow_up_status, follow_up_priority, 
                        assigned_volunteer, assigned_date, last_contact_date, next_action_date, 
                        interested_in, prayer_requests, specific_needs, created_at, updated_at, created_by
                    ) VALUES (
                        @PersonId, @FirstName, @LastName, @Email, @Phone, @AgeRange, @HouseholdType, 
                        @ZipCode, @VisitType, @FirstVisitDate, @LastVisitDate, @VisitCount, 
                        @ConnectionSource, @Campus, @FollowUpStatus, @FollowUpPriority, 
                        @AssignedVolunteer, @AssignedDate, @LastContactDate, @NextActionDate, 
                        @InterestedIn, @PrayerRequests, @SpecificNeeds, @CreatedAt, @UpdatedAt, @CreatedBy
                    )";

                var result = await connection.ExecuteAsync(query, people);
                return result > 0;
            }
        }

        public async Task<bool> UpdateAsync(People people)
        {
            using (var connection = _dbConnectionFactory.GetConnection())
            {
                connection.Open();
                var query = @"
                    UPDATE people SET
                        first_name = @FirstName,
                        last_name = @LastName,
                        email = @Email,
                        phone = @Phone,
                        age_range = @AgeRange,
                        household_type = @HouseholdType,
                        zip_code = @ZipCode,
                        visit_type = @VisitType,
                        last_visit_date = @LastVisitDate,
                        visit_count = @VisitCount,
                        connection_source = @ConnectionSource,
                        campus = @Campus,
                        follow_up_status = @FollowUpStatus,
                        follow_up_priority = @FollowUpPriority,
                        assigned_volunteer = @AssignedVolunteer,
                        assigned_date = @AssignedDate,
                        last_contact_date = @LastContactDate,
                        next_action_date = @NextActionDate,
                        interested_in = @InterestedIn,
                        prayer_requests = @PrayerRequests,
                        specific_needs = @SpecificNeeds,
                        updated_at = @UpdatedAt
                    WHERE person_id = @PersonId";

                var result = await connection.ExecuteAsync(query, people);
                return result > 0;
            }
        }

        public async Task<bool> DeleteAsync(string personId)
        {
            using (var connection = _dbConnectionFactory.GetConnection())
            {
                connection.Open();
                var query = "DELETE FROM people WHERE person_id = @PersonId";
                var result = await connection.ExecuteAsync(query, new { PersonId = personId });
                return result > 0;
            }
        }

        public async Task<int> GetTotalCountAsync()
        {
            using (var connection = _dbConnectionFactory.GetConnection())
            {
                connection.Open();
                var query = "SELECT COUNT(*) FROM people";
                return await connection.ExecuteScalarAsync<int>(query);
            }
        }

        public async Task<IEnumerable<People>> GetPaginatedAsync(int pageNumber, int pageSize)
        {
            using (var connection = _dbConnectionFactory.GetConnection())
            {
                connection.Open();
                var query = @"
                    SELECT * FROM people 
                    ORDER BY created_at DESC 
                    OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";
                
                var offset = (pageNumber - 1) * pageSize;
                return await connection.QueryAsync<People>(query, new { Offset = offset, PageSize = pageSize });
            }
        }

        public async Task<bool> UpdateFollowUpStatusAsync(string personId, string status)
        {
            using (var connection = _dbConnectionFactory.GetConnection())
            {
                connection.Open();
                var query = @"
                    UPDATE people 
                    SET follow_up_status = @Status, updated_at = @UpdatedAt 
                    WHERE person_id = @PersonId";

                var result = await connection.ExecuteAsync(query, new 
                { 
                    PersonId = personId, 
                    Status = status, 
                    UpdatedAt = DateTime.UtcNow 
                });
                return result > 0;
            }
        }

        public async Task<bool> UpdateAssignmentAsync(string personId, string volunteerId, DateTime assignedDate)
        {
            using (var connection = _dbConnectionFactory.GetConnection())
            {
                connection.Open();
                var query = @"
                    UPDATE people 
                    SET assigned_volunteer = @VolunteerId, assigned_date = @AssignedDate, 
                        follow_up_status = 'Assigned', updated_at = @UpdatedAt 
                    WHERE person_id = @PersonId";

                var result = await connection.ExecuteAsync(query, new 
                { 
                    PersonId = personId, 
                    VolunteerId = volunteerId, 
                    AssignedDate = assignedDate,
                    UpdatedAt = DateTime.UtcNow 
                });
                return result > 0;
            }
        }

        public async Task<bool> UpdateLastContactAsync(string personId, DateTime lastContactDate, DateTime? nextActionDate)
        {
            using (var connection = _dbConnectionFactory.GetConnection())
            {
                connection.Open();
                var query = @"
                    UPDATE people 
                    SET last_contact_date = @LastContactDate, next_action_date = @NextActionDate, updated_at = @UpdatedAt 
                    WHERE person_id = @PersonId";

                var result = await connection.ExecuteAsync(query, new 
                { 
                    PersonId = personId, 
                    LastContactDate = lastContactDate,
                    NextActionDate = nextActionDate,
                    UpdatedAt = DateTime.UtcNow 
                });
                return result > 0;
            }
        }
    }
}
