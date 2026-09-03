using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using RM_CMS.Modules.Identity.Domain;
using RM_CMS.Modules.Settings.Data;
using RM_CMS.Modules.Telegram.Data;

namespace RM_CMS.Middleware
{
    /// <summary>
    /// Enforces <c>telegram.require_linking</c>.
    ///
    /// The setting reads "Require users to connect Telegram before using the
    /// application", but it was only ever checked once, in the browser, immediately
    /// after sign-in. So the linking screen appeared and then the Back button walked
    /// straight past it — as did typing a URL, or calling the API directly. The rule
    /// existed in the settings table and nowhere in the request path.
    ///
    /// This is the same shape as <see cref="PasswordChangeRequiredMiddleware"/>, with
    /// one deliberate difference: it does not use a token claim, because linking
    /// happens DURING a session. A claim would stay stale until the next sign-in, so
    /// somebody who had just linked would remain locked out.
    /// </summary>
    public sealed class TelegramLinkRequiredMiddleware
    {
        private const string SettingKey = "telegram.require_linking";

        /// <summary>
        /// Everything needed to GET linked, plus the session basics. Without the
        /// telegram routes the gate would block the only way through itself.
        /// </summary>
        private static readonly string[] AllowedPaths =
        {
            "/api/telegram",
            "/api/auth/me",
            "/api/auth/login",
            "/api/auth/logout",
            "/api/auth/logout-all",
            "/api/auth/refresh",
            "/api/auth/change-password",
            "/api/auth/password-policy"
        };

        private readonly RequestDelegate _next;
        private readonly ILogger<TelegramLinkRequiredMiddleware> _logger;

        public TelegramLinkRequiredMiddleware(
            RequestDelegate next, ILogger<TelegramLinkRequiredMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(
            HttpContext context, ISettingRepository settings, ITelegramRepository telegram)
        {
            if (!context.Request.Path.StartsWithSegments("/api") ||
                context.User?.Identity?.IsAuthenticated != true ||
                IsAllowed(context.Request.Path))
            {
                await _next(context);
                return;
            }

            // AN ADMINISTRATOR IS NEVER BLOCKED.
            //
            // They are the only role that can turn this setting back off, and that
            // switch lives behind /api/admin/settings. Gating them would mean an
            // administrator who enables the rule before linking has locked themselves
            // out of the one screen that could undo it, permanently and with no
            // recovery short of editing the database by hand.
            if (context.User.IsInRole(RoleCodes.Admin) ||
                context.User.HasClaim(ClaimNames.Role, RoleCodes.Admin))
            {
                await _next(context);
                return;
            }

            // Checked before the contact lookup: when the rule is off — the default,
            // and the usual case — this costs one settings read and nothing else.
            if (!await settings.GetBoolAsync(SettingKey, false))
            {
                await _next(context);
                return;
            }

            var personId = context.User.FindFirst(ClaimNames.PersonId)?.Value;

            if (string.IsNullOrWhiteSpace(personId))
            {
                await _next(context);
                return;
            }

            var internalId = await telegram.ResolvePersonIdAsync(personId);

            if (internalId is null)
            {
                await _next(context);
                return;
            }

            var contact = await telegram.GetContactAsync(internalId.Value);
            var linked = contact is not null && contact.OptedOutAt is null && contact.IsVerified;

            if (linked)
            {
                await _next(context);
                return;
            }

            _logger.LogInformation(
                "Blocked {Method} {Path} for {PersonId}: Telegram linking is required",
                context.Request.Method, context.Request.Path, personId);

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/problem+json";

            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Telegram linking required.",
                Detail = "You must connect Telegram before using the application.",
                Instance = context.Request.Path
            };

            problem.Extensions["correlationId"] = context.TraceIdentifier;

            // A distinct code so the client redirects to the linking screen rather
            // than showing a generic refusal.
            problem.Extensions["code"] = "telegram_link_required";

            await context.Response.WriteAsync(JsonSerializer.Serialize(problem, JsonOptions));
        }

        private static bool IsAllowed(PathString path)
        {
            foreach (var allowed in AllowedPaths)
            {
                if (path.StartsWithSegments(allowed, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }
}
