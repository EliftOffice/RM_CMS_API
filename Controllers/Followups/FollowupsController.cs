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
