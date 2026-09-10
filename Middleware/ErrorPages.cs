using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace RM_CMS.Middleware
{
    /// <summary>
    /// What a failed request is answered with.
    /// </summary>
    /// <remarks>
    /// There are two audiences and they need different things. A fetch from a page
    /// script wants <c>application/problem+json</c> it can branch on; a person who
    /// typed a wrong address wants a page that says what happened. Before this, both
    /// got JSON at best — and for a plain 404 they got a completely empty body, so the
    /// browser fell back to its own blank "cannot reach this page", which looks like
    /// the whole site is down rather than one address being wrong.
    ///
    /// The HTML comes from <c>wwwroot/pages/error.html</c> so it can be edited without
    /// touching C#, and is cached after the first read. If that file is missing or
    /// unreadable the built-in fallback below is used — an error page that itself
    /// fails to render is the one failure mode this class exists to prevent.
    /// </remarks>
    public static class ErrorPages
    {
        private const string TemplatePath = "pages/error.html";

        /// <summary>
        /// Read once and kept. The file is small, never changes at runtime, and this is
        /// on the path of a request that has already gone wrong — the wrong moment to
        /// add disk IO.
        /// </summary>
        private static string? _cachedTemplate;
        private static readonly object _cacheLock = new();

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        /// <summary>
        /// Whether this caller should be sent a page rather than JSON.
        /// </summary>
        /// <remarks>
        /// Two conditions, both required. Anything under <c>/api</c> always gets JSON,
        /// even from a browser address bar — a client that parses responses must never
        /// be handed markup because somebody's Accept header was broad. And the caller
        /// must actually have asked for HTML: `fetch` defaults to a wildcard Accept,
        /// so requiring text/html explicitly keeps page scripts on the JSON path.
        /// </remarks>
        public static bool WantsHtml(HttpContext context)
        {
            if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
                return false;

            var accept = context.Request.Headers.Accept.ToString();

            return accept.Contains("text/html", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Writes the response for <paramref name="statusCode"/> in whichever form this
        /// caller should get. Does nothing when the response has already started —
        /// there is nothing left to change, and the log entry is the record.
        /// </summary>
        public static async Task WriteAsync(HttpContext context, int statusCode, IWebHostEnvironment environment)
        {
            if (context.Response.HasStarted) return;

            var correlationId = context.TraceIdentifier;

            // Clear() drops headers as well as the body, and one of them has to
            // survive: the rate limiter sets Retry-After before delegating here, and
            // that value is the only actionable thing a throttled caller is given.
            // Captured and re-applied rather than skipping Clear(), because clearing
            // is what guarantees no half-written body or stale content type is left.
            //
            // The security headers are unaffected — SecurityHeadersMiddleware adds
            // them in an OnStarting callback, which runs after this.
            var retryAfter = context.Response.Headers.RetryAfter;

            context.Response.Clear();

            if (!string.IsNullOrEmpty(retryAfter))
                context.Response.Headers.RetryAfter = retryAfter;

            context.Response.StatusCode = statusCode;

            // Never cached, at any layer. A cached 500 would outlive the fault that
            // caused it and keep showing an error after the problem was fixed.
            context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            context.Response.Headers.Pragma = "no-cache";

            if (WantsHtml(context))
            {
                context.Response.ContentType = "text/html; charset=utf-8";

                await context.Response.WriteAsync(
                    Render(statusCode, correlationId, environment));

                return;
            }

            context.Response.ContentType = "application/problem+json";

            var problem = new ProblemDetails
            {
                Status = statusCode,
                Title = TitleFor(statusCode),
                Detail = MessageFor(statusCode),
                Instance = context.Request.Path
            };

            problem.Extensions["correlationId"] = correlationId;

            await context.Response.WriteAsync(JsonSerializer.Serialize(problem, JsonOptions));
        }

        private static string Render(int statusCode, string? correlationId, IWebHostEnvironment environment)
        {
            var template = Template(environment);

            // Every value is HTML-encoded. The correlation id is server-generated and
            // the rest are constants, so nothing here is caller-controlled today — but
            // an error page is exactly the kind of thing that later grows a "you asked
            // for {path}" line, and encoding now means that change is safe.
            return template
                .Replace("{{STATUS}}", WebUtility.HtmlEncode(statusCode.ToString()), StringComparison.Ordinal)
                .Replace("{{TITLE}}", WebUtility.HtmlEncode(TitleFor(statusCode)), StringComparison.Ordinal)
                .Replace("{{MESSAGE}}", WebUtility.HtmlEncode(MessageFor(statusCode)), StringComparison.Ordinal)
                .Replace("{{CORRELATION_ID}}", WebUtility.HtmlEncode(correlationId ?? string.Empty), StringComparison.Ordinal)
                .Replace("{{REF_HIDDEN}}", string.IsNullOrWhiteSpace(correlationId) ? "hidden" : string.Empty,
                         StringComparison.Ordinal);
        }

        private static string Template(IWebHostEnvironment environment)
        {
            if (_cachedTemplate is not null) return _cachedTemplate;

            lock (_cacheLock)
            {
                if (_cachedTemplate is not null) return _cachedTemplate;

                try
                {
                    var file = environment.WebRootFileProvider.GetFileInfo(TemplatePath);

                    if (file.Exists)
                    {
                        using var stream = file.CreateReadStream();
                        using var reader = new StreamReader(stream);

                        _cachedTemplate = reader.ReadToEnd();
                        return _cachedTemplate;
                    }
                }
                catch (IOException)
                {
                    // Fall through to the built-in. Whatever is wrong with the disk, it
                    // must not turn a 404 into an unhandled exception.
                }

                _cachedTemplate = Fallback;
                return _cachedTemplate;
            }
        }

        /// <summary>
        /// The last resort, compiled in. Ugly on purpose — it is only reached when the
        /// real page is missing, and being plain is better than being absent.
        /// </summary>
        private const string Fallback =
            "<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\">" +
            "<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">" +
            "<title>{{TITLE}} — RM CMS</title></head>" +
            "<body style=\"font-family:sans-serif;max-width:34rem;margin:12vh auto;padding:0 1.5rem;line-height:1.6\">" +
            "<h1>{{STATUS}} — {{TITLE}}</h1><p>{{MESSAGE}}</p>" +
            "<p><a href=\"/\">Go to sign in</a></p>" +
            "<p {{REF_HIDDEN}} style=\"color:#555;font-size:.85rem\">Reference: {{CORRELATION_ID}}</p>" +
            "</body></html>";

        /// <summary>
        /// Deliberately vague above 400 and specific only where being specific helps
        /// the person rather than somebody probing. A 403 says "not allowed", never
        /// which role would have been.
        /// </summary>
        private static string TitleFor(int statusCode) => statusCode switch
        {
            400 => "That request could not be understood",
            401 => "Please sign in",
            403 => "You do not have access to this",
            404 => "Page not found",
            405 => "That is not something this page can do",
            408 => "The request timed out",
            413 => "That file is too large",
            429 => "Too many attempts",
            503 => "Temporarily unavailable",
            _ when statusCode >= 500 => "Something went wrong",
            _ => "That request could not be completed"
        };

        private static string MessageFor(int statusCode) => statusCode switch
        {
            401 => "Your session may have ended. Sign in again to continue.",
            403 => "Your account does not have permission to open this. " +
                   "If you think it should, ask an administrator.",
            404 => "The address may be wrong, or the page may have moved.",
            413 => "Choose a smaller file and try again.",
            429 => "Please wait a moment and try again.",
            503 => "The system is briefly unavailable. Please try again shortly.",
            _ when statusCode >= 500 =>
                "This has been logged. Please try again, and tell the church office if it keeps happening.",
            _ => "Please go back and try again."
        };
    }
}
