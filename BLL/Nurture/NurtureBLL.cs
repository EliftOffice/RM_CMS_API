using RM_CMS.DAL.Nurture;
using RM_CMS.DAL.Volunteers;
using RM_CMS.Data.DTO.Nurture;
using RM_CMS.Data.Models;
using RM_CMS.Utilities;

namespace RM_CMS.BLL.Nurture
{
    public interface INurtureBLL
    {
        Task<ApiResponse<string>> StartSequenceAsync(string personId, string volunteerId, string? teamLeadId);
        Task<ApiResponse<bool>> LogStepAsync(NurtureStepLogDto dto);
        Task<ApiResponse<bool>> CloseSequenceAsync(CloseSequenceDto dto);
        Task<ApiResponse<IEnumerable<NurtureStepDetailDto>>> GetDueStepsForVolunteerAsync(string volunteerId);
        Task<ApiResponse<IEnumerable<NurtureSequenceSummaryDto>>> GetActiveSequencesForTeamLeadAsync(string teamLeadId);
        Task<ApiResponse<IEnumerable<NurtureSequenceSummaryDto>>> GetSequencesAwaitingReviewAsync(string teamLeadId);
        Task<ApiResponse<IEnumerable<NurtureStep>>> GetStepsBySequenceAsync(string sequenceId);
        Task<ApiResponse<bool>> FinalDecisionAsync(
    FinalDecisionDTO dto);
    }

    public class NurtureBLL : INurtureBLL
    {
        private readonly INurtureDAL _nurtureDAL;
        private readonly IVolunteersDAL _volunteersDAL;

        public NurtureBLL(INurtureDAL nurtureDAL, IVolunteersDAL volunteersDAL)
        {
            _nurtureDAL = nurtureDAL;
            _volunteersDAL = volunteersDAL;
        }

        public async Task<ApiResponse<string>> StartSequenceAsync(string personId, string volunteerId, string? teamLeadId)
        {
            try
            {
                return await _nurtureDAL.StartSequenceAsync(personId, volunteerId, teamLeadId);
            }
            catch (Exception ex)
            {
                return new ApiResponse<string>(ResponseType.Error, $"Error starting sequence: {ex.Message}", string.Empty);
            }
        }

        public async Task<ApiResponse<bool>> LogStepAsync(NurtureStepLogDto dto)
        {
            try
            {
                var result = await _nurtureDAL.LogStepAsync(dto);

                // If step 7 done → notify team lead to make final call
                if (result.ResponseType == ResponseType.Success && result.Message.Contains("review"))
                {
                    await NotifyTeamLeadForReviewAsync(dto.SequenceId);
                }
                // If crisis or needs follow-up on a nurture step → escalate same as normal
                else if (result.ResponseType == ResponseType.Success &&
                         (string.Equals(dto.ResponseType, "crisis", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(dto.ResponseType, "needs follow-up", StringComparison.OrdinalIgnoreCase)))
                {
                    // Escalation is handled at controller level — nothing extra needed here
                }

                return result;
            }
            catch (Exception ex)
            {
                return new ApiResponse<bool>(ResponseType.Error, $"Error logging step: {ex.Message}", false);
            }
        }

        public async Task<ApiResponse<bool>> CloseSequenceAsync(CloseSequenceDto dto)
        {
            try
            {
                return await _nurtureDAL.CloseSequenceAsync(dto);
            }
            catch (Exception ex)
            {
                return new ApiResponse<bool>(ResponseType.Error, $"Error closing sequence: {ex.Message}", false);
            }
        }

        public async Task<ApiResponse<IEnumerable<NurtureStepDetailDto>>> GetDueStepsForVolunteerAsync(string volunteerId)
        {
            try
            {
                return await _nurtureDAL.GetDueStepsForVolunteerAsync(volunteerId);
            }
            catch (Exception ex)
            {
                return new ApiResponse<IEnumerable<NurtureStepDetailDto>>(ResponseType.Error, ex.Message, null);
            }
        }

        public async Task<ApiResponse<IEnumerable<NurtureSequenceSummaryDto>>> GetActiveSequencesForTeamLeadAsync(string teamLeadId)
        {
            try
            {
                return await _nurtureDAL.GetActiveSequencesForTeamLeadAsync(teamLeadId);
            }
            catch (Exception ex)
            {
                return new ApiResponse<IEnumerable<NurtureSequenceSummaryDto>>(ResponseType.Error, ex.Message, null);
            }
        }

        public async Task<ApiResponse<IEnumerable<NurtureSequenceSummaryDto>>> GetSequencesAwaitingReviewAsync(string teamLeadId)
        {
            try
            {
                return await _nurtureDAL.GetSequencesAwaitingReviewAsync(teamLeadId);
            }
            catch (Exception ex)
            {
                return new ApiResponse<IEnumerable<NurtureSequenceSummaryDto>>(ResponseType.Error, ex.Message, null);
            }
        }

        public async Task<ApiResponse<IEnumerable<NurtureStep>>> GetStepsBySequenceAsync(string sequenceId)
        {
            try
            {
                return await _nurtureDAL.GetStepsBySequenceAsync(sequenceId);
            }
            catch (Exception ex)
            {
                return new ApiResponse<IEnumerable<NurtureStep>>(ResponseType.Error, ex.Message, null);
            }
        }

        // ─────────────────────────────────────────────────────────────
        // Internal: notify TL when sequence reaches InReview
        // ─────────────────────────────────────────────────────────────
        private async Task NotifyTeamLeadForReviewAsync(string sequenceId)
        {
            try
            {
                var steps = await _nurtureDAL.GetStepsBySequenceAsync(sequenceId);
                // Telegram notification is triggered from CornJob/cron — nothing extra here
                // This is a hook point for future push notifications
            }
            catch { /* non-critical, swallow */ }
        }


        public async Task<ApiResponse<bool>> FinalDecisionAsync(
    FinalDecisionDTO dto)
        {
            try
            {
                if (dto.decision == "PERMANENT")
                {
                    return await _nurtureDAL.MarkPermanentAsync(
                        dto.person_id);
                }

                if (dto.decision == "FAILED")
                {
                    return await _nurtureDAL.MarkFailedAsync(
                        dto.person_id);
                }

                return new ApiResponse<bool>(
                    ResponseType.Error,
                    "Invalid decision",
                    false);
            }
            catch (Exception ex)
            {
                return new ApiResponse<bool>(
                    ResponseType.Error,
                    ex.Message,
                    false);
            }
        }
    }
}
