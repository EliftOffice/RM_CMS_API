using Dapper;
using RM_CMS.Data;
using RM_CMS.Data.DTO.Followups;
using RM_CMS.Data.Models;
using RM_CMS.Utilities;
using System.Text;

namespace RM_CMS.DAL.Followups
{
    public interface IFollowupsDAL
    {
        Task<ApiResponse<bool>> HandleNormalResponseAsync(string followUpId, string personId, string volunteerId);
        Task<ApiResponse<bool>> HandleNeedsFollowUpAsync(string followUpId, string personId, string volunteerId, string teamLeadId, string? notes);
        Task<ApiResponse<bool>> HandleCrisisResponseAsync(
    string followUpId,
    string personId,
    string volunteerId,
    string teamLeadId,
    string? notes
);
        Task<ApiResponse<bool>> HandleNoResponseAsync(
    string followUpId,
    string personId,
    string volunteerId
);
        Task<ApiResponse<string>> LogFollowUpAttemptAsync(FollowUpRequestDTO data);

        Task<ApiResponse<IEnumerable<FollowUp>>> GetFollowUpsAsync(FollowUpsFilterDTO filter);

    }

    public class FollowupsDAL : IFollowupsDAL
    {
        private readonly IDbConnectionFactory _dbConnectionFactory;
        public FollowupsDAL(IDbConnectionFactory dbConnectionFactory)
        {
            _dbConnectionFactory = dbConnectionFactory;
        }

        public async Task<ApiResponse<bool>> HandleNormalResponseAsync(
      string followUpId,
      string personId,
      string volunteerId)
        {
            using (var connection = _dbConnectionFactory.GetConnection())
            {
                try
                {
                    // ✅ FIX 1: Ensure connection is open
                    if (connection.State == System.Data.ConnectionState.Closed)
                        connection.Open();

                    using (var transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            // 1. Update follow-up
                            const string updateFollowUp = @"
                        UPDATE follow_ups SET
                            next_action = 'Mark Complete',
                            next_action_date = NULL
                        WHERE follow_up_id = @FollowUpId;
                    ";

                            await connection.ExecuteAsync(
                                updateFollowUp,
                                new { FollowUpId = followUpId },
                                transaction
                            );

                            // 2. Update person
                            const string updatePerson = @"
                        UPDATE people SET
                            follow_up_status = 'COMPLETE',
                            next_action_date = NULL
                        WHERE person_id = @PersonId;
                    ";

                            await connection.ExecuteAsync(
                                updatePerson,
                                new { PersonId = personId },
                                transaction
                            );

                            // 3. Update volunteer
                            const string updateVolunteer = @"
                        UPDATE volunteers SET
                            current_assignments = current_assignments - 1,
                            total_completed = total_completed + 1
                        WHERE volunteer_id = @VolunteerId;
                    ";

                            await connection.ExecuteAsync(
                                updateVolunteer,
                                new { VolunteerId = volunteerId },
                                transaction
                            );

                            // ✅ Commit if all succeed
                            transaction.Commit();

                            return new ApiResponse<bool>(
                                ResponseType.Success,
                                "Follow-up marked as complete",
                                true
                            );
                        }
                        catch (Exception ex)
                        {
                            // ✅ FIX 2: Rollback on failure
                            transaction.Rollback();

                            return new ApiResponse<bool>(
                                ResponseType.Error,
                                $"Transaction failed: {ex.Message}",
                                false
                            );
                        }
                    }
                }
                catch (Exception ex)
                {
                    // ✅ FIX 3: Show actual error
                    return new ApiResponse<bool>(
                        ResponseType.Error,
                        $"Error handling normal response: {ex.Message}",
                        false
                    );
                }
            }
        }

        public async Task<ApiResponse<bool>> HandleNeedsFollowUpAsync(
      string followUpId,
      string personId,
      string volunteerId,
      string teamLeadId,
      string? notes)
        {
            using (var connection = _dbConnectionFactory.GetConnection())
            {
                try
                {
                    // ✅ FIX 1: Ensure connection is open
                    if (connection.State == System.Data.ConnectionState.Closed)
                        connection.Open();

                    using (var transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            // 1. Update follow-up
                            const string updateFollowUp = @"
                        UPDATE follow_ups SET
                            next_action = 'Escalate to Team Lead',
                            escalation_tier = 'Standard'
                        WHERE follow_up_id = @FollowUpId;
                    ";

                            await connection.ExecuteAsync(
                                updateFollowUp,
                                new { FollowUpId = followUpId },
                                transaction
                            );

                            // 2. Update person
                            const string updatePerson = @"
                        UPDATE people SET
                            follow_up_status = 'ESCALATED',
                            follow_up_priority = 'High'
                        WHERE person_id = @PersonId;
                    ";

                            await connection.ExecuteAsync(
                                updatePerson,
                                new { PersonId = personId },
                                transaction
                            );

                            // 3. Insert escalation
                            const string insertEscalation = @"
                        INSERT INTO escalations (
                            escalation_id,
                            follow_up_id,
                            person_id,
                            volunteer_id,
                            team_lead_id,
                            escalation_date,
                            escalation_tier,
                            escalation_reason,
                            description,
                            status,
                            created_at
                        )
                        VALUES (
                            @EscalationId,
                            @FollowUpId,
                            @PersonId,
                            @VolunteerId,
                            @TeamLeadId,
                            NOW(),
                            'Standard',
                            'Needs Follow-Up',
                            @Description,
                            'New',
                            NOW()
                        );
                    ";

                            var escalationId = Guid.NewGuid().ToString();

                            await connection.ExecuteAsync(insertEscalation, new
                            {
                                EscalationId = escalationId,
                                FollowUpId = followUpId,
                                PersonId = personId,
                                VolunteerId = volunteerId,
                                TeamLeadId = teamLeadId,
                                Description = string.IsNullOrEmpty(notes)
                                    ? "Volunteer indicated person needs additional follow-up"
                                    : notes
                            }, transaction);

                            // 4. Update volunteer
                            const string updateVolunteer = @"
                        UPDATE volunteers SET
                            current_assignments = current_assignments - 1
                        WHERE volunteer_id = @VolunteerId;
                    ";

                            await connection.ExecuteAsync(
                                updateVolunteer,
                                new { VolunteerId = volunteerId },
                                transaction
                            );

                            // ✅ Commit
                            transaction.Commit();

                            return new ApiResponse<bool>(
                                ResponseType.Success,
                                "Follow-up escalated to team lead",
                                true
                            );
                        }
                        catch (Exception ex)
                        {
                            // ✅ Rollback on failure
                            transaction.Rollback();

                            return new ApiResponse<bool>(
                                ResponseType.Error,
                                $"Transaction failed: {ex.Message}",
                                false
                            );
                        }
                    }
                }
                catch (Exception ex)
                {
                    // ✅ Show real error
                    return new ApiResponse<bool>(
                        ResponseType.Error,
                        $"Error handling needs follow-up: {ex.Message}",
                        false
                    );
                }
            }
        }

        public async Task<ApiResponse<bool>> HandleCrisisResponseAsync(
      string followUpId,
      string personId,
      string volunteerId,
      string teamLeadId,
      string? notes)
        {
            using (var connection = _dbConnectionFactory.GetConnection())
            {
                try
                {
                    // ✅ FIX 1: Ensure connection is open
                    if (connection.State == System.Data.ConnectionState.Closed)
                        connection.Open();

                    using (var transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            // 1. Update follow-up
                            const string updateFollowUp = @"
                        UPDATE follow_ups SET
                            next_action = 'Crisis Protocol',
                            escalation_tier = 'Emergency'
                        WHERE follow_up_id = @FollowUpId;
                    ";

                            await connection.ExecuteAsync(
                                updateFollowUp,
                                new { FollowUpId = followUpId },
                                transaction
                            );

                            // 2. Update person
                            const string updatePerson = @"
                        UPDATE people SET
                            follow_up_status = 'ESCALATED',
                            follow_up_priority = 'Urgent'
                        WHERE person_id = @PersonId;
                    ";

                            await connection.ExecuteAsync(
                                updatePerson,
                                new { PersonId = personId },
                                transaction
                            );

                            // 3. Insert escalation (CRISIS)
                            const string insertEscalation = @"
                        INSERT INTO escalations (
                            escalation_id,
                            follow_up_id,
                            person_id,
                            volunteer_id,
                            team_lead_id,
                            escalation_date,
                            escalation_tier,
                            escalation_reason,
                            description,
                            status,
                            created_at
                        )
                        VALUES (
                            @EscalationId,
                            @FollowUpId,
                            @PersonId,
                            @VolunteerId,
                            @TeamLeadId,
                            NOW(),
                            'Emergency',
                            'Crisis',
                            @Description,
                            'New',
                            NOW()
                        );
                    ";

                            var escalationId = Guid.NewGuid().ToString();

                            await connection.ExecuteAsync(insertEscalation, new
                            {
                                EscalationId = escalationId,
                                FollowUpId = followUpId,
                                PersonId = personId,
                                VolunteerId = volunteerId,
                                TeamLeadId = teamLeadId,
                                Description = "🚨 CRISIS: " + (
                                    string.IsNullOrEmpty(notes)
                                        ? "Immediate attention required"
                                        : notes
                                )
                            }, transaction);

                            // ✅ Commit
                            transaction.Commit();

                            return new ApiResponse<bool>(
                                ResponseType.Success,
                                "Crisis escalation created successfully",
                                true
                            );
                        }
                        catch (Exception ex)
                        {
                            // ✅ Rollback on failure
                            transaction.Rollback();

                            return new ApiResponse<bool>(
                                ResponseType.Error,
                                $"Transaction failed: {ex.Message}",
                                false
                            );
                        }
                    }
                }
                catch (Exception ex)
                {
                    // ✅ Show real error
                    return new ApiResponse<bool>(
                        ResponseType.Error,
                        $"Error handling crisis response: {ex.Message}",
                        false
                    );
                }
            }
        }

        public async Task<ApiResponse<bool>> HandleNoResponseAsync(
     string followUpId,
     string personId,
     string volunteerId)
        {
            using (var connection = _dbConnectionFactory.GetConnection())
            {
                try
                {
                    // ✅ FIX 1: Open connection
                    if (connection.State == System.Data.ConnectionState.Closed)
                        connection.Open();

                    // 1. Get attempt count
                    const string countQuery = @"
                SELECT COUNT(*) FROM follow_ups WHERE person_id = @PersonId;
            ";

                    var attemptNumber = await connection.ExecuteScalarAsync<int>(
                        countQuery,
                        new { PersonId = personId }
                    );

                    using (var transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            if (attemptNumber < 3)
                            {
                                // Retry after 3 days
                                var retryDate = DateTime.Now.AddDays(3);

                                // Update follow-up
                                const string updateFollowUp = @"
                            UPDATE follow_ups SET
                                next_action = 'Retry in 3 Days',
                                next_action_date = @RetryDate
                            WHERE follow_up_id = @FollowUpId;
                        ";

                                await connection.ExecuteAsync(updateFollowUp,
                                    new { FollowUpId = followUpId, RetryDate = retryDate },
                                    transaction);

                                // Update person
                                const string updatePerson = @"
                            UPDATE people SET
                                follow_up_status = 'RETRY PENDING',
                                next_action_date = @RetryDate
                            WHERE person_id = @PersonId;
                        ";

                                await connection.ExecuteAsync(updatePerson,
                                    new { PersonId = personId, RetryDate = retryDate },
                                    transaction);

                                transaction.Commit();

                                return new ApiResponse<bool>(
                                    ResponseType.Success,
                                    $"Retry scheduled (Attempt {attemptNumber}/3)",
                                    true
                                );
                            }
                            else
                            {
                                // Update follow-up
                                const string updateFollowUp = @"
                            UPDATE follow_ups SET
                                next_action = 'Mark Unresponsive',
                                next_action_date = NULL
                            WHERE follow_up_id = @FollowUpId;
                        ";

                                await connection.ExecuteAsync(updateFollowUp,
                                    new { FollowUpId = followUpId },
                                    transaction);

                                // Update person
                                const string updatePerson = @"
                            UPDATE people SET
                                follow_up_status = 'UNRESPONSIVE',
                                next_action_date = NULL
                            WHERE person_id = @PersonId;
                        ";

                                await connection.ExecuteAsync(updatePerson,
                                    new { PersonId = personId },
                                    transaction);

                                // Update volunteer
                                const string updateVolunteer = @"
                            UPDATE volunteers SET
                                current_assignments = current_assignments - 1
                            WHERE volunteer_id = @VolunteerId;
                        ";

                                await connection.ExecuteAsync(updateVolunteer,
                                    new { VolunteerId = volunteerId },
                                    transaction);

                                transaction.Commit();

                                return new ApiResponse<bool>(
                                    ResponseType.Success,
                                    "Marked as unresponsive after 3 attempts",
                                    true
                                );
                            }
                        }
                        catch (Exception ex)
                        {
                            // ✅ FIX 2: Rollback
                            transaction.Rollback();

                            return new ApiResponse<bool>(
                                ResponseType.Error,
                                $"Transaction failed: {ex.Message}",
                                false
                            );
                        }
                    }
                }
                catch (Exception ex)
                {
                    // ✅ FIX 3: Real error message
                    return new ApiResponse<bool>(
                        ResponseType.Error,
                        $"Error handling no response: {ex.Message}",
                        false
                    );
                }
            }
        }

        public async Task<ApiResponse<string>> LogFollowUpAttemptAsync(FollowUpRequestDTO data)
        {
            try
            {
                using (var connection = _dbConnectionFactory.GetConnection())
                {
                    // Ensure connection is open
                    if (connection is System.Data.Common.DbConnection dbConn)
                        await dbConn.OpenAsync();
                    else
                        connection.Open();

                    // 1. Validate
                    if (string.IsNullOrEmpty(data.volunteer_id) ||
                        string.IsNullOrEmpty(data.person_id) ||
                        string.IsNullOrEmpty(data.contact_status))
                    {
                        return new ApiResponse<string>(
                            ResponseType.Error,
                            "Missing required fields",
                            string.Empty
                        );
                    }

                    if (data.contact_status == "Contacted" && string.IsNullOrEmpty(data.response_type))
                    {
                        return new ApiResponse<string>(
                            ResponseType.Error,
                            "response_type required when contact_status is Contacted",
                            string.Empty
                        );
                    }

                    // 2. Get attempt number
                    const string countQuery = @"SELECT COUNT(*) FROM follow_ups WHERE person_id = @PersonId;";
                    var previousAttempts = await connection.ExecuteScalarAsync<int>(
                        countQuery,
                        new { PersonId = data.person_id }
                    );

                    var attemptNumber = previousAttempts + 1;

                    // 3. Generate FollowUp ID and date info
                    // 3. Generate FollowUp ID similar to person_id format
                    var year = DateTime.UtcNow.Year;

                    const string seqQuery = @"
    SELECT MAX(CAST(SUBSTRING(follow_up_id, 2) AS UNSIGNED)) 
    FROM follow_ups;
";

                    var seqResult = await connection.ExecuteScalarAsync<int?>(seqQuery);

                    var nextNum = (seqResult ?? 0) + 1;

                    // Dynamic padding (optional)
                    var followUpId = $"F{nextNum.ToString().PadLeft(4, '0')}";

                    var now = DateTime.Now;
                    var weekNumber = System.Globalization.ISOWeek.GetWeekOfYear(now);
                    var month = now.Month;
                    var quarter = (month - 1) / 3 + 1;
                   

                    using (var transaction = connection.BeginTransaction())
                    {
                        // 4. Insert follow-up
                        const string insertQuery = @"
                    INSERT INTO follow_ups (
                        follow_up_id, person_id, volunteer_id, team_lead_id,
                        attempt_number, attempt_date, attempt_time, contact_method,
                        contact_status, response_type, call_duration_min,
                        notes, tags, week_number, month_number, quarter_number, year,
                        created_at
                    )
                    VALUES (
                        @FollowUpId, @PersonId, @VolunteerId, @TeamLeadId,
                        @AttemptNumber, CURDATE(), CURTIME(), @ContactMethod,
                        @ContactStatus, @ResponseType, @CallDuration,
                        @Notes, @Tags, @WeekNumber, @Month, @Quarter, @Year,
                        NOW()
                    );
                ";

                        await connection.ExecuteAsync(insertQuery, new
                        {
                            FollowUpId = followUpId,
                            PersonId = data.person_id,
                            VolunteerId = data.volunteer_id,
                            TeamLeadId = data.team_lead_id,
                            AttemptNumber = attemptNumber,
                            ContactMethod = data.contact_method ?? "Phone Call",
                            ContactStatus = data.contact_status,
                            ResponseType = data.response_type,
                            CallDuration = data.call_duration_min,
                            Notes = data.notes,
                            Tags = data.tags,
                            WeekNumber = weekNumber,
                            Month = month,
                            Quarter = quarter,
                            Year = year
                        }, transaction);

                        // 5. Update last contact date
                        const string updatePerson = @"
                    UPDATE people
                    SET last_contact_date = NOW()
                    WHERE person_id = @PersonId;
                ";

                        await connection.ExecuteAsync(updatePerson,
                            new { PersonId = data.person_id }, transaction);

                        transaction.Commit();
                    }

                    return new ApiResponse<string>(
                        ResponseType.Success,
                        "Follow-up logged successfully", followUpId 
                    );
                }
            }
            catch (Exception ex)
            {
                return new ApiResponse<string>(
                    ResponseType.Error,
                    $"Error logging follow-up: {ex.Message}",
                    string.Empty
                );
            }
        }

        public async Task<ApiResponse<IEnumerable<FollowUp>>> GetFollowUpsAsync(FollowUpsFilterDTO filter)
        {
            try
            {
                using (var connection = _dbConnectionFactory.GetConnection())
                {
                    var query = new StringBuilder("SELECT * FROM follow_ups WHERE 1=1");
                    var parameters = new DynamicParameters();

                    if (!string.IsNullOrEmpty(filter.VolunteerId))
                    {
                        query.Append(" AND volunteer_id = @VolunteerId");
                        parameters.Add("VolunteerId", filter.VolunteerId);
                    }

                    if (!string.IsNullOrEmpty(filter.PersonId))
                    {
                        query.Append(" AND person_id = @PersonId");
                        parameters.Add("PersonId", filter.PersonId);
                    }

                    if (filter.Week.HasValue)
                    {
                        query.Append(" AND week_number = @Week");
                        parameters.Add("Week", filter.Week);
                    }

                    if (!string.IsNullOrEmpty(filter.Status))
                    {
                        query.Append(" AND contact_status = @Status");
                        parameters.Add("Status", filter.Status);
                    }

                    query.Append(" ORDER BY attempt_date DESC LIMIT @Limit");
                    parameters.Add("Limit", filter.Limit);

                    var followUps = await connection.QueryAsync<FollowUp>(
                        query.ToString(),
                        parameters
                    );

                    return new ApiResponse<IEnumerable<FollowUp>>(
                        ResponseType.Success,
                        "Follow-ups retrieved successfully",
                        followUps
                    );
                }
            }
            catch (Exception ex)
            {
                return new ApiResponse<IEnumerable<FollowUp>>(
                    ResponseType.Error,
                    $"Error retrieving follow-ups: {ex.Message}",
                    null
                );
            }
        }

    }

}
