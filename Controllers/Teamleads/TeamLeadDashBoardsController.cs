using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RM_CMS.BLL.TeamLeads;
using RM_CMS.Data.DTO.TeamLeads;
using RM_CMS.Utilities;

namespace RM_CMS.Controllers.TeamLeads
{
    [Route("api/[controller]")]
    [ApiController]
    public class TeamLeadDashBoardsController : ControllerBase
    {

        private readonly ITeamLeadDashBoardBLL _bll;
        private readonly ILogger<TeamLeadDashBoardsController> _logger;

        public TeamLeadDashBoardsController(ITeamLeadDashBoardBLL bll, ILogger<TeamLeadDashBoardsController> logger)
        {
            _bll = bll;
            _logger = logger;
        }

        [HttpGet("team-metrics")]
        public async Task<ActionResult<ApiResponse<TeamLeadMetricsDTO>>> GetTeamMetrics(
            [FromQuery] string teamLeadId)
        {
            try
            {
                _logger.LogInformation("Fetching team metrics for TeamLead: {TeamLeadId}", teamLeadId);

                var result = await _bll.GetTeamHealthMetricsAsync(teamLeadId);

                return HttpResponseHelper.CreateHttpResponse(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching team metrics");

                return HttpResponseHelper.CreateHttpResponse(new ApiResponse<TeamLeadMetricsDTO>(
                    ResponseType.Error,
                    "Error retrieving team metrics",
                    null
                ));
            }
        }
    }
}
