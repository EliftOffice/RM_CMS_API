using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RM_CMS.BLL.Followups;
using RM_CMS.Data.DTO.Followups;
using RM_CMS.Utilities;

namespace RM_CMS.Controllers.Followups
{
    [Route("api/[controller]")]
    [ApiController]
    public class EscalationsController : ControllerBase
    {
        private readonly ILogger<FollowupsController> _logger;
        private readonly IEscalationsBLL _escalationsBLL;
        public EscalationsController(IEscalationsBLL escalationsBLL, ILogger<FollowupsController> logger)
        {
            _escalationsBLL = escalationsBLL;
            _logger = logger;
        }

        [HttpGet("/api/escalations")]
        public async Task<ActionResult<ApiResponse<IEnumerable<EscalationResponseDTO>>>> GetEscalationsAsync([FromQuery] EscalationsFilterDTO filter)
        {
            try
            {
                var result = await _escalationsBLL.GetEscalationsAsync(filter);
                return HttpResponseHelper.CreateHttpResponse(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving escalations");

                return StatusCode(500, new ApiResponse<IEnumerable<EscalationResponseDTO>>(
                    ResponseType.Error,
                    "An error occurred while retrieving escalations",
                    new List<EscalationResponseDTO>()
                ));
            }
        }

        [HttpPatch("/api/escalations/{id}")]
        public async Task<ActionResult<ApiResponse<bool>>> UpdateEscalationAsync(string id, [FromBody] UpdateEscalationDTO dto)
        {
            try
            {
                var result = await _escalationsBLL.UpdateEscalationAsync(id, dto);
                return HttpResponseHelper.CreateHttpResponse(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating escalation");

                return StatusCode(500, new ApiResponse<bool>(
                    ResponseType.Error,
                    "An error occurred while updating escalation",
                    false
                ));
            }
        }
    }
}
