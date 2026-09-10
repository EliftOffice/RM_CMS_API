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

            // The credential was right but the sign-in is not finished — this account
            // has to confirm on Telegram. 200, because nothing has gone wrong, but no
            // cookie is written and the payload carries no token: there is no session
            // to hand out until the tap arrives.
            if (result.Data.Payload.RequiresTelegramVerification)
                return Ok(new ApiResponse<AuthResultDto>(ResponseType.Success, result.Message, result.Data.Payload));

            return IssueSession(result.Data, "Signed in successfully");
        }

        /// <summary>
        /// Sends the Telegram confirmation for a sign-in that is waiting on one.
        /// </summary>
        /// <remarks>
        /// Anonymous, because by definition there is no session yet — the challenge id
        /// returned by <c>login</c> is the only thing the caller holds, and it was
        /// issued to a browser that already passed the first factor.
        ///
        /// Rate-limited on the login bucket. This one DOES belong there: it makes
        /// somebody's phone buzz, so it is the surface worth keeping tight.
        /// </remarks>
        [HttpPost("verify/send")]
        [AllowAnonymous]
        [EnableRateLimiting(RateLimitPolicies.Login)]
        [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
        public async Task<IActionResult> SendVerification([FromBody] LoginChallengeRequest request)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            return Ok(await _identity.SendVerificationPromptAsync(request.ChallengeId, BuildContext()));
        }

        /// <summary>
        /// Asks whether the tap has arrived, and issues the session on the poll that
        /// finds it.
        /// </summary>
        /// <remarks>
        /// Its own rate limit rather than the login one: a screen waiting three minutes
        /// polls perhaps ninety times, which would exhaust a five-per-five-minutes
        /// bucket in the first fifteen seconds and strand a sign-in that was going
        /// perfectly well.
        ///
        /// The session arrives here exactly once. Two tabs polling the same challenge
        /// resolve to one winner in the database, and the loser is told the sign-in is
        /// no longer waiting.
        /// </remarks>
        [HttpPost("verify/poll")]
        [AllowAnonymous]
        [EnableRateLimiting(RateLimitPolicies.LoginMethod)]
        [ProducesResponseType(typeof(ApiResponse<LoginChallengeStatusDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> PollVerification([FromBody] LoginChallengeRequest request)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            var result = await _identity.PollVerificationAsync(request.ChallengeId, BuildContext());

            // The refresh token goes into the HttpOnly cookie and is stripped from the
            // body, exactly as an ordinary sign-in does it.
            if (result.Session is not null)
            {
                WriteRefreshCookie(result.Session.RawRefreshToken, result.Session.RefreshExpiresAt);
                result.Session.Payload.RefreshToken = null;
            }

            return Ok(result.Response);
        }

        /// <summary>
        /// Says whether a given mobile number has to supply a password.
        /// </summary>
        /// <remarks>
        /// The login screen asks this after the number is typed, then either signs the
        /// person in or reveals a password box. It exists because some of the people
        /// who use this system cannot read a password prompt, and an administrator can
        /// mark their account accordingly.
        ///
        /// Anonymous, because it runs before anybody is signed in, and on its own rate
        /// limit rather than the login one. Sharing that bucket would mean every
        /// successful sign-in spent two of the five permitted attempts, and somebody who
        /// mistyped their number once would be locked out before their first real try.
        ///
        /// An unknown number answers "password required", the same as a normal account,
        /// so this cannot be used to find out who has an account here.
        /// </remarks>
        [HttpPost("login-method")]
        [AllowAnonymous]
        [EnableRateLimiting(RateLimitPolicies.LoginMethod)]
        [ProducesResponseType(typeof(ApiResponse<LoginMethodDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
        public async Task<IActionResult> LoginMethod([FromBody] LoginMethodRequest request)
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            return Ok(await _identity.GetLoginMethodAsync(request, BuildContext()));
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

        /// <summary>
        /// The password rules, so a screen can state them up front instead of letting
        /// the user discover them one rejection at a time.
        ///
        /// Served from the same <see cref="AuthOptions"/> the validator reads, so the
        /// rules shown and the rules enforced cannot drift apart. Anonymous because the
        /// change-password screen is reachable before a session is fully established,
        /// and because password *requirements* are not secret — publishing them costs
        /// nothing an attacker could not learn by trying.
        /// </summary>
        [HttpGet("password-policy")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(ApiResponse<PasswordPolicyDto>), StatusCodes.Status200OK)]
        public ActionResult<ApiResponse<PasswordPolicyDto>> PasswordPolicy()
        {
            var policy = new PasswordPolicyDto
            {
                MinLength = _options.PasswordMinLength,
                MaxLength = _options.PasswordMaxLength,
                RequireUppercase = _options.RequireUppercase,
                RequireLowercase = _options.RequireLowercase,
                RequireDigit = _options.RequireDigit,
                RequireNonAlphanumeric = _options.RequireNonAlphanumeric,
                HistoryCount = _options.PasswordHistoryCount
            };

            policy.Rules = BuildRuleList(policy);

            return Ok(new ApiResponse<PasswordPolicyDto>(
                ResponseType.Success, "Password policy", policy));
        }

        /// <summary>
        /// Human-readable rules in the order a person would check them. Kept beside the
        /// flags so a screen can render the list without restating the logic.
        /// </summary>
        private static List<string> BuildRuleList(PasswordPolicyDto p)
        {
            var rules = new List<string>
            {
                $"At least {p.MinLength} characters long"
            };

            if (p.RequireUppercase)       rules.Add("At least one uppercase letter (A-Z)");
            if (p.RequireLowercase)       rules.Add("At least one lowercase letter (a-z)");
            if (p.RequireDigit)           rules.Add("At least one number (0-9)");
            if (p.RequireNonAlphanumeric) rules.Add("At least one special character (for example @ # ! $)");

            // The two that surprise people, so they are stated explicitly rather than
            // discovered by rejection.
            rules.Add("Must not contain the account's username, first name or last name");
            rules.Add("Must not be a common or predictable password");

            if (p.HistoryCount > 0)
                rules.Add($"Must not be one of the last {p.HistoryCount} passwords used");

            return rules;
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
