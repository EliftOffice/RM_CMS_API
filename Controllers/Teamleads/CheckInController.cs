using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RM_CMS.BLL.TeamLeads;
using RM_CMS.Controllers.TeamLeads;
using RM_CMS.Data.DTO.TeamLeads;
using RM_CMS.Utilities;

namespace RM_CMS.Controllers.Teamleads
{
    [Route("api/[controller]")]
    [ApiController]
    public class CheckInController : ControllerBase
    {
        private readonly ICheckInBLL _checkInsBLL;
        private readonly ILogger<TeamLeadDashBoardsController> _logger;

        public CheckInController(ICheckInBLL bll, ILogger<TeamLeadDashBoardsController> logger)
        {
            _checkInsBLL = bll;
            _logger = logger;
        }

        [HttpPost("/api/check-ins")]
        public async Task<ActionResult<ApiResponse<string>>> CreateCheckInAsync([FromBody] CreateCheckInDTO dto)
        {
            try
            {
                var result = await _checkInsBLL.CreateCheckInAsync(dto);
                return HttpResponseHelper.CreateHttpResponse(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating check-in");

                return StatusCode(500, new ApiResponse<string>(
                    ResponseType.Error,
                    "An error occurred while creating check-in",
                    null
                ));
            }
        }
    }
}
