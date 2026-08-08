namespace RM_CMS.Middleware
{
    /// <summary>
    /// Adds defence-in-depth response headers.
    ///
    /// The CSP is applied only to HTML documents: sending it on JSON API responses adds
    /// bytes and blocks nothing, and a wrong CSP on an API response can confuse tooling.
    /// </summary>
    public sealed class SecurityHeadersMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly bool _isDevelopment;

        public SecurityHeadersMiddleware(RequestDelegate next, IHostEnvironment environment)
        {
            _next = next;
            _isDevelopment = environment.IsDevelopment();
        }

        public async Task InvokeAsync(HttpContext context)
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;

                // Stop MIME sniffing — a text/plain upload must never execute as script.
                headers["X-Content-Type-Options"] = "nosniff";

                // Legacy clickjacking defence; CSP frame-ancestors below is the modern one.
                headers["X-Frame-Options"] = "DENY";

                // Do not leak full URLs (which carry ids) to third-party origins.
                headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

                // Switch off browser features this app never uses.
                headers["Permissions-Policy"] =
                    "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), " +
                    "microphone=(), payment=(), usb=(), interest-cohort=()";

                headers["Cross-Origin-Opener-Policy"] = "same-origin";
                headers["Cross-Origin-Resource-Policy"] = "same-origin";

                // Remove the server fingerprint where the host lets us.
                headers.Remove("Server");
                headers.Remove("X-Powered-By");

                if (IsHtmlResponse(context))
                    headers["Content-Security-Policy"] = BuildCsp();

                return Task.CompletedTask;
            });

            await _next(context);
        }

        private static bool IsHtmlResponse(HttpContext context) =>
            context.Response.ContentType?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true;

        /// <summary>
        /// CDNs the existing pages already load jQuery, Bootstrap and icon fonts from.
        /// These are listed explicitly so the policy matches reality — a CSP that blocks
        /// the app's own scripts gets switched off, which is worse than a narrow allow-list.
        ///
        /// FOLLOW-UP: vendor these locally (or add Subresource Integrity hashes). Executing
        /// third-party script from a CDN is a supply-chain risk that CSP cannot mitigate.
        /// </summary>
        private const string ScriptCdns = "https://cdn.jsdelivr.net https://code.jquery.com https://cdnjs.cloudflare.com";
        private const string StyleCdns = "https://cdn.jsdelivr.net https://cdnjs.cloudflare.com";
        private const string FontCdns = "https://cdn.jsdelivr.net https://cdnjs.cloudflare.com";

        private string BuildCsp()
        {
            // NOTE ON 'unsafe-inline':
            // The existing pages carry inline <script> blocks and inline style attributes,
            // and there is no build step to hash or nonce them. Dropping 'unsafe-inline'
            // today would break every screen, so it is retained deliberately and flagged as
            // follow-up work rather than silently omitted.
            var directives = new List<string>
            {
                "default-src 'self'",
                "base-uri 'self'",
                "object-src 'none'",

                // Clickjacking defence: this app is never legitimately framed.
                "frame-ancestors 'none'",

                // Credentials can only ever be posted back to this origin.
                "form-action 'self'",

                "img-src 'self' data:",
                $"font-src 'self' data: {FontCdns}",
                $"style-src 'self' 'unsafe-inline' {StyleCdns}",
                $"script-src 'self' 'unsafe-inline' {ScriptCdns}",

                // XHR/fetch may only reach this origin — an injected script cannot
                // exfiltrate data to an attacker-controlled endpoint.
                "connect-src 'self'"
            };

            if (!_isDevelopment)
                directives.Add("upgrade-insecure-requests");

            return string.Join("; ", directives);
        }
    }
}
