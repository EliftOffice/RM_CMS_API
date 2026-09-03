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
        private readonly IPastorDashboardService _pastor;

        public DashboardsController(ITeamLeadDashboardService teamLead, IPastorDashboardService pastor)
        {
            _teamLead = teamLead;
            _pastor = pastor;
        }

        /// <summary>
        /// The signed-in team lead's dashboard: escalations they owe an answer to, their
        /// team's case load, follow-ups owed, and each volunteer's spare capacity.
        /// </summary>
        [HttpGet("team-lead")]
        [Authorize(Policy = PolicyNames.TeamLeadOrAbove)]
        [ProducesResponseType(typeof(ApiResponse<TeamLeadDashboard>), StatusCodes.Status200OK)]
        public async Task<IActionResult> TeamLead() => Ok(await _teamLead.GetAsync());

        /// <summary>
        /// The signed-in pastor's dashboard: escalations that reached pastor level,
        /// a per-team leaderboard across their scope, and volunteers at risk anywhere
        /// in it. PastorOrAdmin, matching every other campus-wide read in this
        /// application — an administrator previewing this is the same shape as an
        /// administrator previewing a team lead's own dashboard.
        /// </summary>
        [HttpGet("pastor")]
        [Authorize(Policy = PolicyNames.PastorOrAdmin)]
        [ProducesResponseType(typeof(ApiResponse<PastorDashboard>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Pastor() => Ok(await _pastor.GetAsync());
    }
}
