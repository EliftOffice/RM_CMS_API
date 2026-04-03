using Dapper;
using RM_CMS.Data;
using RM_CMS.Data.DTO.Peoples;
using RM_CMS.Data.Models;
using RM_CMS.Utilities;
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
        //Task<ApiResponse<bool>> UpdatePersonAssignmentAsync(string personId, string volunteerId, DateTime nextActionDate);
        //Task<ApiResponse<bool>> UpdateCurrentAssignmentsAsync(string volunteerId);
    }

    public class PeoplesDAL : IPeoplesDAL
    {
        private readonly IDbConnectionFactory _dbConnectionFactory;
        public PeoplesDAL(IDbConnectionFactory dbConnectionFactory)
        {
            _dbConnectionFactory = dbConnectionFactory;
        }

      

//        public async Task<ApiResponse<People>> CreatePersonAsync(People person)
//        {
//            try
//            {
//                using (var connection = _dbConnectionFactory.GetConnection())
//                {
//                    // generate next person id for current year
//                    var year = DateTime.UtcNow.Year;
//                    int prefixLength = 1 + year.ToString().Length; // e.g. 5 for P2026

//                    const string seqQuery = @"
//SELECT MAX(CAST(SUBSTRING(person_id, @PrefixLengthPlusOne) AS UNSIGNED)) AS max_seq
//FROM people
//WHERE person_id LIKE @LikePattern";

//                    var likePattern = $"P{year}%";

//                    var seqResult = await connection.QueryFirstOrDefaultAsync<int?>(seqQuery, new
//                    {
//                        LikePattern = likePattern,
//                        PrefixLengthPlusOne = prefixLength + 1  // substring starting position
//                    });

//                    var nextNum = (seqResult ?? 0) + 1;

//                    var newId = $"P{year}{nextNum.ToString().PadLeft(4, '0')}";

//                    const string insert = @"
//                INSERT INTO people (
//                    person_id, first_name, last_name, email, phone,
//                    visit_type, first_visit_date, last_visit_date, visit_count,
//                    follow_up_status, follow_up_priority, campus, connection_source,
//                    interested_in, prayer_requests, specific_needs,
//                    created_at, created_by
//                ) VALUES (
//                    @PersonId, @FirstName, @LastName, @Email, @Phone,
//                    @VisitType, @FirstVisitDate, @LastVisitDate, @VisitCount,
//                    @FollowUpStatus, @FollowUpPriority, @Campus, @ConnectionSource,
//                    @InterestedIn, @PrayerRequests, @SpecificNeeds,
//                    @CreatedAt, @CreatedBy
//                )";

//                    var parameters = new
//                    {
//                        PersonId = newId,
//                        person.FirstName,
//                        person.LastName,
//                        person.Email,
//                        person.Phone,
//                        person.VisitType,
//                        FirstVisitDate = person.FirstVisitDate == default ? DateTime.UtcNow : person.FirstVisitDate,
//                        LastVisitDate = person.LastVisitDate ?? DateTime.UtcNow,
//                        VisitCount = person.VisitCount == 0 ? 1 : person.VisitCount,
//                        FollowUpStatus = string.IsNullOrEmpty(person.FollowUpStatus) ? "NEW" : person.FollowUpStatus,
//                        person.FollowUpPriority,
//                        person.Campus,
//                        person.ConnectionSource,
//                        person.InterestedIn,
//                        person.PrayerRequests,
//                        person.SpecificNeeds,
//                        CreatedAt = DateTime.UtcNow,                       
//                        person.CreatedBy
//                    };

//                    var rows = await connection.ExecuteAsync(insert, parameters);

//                    if (rows == 0)
//                    {
//                        return new ApiResponse<People>(ResponseType.Error, "Failed to create person", null);
//                    }

//                    // return created person with assigned id
//                    person.PersonId = newId;
//                    person.CreatedAt = DateTime.UtcNow;
//                    person.UpdatedAt = DateTime.UtcNow;

//                    return new ApiResponse<People>(ResponseType.Success, "Person created", person);
//                }
//            }
//            catch (Exception ex)
//            {
//                return new ApiResponse<People>(ResponseType.Error, $"Error creating person: {ex.Message}", null);
//            }
//        }
        public async Task<ApiResponse<People>> CreatePersonAsync(People person)
        {
            try
            {
                using (var connection = _dbConnectionFactory.GetConnection())
                {
                    var year = DateTime.UtcNow.Year;

                    // 1. Insert the record WITHOUT person_id to get the auto-increment id
                    const string insertWithoutPersonId = @"
                INSERT INTO people (
                    first_name, last_name, email, phone,
                    visit_type, first_visit_date, last_visit_date, visit_count,
                    follow_up_status, follow_up_priority, campus, connection_source,
                    interested_in, prayer_requests, specific_needs,
                    created_at, created_by, age_range, zip_code, household_type
                ) VALUES (
                    @FirstName, @LastName, @Email, @Phone,
                    @VisitType, @FirstVisitDate, @LastVisitDate, @VisitCount,
                    @FollowUpStatus, @FollowUpPriority, @Campus, @ConnectionSource,
                    @InterestedIn, @PrayerRequests, @SpecificNeeds,
                    @CreatedAt, @CreatedBy,@AgeRange, @ZipCode, @HouseholdType
                );
                SELECT LAST_INSERT_ID();";  // Get auto-increment id of the inserted row

                    var parameters = new
                    {
                        person.FirstName,
                        person.LastName,
                        person.Email,
                        person.Phone,
                        person.VisitType,
                        FirstVisitDate = person.FirstVisitDate == default ? DateTime.UtcNow : person.FirstVisitDate,
                        LastVisitDate = person.LastVisitDate ?? DateTime.UtcNow,
                        VisitCount = person.VisitCount == 0 ? 1 : person.VisitCount,
                        FollowUpStatus = string.IsNullOrEmpty(person.FollowUpStatus) ? "NEW" : person.FollowUpStatus,
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
                        person.HouseholdType
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
                            WHERE (@Email IS NOT NULL AND email = @Email)
                               OR (@Phone IS NOT NULL AND phone = @Phone)
                            LIMIT 1;
                        ";

                    var person = await connection.QueryFirstOrDefaultAsync<People>(
                        query,
                        new { Email = email, Phone = phone }
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
                SET visit_count = IFNULL(visit_count, 0) + 1,
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
      



        //public async Task<ApiResponse<bool>> UpdatePersonAssignmentAsync(string personId, string volunteerId, DateTime nextActionDate)
        //{
        //    try
        //    {
        //        using (var connection = _dbConnectionFactory.GetConnection())
        //        {
        //            const string query = @"
        //         UPDATE people SET
        //             assigned_volunteer = @VolunteerId,
        //             assigned_date = @AssignedDate,
        //             follow_up_status = 'ASSIGNED',
        //             next_action_date = @NextActionDate,
        //             updated_at = @UpdatedAt
        //         WHERE person_id = @PersonId";

        //            var parameters = new
        //            {
        //                VolunteerId = volunteerId,
        //                AssignedDate = DateTime.UtcNow,
        //                NextActionDate = nextActionDate,
        //                UpdatedAt = DateTime.UtcNow,
        //                PersonId = personId
        //            };

        //            var rowsAffected = await connection.ExecuteAsync(query, parameters);

        //            if (rowsAffected == 0)
        //            {
        //                return new ApiResponse<bool>(
        //                    ResponseType.Error,
        //                    "Failed to update person assignment",
        //                    false
        //                );
        //            }

        //            return new ApiResponse<bool>(
        //                ResponseType.Success,
        //                "Person assignment updated successfully",
        //                true
        //            );
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        return new ApiResponse<bool>(
        //            ResponseType.Error,
        //            $"Error updating person assignment: {ex.Message}",
        //            false
        //        );
        //    }
        //}
        //public async Task<ApiResponse<bool>> UpdateCurrentAssignmentsAsync(string volunteerId)
        //{
        //    try
        //    {
        //        using (var connection = _dbConnectionFactory.GetConnection())
        //        {
        //            const string query = @"
        //        UPDATE volunteers SET
        //            current_assignments = current_assignments + 1,
        //            updated_at = @UpdatedAt
        //        WHERE volunteer_id = @VolunteerId";

        //            var parameters = new
        //            {
        //                UpdatedAt = DateTime.UtcNow,
        //                VolunteerId = volunteerId
        //            };

        //            var rowsAffected = await connection.ExecuteAsync(query, parameters);

        //            if (rowsAffected == 0)
        //            {
        //                return new ApiResponse<bool>(
        //                    ResponseType.Error,
        //                    "Failed to update volunteer assignments",
        //                    false
        //                );
        //            }

        //            return new ApiResponse<bool>(
        //                ResponseType.Success,
        //                "Volunteer assignments updated successfully",
        //                true
        //            );
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        return new ApiResponse<bool>(
        //            ResponseType.Error,
        //            $"Error updating volunteer assignments: {ex.Message}",
        //            false
        //        );
        //    }
        //}
    }



}
