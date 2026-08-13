using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using RM_CMS.Modules.Identity.Domain;
using RM_CMS.Modules.Identity.Services;
using RM_CMS.Security;
using RM_CMS.Utilities;

namespace RM_CMS.Modules.Identity.Api
{
    /// <summary>
    /// Sign-in, token refresh, sign-out and self-service password change.
    ///
    /// Token placement:
    ///   • access token  -> response body, held in memory by the client (never localStorage)
    ///   • refresh token -> HttpOnly + Secure + SameSite=Strict cookie, unreadable by script
    ///
    /// That split is the point: an XSS payload can steal at most a token that expires
    /// in minutes, and cannot silently mint new ones.
    /// </summary>
    [ApiController]
    [Route("api/auth")]
    [Produces("application/json")]
    public sealed class AuthController : ControllerBase
    {
        private readonly IIdentityService _identity;
        private readonly AuthOptions _options;
        private readonly ICurrentIdentity _current;

        public AuthController(
            IIdentityService identity,
            IOptions<AuthOptions> options,
            ICurrentIdentity current)
        {
            _identity = identity;
            _options = options.Value;
            _current = current;
        }

        /// <summary>Exchanges credentials for an access token and a refresh cookie.</summary>
        [HttpPost("login")]
        [AllowAnonymous]
        [EnableRateLimiting(RateLimitPolicies.Login)]
        [ProducesResponseType(typeof(ApiResponse<AuthResultDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            var result = await _identity.LoginAsync(request, BuildContext());

            if (result.ResponseType != ResponseType.Success || result.Data is null)
            {
                // 401, not 400: the credentials were understood and rejected.
                return Unauthorized(new ApiResponse<AuthResultDto>(result.ResponseType, result.Message, null!));
            }

            return IssueSession(result.Data, "Signed in successfully");
        }

        /// <summary>
        /// Rotates the refresh cookie and returns a fresh access token.
        /// Anonymous by design — the caller's proof is the cookie, and their access
        /// token has usually just expired.
        /// </summary>
        [HttpPost("refresh")]
        [AllowAnonymous]
        [EnableRateLimiting(RateLimitPolicies.Refresh)]
        [ProducesResponseType(typeof(ApiResponse<AuthResultDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> Refresh([FromBody] RefreshRequest? request)
        {
            var rawToken = ReadRefreshCookie() ?? request?.RefreshToken;

            var result = await _identity.RefreshAsync(rawToken, BuildContext());

            if (result.ResponseType != ResponseType.Success || result.Data is null)
            {
                // Clear the cookie so a replayed or poisoned token is not resent forever.
                ClearRefreshCookie();
                return Unauthorized(new ApiResponse<AuthResultDto>(ResponseType.Error, result.Message, null!));
            }

            return IssueSession(result.Data, "Token refreshed");
        }

        /// <summary>
        /// Ends the session on this device. Anonymous so that signing out still works
        /// once the access token has expired.
        /// </summary>
        [HttpPost("logout")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Logout()
        {
            var result = await _identity.LogoutAsync(ReadRefreshCookie(), _current.AccountId, BuildContext());

            ClearRefreshCookie();

            return Ok(result);
        }

        /// <summary>Ends every session for the signed-in account, on all devices.</summary>
        [HttpPost("logout-all")]
        [Authorize(Policy = PolicyNames.Authenticated)]
        [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
        public async Task<IActionResult> LogoutAll()
        {
            var accountId = _current.AccountId;

            if (string.IsNullOrWhiteSpace(accountId))
                return Unauthorized();

            var result = await _identity.LogoutAllAsync(accountId, BuildContext());

            ClearRefreshCookie();

            return Ok(result);
        }

        /// <summary>The signed-in account's own profile and roles.</summary>
        [HttpGet("me")]
        [Authorize(Policy = PolicyNames.Authenticated)]
        [ProducesResponseType(typeof(ApiResponse<AccountDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Me()
        {
            var accountId = _current.AccountId;

            if (string.IsNullOrWhiteSpace(accountId))
                return Unauthorized();

            return Ok(await _identity.GetAccountAsync(accountId));
        }

        /// <summary>Active sessions — one per signed-in device.</summary>
        [HttpGet("sessions")]
        [Authorize(Policy = PolicyNames.Authenticated)]
        [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<SessionDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Sessions()
        {
            var accountId = _current.AccountId;

            if (string.IsNullOrWhiteSpace(accountId))
                return Unauthorized();

            return Ok(await _identity.GetSessionsAsync(accountId));
        }

        /// <summary>
        /// Changes the caller's own password. Requires the current password, and
        /// revokes every session including this one.
        /// </summary>
        [HttpPost("change-password")]
        [Authorize(Policy = PolicyNames.Authenticated)]
        [EnableRateLimiting(RateLimitPolicies.Sensitive)]
        [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
        public async Task<ActionResult<ApiResponse<bool>>> ChangePassword([FromBody] ChangePasswordRequest request)
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            var accountId = _current.AccountId;

            if (string.IsNullOrWhiteSpace(accountId))
                return Unauthorized();

            var result = await _identity.ChangePasswordAsync(accountId, request, BuildContext());

            if (result.ResponseType == ResponseType.Success)
                ClearRefreshCookie();

            return HttpResponseHelper.CreateHttpResponse(result);
        }

        // ------------------------------------------------------------------
        // Cookie handling
        // ------------------------------------------------------------------

        private IActionResult IssueSession(IssuedSession issued, string message)
        {
            WriteRefreshCookie(issued.RawRefreshToken, issued.RefreshExpiresAt);

            // Deliberately absent from the body: the refresh token lives only in the
            // HttpOnly cookie, so page script can never read or exfiltrate it.
            issued.Payload.RefreshToken = null;

            return Ok(new ApiResponse<AuthResultDto>(ResponseType.Success, message, issued.Payload));
        }

        private string CookieName
        {
            get
            {
                // The __Host- prefix is only valid on a Secure cookie over HTTPS. Over
                // plain HTTP (local development) the browser drops it silently, which
                // looks exactly like "refresh is broken" — so fall back to an
                // unprefixed name there.
                var configured = _options.RefreshCookieName;

                if (Request.IsHttps || !configured.StartsWith("__Host-", StringComparison.Ordinal))
                    return configured;

                return configured["__Host-".Length..];
            }
        }

        private string? ReadRefreshCookie() =>
            Request.Cookies.TryGetValue(CookieName, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : null;

        private void WriteRefreshCookie(string rawToken, DateTime expiresAt)
        {
            Response.Cookies.Append(CookieName, rawToken, new CookieOptions
            {
                HttpOnly = true,                 // unreadable by document.cookie / XSS
                Secure = true,                   // HTTPS only
                SameSite = SameSiteMode.Strict,  // never sent cross-site => CSRF-proof
                Path = "/",                      // required by the __Host- prefix
                Expires = new DateTimeOffset(expiresAt, TimeSpan.Zero),
                IsEssential = true
            });
        }

        private void ClearRefreshCookie()
        {
            Response.Cookies.Append(CookieName, string.Empty, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Path = "/",
                Expires = DateTimeOffset.UnixEpoch,
                IsEssential = true
            });
        }

        private RequestContext BuildContext() => new(
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString(),
            HttpContext.TraceIdentifier);
    }
}
