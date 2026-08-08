using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace RM_CMS.Security
{
    /// <summary>
    /// Authorises callers that cannot hold a JWT — the external cron scheduler and the
    /// Telegram webhook. Both present a pre-shared secret instead of a user identity.
    /// </summary>
    public static class SecretComparer
    {
        /// <summary>
        /// Fixed-time comparison. A naive <c>==</c> returns as soon as bytes differ, which
        /// leaks the secret one character at a time to an attacker who can measure latency.
        /// </summary>
        public static bool Equals(string? provided, string? expected)
        {
            if (string.IsNullOrEmpty(provided) || string.IsNullOrEmpty(expected))
                return false;

            var providedBytes = Encoding.UTF8.GetBytes(provided);
            var expectedBytes = Encoding.UTF8.GetBytes(expected);

            return CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
        }
    }

    /// <summary>
    /// Requirement satisfied either by an Admin JWT or by a valid service key header.
    /// Used for the scheduled-job endpoints, which a machine triggers on a timer but an
    /// administrator may also fire by hand.
    /// </summary>
    public sealed class ServiceKeyOrAdminRequirement : IAuthorizationRequirement
    {
        public const string HeaderName = "X-Service-Key";
    }

    public sealed class ServiceKeyOrAdminHandler : AuthorizationHandler<ServiceKeyOrAdminRequirement>
    {
        private readonly IHttpContextAccessor _accessor;
        private readonly AuthOptions _options;
        private readonly ILogger<ServiceKeyOrAdminHandler> _logger;

        public ServiceKeyOrAdminHandler(
            IHttpContextAccessor accessor,
            IOptions<AuthOptions> options,
            ILogger<ServiceKeyOrAdminHandler> logger)
        {
            _accessor = accessor;
            _options = options.Value;
            _logger = logger;
        }

        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context, ServiceKeyOrAdminRequirement requirement)
        {
            // Path 1 — a signed-in administrator.
            if (context.User.HasClaim(AppClaimTypes.Role, Roles.Admin))
            {
                context.Succeed(requirement);
                return Task.CompletedTask;
            }

            // Path 2 — the scheduler's service key.
            var httpContext = _accessor.HttpContext;

            if (httpContext is not null &&
                httpContext.Request.Headers.TryGetValue(ServiceKeyOrAdminRequirement.HeaderName, out var provided))
            {
                if (string.IsNullOrWhiteSpace(_options.ServiceApiKey))
                {
                    _logger.LogError(
                        "A service key was presented but Auth:ServiceApiKey is not configured. " +
                        "Set it via the environment variable Auth__ServiceApiKey.");
                }
                else if (SecretComparer.Equals(provided.ToString(), _options.ServiceApiKey))
                {
                    _logger.LogInformation("Scheduled job authorised via service key from {RemoteIp}",
                        httpContext.Connection.RemoteIpAddress);

                    context.Succeed(requirement);
                    return Task.CompletedTask;
                }
                else
                {
                    _logger.LogWarning("Invalid service key presented from {RemoteIp}",
                        httpContext.Connection.RemoteIpAddress);
                }
            }

            // Neither path matched — leave unsatisfied, which produces a 403.
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Validates Telegram's <c>X-Telegram-Bot-Api-Secret-Token</c> header.
    ///
    /// Telegram sends this on every webhook delivery when the webhook was registered with
    /// a <c>secret_token</c>. Without it the webhook URL is world-callable, so anyone who
    /// guesses the path can inject fake chat messages.
    ///
    /// Register the webhook with:
    ///   curl -X POST "https://api.telegram.org/bot&lt;TOKEN&gt;/setWebhook" \
    ///        -d "url=https://rmoffice.online/api/Telegram/webhook" \
    ///        -d "secret_token=&lt;same value as Auth__TelegramWebhookSecret&gt;"
    /// </summary>
    /// <remarks>
    /// Implemented as an <see cref="IAsyncAuthorizationFilter"/>, not an action filter:
    /// authorization filters run before model binding and validation, so a caller with no
    /// secret gets a flat 404 instead of a 400 that would confirm the route exists.
    /// </remarks>
    public sealed class TelegramWebhookSecretAttribute : Attribute, IAsyncAuthorizationFilter
    {
        private const string HeaderName = "X-Telegram-Bot-Api-Secret-Token";

        public Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            var options = context.HttpContext.RequestServices
                .GetRequiredService<IOptions<AuthOptions>>().Value;

            var logger = context.HttpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("TelegramWebhook");

            if (string.IsNullOrWhiteSpace(options.TelegramWebhookSecret))
            {
                // Fail closed. An unconfigured secret must not mean "allow everyone".
                logger.LogError(
                    "Telegram webhook rejected: Auth:TelegramWebhookSecret is not configured. " +
                    "Set the environment variable Auth__TelegramWebhookSecret and re-register the webhook.");

                context.Result = new StatusCodeResult(StatusCodes.Status503ServiceUnavailable);
                return Task.CompletedTask;
            }

            context.HttpContext.Request.Headers.TryGetValue(HeaderName, out var provided);

            if (!SecretComparer.Equals(provided.ToString(), options.TelegramWebhookSecret))
            {
                logger.LogWarning(
                    "Telegram webhook rejected: bad or missing secret token from {RemoteIp}",
                    context.HttpContext.Connection.RemoteIpAddress);

                // 404 rather than 401: do not confirm to a prober that this path exists.
                context.Result = new NotFoundResult();
                return Task.CompletedTask;
            }

            return Task.CompletedTask;
        }
    }
}
