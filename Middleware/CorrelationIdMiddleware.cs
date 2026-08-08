namespace RM_CMS.Middleware
{
    /// <summary>
    /// Assigns every request a correlation id, echoes it back on the response, and pushes
    /// it into the logging scope so all structured log lines for a request share one id.
    ///
    /// An inbound id is accepted only if it looks like a GUID — otherwise a caller could
    /// inject newlines or arbitrary text into log output (log forging).
    /// </summary>
    public sealed class CorrelationIdMiddleware
    {
        public const string HeaderName = "X-Correlation-Id";

        private readonly RequestDelegate _next;
        private readonly ILogger<CorrelationIdMiddleware> _logger;

        public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var correlationId = ResolveCorrelationId(context);

            // TraceIdentifier is what the ProblemDetails handler and the audit log read.
            context.TraceIdentifier = correlationId;

            context.Response.OnStarting(() =>
            {
                context.Response.Headers[HeaderName] = correlationId;
                return Task.CompletedTask;
            });

            using (_logger.BeginScope(new Dictionary<string, object>
            {
                ["CorrelationId"] = correlationId
            }))
            {
                await _next(context);
            }
        }

        private static string ResolveCorrelationId(HttpContext context)
        {
            if (context.Request.Headers.TryGetValue(HeaderName, out var supplied))
            {
                var candidate = supplied.ToString();

                if (Guid.TryParse(candidate, out var parsed))
                    return parsed.ToString();
            }

            return Guid.NewGuid().ToString();
        }
    }
}
