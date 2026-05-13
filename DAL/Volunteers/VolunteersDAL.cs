using Dapper;
using RM_CMS.DAL.CommonDAL;
using RM_CMS.DAL.Peoples;
using RM_CMS.Data;
using RM_CMS.Data.DTO;
using RM_CMS.Data.DTO.Volunteers;
using RM_CMS.Data.Models;
using RM_CMS.Utilities;
using System.Text.Json;

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
        Task<ApiResponse<List<VolunteerLookupDto>>> GetVolunteersAsyncByMobile(string mobile);
        Task<ApiResponse<string>> UpdateVolunteerMobileAsync(string volunteerId, string mobile);

        // New DAL methods
        Task<ApiResponse<TelegramChatDto>> GetLatestTelegramChatAsync();
        Task<ApiResponse<bool>> UpdateVolunteerTelegramAsync(UpdateVolunteerTelegramDto dto);

        // Manual assign to specific volunteer
        Task<ApiResponse<AssignedVolunteerDTO>> ManualAssignToVolunteerAsync(string personId, string volunteerId);
    }

    public class VolunteersDAL : IVolunteersDAL
    {
        private readonly IDbConnectionFactory _dbConnectionFactory;
        private readonly IConfiguration _configuration;
        private readonly ITelegram _telegram;

        public VolunteersDAL(IDbConnectionFactory dbConnectionFactory,IConfiguration config,ITelegram tel)
        {
            _dbConnectionFactory = dbConnectionFactory;
            _configuration = config;
            _telegram = tel;
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
                    SELECT volunteer_id, first_name, last_name, capacity_max, current_assignments, telegram_chat_id
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
                        //        const string updateOnAssign = @"
                        //    UPDATE volunteers SET
                        //        current_assignments = current_assignments + 1,
                        //        total_assigned=total_assigned+1,                        
                        //        updated_at = NOW()
                        //    WHERE volunteer_id = @VolunteerId;
                        //";
                        const string updateOnAssign = @"
                                                    UPDATE volunteers
                                                    SET
                                                        current_assignments = current_assignments + 1,
                                                        total_assigned = total_assigned + 1  WHERE volunteer_id = @VolunteerId;
                                                    UPDATE volunteers
                                                    SET
                                                        completion_rate = 
                                                            CASE 
                                                                WHEN total_assigned = 0 THEN 0
                                                                ELSE ROUND((total_completed * 100.0) / (total_assigned), 2)
                                                            END                                                        
                                                    WHERE volunteer_id = @VolunteerId;
                                                    ";

                        await connection.ExecuteAsync(updateOnAssign,
                            new { VolunteerId = volunteer.volunteer_id },
                            transaction);

                        // 🔥 5. Send Telegram AFTER commit
                       

                        transaction.Commit();

                        if (string.IsNullOrEmpty(volunteer.telegram_chat_id))
                        {
                            var message = $@"
👋 Hi {volunteer.first_name},

📌 A new follow-up has been assigned to you.

👉 Please check your dashboard:
https://rmoffice.online/templates/Volunteers/Login.html
🙏 Thank you!
";



                            _ = SendTelegramMessageAsync(volunteer.telegram_chat_id, message);
                        }

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
                    const string query = @"SELECT 
    v.volunteer_id AS VolunteerId,
    v.first_name AS FirstName,
    v.last_name AS LastName,
    v.email AS Email,
    v.phone AS Phone,
    v.status AS Status,
    v.level AS Level,
    v.start_date AS StartDate,
    v.end_date AS EndDate,
    v.capacity_band AS CapacityBand,
    v.capacity_min AS CapacityMin,
    v.capacity_max AS CapacityMax,
    v.current_assignments AS CurrentAssignments,
    v.total_completed AS TotalCompleted,
    v.total_assigned AS TotalAssigned,
    v.completion_rate AS CompletionRate,
    v.avg_response_time AS AvgResponseTime,
    v.last_check_in AS LastCheckIn,
    v.next_check_in AS NextCheckIn,
    v.emotional_tone AS EmotionalTone,
    v.vnps_score AS VnpsScore,
    v.burnout_risk AS BurnoutRisk,
    v.team_lead AS TeamLead,
    v.campus AS Campus,
    v.level_0_complete AS Level0Complete,
    v.crisis_trained AS CrisisTrained,
    v.confidentiality_signed AS ConfidentialitySigned,
    v.background_check AS BackgroundCheck,
    v.boundary_violations AS BoundaryViolations,
    v.last_violation_date AS LastViolationDate,
    v.created_at AS CreatedAt,
    v.updated_at AS UpdatedAt,

    CONCAT(tl.first_name, ' ', tl.last_name) AS TeamLeadFullName

FROM volunteers v
LEFT JOIN team_leads tl 
    ON v.team_lead = tl.team_lead_id
WHERE v.volunteer_id = @VolunteerId;
            ";

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

                    if (volunteers == null || !volunteers.Any())
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

    -- From followups
    f.attempt_date         AS AttemptDate,
    f.response_type        AS ResponseType,

    p.interested_in        AS InterestedIn,
    p.prayer_requests      AS PrayerRequests,
    p.specific_needs       AS SpecificNeeds,
    p.created_at           AS CreatedAt,
    p.updated_at           AS UpdatedAt,
    p.created_by           AS CreatedBy

FROM people p

LEFT JOIN follow_ups f 
    ON f.person_id = p.person_id
    AND f.attempt_number = (
        SELECT MAX(f2.attempt_number)
        FROM follow_ups f2
        WHERE f2.person_id = p.person_id
    )

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
                    // 🔹 2.5 Check duplicate mobile
                    const string duplicateQuery = @"
    SELECT COUNT(1)
    FROM volunteers
    WHERE phone = @Phone;
";

                    var exists = await connection.ExecuteScalarAsync<int>(duplicateQuery, new
                    {
                        Phone = volunteer.Phone
                    });

                    if (exists > 0)
                    {
                        return new ApiResponse<VolunteerResponseDto>(
                            ResponseType.Warning,
                            "Mobile number already exists..Duplicate Entry...",
                            null
                        );
                    }

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
                        updated_at,level,telegram_chat_id
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
                        NOW(),'Level 1',@TelegramChatId
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
                        CapacityMax = capacity.Max,
                         volunteer.TelegramChatId
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
                    if (result != null)
                        SendTelegramMessageAsync(volunteer.TelegramChatId, @"🎉 Welcome to RM Volunteers!

✅ Your registration was completed successfully.");

                    return new ApiResponse<VolunteerResponseDto>(
                        ResponseType.Success,
                        "Volunteer created successfully",
                        result ?? new VolunteerResponseDto()
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

        public async Task<ApiResponse<List<VolunteerLookupDto>>> GetVolunteersAsyncByMobile(string mobile)
        {
            try
            {
                using (var connection = _dbConnectionFactory.GetConnection())
                {
                    const string query = @"
                SELECT 
                    volunteer_id AS VolunteerId,
                    first_name AS FirstName,
                    last_name AS LastName,
                    phone AS Phone,
                    telegram_chat_id as ChatID
                FROM volunteers
                WHERE phone = @Mobile Limit 1;
            ";

                    var volunteers = (await connection.QueryAsync<VolunteerLookupDto>(
                        query,
                        new { Mobile = mobile }
                    )).ToList();

                    if (!volunteers.Any())
                    {
                        return new ApiResponse<List<VolunteerLookupDto>>(
                            ResponseType.Warning,
                            "No volunteers found for this mobile number",
                            new List<VolunteerLookupDto>()
                        );
                    }
                    else
                    {
                        volunteers[0].OTP = GenerateOtp();
                        SendTelegramMessageAsync(volunteers[0].ChatID, volunteers[0].OTP);
                    }

                    return new ApiResponse<List<VolunteerLookupDto>>(
                        ResponseType.Success,
                        "Volunteer retrieved successfully",
                        volunteers
                    );
                }
            }
            catch (Exception ex)
            {
                return new ApiResponse<List<VolunteerLookupDto>>(
                    ResponseType.Error,
                    $"Error retrieving volunteers: {ex.Message}",
                    new List<VolunteerLookupDto>()
                );
            }
        }

        public async Task<ApiResponse<string>> UpdateVolunteerMobileAsync(string volunteerId, string mobile)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(volunteerId) || string.IsNullOrWhiteSpace(mobile))
                {
                    return new ApiResponse<string>(
                        ResponseType.Warning,
                        "VolunteerId and Mobile number are required",
                        null
                    );
                }

                using (var connection = _dbConnectionFactory.GetConnection())
                {
                    const string query = @"
                UPDATE volunteers
                SET phone = @Mobile,
                    updated_at = NOW()
                WHERE volunteer_id = @VolunteerId;
            ";

                    var rowsAffected = await connection.ExecuteAsync(query, new
                    {
                        VolunteerId = volunteerId,
                        Mobile = mobile
                    });

                    if (rowsAffected == 0)
                    {
                        return new ApiResponse<string>(
                            ResponseType.Warning,
                            "Volunteer not found",
                            null
                        );
                    }

                    return new ApiResponse<string>(
                        ResponseType.Success,
                        "Mobile number updated successfully",
                        null
                    );
                }
            }
            catch (Exception ex)
            {
                return new ApiResponse<string>(
                    ResponseType.Error,
                    $"DAL Error updating mobile: {ex.Message}",
                    null
                );
            }
        }      
        
        private async Task SendTelegramMessageAsync(string chatId, string message)
        {
            try
            {
                using var client = new HttpClient();

                //var token = _configuration["Telegram:BotToken"];
                var token = _telegram.GetTelegramBotToken().Result.Data;

                if (string.IsNullOrEmpty(token)) return;

                var url = $"https://api.telegram.org/bot{token}/sendMessage" +
                          $"?chat_id={chatId}&text={Uri.EscapeDataString(message)}";

                await client.GetAsync(url);
            }
            catch
            {
                // Optional: log error (don't break main flow)
            }
        }

        public async Task<ApiResponse<TelegramChatDto>> GetLatestTelegramChatAsync()
        {
            try
            {
               // var token = _configuration["Telegram:BotToken"];
                var token = _telegram.GetTelegramBotToken().Result.Data;
                if (string.IsNullOrEmpty(token))
                    return new ApiResponse<TelegramChatDto>(ResponseType.Error, "Bot token not configured", null);

                using var client = new HttpClient();
                var url = $"https://api.telegram.org/bot{token}/getUpdates?limit=5";
                var resp = await client.GetAsync(url);
                if (!resp.IsSuccessStatusCode)
                    return new ApiResponse<TelegramChatDto>(ResponseType.Error, "Failed to fetch updates", null);

                var json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("result", out var results) && results.GetArrayLength() > 0)
                {
                    // find latest message with chat
                    for (int i = results.GetArrayLength() - 1; i >= 0; i--)
                    {
                        var item = results[i];
                        if (item.TryGetProperty("message", out var message))
                        {
                            if (message.TryGetProperty("chat", out var chat))
                            {
                                var chatId = chat.GetProperty("id").GetRawText().Trim('"');
                                string name = "";
                                if (chat.TryGetProperty("first_name", out var first)) name = first.GetString();
                                if (string.IsNullOrEmpty(name) && chat.TryGetProperty("username", out var un)) name = un.GetString();

                                return new ApiResponse<TelegramChatDto>(ResponseType.Success, "Chat found", new TelegramChatDto { ChatId = chatId, Name = name });
                            }
                        }
                    }
                }

                return new ApiResponse<TelegramChatDto>(ResponseType.Warning, "No chat updates found", null);
            }
            catch (Exception ex)
            {
                return new ApiResponse<TelegramChatDto>(ResponseType.Error, $"Error fetching updates: {ex.Message}", null);
            }
        }

        public async Task<ApiResponse<bool>> UpdateVolunteerTelegramAsync(UpdateVolunteerTelegramDto dto)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dto.VolunteerId) || string.IsNullOrWhiteSpace(dto.TelegramChatId))
                    return new ApiResponse<bool>(ResponseType.Warning, "VolunteerId and TelegramChatId required", false);

                using var connection = _dbConnectionFactory.GetConnection();
                const string q = @"UPDATE volunteers SET telegram_chat_id = @ChatId, updated_at = NOW() WHERE volunteer_id = @VolunteerId";
                var rows = await connection.ExecuteAsync(q, new { ChatId = dto.TelegramChatId, VolunteerId = dto.VolunteerId });
                if (rows == 0)
                    return new ApiResponse<bool>(ResponseType.Warning, "Volunteer not found", false);

                return new ApiResponse<bool>(ResponseType.Success, "Telegram ChatId updated", true);
            }
            catch (Exception ex)
            {
                return new ApiResponse<bool>(ResponseType.Error, $"Error updating telegram id: {ex.Message}", false);
            }
        }

        public async Task<ApiResponse<AssignedVolunteerDTO>> ManualAssignToVolunteerAsync(string personId, string volunteerId)
        {
            try
            {
                using var connection = _dbConnectionFactory.GetConnection();

                if (connection is System.Data.Common.DbConnection dbConn)
                    await dbConn.OpenAsync();
                else
                    connection.Open();

                using var transaction = connection.BeginTransaction();

                // 1. Verify person exists
                const string personQuery = @"SELECT person_id AS PersonId, first_name AS FirstName, last_name AS LastName FROM people WHERE person_id = @PersonId FOR UPDATE";
                var person = await connection.QueryFirstOrDefaultAsync<People>(personQuery, new { PersonId = personId }, transaction);
                if (person == null)
                {
                    transaction.Rollback();
                    return new ApiResponse<AssignedVolunteerDTO>(ResponseType.Error, "Person not found", null);
                }

                // 2. Verify volunteer exists and has capacity
                const string volQuery = @"SELECT volunteer_id AS VolunteerId, first_name AS FirstName, last_name AS LastName, capacity_max, current_assignments FROM volunteers WHERE volunteer_id = @VolunteerId FOR UPDATE";
                var volunteer = await connection.QueryFirstOrDefaultAsync<dynamic>(volQuery, new { VolunteerId = volunteerId }, transaction);
                if (volunteer == null)
                {
                    transaction.Rollback();
                    return new ApiResponse<AssignedVolunteerDTO>(ResponseType.Error, "Volunteer not found", null);
                }

                // Check capacity
                int capacityMax = volunteer.capacity_max ?? 0;
                int current = volunteer.current_assignments ?? 0;
                if (current >= capacityMax)
                {
                    transaction.Rollback();
                    return new ApiResponse<AssignedVolunteerDTO>(ResponseType.Warning, "Volunteer has no capacity", null);
                }

                // 3. Update person
                const string updatePerson = @"
                    UPDATE people SET
                        assigned_volunteer = @VolunteerId,
                        assigned_date = NOW(),
                        follow_up_status = 'ASSIGNED',
                        next_action_date = NOW() + INTERVAL 48 HOUR
                    WHERE person_id = @PersonId;
                ";

                await connection.ExecuteAsync(updatePerson, new { VolunteerId = volunteerId, PersonId = personId }, transaction);

                // 4. Update volunteer counters
                const string updateVolunteer = @"
                    UPDATE volunteers
                    SET current_assignments = current_assignments + 1,
                        total_assigned = total_assigned + 1
                    WHERE volunteer_id = @VolunteerId;
                ";

                await connection.ExecuteAsync(updateVolunteer, new { VolunteerId = volunteerId }, transaction);

                transaction.Commit();

                var assigned = new AssignedVolunteerDTO
                {
                    volunteer_id = volunteer.VolunteerId ?? volunteer.volunteer_id,
                    first_name = volunteer.FirstName ?? volunteer.first_name,
                    last_name = volunteer.LastName ?? volunteer.last_name,
                    people_id = person.PersonId,
                    people_name = person.FirstName + " " + person.LastName
                };

                return new ApiResponse<AssignedVolunteerDTO>(ResponseType.Success, "Manual assignment completed", assigned);
            }
            catch (Exception ex)
            {
                return new ApiResponse<AssignedVolunteerDTO>(ResponseType.Error, $"Error during manual assignment: {ex.Message}", null);
            }
        }

        private string GenerateOtp()
        {
            Random random = new Random();

            int otp = random.Next(1000, 9999);

            return otp.ToString();
        }

    }
}