using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RM_CMS.BLL.Pastors;
using RM_CMS.Data.DTO;
using RM_CMS.Data.DTO.Pastors;
using RM_CMS.Data.DTO.TeamLeads;
using RM_CMS.Utilities;

namespace RM_CMS.Controllers.Pastors
{
    [Route("api/[controller]")]
    [ApiController]
    public class PastorsController : ControllerBase
    {
        private readonly IPastorDashboardBLL _pastorDashboardBLL;
        private readonly ILogger<PastorsController> _logger;

        public PastorsController(IPastorDashboardBLL pastorDashboardBLL, ILogger<PastorsController> logger)
        {
            _pastorDashboardBLL = pastorDashboardBLL;
            _logger = logger;
        }

        /// <summary>
        /// Retrieves system health metrics including active volunteers, retention rates, completion rates, and overall health status
        /// </summary>
        /// <returns>ApiResponse with system health metrics</returns>
        [HttpGet("system-health")]
        [ProducesResponseType(typeof(ApiResponse<SystemHealthDTO>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<SystemHealthDTO>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<SystemHealthDTO>), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<ApiResponse<SystemHealthDTO>>> GetSystemHealth()
        {
            try
            {
                _logger.LogInformation("Retrieving system health metrics");

                var result = await _pastorDashboardBLL.GetSystemHealthAsync();

                _logger.LogInformation($"System health metrics retrieved successfully. Status: {result.ResponseType}");
                return HttpResponseHelper.CreateHttpResponse(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving system health metrics");
                return HttpResponseHelper.CreateHttpResponse(new ApiResponse<SystemHealthDTO>(
                    ResponseType.Error,
                    "An error occurred while retrieving system health metrics",
                    null
                ));
            }
        }


        [HttpGet("kpis")]
        public async Task<ActionResult<ApiResponse<KPIsDTO>>> GetKPIs()
        {
            var result = await _pastorDashboardBLL.GetKPIsAsync();
            return HttpResponseHelper.CreateHttpResponse(result);
        }

        [HttpGet("team-lead-performance")]
        public async Task<ActionResult<ApiResponse<List<TeamLeadPerformanceDTO>>>> GetTeamLeadPerformance()
        {
            try
            {
                _logger.LogInformation("Fetching team lead performance");

                var result = await _pastorDashboardBLL.GetTeamLeadPerformanceAsync();

                return HttpResponseHelper.CreateHttpResponse(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching team lead performance");

                return HttpResponseHelper.CreateHttpResponse(new ApiResponse<List<TeamLeadPerformanceDTO>>(
                    ResponseType.Error,
                    "An error occurred while retrieving team lead performance",
                    null
                ));
            }
        }

        [HttpGet("pipeline-health")]
        public async Task<ActionResult<ApiResponse<PipelineHealthDTO>>> GetPipelineHealth()
        {
            try
            {
                _logger.LogInformation("Fetching pipeline health");

                var result = await _pastorDashboardBLL.GetPipelineHealthAsync();

                return HttpResponseHelper.CreateHttpResponse(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching pipeline health");

                return HttpResponseHelper.CreateHttpResponse(new ApiResponse<PipelineHealthDTO>(
                    ResponseType.Error,
                    "An error occurred while retrieving pipeline health",
                    null
                ));
            }
        }

    }
}
