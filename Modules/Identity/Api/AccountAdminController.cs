using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RM_CMS.Modules.Identity.Domain;
using RM_CMS.Modules.Identity.Services;
using RM_CMS.Security;
using RM_CMS.Utilities;

namespace RM_CMS.Modules.Identity.Api
{
    /// <summary>
    /// Account administration. Every action is Admin-only and audited.
    ///
    /// Accounts attach to people who already exist — the caller supplies a person's
    /// public id rather than their name and number. That separation is what removed
    /// the five-way duplication of contact details in the MVP, so the admin flow is
    /// "find the person, then grant access".
    /// </summary>
    [ApiController]
    [Route("api/admin/accounts")]
    [Authorize(Policy = PolicyNames.AdminOnly)]
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
    [Produces("application/json")]
    public sealed class AccountAdminController : ControllerBase
    {
        private readonly IIdentityService _identity;
        private readonly ICurrentIdentity _current;

        public AccountAdminController(IIdentityService identity, ICurrentIdentity current)
        {
            _identity = identity;
            _current = current;
        }

        /// <summary>Paged account list, optionally filtered by search text or role.</summary>
        [HttpGet]
        [ProducesResponseType(typeof(ApiResponse<PagedResult<AccountDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> List(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 25,
            [FromQuery] string? search = null,
            [FromQuery] string? role = null)
        {
            return Ok(await _identity.ListAccountsAsync(page, pageSize, search, role));
        }

        /// <summary>A single account by its public id.</summary>
        [HttpGet("{accountId}")]
        [ProducesResponseType(typeof(ApiResponse<AccountDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Get(string accountId)
        {
            // Reject a malformed id before it reaches the database.
            if (!Ulid.IsValid(accountId))
                return BadRequest(new ApiResponse<AccountDto>(ResponseType.Warning, "Invalid account id.", null!));

            return Ok(await _identity.GetAccountAsync(accountId));
        }

        /// <summary>
        /// Grants an existing person access. If <c>initialPassword</c> is omitted a
        /// compliant password is generated and returned exactly once — it cannot be
        /// retrieved afterwards.
        /// </summary>
        [HttpPost]
        [ProducesResponseType(typeof(ApiResponse<CreatedCredentialDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<ApiResponse<CreatedCredentialDto>>> Create([FromBody] CreateAccountRequest request)
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            var result = await _identity.CreateAccountAsync(request, _current.AccountId, BuildContext());

            return HttpResponseHelper.CreateHttpResponse(result);
        }

        /// <summary>Sets or resets a password and revokes all of that account's sessions.</summary>
        [HttpPost("{accountId}/password")]
        [ProducesResponseType(typeof(ApiResponse<CreatedCredentialDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<ApiResponse<CreatedCredentialDto>>> SetPassword(
            string accountId, [FromBody] SetPasswordRequest request)
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            if (!Ulid.IsValid(accountId))
                return BadRequest(new ApiResponse<CreatedCredentialDto>(ResponseType.Warning, "Invalid account id.", null!));

            var result = await _identity.SetPasswordAsync(accountId, request, _current.AccountId, BuildContext());

            return HttpResponseHelper.CreateHttpResponse(result);
        }

        /// <summary>
        /// Replaces an account's roles. Sessions are revoked so the change takes effect
        /// immediately rather than when the current access token happens to expire.
        /// </summary>
        [HttpPut("{accountId}/roles")]
        [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
        public async Task<ActionResult<ApiResponse<bool>>> UpdateRoles(
            string accountId, [FromBody] UpdateRolesRequest request)
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            if (!Ulid.IsValid(accountId))
                return BadRequest(new ApiResponse<bool>(ResponseType.Warning, "Invalid account id.", false));

            var result = await _identity.UpdateRolesAsync(accountId, request, _current.AccountId, BuildContext());

            return HttpResponseHelper.CreateHttpResponse(result);
        }

        /// <summary>Enables or disables an account. Disabling revokes all sessions at once.</summary>
        [HttpPut("{accountId}/status")]
        [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
        public async Task<ActionResult<ApiResponse<bool>>> SetStatus(
            string accountId, [FromBody] SetAccountStatusRequest request)
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            if (!Ulid.IsValid(accountId))
                return BadRequest(new ApiResponse<bool>(ResponseType.Warning, "Invalid account id.", false));

            var result = await _identity.SetAccountStatusAsync(accountId, request, _current.AccountId, BuildContext());

            return HttpResponseHelper.CreateHttpResponse(result);
        }

        /// <summary>The assignable role codes.</summary>
        [HttpGet("/api/admin/roles")]
        [ProducesResponseType(typeof(ApiResponse<string[]>), StatusCodes.Status200OK)]
        public IActionResult GetRoles() =>
            Ok(new ApiResponse<string[]>(ResponseType.Success, "Roles", RoleCodes.All));

        private RequestContext BuildContext() => new(
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString(),
            HttpContext.TraceIdentifier);
    }
}
