using Microsoft.AspNetCore.Mvc;
using RM_CMS.BLL.Followups;
using RM_CMS.BLL.Volunteers;
using RM_CMS.Controllers.Volunteers;
using RM_CMS.Data.DTO.Followups;
using RM_CMS.Data.Models;
using RM_CMS.Utilities;

namespace RM_CMS.Controllers.Followups
{
    [Route("api/[controller]")]
    [ApiController]
    public class FollowupsController : ControllerBase
    {
        private readonly ILogger<FollowupsController> _logger;
        private readonly IFollowupsBLL _FollowupsBLL;
        public FollowupsController(IFollowupsBLL followupsBLL, ILogger<FollowupsController> logger)
        {
            _FollowupsBLL = followupsBLL;
            _logger = logger;
        }


   //     [HttpPost("normal-response")]
   //     public async Task<ActionResult<ApiResponse<bool>>> HandleNormalResponse(
   //[FromQuery] string followUpId,
   //[FromQuery] string personId,
   //[FromQuery] string volunteerId)
   //     {
   //         try
   //         {
   //             _logger.LogInformation("Handling normal response for FollowUpId: {FollowUpId}", followUpId);

   //             var result = await _FollowupsBLL.HandleNormalResponseAsync(followUpId, personId, volunteerId);

   //             return HttpResponseHelper.CreateHttpResponse(result);
   //         }
   //         catch (Exception ex)
   //         {
   //             _logger.LogError(ex, "Error handling normal response");

   //             return HttpResponseHelper.CreateHttpResponse(new ApiResponse<bool>(
   //                 ResponseType.Error,
   //                 "An error occurred while processing normal response",
   //                 false
   //             ));
   //         }
   //     }


   //     [HttpPost("needs-follow-up")]
   //     public async Task<ActionResult<ApiResponse<bool>>> HandleNeedsFollowUp(
   // [FromQuery] string followUpId,
   // [FromQuery] string personId,
   // [FromQuery] string volunteerId,
   // [FromQuery] string teamLeadId,
   // [FromQuery] string? notes)
   //     {
   //         try
   //         {
   //             _logger.LogInformation("Handling needs follow-up for FollowUpId: {FollowUpId}", followUpId);

   //             var result = await _FollowupsBLL.HandleNeedsFollowUpAsync(
   //                 followUpId,
   //                 personId,
   //                 volunteerId,
   //                 teamLeadId,
   //                 notes
   //             );

   //             return HttpResponseHelper.CreateHttpResponse(result);
   //         }
   //         catch (Exception ex)
   //         {
   //             _logger.LogError(ex, "Error handling needs follow-up");

   //             return HttpResponseHelper.CreateHttpResponse(new ApiResponse<bool>(
   //                 ResponseType.Error,
   //                 "An error occurred while processing needs follow-up",
   //                 false
   //             ));
   //         }
   //     }

   //     [HttpPost("crisis-response")]
   //     public async Task<ActionResult<ApiResponse<bool>>> HandleCrisisResponse(
   // [FromQuery] string followUpId,
   // [FromQuery] string personId,
   // [FromQuery] string volunteerId,
   // [FromQuery] string teamLeadId,
   // [FromQuery] string? notes)
   //     {
   //         try
   //         {
   //             _logger.LogInformation("Handling crisis response for FollowUpId: {FollowUpId}", followUpId);

   //             var result = await _FollowupsBLL.HandleCrisisResponseAsync(
   //                 followUpId,
   //                 personId,
   //                 volunteerId,
   //                 teamLeadId,
   //                 notes
   //             );

   //             return HttpResponseHelper.CreateHttpResponse(result);
   //         }
   //         catch (Exception ex)
   //         {
   //             _logger.LogError(ex, "Error handling crisis response");

   //             return HttpResponseHelper.CreateHttpResponse(new ApiResponse<bool>(
   //                 ResponseType.Error,
   //                 "An error occurred while processing crisis response",
   //                 false
   //             ));
   //         }
   //     }

   //     [HttpPost("no-response")]
   //     public async Task<ActionResult<ApiResponse<bool>>> HandleNoResponse(
   // [FromQuery] string followUpId,
   // [FromQuery] string personId,
   // [FromQuery] string volunteerId)
   //     {
   //         try
   //         {
   //             _logger.LogInformation("Handling no response for FollowUpId: {FollowUpId}", followUpId);

   //             var result = await _FollowupsBLL.HandleNoResponseAsync(
   //                 followUpId,
   //                 personId,
   //                 volunteerId
   //             );

   //             return HttpResponseHelper.CreateHttpResponse(result);
   //         }
   //         catch (Exception ex)
   //         {
   //             _logger.LogError(ex, "Error handling no response");

   //             return HttpResponseHelper.CreateHttpResponse(new ApiResponse<bool>(
   //                 ResponseType.Error,
   //                 "An error occurred while processing no response",
   //                 false
   //             ));
   //         }
   //     }

        [HttpPost("log-followup")]
        public async Task<ActionResult<ApiResponse<object>>> LogFollowUpAttempt([FromBody] FollowUpRequestDTO data)
        {
            try
            {
                _logger.LogInformation("Logging follow-up for PersonId: {PersonId}", data.person_id);

                var result = await _FollowupsBLL.LogFollowUpAttemptAsync(data);

                return HttpResponseHelper.CreateHttpResponse(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error logging follow-up");

                return HttpResponseHelper.CreateHttpResponse(new ApiResponse<object>(
                    ResponseType.Error,
                    "An error occurred while logging follow-up",
                    null
                ));
            }
        }

        [HttpGet("/api/follow-ups")]
        public async Task<ActionResult<ApiResponse<IEnumerable<FollowUp>>>> GetFollowUpsAsync([FromQuery] FollowUpsFilterDTO filter)
        {
            try
            {
                var result = await _FollowupsBLL.GetFollowUpsByFilterAsync(filter);
                return HttpResponseHelper.CreateHttpResponse(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving follow-ups");

                return StatusCode(500, new ApiResponse<IEnumerable<FollowUp>>(
                    ResponseType.Error,
                    "An error occurred while retrieving follow-ups",
                    new List<FollowUp>()
                ));
            }
        }
    }
}
