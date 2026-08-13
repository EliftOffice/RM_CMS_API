using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using RM_CMS.Modules.Identity.Domain;

namespace RM_CMS.Middleware
{
    /// <summary>
    /// Blocks API access for accounts flagged <c>must_change_password</c>.
    ///
    /// Without this the flag is advisory only: the frontend redirects to the change-password
    /// screen, but the access token is perfectly valid, so anyone calling the API directly
    /// (curl, Postman, a stale tab) keeps working on an admin-issued temporary password
    /// indefinitely. That matters most right after the migration, when every back-filled
    /// staff account starts in exactly this state.
    ///
    /// Only the endpoints needed to *resolve* the state stay reachable.
    /// </summary>
    public sealed class PasswordChangeRequiredMiddleware
    {
        private static readonly string[] AllowedPaths =
        {
            "/api/auth/change-password",
            "/api/auth/me",
            "/api/auth/logout",
            "/api/auth/logout-all",
            "/api/auth/refresh",
            "/api/auth/login"
        };

        private readonly RequestDelegate _next;
        private readonly ILogger<PasswordChangeRequiredMiddleware> _logger;

        public PasswordChangeRequiredMiddleware(RequestDelegate next, ILogger<PasswordChangeRequiredMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Only guards the API. Static pages must stay reachable so the user can
            // actually load the change-password screen.
            if (!context.Request.Path.StartsWithSegments("/api") ||
                context.User?.Identity?.IsAuthenticated != true ||
                !context.User.HasClaim(ClaimNames.MustChangePassword, "1") ||
                IsAllowed(context.Request.Path))
            {
                await _next(context);
                return;
            }

            _logger.LogInformation(
                "Blocked {Method} {Path} for {UserId}: password change required",
                context.Request.Method, context.Request.Path,
                context.User.FindFirst(ClaimNames.Subject)?.Value);

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/problem+json";

            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Password change required.",
                Detail = "You must set a new password before using the application.",
                Instance = context.Request.Path
            };

            problem.Extensions["correlationId"] = context.TraceIdentifier;

            // A distinct code so the client can redirect rather than show a generic error.
            problem.Extensions["code"] = "password_change_required";

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
