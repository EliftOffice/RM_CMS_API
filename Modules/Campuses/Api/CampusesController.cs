using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RM_CMS.Modules.Campuses.Services;
using RM_CMS.Modules.Identity.Domain;
using RM_CMS.Security;
using RM_CMS.Utilities;

namespace RM_CMS.Modules.Campuses.Api
{
    /// <summary>
    /// Campus administration. Administrators only.
    ///
    /// Campus is the tenancy boundary the whole system scopes on, so creating or
    /// retiring one changes who can read whose pastoral records. That is not a team
    /// lead's decision and not a pastor's.
    /// </summary>
    [ApiController]
    [Route("api/admin/campuses")]
    [Produces("application/json")]
    [Authorize(Policy = PolicyNames.AdminOnly)]
    public sealed class CampusAdminController : ControllerBase
    {
        private readonly ICampusService _campuses;

        public CampusAdminController(ICampusService campuses) => _campuses = campuses;

        /// <summary>Every campus, with what is attached to each.</summary>
        [HttpGet]
        [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<CampusDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> List([FromQuery] bool includeInactive = true) =>
            Ok(await _campuses.ListAsync(includeInactive));

        [HttpPost]
        [EnableRateLimiting(RateLimitPolicies.Sensitive)]
        [ProducesResponseType(typeof(ApiResponse<CampusDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<ApiResponse<CampusDto>>> Create([FromBody] CreateCampusRequest request)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            return HttpResponseHelper.CreateHttpResponse(await _campuses.CreateAsync(request));
        }

        [HttpPut("{id}")]
        [EnableRateLimiting(RateLimitPolicies.Sensitive)]
        [ProducesResponseType(typeof(ApiResponse<CampusDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<ApiResponse<CampusDto>>> Update(string id, [FromBody] UpdateCampusRequest request)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            // Deliberately NOT Ulid.IsValid here. The campus seeded by schema.sql is
            // '01JCAMPUS0000000000000001' — 25 characters, and it contains a 'U',
            // which Crockford base32 excludes. It is a readable placeholder rather
            // than a generated id, and validating the format would make the one
            // campus every deployment starts with the only one that cannot be
            // edited. An unknown id falls through to "Campus not found." anyway.
            return HttpResponseHelper.CreateHttpResponse(await _campuses.UpdateAsync(id, request));
        }
    }

    /// <summary>
    /// The picker feed, split from the admin controller because its audience is
    /// different: intake operators need the list of campuses to file a visitor
    /// against, and they are not administrators.
    ///
    /// It returns id, code and name only — never the counts, which would tell an
    /// operator at one site how many open cases another site is carrying.
    /// </summary>
    [ApiController]
    [Route("api/campuses")]
    [Produces("application/json")]
    public sealed class CampusReferenceController : ControllerBase
    {
        private readonly ICampusService _campuses;

        public CampusReferenceController(ICampusService campuses) => _campuses = campuses;

        /// <summary>
        /// Active campuses the caller may file against. A campus-scoped account gets
        /// exactly one back, so the picker cannot be used to reach across sites.
        /// </summary>
        [HttpGet("options")]
        [Authorize(Policy = PolicyNames.CanRecordVisitors)]
        [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<CampusOptionDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Options() => Ok(await _campuses.OptionsAsync());
    }
}
