using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RM_CMS.BLL.Auth;
using RM_CMS.Data.DTO.Auth;
using RM_CMS.Security;
using RM_CMS.Utilities;

namespace RM_CMS.Controllers.Auth
{
    /// <summary>
    /// Account administration. Every action is Admin-only and audited.
    ///
    /// This is the migration path for the existing user base: staff rows back-filled by
    /// <c>auth_schema.sql</c> arrive disabled and password-less, and an administrator
    /// enables them and issues a first password here.
    /// </summary>
    [ApiController]
    [Route("api/admin/auth")]
    [Authorize(Policy = Policies.AdminOnly)]
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
    [Produces("application/json")]
    public sealed class AuthAdminController : ControllerBase
    {
        private readonly IAuthBLL _auth;
        private readonly ICurrentUser _currentUser;
        private readonly ILogger<AuthAdminController> _logger;

        public AuthAdminController(IAuthBLL auth, ICurrentUser currentUser, ILogger<AuthAdminController> logger)
        {
            _auth = auth;
            _currentUser = currentUser;
            _logger = logger;
        }

        /// <summary>Paged list of accounts.</summary>
        [HttpGet("users")]
        [ProducesResponseType(typeof(ApiResponse<PaginatedResultDto<AuthUserDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> ListUsers(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 25,
            [FromQuery] string? search = null)
        {
            return Ok(await _auth.ListUsersAsync(page, pageSize, search));
        }

        /// <summary>
        /// Creates an account. If <c>initialPassword</c> is omitted a compliant password is
        /// generated and returned exactly once — it is never recoverable afterwards.
        /// </summary>
        [HttpPost("users")]
        [ProducesResponseType(typeof(ApiResponse<CreatedCredentialDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<ApiResponse<CreatedCredentialDto>>> CreateUser([FromBody] CreateAuthUserRequest request)
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            var result = await _auth.CreateUserAsync(request, _currentUser.UserId, BuildContext());

            return HttpResponseHelper.CreateHttpResponse(result);
        }

        /// <summary>Sets or resets a user's password and revokes all of their sessions.</summary>
        [HttpPost("users/{userId}/set-password")]
        [ProducesResponseType(typeof(ApiResponse<CreatedCredentialDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<ApiResponse<CreatedCredentialDto>>> SetPassword(string userId, [FromBody] SetPasswordRequest request)
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            var result = await _auth.SetPasswordAsync(userId, request, _currentUser.UserId, BuildContext());

            return HttpResponseHelper.CreateHttpResponse(result);
        }

        /// <summary>Replaces a user's roles. Existing sessions are revoked so the change takes effect immediately.</summary>
        [HttpPut("users/{userId}/roles")]
        [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
        public async Task<ActionResult<ApiResponse<bool>>> UpdateRoles(string userId, [FromBody] UpdateRolesRequest request)
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            var result = await _auth.UpdateRolesAsync(userId, request, _currentUser.UserId, BuildContext());

            return HttpResponseHelper.CreateHttpResponse(result);
        }

        /// <summary>Enables or disables an account. Disabling revokes all sessions immediately.</summary>
        [HttpPut("users/{userId}/status")]
        [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
        public async Task<ActionResult<ApiResponse<bool>>> SetStatus(string userId, [FromBody] SetAccountStatusRequest request)
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            var result = await _auth.SetAccountStatusAsync(userId, request, _currentUser.UserId, BuildContext());

            return HttpResponseHelper.CreateHttpResponse(result);
        }

        /// <summary>The list of assignable role names.</summary>
        [HttpGet("roles")]
        [ProducesResponseType(typeof(ApiResponse<string[]>), StatusCodes.Status200OK)]
        public IActionResult GetRoles() =>
            Ok(new ApiResponse<string[]>(ResponseType.Success, "Roles", Security.Roles.All));

        private AuthRequestContext BuildContext() => new(
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString(),
            HttpContext.TraceIdentifier);
    }
}
