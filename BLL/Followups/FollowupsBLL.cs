using RM_CMS.DAL.Followups;
using RM_CMS.DAL.Nurture;
using RM_CMS.DAL.Volunteers;
using RM_CMS.Data.DTO.Followups;
using RM_CMS.Data.Models;
using RM_CMS.Utilities;

namespace RM_CMS.BLL.Followups
{
    public interface IFollowupsBLL
    {
        Task<ApiResponse<object>> LogFollowUpAttemptAsync(FollowUpRequestDTO data);
        Task<ApiResponse<IEnumerable<FollowUp>>> GetFollowUpsByFilterAsync(FollowUpsFilterDTO filter);
    }

    public class FollowupsBLL : IFollowupsBLL
    {
        private readonly IFollowupsDAL _followupsDAL;
        private readonly IVolunteersDAL _volunteersDAL;
       

        public FollowupsBLL(IFollowupsDAL followupsDAL, IVolunteersDAL volunteersDAL,INurtureDAL nurtureDAL)
        {
            _followupsDAL = followupsDAL;
            _volunteersDAL = volunteersDAL;
           
        }

        // ✅ NORMAL
        public async Task<ApiResponse<bool>> HandleNormalResponseAsync(FollowUpRequestDTO dto)
        {
            try
            {
                return await _followupsDAL.HandleNormalResponseAsync(dto);
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

        // ✅ NEEDS FOLLOW-UP
        public async Task<ApiResponse<bool>> HandleNeedsFollowUpAsync(FollowUpRequestDTO dto)
        {
            try
            {
                return await _followupsDAL.HandleNeedsFollowUpAsync(dto);
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


        public async Task<ApiResponse<bool>> HandleNotContactedFollowUpAsync(FollowUpRequestDTO dto)
        {
            try
            {
                return await _followupsDAL.HandleNotContactedResponseAsync(dto);
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

        // ✅ CRISIS
        public async Task<ApiResponse<bool>> HandleCrisisResponseAsync(FollowUpRequestDTO dto)
        {
            try
            {
                return await _followupsDAL.HandleCrisisResponseAsync(dto);
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

        // ✅ NO RESPONSE
        public async Task<ApiResponse<bool>> HandleNoResponseAsync(FollowUpRequestDTO dto)
        {
            try
            {
                return await _followupsDAL.HandleNoResponseAsync(dto);
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

        // ✅ MAIN FLOW (LOG + ROUTE)
        public async Task<ApiResponse<object>> LogFollowUpAttemptAsync(FollowUpRequestDTO data)
        {
            try
            {
                // 1. Log follow-up
                var result = await _followupsDAL.LogFollowUpAttemptAsync(data);

                if (result.ResponseType != ResponseType.Success || result.Data == null)
                {
                    return new ApiResponse<object>(
                        ResponseType.Error,
                        "Failed to log follow-up",
                        null
                    );
                }

                // 2. Attach generated followUpId back to DTO
                data.follow_up_id = result.Data;

                

                // 3. Route based on response type
                ApiResponse<bool> routeResult = data.response_type?.Trim().ToLower() switch
                {
                    "normal" => await HandleNormalResponseAsync(data),

                    "needs follow-up" => await HandleNeedsFollowUpAsync(data),
                    "needs followup" => await HandleNeedsFollowUpAsync(data),

                    "crisis" => await HandleCrisisResponseAsync(data),

                    "no response" => await HandleNoResponseAsync(data),
                    "noresponse" => await HandleNoResponseAsync(data),
                    "not contacted" => await HandleNotContactedFollowUpAsync(data),

                    _ => new ApiResponse<bool>(
                        ResponseType.Error,
                        $"Unknown response type: {data.response_type}",
                        false)
                };

                if (routeResult.ResponseType != ResponseType.Success)
                {
                    return new ApiResponse<object>(
                        ResponseType.Error,
                        $"Follow-up logged but routing failed: {routeResult.Message}",
                        new { follow_up_id = data.follow_up_id }
                    );
                }
                try
                {
                    if (
    string.Equals(data.response_type, "needs follow-up", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(data.response_type, "crisis", StringComparison.OrdinalIgnoreCase)
)
                    {
                        if (
      string.Equals(data.response_type, "needs follow-up", StringComparison.OrdinalIgnoreCase) ||
      string.Equals(data.response_type, "crisis", StringComparison.OrdinalIgnoreCase)
  )
                        {
                            if (!string.IsNullOrEmpty(data.team_lead_id))
                            {
                                string alertTitle;
                                string assignText;

                                if (string.Equals(data.response_type, "crisis", StringComparison.OrdinalIgnoreCase))
                                {
                                    alertTitle = "🚨 Crisis Alert";
                                    assignText = $"మీకు <b>{data.volunteer_name}</b> Crisis assign చేశారు.";
                                }
                                else
                                {
                                    alertTitle = "🤝 Needs Follow-Up";
                                    assignText = $"మీకు <b>{data.volunteer_name}</b>  Follow-Up assign చేశారు.";
                                }

                                string message = $@"
{alertTitle}

🙏 Praise the Lord {data.team_lead_name},

{assignText}

👉 https://rmoffice.online
";

                                await _volunteersDAL.SendTelegramMessageAsync(
                                    data.telegram_chat_id,
                                    message
                                );
                            }
                        }
                    }
                }
                catch (Exception ex)
                {

                    throw;
                }
                

                // 4. Success
                return new ApiResponse<object>(
                    ResponseType.Success,
                    "Follow-up logged and routed successfully",
                    new { follow_up_id = data.follow_up_id }
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

        // ✅ FILTER
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