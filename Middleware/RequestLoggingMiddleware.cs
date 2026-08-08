using System.Diagnostics;
using System.Text.RegularExpressions;

namespace RM_CMS.Middleware
{
    /// <summary>
    /// Structured access log: one line per request with method, redacted path, status,
    /// duration and caller identity.
    ///
    /// Deliberately never logs request or response bodies, the Authorization header, or
    /// Cookie headers — those carry passwords, JWTs and refresh tokens.
    /// </summary>
    public sealed partial class RequestLoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<RequestLoggingMiddleware> _logger;

        /// <summary>Requests slower than this are logged at Warning.</summary>
        private const int SlowRequestMs = 2000;

        public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Static assets would drown the log without adding anything.
            if (IsStaticAsset(context.Request.Path))
            {
                await _next(context);
                return;
            }

            var stopwatch = Stopwatch.StartNew();

            try
            {
                await _next(context);
            }
            finally
            {
                stopwatch.Stop();

                var status = context.Response.StatusCode;
                var elapsed = stopwatch.ElapsedMilliseconds;

                var level = status switch
                {
                    >= 500 => LogLevel.Error,
                    401 or 403 => LogLevel.Warning,   // authentication / authorization failures
                    429 => LogLevel.Warning,          // rate limit tripped
                    >= 400 => LogLevel.Information,
                    _ when elapsed > SlowRequestMs => LogLevel.Warning,
                    _ => LogLevel.Information
                };

                _logger.Log(level,
                    "{Method} {Path} responded {StatusCode} in {ElapsedMs}ms for user {UserId} from {RemoteIp}",
                    context.Request.Method,
                    RedactPath(context.Request.Path),
                    status,
                    elapsed,
                    context.User?.FindFirst("sub")?.Value ?? "anonymous",
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
            }
        }

        private static bool IsStaticAsset(PathString path)
        {
            var value = path.Value;
            if (string.IsNullOrEmpty(value)) return false;

            return value.EndsWith(".css", StringComparison.OrdinalIgnoreCase)
                || value.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
                || value.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                || value.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                || value.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                || value.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
                || value.EndsWith(".ico", StringComparison.OrdinalIgnoreCase)
                || value.EndsWith(".woff", StringComparison.OrdinalIgnoreCase)
                || value.EndsWith(".woff2", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Masks anything that looks like a phone number in the path. Several legacy routes
        /// embed mobile numbers (e.g. /api/volunteers/mobile/9876543210), and personal data
        /// should not accumulate in log files.
        /// </summary>
        private static string RedactPath(PathString path)
        {
            var value = path.Value ?? string.Empty;
            return PhoneNumberPattern().Replace(value, "/{redacted}");
        }

        [GeneratedRegex(@"/\+?\d{7,15}(?=/|$)")]
        private static partial Regex PhoneNumberPattern();
    }
}
