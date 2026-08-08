using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace RM_CMS.Middleware
{
    /// <summary>
    /// Terminal exception handler. Converts anything that escapes a controller into an
    /// RFC 7807 ProblemDetails response.
    ///
    /// The full exception goes to the log; the client gets a generic message and the
    /// correlation id. Stack traces, SQL text and connection strings never cross the wire —
    /// including in Development, so that behaviour cannot differ between environments.
    /// </summary>
    public sealed class ExceptionHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionHandlingMiddleware> _logger;

        public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
        {
            _next = next;
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

                await WriteProblemAsync(context);
            }
        }

        private static async Task WriteProblemAsync(HttpContext context)
        {
            if (context.Response.HasStarted)
                return; // too late to change the response; the log entry is the record

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/problem+json";

            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred.",
                Detail = "The request could not be completed. Quote the correlation id when reporting this.",
                Instance = context.Request.Path,
                Type = "https://datatracker.ietf.org/doc/html/rfc9110#section-15.6.1"
            };

            problem.Extensions["correlationId"] = context.TraceIdentifier;

            await context.Response.WriteAsync(JsonSerializer.Serialize(problem, JsonOptions));
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }
}
