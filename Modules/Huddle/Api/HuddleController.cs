using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RM_CMS.Modules.Huddle.Services;
using RM_CMS.Modules.Identity.Domain;
using RM_CMS.Utilities;

namespace RM_CMS.Modules.Huddle.Api
{
    /// <summary>
    /// The weekly team huddle.
    ///
    /// Scoped to the teams the caller leads, from their token. The MVP took a
    /// teamLeadId on the query string, so any signed-in user could pull up another
    /// lead's agenda — including the notes on every pastoral conversation their team
    /// had that week.
    /// </summary>
    [ApiController]
    [Route("api/huddle")]
    [Produces("application/json")]
    [Authorize(Policy = PolicyNames.TeamLeadOrAbove)]
    public sealed class HuddleController : ControllerBase
    {
        private readonly IHuddleService _huddle;

        public HuddleController(IHuddleService huddle) => _huddle = huddle;

        /// <summary>
        /// This week's contacts awaiting a verdict, plus how many older ones have piled
        /// up behind them.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(ApiResponse<HuddleAgendaDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Agenda([FromQuery] int? lookbackDays = null) =>
            Ok(await _huddle.GetAgendaAsync(lookbackDays));

        /// <summary>
        /// Records a batch of verdicts — the whole sitting in one call, rather than a
        /// request and a confirmation dialog per row.
        /// </summary>
        [HttpPost("verdicts")]
        [ProducesResponseType(typeof(ApiResponse<VerdictResultDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<ApiResponse<VerdictResultDto>>> Submit(
            [FromBody] SubmitVerdictsRequest request)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            return HttpResponseHelper.CreateHttpResponse(await _huddle.SubmitAsync(request));
        }
    }
}
