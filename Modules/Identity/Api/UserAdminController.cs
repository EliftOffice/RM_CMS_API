using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RM_CMS.Middleware;
using RM_CMS.Modules.Identity.Domain;
using RM_CMS.Modules.Identity.Services;
using RM_CMS.Security;
using RM_CMS.Utilities;

namespace RM_CMS.Modules.Identity.Api
{
    /// <summary>
    /// User management for administrators: who exists, what they may do, and how
    /// somebody moves up.
    ///
    /// Distinct from <see cref="AccountAdminController"/>, which manages the login
    /// record alone. This one works at the level of a PERSON — their identity, their
    /// access, their authority and their volunteer standing together — because that
    /// is the unit an administrator actually thinks in.
    ///
    /// Every action is Admin-only. Role changes are the mechanism by which somebody
    /// could grant themselves more authority, so there is no lesser tier here.
    /// </summary>
    [ApiController]
    [Route("api/admin/users")]
    [Produces("application/json")]
    [Authorize(Policy = PolicyNames.AdminOnly)]
    public sealed class UserAdminController : ControllerBase
    {
        private readonly IUserDirectoryService _users;

        public UserAdminController(IUserDirectoryService users) => _users = users;

        /// <summary>
        /// The directory. One row per person who has a login, a volunteer record, or
        /// both — a plain visitor is not a user and is found through the people picker.
        ///
        /// <paramref name="role"/> backs the per-role screens (Volunteers, Team Leads,
        /// Pastors, Data Entry) so they are all one endpoint rather than four.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(ApiResponse<PagedResult<DirectoryUserDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> List(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 25,
            [FromQuery] string? search = null,
            [FromQuery] string? role = null,
            [FromQuery] bool? isActive = null)
        {
            return Ok(await _users.ListAsync(page, pageSize, search, role, isActive));
        }

        /// <summary>One person's full standing, plus when each role was granted.</summary>
        [HttpGet("{personId}")]
        [ProducesResponseType(typeof(ApiResponse<DirectoryUserDetailDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Get(string personId)
        {
            if (!Ulid.IsValid(personId))
                return BadRequest(new ApiResponse<DirectoryUserDetailDto>(
                    ResponseType.Warning, "Invalid person id.", null!));

            return Ok(await _users.GetAsync(personId));
        }

        /// <summary>
        /// Creates a user directly. Attaches to an existing person when
        /// <c>personId</c> is supplied, otherwise records the person at the same time
        /// — through the same duplicate detection the intake screen uses, so this
        /// cannot become a back door for a second copy of somebody.
        /// </summary>
        [HttpPost]
        [EnableRateLimiting(RateLimitPolicies.Sensitive)]
        [ProducesResponseType(typeof(ApiResponse<UserChangeResultDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Create([FromBody] CreateUserRequest request)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            return Ok(await _users.CreateAsync(request, BuildContext()));
        }

        /// <summary>
        /// Moves an existing person up the ladder:
        /// Visitor -> Volunteer -> Team Lead -> Pastor.
        ///
        /// Additive by design. A promoted volunteer keeps their volunteer record and
        /// their existing role, so a player-coach team lead can still carry cases, and
        /// their history stays attached to the same person throughout.
        /// </summary>
        [HttpPost("{personId}/promote")]
        [EnableRateLimiting(RateLimitPolicies.Sensitive)]
        [ProducesResponseType(typeof(ApiResponse<UserChangeResultDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Promote(string personId, [FromBody] PromoteRequest request)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            if (!Ulid.IsValid(personId))
                return BadRequest(new ApiResponse<UserChangeResultDto>(
                    ResponseType.Warning, "Invalid person id.", null!));

            return Ok(await _users.PromoteAsync(personId, request, BuildContext()));
        }

        /// <summary>The roles an administrator may grant, with display labels.</summary>
        [HttpGet("/api/admin/user-roles")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
        public IActionResult Roles() =>
            Ok(new ApiResponse<object>(ResponseType.Success, "Roles", new
            {
                roles = new[]
                {
                    new { code = RoleCodes.DataEntry, label = "Data Entry Operator", onLadder = false },
                    new { code = RoleCodes.Volunteer, label = "Volunteer",           onLadder = true  },
                    new { code = RoleCodes.TeamLead,  label = "Team Lead",           onLadder = true  },
                    new { code = RoleCodes.Pastor,    label = "Pastor",              onLadder = true  },
                    new { code = RoleCodes.Admin,     label = "Administrator",       onLadder = false }
                }
            }));

        private RequestContext BuildContext() => new(
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString(),
            HttpContext.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString());
    }
}
