using Dapper;
using Microsoft.AspNetCore.Http.HttpResults;
using RM_CMS.Data;
using RM_CMS.Data.DTO.Peoples;
using RM_CMS.Data.Models;
using RM_CMS.Utilities;
using System.Reflection.Emit;
using System.Text;

namespace RM_CMS.DAL.Peoples
{

    public interface IPeoplesDAL
    {

        Task<ApiResponse<People>> CreatePersonAsync(People datas);
        Task<ApiResponse<People>> FindByEmailOrPhoneAsync(string? email, string? phone);
        Task<ApiResponse<bool>> UpdatePersonVisitAsync(string personId);
        Task<ApiResponse<bool>> IncrementVisitCountAsync(string personId);
        Task<ApiResponse<People>> GetPersonByIdAsync(string personId);
        Task<ApiResponse<IEnumerable<People>>> GetPeopleByFilterAsync(PeoplesFilterDTO filter);
    }

    public class PeoplesDAL : IPeoplesDAL
    {
        private readonly IDbConnectionFactory _dbConnectionFactory;
        public PeoplesDAL(IDbConnectionFactory dbConnectionFactory)
        {
            _dbConnectionFactory = dbConnectionFactory;
        }
        public async Task<ApiResponse<People>> CreatePersonAsync(People person)
        {
            try
            {
                using (var connection = _dbConnectionFactory.GetConnection())
                {
                    var year = DateTime.UtcNow.Year;
                    person.PersonId = Guid.NewGuid().ToString();

                    // 1. Insert the record WITHOUT person_id to get the auto-increment id
                    const string insertWithoutPersonId = @"
                INSERT INTO people (person_id,
                    first_name, last_name, email, phone,
                    visit_type, first_visit_date, last_visit_date, visit_count,
                    follow_up_status, follow_up_priority, campus, connection_source,
                    interested_in, prayer_requests, specific_needs,
                    created_at, created_by, age_range, zip_code, household_type,reference_name, reference_phone,address
                ) VALUES (
                    @PersonId,@FirstName, @LastName, @Email, @Phone,
                    @VisitType, @FirstVisitDate, @LastVisitDate, @VisitCount,
                    @FollowUpStatus, @FollowUpPriority, @Campus, @ConnectionSource,
                    @InterestedIn, @PrayerRequests, @SpecificNeeds,
                    @CreatedAt, @CreatedBy,@AgeRange, @ZipCode, @HouseholdType,@RefName, @RefPhone, @Address
                );
                SELECT LAST_INSERT_ID();";  // Get auto-increment id of the inserted row

                    var parameters = new
                    {
                        person.PersonId, // This will be updated later
                        person.FirstName,
                        person.LastName,
                        person.Email,
                        person.Phone,
                        person.VisitType,
                        person.FirstVisitDate,
                        person.LastVisitDate,
                        person.VisitCount,
                        person.FollowUpStatus,
                        person.FollowUpPriority,
                        person.Campus,
                        person.ConnectionSource,
                        person.InterestedIn,
                        person.PrayerRequests,
                        person.SpecificNeeds,
                        CreatedAt = DateTime.UtcNow,
                        person.CreatedBy,
                        person.AgeRange,
                        person.ZipCode,
                        person.HouseholdType,
                        person.refPhone,
                        person.RefName,
                        person.Address
                    };

                    // 2. Insert and get the new auto-increment ID
                    var newId = await connection.ExecuteScalarAsync<long>(insertWithoutPersonId, parameters);

                    // 3. Construct the person_id as "P" + year + auto_increment_id padded to 6 digits
                    var newPersonId = $"P{year}{newId.ToString().PadLeft(3, '0')}";

                    // 4. Update the record with the generated person_id
                    const string updatePersonId = @"
                UPDATE people
                SET person_id = @PersonId,
                    updated_at = @UpdatedAt
                WHERE id = @Id;
            ";

                    await connection.ExecuteAsync(updatePersonId, new
                    {
                        PersonId = newPersonId,
                        UpdatedAt = DateTime.UtcNow,
                        Id = newId
                    });

                    // 5. Set the values on the person object and return
                    person.PersonId = newPersonId;
                    person.CreatedAt = DateTime.UtcNow;
                    person.UpdatedAt = DateTime.UtcNow;

                    return new ApiResponse<People>(ResponseType.Success, "Person created", person);
                }
            }
            catch (Exception ex)
            {
                return new ApiResponse<People>(ResponseType.Error, $"Error creating person: {ex.Message}", null);
            }
        }
        public async Task<ApiResponse<People>> FindByEmailOrPhoneAsync(string? email, string? phone)
        {
            try
            {
                using (var connection = _dbConnectionFactory.GetConnection())
                {
                    const string query = @"
                            SELECT 
                                person_id AS PersonId,
                                first_name AS FirstName,
                                last_name AS LastName,
                                email AS Email,
                                phone AS Phone
                            FROM people
                            WHERE (@Phone IS NOT NULL AND phone = @Phone)
                            LIMIT 1;
                        ";
                    //(@Email IS NOT NULL AND email = @Email)
                    //           OR



                    //age_range AS AgeRange,
                    //household_type AS HouseholdType,
                    //zip_code AS ZipCode,
                    //visit_type AS VisitType,
                    //first_visit_date AS FirstVisitDate,
                    //last_visit_date AS LastVisitDate,
                    //visit_count AS VisitCount,
                    //connection_source AS ConnectionSource,
                    //campus AS Campus,
                    //follow_up_status AS FollowUpStatus,
                    //follow_up_priority AS FollowUpPriority,
                    //assigned_volunteer AS AssignedVolunteer,
                    //assigned_date AS AssignedDate,
                    //last_contact_date AS LastContactDate,
                    //next_action_date AS NextActionDate,
                    //interested_in AS InterestedIn,
                    //prayer_requests AS PrayerRequests,
                    //specific_needs AS SpecificNeeds,
                    //created_at AS CreatedAt,
                    //updated_at AS UpdatedAt,
                    //created_by AS CreatedBy
                    var person = await connection.QueryFirstOrDefaultAsync<People>(
                        query,
                        //new { Email = email, Phone = phone }
                        new { Phone = phone }
                    );

                    if (person == null)
                    {
                        return new ApiResponse<People>(ResponseType.Warning, "No matching person found", null);
                    }

                    return new ApiResponse<People>(ResponseType.Success, "Person found", person);
                }
            }
            catch (Exception ex)
            {
                return new ApiResponse<People>(ResponseType.Error, $"Error finding person: {ex.Message}", null);
            }
        }
        public async Task<ApiResponse<bool>> UpdatePersonVisitAsync(string personId)
        {
            try
            {
                using (var connection = _dbConnectionFactory.GetConnection())
                {
                    const string query = @"
                 UPDATE people SET
                     last_visit_date = @LastVisitDate,
                     visit_count = visit_count + 1,
                     visit_type = @VisitType,
                     follow_up_status = @FollowUpStatus,
                     updated_at = @UpdatedAt
                 WHERE person_id = @PersonId";

                    var parameters = new
                    {
                        LastVisitDate = DateTime.UtcNow,
                        VisitType = "Returning Visitor",
                        FollowUpStatus = "NEW",
                        UpdatedAt = DateTime.UtcNow,
                        PersonId = personId
                    };

                    var rows = await connection.ExecuteAsync(query, parameters);

                    if (rows == 0)
                    {
                        return new ApiResponse<bool>(ResponseType.Error, "Failed to update person visit", false);
                    }

                    return new ApiResponse<bool>(ResponseType.Success, "Person visit updated", true);
                }
            }
            catch (Exception ex)
            {
                return new ApiResponse<bool>(ResponseType.Error, $"Error updating person visit: {ex.Message}", false);
            }
        }
        public async Task<ApiResponse<bool>> IncrementVisitCountAsync(string personId)
        {
            try
            {
                using (var connection = _dbConnectionFactory.GetConnection())
                {
                    if (connection is System.Data.Common.DbConnection dbConn)
                        await dbConn.OpenAsync();
                    else
                        connection.Open();

                    const string query = @"
                UPDATE people
                SET visit_count = IFNULL(visit_count, 0) + 1,visit_type='Returning Visitor',
                    updated_at = NOW()
                WHERE person_id = @PersonId;
            ";

                    var rowsAffected = await connection.ExecuteAsync(query, new { PersonId = personId });

                    if (rowsAffected > 0)
                        return new ApiResponse<bool>(ResponseType.Success, "Visit count incremented", true);
                    else
                        return new ApiResponse<bool>(ResponseType.Warning, "Person not found", false);
                }
            }
            catch (Exception ex)
            {
                return new ApiResponse<bool>(ResponseType.Error, $"Error incrementing visit count: {ex.Message}", false);
            }
        }
        public async Task<ApiResponse<People>> GetPersonByIdAsync(string personId)
        {
            try
            {
                using (var connection = _dbConnectionFactory.GetConnection())
                {
                    const string query = @"
SELECT 
    person_id AS PersonId,
    first_name AS FirstName,
    last_name AS LastName,
    email AS Email,
    phone AS Phone,
    age_range AS AgeRange,
    household_type AS HouseholdType,
    zip_code AS ZipCode,
    visit_type AS VisitType,
    first_visit_date AS FirstVisitDate,
    last_visit_date AS LastVisitDate,
    visit_count AS VisitCount,
    connection_source AS ConnectionSource,
    campus AS Campus,
    follow_up_status AS FollowUpStatus,
    follow_up_priority AS FollowUpPriority,
    assigned_volunteer AS AssignedVolunteer,
    assigned_date AS AssignedDate,
    last_contact_date AS LastContactDate,
    next_action_date AS NextActionDate,
    interested_in AS InterestedIn,
    prayer_requests AS PrayerRequests,
    specific_needs AS SpecificNeeds,
    created_at AS CreatedAt,
    updated_at AS UpdatedAt,
    created_by AS CreatedBy
FROM people
WHERE person_id = @PersonId";

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

        public async Task<ApiResponse<IEnumerable<People>>> GetPeopleByFilterAsync(PeoplesFilterDTO filter)
        {
            try
            {
                using (var connection = _dbConnectionFactory.GetConnection())
                {
                    var query = new StringBuilder("SELECT * FROM people WHERE 1=1");
                    var parameters = new DynamicParameters();

                    if (!string.IsNullOrEmpty(filter.Status))
                    {
                        query.Append(" AND follow_up_status = @Status");
                        parameters.Add("Status", filter.Status);
                    }

                    if (!string.IsNullOrEmpty(filter.Priority))
                    {
                        query.Append(" AND follow_up_priority = @Priority");
                        parameters.Add("Priority", filter.Priority);
                    }

                    if (!string.IsNullOrEmpty(filter.AssignedTo))
                    {
                        query.Append(" AND assigned_volunteer = @AssignedTo");
                        parameters.Add("AssignedTo", filter.AssignedTo);
                    }

                    if (!string.IsNullOrEmpty(filter.Campus))
                    {
                        query.Append(" AND campus = @Campus");
                        parameters.Add("Campus", filter.Campus);
                    }

                    query.Append(" ORDER BY created_at DESC LIMIT @Limit");
                    parameters.Add("Limit", filter.Limit);

                    var people = await connection.QueryAsync<People>(
                        query.ToString(),
                        parameters
                    );

                    return new ApiResponse<IEnumerable<People>>(
                        ResponseType.Success,
                        "People retrieved successfully",
                        people
                    );
                }
            }
            catch (Exception ex)
            {
                return new ApiResponse<IEnumerable<People>>(
                    ResponseType.Error,
                    $"Error retrieving people: {ex.Message}",
                    null
                );
            }
        }

    }



}
