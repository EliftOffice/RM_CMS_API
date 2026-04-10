using Dapper;
using RM_CMS.DAL.Peoples;
using RM_CMS.Data;
using RM_CMS.Data.DTO;
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
        Task<ApiResponse<List<People>>> GetVolunteerAssignmentsAsync(string volunteerId);
        Task<ApiResponse<VolunteerResponseDto>> CreateVolunteerAsync(CreateVolunteerDto volunteer);
        Task<ApiResponse<bool>> ExistsByEmailAsync(string email);
        Task<ApiResponse<List<Volunteer>>> GetVolunteersAsync();
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
                    //        const string personQuery = @"
                    //    SELECT person_id, campus 
                    //    FROM people 
                    //    WHERE person_id = @PersonId;
                    //";

                    //        var person = await connection.QueryFirstOrDefaultAsync<dynamic>(
                    //            personQuery,
                    //            new { PersonId = personId }
                    //        );

                    var response = await new PeoplesDAL(_dbConnectionFactory)
                        .GetPersonByIdAsync(personId);

                    if (response.Data == null)
                    {
                        return new ApiResponse<AssignedVolunteerDTO>(
                            ResponseType.Error,
                            "Person not found",
                            null
                        );
                    }

                    // 🔴 Check assignment
                    //if (response.Data.FollowUpStatus.ToLower() == "assigned")
                    //{
                    //    return new ApiResponse<AssignedVolunteerDTO>(
                    //        ResponseType.Error,
                    //        "Person already assigned",
                    //        null
                    //    );
                    //}

                    // ✅ Now you also have campus
                    var campus = response.Data.Campus;

                   

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
                            new { Campus = campus },
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

        public async Task<ApiResponse<List<Volunteer>>> GetVolunteersAsync()
        {
            try
            {
                using (var connection = _dbConnectionFactory.GetConnection())
                {
                    const string query = @"SELECT * FROM volunteers";

                    var volunteers = (await connection.QueryAsync<Volunteer>(query)).ToList();

                    if (!volunteers.Any())
                    {
                        return new ApiResponse<List<Volunteer>>(
                            ResponseType.Warning,
                            "No volunteers found",
                            new List<Volunteer>()
                        );
                    }

                    return new ApiResponse<List<Volunteer>>(
                        ResponseType.Success,
                        "Volunteers retrieved successfully",
                        volunteers
                    );
                }
            }
            catch (Exception ex)
            {
                return new ApiResponse<List<Volunteer>>(
                    ResponseType.Error,
                    $"Error retrieving volunteers: {ex.Message}",
                    new List<Volunteer>()
                );
            }
        }

        public async Task<ApiResponse<List<People>>> GetVolunteerAssignmentsAsync(string volunteerId)
        {
            try
            {
                using (var connection = _dbConnectionFactory.GetConnection())
                {
                    const string query = @"SELECT 
                                            p.person_id            AS PersonId,
                                            p.first_name           AS FirstName,
                                            p.last_name            AS LastName,
                                            p.email                AS Email,
                                            p.phone                AS Phone,
                                            p.age_range            AS AgeRange,
                                            p.household_type       AS HouseholdType,
                                            p.zip_code             AS ZipCode,
                                            p.visit_type           AS VisitType,
                                            p.first_visit_date     AS FirstVisitDate,
                                            p.last_visit_date      AS LastVisitDate,
                                            p.visit_count          AS VisitCount,
                                            p.connection_source    AS ConnectionSource,
                                            p.campus               AS Campus,
                                            p.follow_up_status     AS FollowUpStatus,
                                            p.follow_up_priority   AS FollowUpPriority,
                                            p.assigned_volunteer   AS AssignedVolunteer,
                                            p.assigned_date        AS AssignedDate,
                                            p.last_contact_date    AS LastContactDate,
                                            p.next_action_date     AS NextActionDate,
                                            p.interested_in        AS InterestedIn,
                                            p.prayer_requests      AS PrayerRequests,
                                            p.specific_needs       AS SpecificNeeds,
                                            p.created_at           AS CreatedAt,
                                            p.updated_at           AS UpdatedAt,
                                            p.created_by           AS CreatedBy
                                        FROM people p
                                        WHERE p.assigned_volunteer = @VolunteerId
                                          AND p.follow_up_status IN ('ASSIGNED', 'RETRY PENDING')
                                        ORDER BY p.next_action_date;";

                    var assignments = await connection.QueryAsync<People>(
                        query,
                        new { VolunteerId = volunteerId }
                    );

                    var list = assignments?.ToList() ?? new List<People>();

                    return new ApiResponse<List<People>>(
                        ResponseType.Success,
                        "Volunteer assignments retrieved successfully",
                        list
                    );
                }
            }
            catch (Exception ex)
            {
                return new ApiResponse<List<People>>(
                    ResponseType.Error,
                    $"Error retrieving assignments: {ex.Message}",
                    null
                );
            }
        }

        
        public async Task<ApiResponse<VolunteerResponseDto>> CreateVolunteerAsync(CreateVolunteerDto volunteer)
        {
            try
            {
                using (var connection = _dbConnectionFactory.GetConnection())
                {
                    // 🔹 1. Generate ID
                    const string idQuery = @"
                SELECT CONCAT('V', LPAD(IFNULL(MAX(CAST(SUBSTRING(volunteer_id, 2) AS UNSIGNED)), 0) + 1, 3, '0'))
                FROM volunteers;";

                    var newId = await connection.ExecuteScalarAsync<string>(idQuery);
                    volunteer.VolunteerId = newId;

                    // 🔹 2. Get Capacity from DB
                    const string capacityQuery = @"
                SELECT min_per_week AS Min, max_per_week AS Max
                FROM capacity_bands
                WHERE band_name = @CapacityBand;";

                    var capacity = await connection.QueryFirstOrDefaultAsync<(int Min, int Max)>(
                        capacityQuery,
                        new { CapacityBand = volunteer.CapacityBand }
                    );

                    if (capacity == default)
                    {
                        return new ApiResponse<VolunteerResponseDto>(
                            ResponseType.Error,
                            $"Invalid capacity band: {volunteer.CapacityBand}",
                            null
                        );
                    }

                    // 🔹 3. Insert
                  const string insertQuery = @"
                    INSERT INTO volunteers (
                        volunteer_id,
                        first_name,
                        last_name,
                        email,
                        phone,
                        team_lead,
                        start_date,
                        capacity_band,
                        capacity_min,
                        capacity_max,
                        status,
                        last_check_in,        
                        next_check_in,       
                        created_at,
                        updated_at
                    )
                    VALUES (
                        @VolunteerId,
                        @FirstName,
                        @LastName,
                        @Email,
                        @Phone,
                        @TeamLead,
                        @StartDate,
                        @CapacityBand,
                        @CapacityMin,
                        @CapacityMax,
                        'Active',
                        CURRENT_DATE,         
                        CURRENT_DATE,         
                        NOW(),
                        NOW()
                    );";

                    await connection.ExecuteAsync(insertQuery, new
                    {
                        volunteer.VolunteerId,
                        volunteer.FirstName,
                        volunteer.LastName,
                        volunteer.Email,
                        volunteer.Phone,
                        volunteer.TeamLead,
                        volunteer.StartDate,
                        volunteer.CapacityBand,
                        CapacityMin = capacity.Min,
                        CapacityMax = capacity.Max
                    });

                    // 🔥 4. Fetch FULL DATA → MAP TO DTO (NO MANUAL MAPPING)
                    const string selectQuery = @"
                SELECT 
                    volunteer_id AS VolunteerId,
                    first_name AS FirstName,
                    last_name AS LastName,
                    email AS Email,
                    phone AS Phone,
                    status AS Status,
                    level AS Level,
                    start_date AS StartDate,
                    end_date AS EndDate,
                    capacity_band AS CapacityBand,
                    capacity_min AS CapacityMin,
                    capacity_max AS CapacityMax,
                    current_assignments AS CurrentAssignments,
                    total_completed AS TotalCompleted,
                    total_assigned AS TotalAssigned,
                    completion_rate AS CompletionRate,
                    avg_response_time AS AvgResponseTime,
                    last_check_in AS LastCheckIn,
                    next_check_in AS NextCheckIn,
                    emotional_tone AS EmotionalTone,
                    vnps_score AS VnpsScore,
                    burnout_risk AS BurnoutRisk,
                    team_lead AS TeamLead,
                    campus AS Campus,
                    level_0_complete AS Level0Complete,
                    crisis_trained AS CrisisTrained,
                    confidentiality_signed AS ConfidentialitySigned,
                    background_check AS BackgroundCheck,
                    boundary_violations AS BoundaryViolations,
                    last_violation_date AS LastViolationDate,
                    created_at AS CreatedAt,
                    updated_at AS UpdatedAt
                FROM volunteers
                WHERE volunteer_id = @VolunteerId;";

                    var result = await connection.QueryFirstOrDefaultAsync<VolunteerResponseDto>(
                        selectQuery,
                        new { VolunteerId = volunteer.VolunteerId }
                    );

                    return new ApiResponse<VolunteerResponseDto>(
                        ResponseType.Success,
                        "Volunteer created successfully",
                        result
                    );
                }
            }
            catch (Exception ex)
            {
                return new ApiResponse<VolunteerResponseDto>(
                    ResponseType.Error,
                    $"DAL Error creating volunteer: {ex.Message}",
                    null
                );
            }
        }

        public async Task<ApiResponse<bool>> ExistsByEmailAsync(string email)
        {
            try
            {
                using (var connection = _dbConnectionFactory.GetConnection())
                {
                    const string query = @"
SELECT COUNT(1) 
FROM volunteers 
WHERE LOWER(email) = @Email;";

                    var count = await connection.ExecuteScalarAsync<int>(
                        query,
                        new { Email = email }
                    );

                    return new ApiResponse<bool>(
                        ResponseType.Success,
                        "Checked successfully",
                        count > 0
                    );
                }
            }
            catch (Exception ex)
            {
                return new ApiResponse<bool>(
                    ResponseType.Error,
                    $"DAL Error checking email: {ex.Message}",
                    false
                );
            }
        }

    }
}