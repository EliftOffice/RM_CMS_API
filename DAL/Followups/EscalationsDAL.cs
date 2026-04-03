using Dapper;
using RM_CMS.Data;
using RM_CMS.Data.DTO.Followups;
using RM_CMS.Utilities;
using System.Text;

namespace RM_CMS.DAL.Followups
{
    public interface IEscalationsDAL
    {
        Task<ApiResponse<IEnumerable<EscalationResponseDTO>>> GetEscalationsAsync(EscalationsFilterDTO filter);
        Task<ApiResponse<bool>> UpdateEscalationAsync(string escalationId, UpdateEscalationDTO dto);
    }
    public class EscalationsDAL:IEscalationsDAL
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
    }
}
