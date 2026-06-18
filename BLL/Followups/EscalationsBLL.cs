using RM_CMS.DAL.Followups;
using RM_CMS.DAL.Nurture;
using RM_CMS.Data.DTO.Followups;
using RM_CMS.Data.DTO.TeamLeads;
using RM_CMS.Utilities;

namespace RM_CMS.BLL.Followups
{
    public interface IEscalationsBLL
    {
        Task<ApiResponse<IEnumerable<EscalationResponseDTO>>> GetEscalationsAsync(EscalationsFilterDTO filter);
        Task<ApiResponse<bool>> UpdateEscalationAsync(string escalationId, UpdateEscalationDTO dto);
        // ✅ STEP 5: DASHBOARD
        Task<ApiResponse<IEnumerable<EscalationDTO>>> GetPendingEscalationsAsync(string teamLeadId);
        // ✅ GET DETAILS
        Task<ApiResponse<EscalationDTO>> GetEscalationByIdAsync(string escalationId);
        // ✅ STEP 5: ACKNOWLEDGE (New → In Progress)
        Task<ApiResponse<bool>> AcknowledgeEscalationAsync(string escalationId);
        // ✅ STEP 6: RESOLVE / REFER / CLOSE
        Task<ApiResponse<bool>> ResolveEscalationAsync(ResolveEscalationDTO dto);
        Task<ApiResponse<bool>> UpdateEscalationApprAsync(UpdateEscalationDTO dto);
    }
    public class EscalationsBLL : IEscalationsBLL
    {
        private readonly IEscalationsDAL _escalationsDAL;
        private readonly INurtureDAL _nurtureDAL;
        public EscalationsBLL(IEscalationsDAL escalationsDAL,INurtureDAL nurtureDAL)
        {
            _escalationsDAL = escalationsDAL;
            _nurtureDAL = nurtureDAL;
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

        #region [Escalations Further steps]

        // ✅ STEP 5: DASHBOARD
        public async Task<ApiResponse<IEnumerable<EscalationDTO>>> GetPendingEscalationsAsync(string teamLeadId)
        {
            try
            {
                if (string.IsNullOrEmpty(teamLeadId))
                    return new ApiResponse<IEnumerable<EscalationDTO>>(ResponseType.Warning, "Team Lead ID required", null);

                // ✅ Directly return DAL response
                return await _escalationsDAL.GetPendingEscalationsAsync(teamLeadId);
            }
            catch (Exception ex)
            {
                return new ApiResponse<IEnumerable<EscalationDTO>>(
                    ResponseType.Error,
                    ex.Message,
                    null
                );
            }
        }

        // ✅ GET DETAILS
        public async Task<ApiResponse<EscalationDTO>> GetEscalationByIdAsync(string escalationId)
        {
            try
            {
                if (string.IsNullOrEmpty(escalationId))
                    return new ApiResponse<EscalationDTO>(ResponseType.Warning, "Escalation ID required", null);

                // ✅ Direct DAL response
                return await _escalationsDAL.GetEscalationByIdAsync(escalationId);
            }
            catch (Exception ex)
            {
                return new ApiResponse<EscalationDTO>(
                    ResponseType.Error,
                    ex.Message,
                    null
                );
            }
        }

        // ✅ STEP 5: ACKNOWLEDGE (New → In Progress)
        public async Task<ApiResponse<bool>> AcknowledgeEscalationAsync(string escalationId)
        {
            try
            {
                if (string.IsNullOrEmpty(escalationId))
                    return new ApiResponse<bool>(ResponseType.Warning, "Escalation ID required", false);

                // 🔍 Get escalation (DAL now returns ApiResponse)
                var escalationResponse = await _escalationsDAL.GetEscalationByIdAsync(escalationId);

                if (escalationResponse.ResponseType != ResponseType.Success || escalationResponse.Data == null)
                    return new ApiResponse<bool>(ResponseType.Warning, "Escalation not found", false);

                var escalation = escalationResponse.Data;

                if (escalation.Status != "New")
                    return new ApiResponse<bool>(ResponseType.Warning, "Only NEW escalations can be acknowledged", false);

                // ✅ Directly return DAL result
                return await _escalationsDAL.AcknowledgeEscalationAsync(
                    escalationId,
                    DateTime.UtcNow
                );
            }
            catch (Exception ex)
            {
                return new ApiResponse<bool>(
                    ResponseType.Error,
                    ex.Message,
                    false
                );
            }
        }

        // ✅ STEP 6: RESOLVE / REFER / CLOSE
        public async Task<ApiResponse<bool>> ResolveEscalationAsync(ResolveEscalationDTO dto)
        {
            try
            {
                if (dto == null || string.IsNullOrEmpty(dto.EscalationId))
                    return new ApiResponse<bool>(ResponseType.Warning, "Invalid request", false);

                // 🔍 Get escalation
                var escalationResponse = await _escalationsDAL.GetEscalationByIdAsync(dto.EscalationId);

                if (escalationResponse.ResponseType != ResponseType.Success || escalationResponse.Data == null)
                    return new ApiResponse<bool>(ResponseType.Warning, "Escalation not found", false);

                var escalation = escalationResponse.Data;

                // 🔴 VALID STATUS
                var allowedStatuses = new[] { "Resolved", "Referred Out", "Closed" };

                if (!allowedStatuses.Contains(dto.Status))
                    return new ApiResponse<bool>(ResponseType.Warning, "Invalid status", false);

                if (escalation.Status == "Closed")
                    return new ApiResponse<bool>(ResponseType.Warning, "Already closed", false);

                // 🔴 VALID OUTCOME
                if (string.IsNullOrEmpty(dto.Outcome))
                    return new ApiResponse<bool>(ResponseType.Warning, "Outcome is required", false);

                // 💾 UPDATE ESCALATION
                var result = await _escalationsDAL.ResolveEscalationFullAsync(dto);

                // 🔄 UPDATE PERSON STATUS (no need to block main result)
                await _escalationsDAL.UpdatePersonStatusAsync(escalation.PersonId, "COMPLETE");

                // 4. Start nurture sequence (outside transaction — has its own)
                await _nurtureDAL.StartSequenceAsync(escalation.PersonId, escalation.VolunteerId, escalation.TeamLeadId);

                return result;
            }
            catch (Exception ex)
            {
                return new ApiResponse<bool>(
                    ResponseType.Error,
                    ex.Message,
                    false
                );
            }
        }

        #endregion

        public async Task<ApiResponse<bool>> UpdateEscalationApprAsync(UpdateEscalationDTO dto)
        {
            try
            {
                if (dto == null || string.IsNullOrEmpty(dto.FollowUpId) || string.IsNullOrEmpty(dto.EscalationAppropriate))
                {
                    return new ApiResponse<bool>(
                        ResponseType.Error,
                        "Invalid  Inputs",
                        false
                    );
                }
                return await _escalationsDAL.UpdateEscalationAsync(dto);


            }
            catch (Exception ex)
            {
                return new ApiResponse<bool>(
                    ResponseType.Error,
                    ex.Message,
                    false
                );
            }
        }
    }
}
