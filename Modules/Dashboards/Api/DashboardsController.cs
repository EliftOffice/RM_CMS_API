using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RM_CMS.Modules.Dashboards.Domain;
using RM_CMS.Modules.Dashboards.Services;
using RM_CMS.Modules.Identity.Domain;
using RM_CMS.Utilities;

namespace RM_CMS.Modules.Dashboards.Api
{
    /// <summary>
    /// Role landing pages.
    ///
    /// Note what this controller does NOT accept: a team lead id. The dashboard is
    /// always the caller's own, resolved from their token. The route it replaces took
    /// the id from the query string, which made every team lead's escalation queue
    /// readable by anyone who could sign in.
    /// </summary>
    [ApiController]
    [Route("api/dashboards")]
    [Produces("application/json")]
    public sealed class DashboardsController : ControllerBase
    {
        private readonly ITeamLeadDashboardService _teamLead;

        public DashboardsController(ITeamLeadDashboardService teamLead) => _teamLead = teamLead;

        /// <summary>
        /// The signed-in team lead's dashboard: escalations they owe an answer to, their
        /// team's case load, follow-ups owed, and each volunteer's spare capacity.
        /// </summary>
        [HttpGet("team-lead")]
        [Authorize(Policy = PolicyNames.TeamLeadOrAbove)]
        [ProducesResponseType(typeof(ApiResponse<TeamLeadDashboard>), StatusCodes.Status200OK)]
        public async Task<IActionResult> TeamLead() => Ok(await _teamLead.GetAsync());
    }
}
