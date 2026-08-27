using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RM_CMS.Modules.CheckIns.Services;
using RM_CMS.Modules.Identity.Domain;
using RM_CMS.Utilities;

namespace RM_CMS.Modules.CheckIns.Api
{
    /// <summary>
    /// Volunteer check-ins — the team lead looking after their own people.
    ///
    /// Everything here is scoped to the teams the CALLER leads, resolved from their
    /// token. There is no team or team-lead parameter to pass.
    /// </summary>
    [ApiController]
    [Route("api/check-ins")]
    [Produces("application/json")]
    [Authorize(Policy = PolicyNames.TeamLeadOrAbove)]
    public sealed class CheckInsController : ControllerBase
    {
        private readonly ICheckInService _checkIns;

        public CheckInsController(ICheckInService checkIns) => _checkIns = checkIns;

        /// <summary>
        /// Records a check-in. The conductor is the signed-in team lead; a capacity
        /// band may be changed as part of the same conversation.
        /// </summary>
        [HttpPost]
        [ProducesResponseType(typeof(ApiResponse<CheckInDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<ApiResponse<CheckInDto>>> Record([FromBody] RecordCheckInRequest request)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            return HttpResponseHelper.CreateHttpResponse(await _checkIns.RecordAsync(request));
        }

        /// <summary>
        /// Who on the caller's teams is due one, longest-waiting first. Volunteers who
        /// have never had a check-in come first — that is its own kind of overdue.
        /// </summary>
        [HttpGet("due")]
        [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<CheckInDueDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Due() => Ok(await _checkIns.GetDueAsync());

        /// <summary>One volunteer's check-in history, most recent first.</summary>
        [HttpGet("volunteer/{volunteerId}")]
        [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<CheckInDto>>), StatusCodes.Status200OK)]
        public async Task<ActionResult<ApiResponse<IReadOnlyList<CheckInDto>>>> ForVolunteer(
            string volunteerId, [FromQuery] int limit = 20)
        {
            if (!Ulid.IsValid(volunteerId))
            {
                return BadRequest(new ApiResponse<IReadOnlyList<CheckInDto>>(
                    ResponseType.Warning, "Invalid volunteer id.", null!));
            }

            return HttpResponseHelper.CreateHttpResponse(
                await _checkIns.GetForVolunteerAsync(volunteerId, limit));
        }
    }
}
