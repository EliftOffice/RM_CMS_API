using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RM_CMS.Modules.Areas.Services;
using RM_CMS.Modules.Identity.Domain;
using RM_CMS.Security;
using RM_CMS.Utilities;

namespace RM_CMS.Modules.Areas.Api
{
    /// <summary>
    /// The locality list: the type-ahead intake uses, and the screen that maintains it.
    ///
    /// Every route sits behind <see cref="PolicyNames.CanRecordVisitors"/> rather than
    /// AdminOnly, because two different roles legitimately reach this controller and
    /// which of them may WRITE is a setting, not a claim. The policy is the outer
    /// fence — it keeps out anyone with no business here at all — and
    /// <see cref="IAreaService"/> decides what each caller inside it may do. Putting
    /// AdminOnly on the management routes would make the grant unreachable.
    /// </summary>
    [ApiController]
    [Route("api/areas")]
    [Produces("application/json")]
    [Authorize(Policy = PolicyNames.CanRecordVisitors)]
    public sealed class AreasController : ControllerBase
    {
        private readonly IAreaService _areas;

        public AreasController(IAreaService areas) => _areas = areas;

        /// <summary>
        /// Type-ahead options at one campus. Active areas only, id and name only.
        /// An empty <paramref name="q"/> returns the first page, which is what the
        /// operator sees before typing anything.
        /// </summary>
        [HttpGet("options")]
        [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<AreaOptionDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Options([FromQuery] string? q, [FromQuery] string? campusId) =>
            Ok(await _areas.SuggestAsync(campusId, q));

        /// <summary>What this caller may do on the management screen.</summary>
        [HttpGet("access")]
        [ProducesResponseType(typeof(ApiResponse<AreaAccessDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Access() => Ok(await _areas.GetAccessAsync());

        /// <summary>
        /// The management list, with how many people are filed against each area.
        /// Refused unless the caller may manage areas.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<AreaDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> List([FromQuery] bool includeInactive = true) =>
            Ok(await _areas.ListAsync(includeInactive));

        [HttpPost]
        [EnableRateLimiting(RateLimitPolicies.Sensitive)]
        [ProducesResponseType(typeof(ApiResponse<AreaDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<ApiResponse<AreaDto>>> Create([FromBody] CreateAreaRequest request)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            return HttpResponseHelper.CreateHttpResponse(await _areas.CreateAsync(request));
        }

        /// <summary>Rename or retire. There is no delete — people point at these rows.</summary>
        [HttpPut("{id}")]
        [EnableRateLimiting(RateLimitPolicies.Sensitive)]
        [ProducesResponseType(typeof(ApiResponse<AreaDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<ApiResponse<AreaDto>>> Update(string id, [FromBody] UpdateAreaRequest request)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            return HttpResponseHelper.CreateHttpResponse(await _areas.UpdateAsync(id, request));
        }
    }
}
