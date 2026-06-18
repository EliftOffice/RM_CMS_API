using RM_CMS.BLL.Followups;
using RM_CMS.DAL.Followups;
using RM_CMS.DAL.Nurture;
using RM_CMS.DAL.Volunteers;
using RM_CMS.Data.DTO.Followups;
using RM_CMS.Data.DTO.Nurture;
using RM_CMS.Data.DTO.TeamLeads;
using RM_CMS.Data.Models;
using RM_CMS.Utilities;
using static System.Runtime.InteropServices.JavaScript.JSType;

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
    }

    public class NurtureBLL : INurtureBLL
    {
        private readonly INurtureDAL _nurtureDAL;
        private readonly IVolunteersDAL _volunteersDAL;
        
        private readonly IFollowupsBLL _followupsBLL;

        public NurtureBLL(INurtureDAL nurtureDAL, IVolunteersDAL volunteersDAL, IFollowupsBLL followupsBLL)
        {
            _nurtureDAL = nurtureDAL;
            _volunteersDAL = volunteersDAL;
            _followupsBLL= followupsBLL;
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

                //var objVolunteer = await _volunteersDAL.GetVolunteerByIdAsync(dto.VolunteerId);

                //var res = await _followupsBLL.LogFollowUpAttemptAsync(new FollowUpRequestDTO
                //{
                //    person_id = dto.PersonId,
                //    volunteer_id = dto.VolunteerId,
                //    response_type = dto.ResponseType,
                //    call_duration_min = dto.CallDurationMin,
                //    notes = dto.Notes,
                //    volunteer_name= objVolunteer.Data.LastName+objVolunteer.Data.FirstName
                //});

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
    }
}
