using RM_CMS.DAL.Followups;
using RM_CMS.Data.DTO.Followups;
using RM_CMS.Utilities;

namespace RM_CMS.BLL.Followups
{
    public interface IEscalationsBLL{
        Task<ApiResponse<IEnumerable<EscalationResponseDTO>>> GetEscalationsAsync(EscalationsFilterDTO filter);
        Task<ApiResponse<bool>> UpdateEscalationAsync(string escalationId, UpdateEscalationDTO dto);
    }
    public class EscalationsBLL:IEscalationsBLL
    {
        private readonly IEscalationsDAL _escalationsDAL;
        public EscalationsBLL( IEscalationsDAL escalationsDAL)
        {
            _escalationsDAL = escalationsDAL;
        }

        public async Task<ApiResponse<IEnumerable<EscalationResponseDTO>>> GetEscalationsAsync(EscalationsFilterDTO filter)
        {
            try
            {
                return await _escalationsDAL.GetEscalationsAsync(filter);
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
                return await _escalationsDAL.UpdateEscalationAsync(escalationId, dto);
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
