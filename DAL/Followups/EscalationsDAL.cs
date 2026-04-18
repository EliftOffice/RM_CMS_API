using Dapper;
using RM_CMS.Data;
using RM_CMS.Data.DTO.Followups;
using RM_CMS.Data.DTO.TeamLeads;
using RM_CMS.Utilities;
using System.Text;

namespace RM_CMS.DAL.Followups
{
    public interface IEscalationsDAL
    {
        Task<ApiResponse<IEnumerable<EscalationResponseDTO>>> GetEscalationsAsync(EscalationsFilterDTO filter);
        Task<ApiResponse<bool>> UpdateEscalationAsync(string escalationId, UpdateEscalationDTO dto);
        // ✅ STEP 5: DASHBOARD
        Task<ApiResponse<IEnumerable<EscalationDTO>>> GetPendingEscalationsAsync(string teamLeadId);
        // ✅ GET DETAILS
        Task<ApiResponse<EscalationDTO>> GetEscalationByIdAsync(string escalationId);
        // ✅ STEP 5: ACKNOWLEDGE (New → In Progress)
        Task<ApiResponse<bool>> AcknowledgeEscalationAsync(string escalationId, DateTime acknowledgedAt);
        // ✅ NOTIFICATION TRACKING
        Task<ApiResponse<bool>> UpdateNotifiedAtAsync(string escalationId, DateTime notifiedAt);
        // ✅ STEP 6: RESOLVE / REFER / CLOSE
        Task<ApiResponse<bool>> ResolveEscalationFullAsync(ResolveEscalationDTO dto);
        // ✅ TEAM LEAD LOOKUP
        Task<ApiResponse<string>> GetTeamLeadByVolunteerAsync(string volunteerId);
        // ✅ UPDATE PERSON STATUS (POST RESOLUTION)
        Task<ApiResponse<bool>> UpdatePersonStatusAsync(string personId, string status);
        // ✅ CREATE ESCALATION (AUTO ROUTING)
        Task<ApiResponse<string>> CreateEscalationAsync(EscalationDTO escalation);
        Task<ApiResponse<bool>> UpdateEscalationAsync(UpdateEscalationDTO dto);
    }
    public class EscalationsDAL : IEscalationsDAL
    {
        private readonly IDbConnectionFactory _dbConnectionFactory;

        public EscalationsDAL(IDbConnectionFactory dbConnectionFactory)
        {
            _dbConnectionFactory = dbConnectionFactory;
        }

        public async Task<ApiResponse<IEnumerable<EscalationResponseDTO>>> GetEscalationsAsync(EscalationsFilterDTO filter)
        {
            try
            {
                using (var connection = _dbConnectionFactory.GetConnection())
                {
                    var query = new StringBuilder(@"
                SELECT e.*, 
                       CONCAT(p.first_name, ' ', p.last_name) as PersonName,
                       CONCAT(v.first_name, ' ', v.last_name) as VolunteerName
                FROM escalations e
                JOIN people p ON e.person_id = p.person_id
                JOIN volunteers v ON e.volunteer_id = v.volunteer_id
                WHERE 1=1
            ");

                    var parameters = new DynamicParameters();

                    if (!string.IsNullOrEmpty(filter.TeamLeadId))
                    {
                        query.Append(" AND e.team_lead_id = @TeamLeadId");
                        parameters.Add("TeamLeadId", filter.TeamLeadId);
                    }

                    if (!string.IsNullOrEmpty(filter.Status))
                    {
                        query.Append(" AND e.status = @Status");
                        parameters.Add("Status", filter.Status);
                    }

                    if (!string.IsNullOrEmpty(filter.Tier))
                    {
                        query.Append(" AND e.escalation_tier = @Tier");
                        parameters.Add("Tier", filter.Tier);
                    }

                    query.Append(" ORDER BY e.escalation_date DESC");

                    var escalations = await connection.QueryAsync<EscalationResponseDTO>(
                        query.ToString(),
                        parameters
                    );

                    return new ApiResponse<IEnumerable<EscalationResponseDTO>>(
                        ResponseType.Success,
                        "Escalations retrieved successfully",
                        escalations
                    );
                }
            }
            catch (Exception ex)
            {
                return new ApiResponse<IEnumerable<EscalationResponseDTO>>(
                    ResponseType.Error,
                    $"Error retrieving escalations: {ex.Message}",
                    null
                );
            }
        }
        public async Task<ApiResponse<bool>> UpdateEscalationAsync(string escalationId, UpdateEscalationDTO dto)
        {
            try
            {
                using (var connection = _dbConnectionFactory.GetConnection())
                {
                    const string query = @"
                UPDATE escalations SET
                    status = @Status,
                    resolution_notes = @ResolutionNotes,
                    outcome = @Outcome,
                    resolved_date = CASE 
                        WHEN @Status = 'Resolved' THEN NOW() 
                        ELSE resolved_date 
                    END,
                    updated_at = NOW()
                WHERE escalation_id = @EscalationId";

                    var rowsAffected = await connection.ExecuteAsync(query, new
                    {
                        Status = dto.Status,
                        ResolutionNotes = dto.ResolutionNotes,
                        Outcome = dto.Outcome,
                        EscalationId = escalationId
                    });

                    if (rowsAffected == 0)
                    {
                        return new ApiResponse<bool>(
                            ResponseType.Error,
                            $"Escalation with ID '{escalationId}' not found",
                            false
                        );
                    }

                    return new ApiResponse<bool>(
                        ResponseType.Success,
                        "Escalation updated successfully",
                        true
                    );
                }
            }
            catch (Exception ex)
            {
                return new ApiResponse<bool>(
                    ResponseType.Error,
                    $"Error updating escalation: {ex.Message}",
                    false
                );
            }
        }


        #region [ESCALATION Work Flow]

        // ✅ CREATE ESCALATION (AUTO ROUTING)
        public async Task<ApiResponse<string>> CreateEscalationAsync(EscalationDTO escalation)
        {
            try
            {
                const string query = @"
            INSERT INTO escalations (
                escalation_id, follow_up_id, person_id, volunteer_id, team_lead_id,
                escalation_date, escalation_tier, escalation_reason,
                description, status, created_at
            )
            VALUES (
                @EscalationId, @FollowUpId, @PersonId, @VolunteerId, @TeamLeadId,
                NOW(), @EscalationTier, @EscalationReason,
                @Description, 'New', NOW()
            );";

                using var connection = _dbConnectionFactory.GetConnection();

                escalation.EscalationId = await GenerateEscalationId(connection);

                await connection.ExecuteAsync(query, escalation);

                return new ApiResponse<string>(
                    ResponseType.Success,
                    "Escalation created successfully",
                    escalation.EscalationId
                );
            }
            catch (Exception ex)
            {
                return new ApiResponse<string>(
                    ResponseType.Error,
                    $"Error creating escalation: {ex.Message}",
                    null
                );
            }
        }

        // ✅ DASHBOARD (TEAM LEAD VIEW)
        public async Task<ApiResponse<IEnumerable<EscalationDTO>>> GetPendingEscalationsAsync(string teamLeadId)
        {
            try
            {
                const string query = @"
            SELECT *
            FROM escalations
            WHERE team_lead_id = @TeamLeadId
              AND status IN ('New', 'In Progress')
            ORDER BY escalation_date DESC;";

                using var connection = _dbConnectionFactory.GetConnection();

                var data = await connection.QueryAsync<EscalationDTO>(query, new { TeamLeadId = teamLeadId });

                return new ApiResponse<IEnumerable<EscalationDTO>>(
                    ResponseType.Success,
                    "Escalations fetched successfully",
                    data
                );
            }
            catch (Exception ex)
            {
                return new ApiResponse<IEnumerable<EscalationDTO>>(
                    ResponseType.Error,
                    $"Error fetching escalations: {ex.Message}",
                    null
                );
            }
        }

        // ✅ GET BY ID
        public async Task<ApiResponse<EscalationDTO>> GetEscalationByIdAsync(string escalationId)
        {
            try
            {
                const string query = @"
                                    SELECT 
                                        escalation_id        AS EscalationId,
                                        follow_up_id         AS FollowUpId,
                                        person_id            AS PersonId,
                                        volunteer_id         AS VolunteerId,
                                        team_lead_id         AS TeamLeadId,

                                        escalation_date      AS EscalationDate,
                                        escalation_tier      AS EscalationTier,
                                        escalation_reason    AS EscalationReason,
                                        description          AS Description,

                                        status               AS Status,
                                        assigned_to          AS AssignedTo,

                                        acknowledged_at      AS AcknowledgedAt,
                                        resolved_date        AS ResolvedDate,

                                        resolution_notes     AS ResolutionNotes,
                                        outcome              AS Outcome,

                                        resource_connected   AS ResourceConnected,
                                        follow_up_scheduled  AS FollowUpScheduled,

                                        crisis_protocol_followed AS CrisisProtocolFollowed,
                                        authorities_contacted    AS AuthoritiesContacted,
                                        volunteer_debriefed      AS VolunteerDebriefed,

                                        created_at           AS CreatedAt,
                                        notified_at          AS NotifiedAt

                                    FROM escalations
                                    WHERE escalation_id = @EscalationId;";

                using var connection = _dbConnectionFactory.GetConnection();

                var data = await connection.QueryFirstOrDefaultAsync<EscalationDTO>(query, new { EscalationId = escalationId });

                if (data == null)
                {
                    return new ApiResponse<EscalationDTO>(
                        ResponseType.Warning,
                        "Escalation not found",
                        null
                    );
                }

                return new ApiResponse<EscalationDTO>(
                    ResponseType.Success,
                    "Escalation fetched successfully",
                    data
                );
            }
            catch (Exception ex)
            {
                return new ApiResponse<EscalationDTO>(
                    ResponseType.Error,
                    $"Error fetching escalation: {ex.Message}",
                    null
                );
            }
        }

        // ✅ ACKNOWLEDGE (New → In Progress)
        public async Task<ApiResponse<bool>> AcknowledgeEscalationAsync(string escalationId, DateTime acknowledgedAt)
        {
            try
            {
                const string query = @"
            UPDATE escalations
            SET status = 'In Progress',
                acknowledged_at = @AcknowledgedAt
            WHERE escalation_id = @EscalationId
              AND status = 'New';";

                using var connection = _dbConnectionFactory.GetConnection();

                var rows = await connection.ExecuteAsync(query, new
                {
                    EscalationId = escalationId,
                    AcknowledgedAt = acknowledgedAt
                });

                return new ApiResponse<bool>(
                    ResponseType.Success,
                    rows > 0 ? "Escalation acknowledged" : "No record updated",
                    rows > 0
                );
            }
            catch (Exception ex)
            {
                return new ApiResponse<bool>(
                    ResponseType.Error,
                    $"Error acknowledging escalation: {ex.Message}",
                    false
                );
            }
        }

        // ✅ NOTIFICATION TRACKING
        public async Task<ApiResponse<bool>> UpdateNotifiedAtAsync(string escalationId, DateTime notifiedAt)
        {
            try
            {
                const string query = @"
            UPDATE escalations
            SET notified_at = @NotifiedAt
            WHERE escalation_id = @EscalationId;";

                using var connection = _dbConnectionFactory.GetConnection();

                var rows = await connection.ExecuteAsync(query, new
                {
                    EscalationId = escalationId,
                    NotifiedAt = notifiedAt
                });

                return new ApiResponse<bool>(
                    ResponseType.Success,
                    "Notification updated",
                    rows > 0
                );
            }
            catch (Exception ex)
            {
                return new ApiResponse<bool>(
                    ResponseType.Error,
                    $"Error updating notification: {ex.Message}",
                    false
                );
            }
        }

        // ✅ FULL RESOLUTION (CORE FEATURE)
        public async Task<ApiResponse<bool>> ResolveEscalationFullAsync(ResolveEscalationDTO dto)
        {
            try
            {
                const string query = @"
            UPDATE escalations
            SET status = @Status,
                resolved_date = NOW(),
                resolution_notes = @Notes,
                outcome = @Outcome,
                resource_connected = @ResourceConnected,
                follow_up_scheduled = @FollowUpScheduled
            WHERE escalation_id = @EscalationId;";

                using var connection = _dbConnectionFactory.GetConnection();

                var rows = await connection.ExecuteAsync(query, new
                {
                    dto.EscalationId,
                    dto.Status,
                    dto.Notes,
                    dto.Outcome,
                    dto.ResourceConnected,
                    dto.FollowUpScheduled
                });

                return new ApiResponse<bool>(
                    ResponseType.Success,
                    rows > 0 ? "Escalation resolved successfully" : "No update happened",
                    rows > 0
                );
            }
            catch (Exception ex)
            {
                return new ApiResponse<bool>(
                    ResponseType.Error,
                    $"Error resolving escalation: {ex.Message}",
                    false
                );
            }
        }

        // ✅ TEAM LEAD LOOKUP
        public async Task<ApiResponse<string>> GetTeamLeadByVolunteerAsync(string volunteerId)
        {
            try
            {
                const string query = @"
            SELECT team_lead
            FROM volunteers
            WHERE volunteer_id = @VolunteerId;";

                using var connection = _dbConnectionFactory.GetConnection();

                var result = await connection.QueryFirstOrDefaultAsync<string>(query, new { VolunteerId = volunteerId });

                if (string.IsNullOrEmpty(result))
                {
                    return new ApiResponse<string>(
                        ResponseType.Warning,
                        "Team Lead not found",
                        null
                    );
                }

                return new ApiResponse<string>(
                    ResponseType.Success,
                    "Team Lead fetched",
                    result
                );
            }
            catch (Exception ex)
            {
                return new ApiResponse<string>(
                    ResponseType.Error,
                    $"Error fetching team lead: {ex.Message}",
                    null
                );
            }
        }

        // ✅ UPDATE PERSON STATUS (POST RESOLUTION)
        public async Task<ApiResponse<bool>> UpdatePersonStatusAsync(string personId, string status)
        {
            try
            {
                const string query = @"
            UPDATE people
            SET follow_up_status = @Status
            WHERE person_id = @PersonId;";

                using var connection = _dbConnectionFactory.GetConnection();

                var rows = await connection.ExecuteAsync(query, new
                {
                    PersonId = personId,
                    Status = status
                });

                return new ApiResponse<bool>(
                    ResponseType.Success,
                    "Person status updated",
                    rows > 0
                );
            }
            catch (Exception ex)
            {
                return new ApiResponse<bool>(
                    ResponseType.Error,
                    $"Error updating person: {ex.Message}",
                    false
                );
            }
        }

        // 🔑 ID GENERATOR (unchanged)
        private async Task<string> GenerateEscalationId(System.Data.IDbConnection connection)
        {
            const string query = @"
        SELECT CONCAT('ESC', LPAD(IFNULL(MAX(CAST(SUBSTRING(escalation_id, 4) AS UNSIGNED)), 0) + 1, 4, '0'))
        FROM escalations;";

            return await connection.ExecuteScalarAsync<string>(query);
        }

        #endregion



        public async Task<ApiResponse<bool>> UpdateEscalationAsync(UpdateEscalationDTO dto)
        {
            try
            {
                const string query = @"
            UPDATE follow_ups
            SET escalation_appropriate = @EscalationAppropriate
            WHERE follow_up_id = @FollowUpId;";

                using var connection = _dbConnectionFactory.GetConnection();

                var rows = await connection.ExecuteAsync(query, new
                {
                    dto.FollowUpId,
                    dto.EscalationAppropriate
                });

                return new ApiResponse<bool>(
                    ResponseType.Success,
                    "Escalation updated",
                    rows > 0
                );
            }
            catch (Exception ex)
            {
                return new ApiResponse<bool>(
                    ResponseType.Error,
                    $"Error updating escalation: {ex.Message}",
                    false
                );
            }
        }
    }
}
