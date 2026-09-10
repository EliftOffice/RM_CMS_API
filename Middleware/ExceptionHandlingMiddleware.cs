namespace RM_CMS.Middleware
{
    /// <summary>
    /// Terminal exception handler. Converts anything that escapes a controller into an
    /// RFC 7807 ProblemDetails response.
    ///
    /// The full exception goes to the log; the client gets a generic message and the
    /// correlation id. Stack traces, SQL text and connection strings never cross the wire —
    /// including in Development, so that behaviour cannot differ between environments.
    ///
    /// WHAT the client gets is decided by <see cref="ErrorPages"/>: a page script gets
    /// ProblemDetails as before, and somebody who navigated in a browser gets the error
    /// page. That used to be JSON for everybody, so an unhandled exception on a page
    /// request showed a wall of JSON in the browser window.
    /// </summary>
    public sealed class ExceptionHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<ExceptionHandlingMiddleware> _logger;

        public ExceptionHandlingMiddleware(
            RequestDelegate next,
            IWebHostEnvironment environment,
            ILogger<ExceptionHandlingMiddleware> logger)
        {
            _next = next;
            _environment = environment;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                // The client hung up. Not an error, and the response is already gone.
                _logger.LogInformation("Request {Method} {Path} was cancelled by the client",
                    context.Request.Method, context.Request.Path);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Unhandled exception for {Method} {Path} (correlation {CorrelationId})",
                    context.Request.Method, context.Request.Path, context.TraceIdentifier);

                await ErrorPages.WriteAsync(context, StatusCodes.Status500InternalServerError, _environment);
            }
        }

    }
}
