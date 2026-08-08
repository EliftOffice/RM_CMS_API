using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using RM_CMS.BLL.Auth;
using RM_CMS.Data.DTO.Auth;
using RM_CMS.Security;
using RM_CMS.Utilities;

namespace RM_CMS.Controllers.Auth
{
    /// <summary>
    /// Sign-in, token refresh, sign-out and self-service password change.
    ///
    /// Token placement:
    ///   • access token  -> response body, held in memory by the client (never localStorage)
    ///   • refresh token -> HttpOnly + Secure + SameSite=Strict cookie, unreadable by JS
    /// That split means an XSS payload can steal at most a token that expires in minutes,
    /// and cannot silently mint new ones.
    /// </summary>
    [ApiController]
    [Route("api/auth")]
    [Produces("application/json")]
    public sealed class AuthController : ControllerBase
    {
        private readonly IAuthBLL _auth;
        private readonly AuthOptions _options;
        private readonly ICurrentUser _currentUser;
        private readonly ILogger<AuthController> _logger;

        public AuthController(
            IAuthBLL auth,
            IOptions<AuthOptions> options,
            ICurrentUser currentUser,
            ILogger<AuthController> logger)
        {
            _auth = auth;
            _options = options.Value;
            _currentUser = currentUser;
            _logger = logger;
        }

        /// <summary>Exchanges credentials for an access token and a refresh cookie.</summary>
        [HttpPost("login")]
        [AllowAnonymous]
        [EnableRateLimiting(RateLimitPolicies.Login)]
        [ProducesResponseType(typeof(ApiResponse<AuthResultDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            var result = await _auth.LoginAsync(request, BuildContext());

            if (result.ResponseType != ResponseType.Success || result.Data is null)
            {
                // 401, not 400: the credentials were understood and rejected.
                return Unauthorized(new ApiResponse<AuthResultDto>(result.ResponseType, result.Message, null!));
            }

            return IssueTokens(result.Data, "Signed in successfully");
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
        public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest? request)
        {
            var rawToken = ReadRefreshCookie() ?? request?.RefreshToken;

            var result = await _auth.RefreshAsync(rawToken, BuildContext());

            if (result.ResponseType != ResponseType.Success || result.Data is null)
            {
                // Clear the cookie so a poisoned/replayed token is not resent forever.
                ClearRefreshCookie();
                return Unauthorized(new ApiResponse<AuthResultDto>(ResponseType.Error, result.Message, null!));
            }

            return IssueTokens(result.Data, "Token refreshed");
        }

        /// <summary>Ends the current session (this device) and clears the refresh cookie.</summary>
        [HttpPost("logout")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Logout()
        {
            var rawToken = ReadRefreshCookie();

            var result = await _auth.LogoutAsync(rawToken, _currentUser.UserId, BuildContext());

            ClearRefreshCookie();

            return Ok(result);
        }

        /// <summary>Ends every session for the signed-in user, on all devices.</summary>
        [HttpPost("logout-all")]
        [Authorize(Policy = Policies.Authenticated)]
        [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
        public async Task<IActionResult> LogoutAll()
        {
            var userId = _currentUser.UserId;

            if (string.IsNullOrWhiteSpace(userId))
                return Unauthorized();

            var result = await _auth.LogoutAllAsync(userId, BuildContext());

            ClearRefreshCookie();

            return Ok(result);
        }

        /// <summary>The signed-in user's own profile and roles.</summary>
        [HttpGet("me")]
        [Authorize(Policy = Policies.Authenticated)]
        [ProducesResponseType(typeof(ApiResponse<AuthUserDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Me()
        {
            var userId = _currentUser.UserId;

            if (string.IsNullOrWhiteSpace(userId))
                return Unauthorized();

            return Ok(await _auth.GetCurrentUserAsync(userId));
        }

        /// <summary>Lists the user's active sessions (one per signed-in device).</summary>
        [HttpGet("sessions")]
        [Authorize(Policy = Policies.Authenticated)]
        [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ActiveSessionDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Sessions()
        {
            var userId = _currentUser.UserId;

            if (string.IsNullOrWhiteSpace(userId))
                return Unauthorized();

            return Ok(await _auth.GetActiveSessionsAsync(userId));
        }

        /// <summary>
        /// Changes the caller's own password. Succeeds only with the current password,
        /// and revokes every session including this one.
        /// </summary>
        [HttpPost("change-password")]
        [Authorize(Policy = Policies.Authenticated)]
        [EnableRateLimiting(RateLimitPolicies.Sensitive)]
        [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
        public async Task<ActionResult<ApiResponse<bool>>> ChangePassword([FromBody] ChangePasswordRequest request)
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            var userId = _currentUser.UserId;

            if (string.IsNullOrWhiteSpace(userId))
                return Unauthorized();

            var result = await _auth.ChangePasswordAsync(userId, request, BuildContext());

            if (result.ResponseType == ResponseType.Success)
                ClearRefreshCookie();

            return HttpResponseHelper.CreateHttpResponse(result);
        }

        // ------------------------------------------------------------------
        // Cookie handling
        // ------------------------------------------------------------------

        private IActionResult IssueTokens(AuthIssueResult issued, string message)
        {
            WriteRefreshCookie(issued.RawRefreshToken, issued.RefreshExpiresUtc);

            // The refresh token is deliberately absent from the body: it lives only in the
            // HttpOnly cookie, so page JavaScript can never read or exfiltrate it.
            issued.Payload.RefreshToken = null;

            return Ok(new ApiResponse<AuthResultDto>(ResponseType.Success, message, issued.Payload));
        }

        private string CookieName
        {
            get
            {
                // The __Host- prefix is only valid on a Secure cookie served over HTTPS.
                // Over plain HTTP (local development) the browser silently drops it, which
                // would look like "refresh is broken", so fall back to an unprefixed name.
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

        private void WriteRefreshCookie(string rawToken, DateTime expiresUtc)
        {
            Response.Cookies.Append(CookieName, rawToken, new CookieOptions
            {
                HttpOnly = true,                        // unreadable by document.cookie / XSS
                Secure = true,                          // HTTPS only
                SameSite = SameSiteMode.Strict,         // not sent on any cross-site request => CSRF-proof
                Path = "/",                             // required by the __Host- prefix
                Expires = new DateTimeOffset(expiresUtc, TimeSpan.Zero),
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

        private AuthRequestContext BuildContext() => new(
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString(),
            HttpContext.TraceIdentifier);
    }
}
