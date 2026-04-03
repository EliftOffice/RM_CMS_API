using RM_CMS.DAL.Followups;
using RM_CMS.Data.DTO.Followups;
using RM_CMS.Data.Models;
using RM_CMS.Utilities;

namespace RM_CMS.BLL.Followups
{
    public interface IFollowupsBLL
    {
        Task<ApiResponse<bool>> HandleNormalResponseAsync(string followUpId, string personId, string volunteerId);
        Task<ApiResponse<bool>> HandleNeedsFollowUpAsync(
               string followUpId,
               string personId,
               string volunteerId,
               string teamLeadId,
               string? notes
           );

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

        Task<ApiResponse<object>> LogFollowUpAttemptAsync(FollowUpRequestDTO data);
        Task<ApiResponse<IEnumerable<FollowUp>>> GetFollowUpsByFilterAsync(FollowUpsFilterDTO filter);


    }
    public class FollowupsBLL : IFollowupsBLL
    {
        private readonly IFollowupsDAL _followupsDAL;

        public FollowupsBLL(IFollowupsDAL followupsDAL)
        {
            _followupsDAL = followupsDAL;
        }

        public async Task<ApiResponse<bool>> HandleNormalResponseAsync(string followUpId, string personId, string volunteerId)
        {
            try
            {
                // Call DAL directly
                return await _followupsDAL.HandleNormalResponseAsync(followUpId, personId, volunteerId);

            }
            catch (Exception)
            {
                return new ApiResponse<bool>(
                    ResponseType.Error,
                    "Error processing normal response",
                    false
                );
            }
        }

        public async Task<ApiResponse<bool>> HandleNeedsFollowUpAsync(
          string followUpId,
          string personId,
          string volunteerId,
          string teamLeadId,
          string? notes)
        {
            try
            {
                var result = await _followupsDAL.HandleNeedsFollowUpAsync(
                    followUpId,
                    personId,
                    volunteerId,
                    teamLeadId,
                    notes
                );

                return result;
            }
            catch (Exception)
            {
                return new ApiResponse<bool>(
                    ResponseType.Error,
                    "Error processing needs follow-up",
                    false
                );
            }
        }

        public async Task<ApiResponse<bool>> HandleCrisisResponseAsync(
           string followUpId,
           string personId,
           string volunteerId,
           string teamLeadId,
           string? notes)
        {
            try
            {
                var result = await _followupsDAL.HandleCrisisResponseAsync(
                    followUpId,
                    personId,
                    volunteerId,
                    teamLeadId,
                    notes
                );

                return result;
            }
            catch (Exception)
            {
                return new ApiResponse<bool>(
                    ResponseType.Error,
                    "Error processing crisis response",
                    false
                );
            }
        }

        public async Task<ApiResponse<bool>> HandleNoResponseAsync(
           string followUpId,
           string personId,
           string volunteerId)
        {
            try
            {
                var result = await _followupsDAL.HandleNoResponseAsync(
                    followUpId,
                    personId,
                    volunteerId
                );

                return result;
            }
            catch (Exception)
            {
                return new ApiResponse<bool>(
                    ResponseType.Error,
                    "Error processing no response",
                    false
                );
            }
        }

        public async Task<ApiResponse<object>> LogFollowUpAttemptAsync(FollowUpRequestDTO data)
        {
            try
            {
                // 1. Log follow-up in the database
                var result = await _followupsDAL.LogFollowUpAttemptAsync(data);

                if (result.ResponseType != ResponseType.Success || result.Data == null)
                {
                    return new ApiResponse<object>(
                        ResponseType.Error,
                        "Failed to log follow-up",
                        null
                    );
                }

                // 2. Extract the generated followUpId
                var followUpId = result.Data;

                // 3. Route follow-up based on response_type
                ApiResponse<bool> routeResult = data.response_type?.Trim().ToLower() switch
                {
                    "normal" => await HandleNormalResponseAsync(followUpId, data.person_id, data.volunteer_id),

                    "needs follow-up" => await HandleNeedsFollowUpAsync(
                        followUpId, data.person_id, data.volunteer_id, data.team_lead_id, data.notes),

                    "needs followup" => await HandleNeedsFollowUpAsync(
                        followUpId, data.person_id, data.volunteer_id, data.team_lead_id, data.notes),

                    "crisis" => await HandleCrisisResponseAsync(
                        followUpId, data.person_id, data.volunteer_id, data.team_lead_id, data.notes),

                    "no response" => await HandleNoResponseAsync(followUpId, data.person_id, data.volunteer_id),

                    "noresponse" => await HandleNoResponseAsync(followUpId, data.person_id, data.volunteer_id),

                    _ => new ApiResponse<bool>(ResponseType.Error, $"Unknown response type: {data.response_type}", false)
                };

                if (routeResult.ResponseType != ResponseType.Success)
                {
                    return new ApiResponse<object>(
                        ResponseType.Error,
                        $"Follow-up logged but failed to route: {routeResult.Message}",
                        new { follow_up_id = followUpId }
                    );
                }

                // 4. Return success with followUpId
                return new ApiResponse<object>(
                    ResponseType.Success,
                    "Follow-up logged and routed successfully",
                    new { follow_up_id = followUpId }
                );
            }
            catch (Exception ex)
            {
                return new ApiResponse<object>(
                    ResponseType.Error,
                    $"Error processing follow-up: {ex.Message}",
                    null
                );
            }
        }

        public async Task<ApiResponse<IEnumerable<FollowUp>>> GetFollowUpsByFilterAsync(FollowUpsFilterDTO filter)
        {
            try
            {
                return await _followupsDAL.GetFollowUpsAsync(filter);
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