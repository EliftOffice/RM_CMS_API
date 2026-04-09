using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RM_CMS.BLL.Followups;
using RM_CMS.Data.DTO.Followups;
using RM_CMS.Data.DTO.TeamLeads;
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

        //[HttpGet("/api/escalations")]
        //public async Task<ActionResult<ApiResponse<IEnumerable<EscalationResponseDTO>>>> GetEscalationsAsync([FromQuery] EscalationsFilterDTO filter)
        //{
        //    try
        //    {
        //        var result = await _escalationsBLL.GetEscalationsAsync(filter);
        //        return HttpResponseHelper.CreateHttpResponse(result);
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError(ex, "Error retrieving escalations");

        //        return StatusCode(500, new ApiResponse<IEnumerable<EscalationResponseDTO>>(
        //            ResponseType.Error,
        //            "An error occurred while retrieving escalations",
        //            new List<EscalationResponseDTO>()
        //        ));
        //    }
        //}

        //[HttpPatch("/api/escalations/{id}")]
        //public async Task<ActionResult<ApiResponse<bool>>> UpdateEscalationAsync(string id, [FromBody] UpdateEscalationDTO dto)
        //{
        //    try
        //    {
        //        var result = await _escalationsBLL.UpdateEscalationAsync(id, dto);
        //        return HttpResponseHelper.CreateHttpResponse(result);
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError(ex, "Error updating escalation");

        //        return StatusCode(500, new ApiResponse<bool>(
        //            ResponseType.Error,
        //            "An error occurred while updating escalation",
        //            false
        //        ));
        //    }
        //}

        #region [Escalations APIs]

        // ✅ 1. GET PENDING (Dashboard)
        [HttpGet("pending/{teamLeadId}")]
        public async Task<ActionResult<ApiResponse<IEnumerable<EscalationDTO>>>> GetPendingEscalations(string teamLeadId)
        {
            try
            {
                var response = await _escalationsBLL.GetPendingEscalationsAsync(teamLeadId);
                return HttpResponseHelper.CreateHttpResponse(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching pending escalations");

                return StatusCode(500, new ApiResponse<IEnumerable<EscalationDTO>>(
                    ResponseType.Error,
                    "An error occurred while fetching escalations",
                    null
                ));
            }
        }

        // ✅ 2. GET BY ID (Details View)
        [HttpGet("{escalationId}")]
        public async Task<ActionResult<ApiResponse<EscalationDTO>>> GetEscalationById(string escalationId)
        {
            try
            {
                var response = await _escalationsBLL.GetEscalationByIdAsync(escalationId);
                return HttpResponseHelper.CreateHttpResponse(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching escalation details");

                return StatusCode(500, new ApiResponse<EscalationDTO>(
                    ResponseType.Error,
                    "An error occurred while fetching escalation details",
                    null
                ));
            }
        }

        // ✅ 3. ACKNOWLEDGE (New → In Progress)
        [HttpPost("acknowledge/{escalationId}")]
        public async Task<ActionResult<ApiResponse<bool>>> AcknowledgeEscalation(string escalationId)
        {
            try
            {
                var response = await _escalationsBLL.AcknowledgeEscalationAsync(escalationId);
                return HttpResponseHelper.CreateHttpResponse(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error acknowledging escalation");

                return StatusCode(500, new ApiResponse<bool>(
                    ResponseType.Error,
                    "An error occurred while acknowledging escalation",
                    false
                ));
            }
        }

        // ✅ 4. RESOLVE / REFER / CLOSE
        [HttpPost("resolve")]
        public async Task<ActionResult<ApiResponse<bool>>> ResolveEscalation([FromBody] ResolveEscalationDTO dto)
        {
            try
            {
                var response = await _escalationsBLL.ResolveEscalationAsync(dto);
                return HttpResponseHelper.CreateHttpResponse(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resolving escalation");

                return StatusCode(500, new ApiResponse<bool>(
                    ResponseType.Error,
                    "An error occurred while resolving escalation",
                    false
                ));
            }
        }

        #endregion
    }
}
