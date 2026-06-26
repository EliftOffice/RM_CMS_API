using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RM_CMS.BLL.TeamLeads;
using RM_CMS.Data.DTO.TeamLeads;
using RM_CMS.Utilities;
using RM_CMS.Data.Models; // FollowUp model

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
        public async Task<ActionResult<ApiResponse<TeamLeadMetricsDTO>>> GetTeamMetrics([FromQuery] string teamLeadId)
        {
            try
            {
                _logger.LogInformation("Fetching team metrics for TeamLead: {TeamLeadId}", teamLeadId);

                var result = await _bll.GetTeamHealthMetricsAsyncV1(teamLeadId);

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

        [HttpGet("team-huddle")]
        public async Task<ActionResult<ApiResponse<IEnumerable<FollowUp>>>> GetTeamHuddleFollowUps([FromQuery] string teamLeadId, [FromQuery] int? week)
        {
            try
            {
                _logger.LogInformation("Fetching team huddle follow-ups for TeamLead: {TeamLeadId} week: {Week}", teamLeadId, week);

                var result = await _bll.GetTeamHuddleFollowUpsAsync(teamLeadId, week);
                return HttpResponseHelper.CreateHttpResponse(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching team huddle follow-ups");

                return HttpResponseHelper.CreateHttpResponse(new ApiResponse<IEnumerable<FollowUp>>(ResponseType.Error, "Error retrieving follow-ups", null));
            }
        }

        [HttpGet("team-huddle/dto")]
        public async Task<ActionResult<ApiResponse<IEnumerable<TeamHuddleFollowUpDTO>>>> GetTeamHuddleFollowUpsDto([FromQuery] string teamLeadId, [FromQuery] int? week)
        {
            try
            {
                _logger.LogInformation("Fetching team huddle follow-ups DTO for TeamLead: {TeamLeadId} week: {Week}", teamLeadId, week);

                var result = await _bll.GetTeamHuddleFollowUpsDtoAsync(teamLeadId, week);
                return HttpResponseHelper.CreateHttpResponse(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching team huddle follow-ups DTO");

                return HttpResponseHelper.CreateHttpResponse(new ApiResponse<IEnumerable<TeamHuddleFollowUpDTO>>(ResponseType.Error, "Error retrieving follow-ups", null));
            }
        }


        [HttpPost("save-team-lead")]
        public async Task<ActionResult<ApiResponse<bool>>> SaveTeamLead([FromBody] TeamLeadDTO teamLead)
        {
            try
            {
                _logger.LogInformation("Saving Team Lead: {@TeamLead}", teamLead);

                var result = await _bll.SaveTeamLeadAsync(teamLead);

                return HttpResponseHelper.CreateHttpResponse(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving team lead");

                return HttpResponseHelper.CreateHttpResponse(new ApiResponse<bool>(
                    ResponseType.Error,
                    "Error saving team lead",
                    false
                ));
            }
        }


        [HttpGet("team-leads")]
        public async Task<ActionResult<ApiResponse<List<TeamLeadDTO>>>> GetTeamLeads()
        {
            try
            {
                _logger.LogInformation("Fetching active team leads");

                var result = await _bll.GetTeamLeadsAsync();

                return HttpResponseHelper.CreateHttpResponse(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching team leads");

                return HttpResponseHelper.CreateHttpResponse(
                    new ApiResponse<List<TeamLeadDTO>>(
                        ResponseType.Error,
                        "Error fetching team leads",
                        null
                    )
                );
            }
        }


    }
}
