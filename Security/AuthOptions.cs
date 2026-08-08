using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.Extensions.Options;

namespace RM_CMS.Security
{
    /// <summary>
    /// JWT issuing/validation settings. Bound from configuration section <c>Jwt</c>.
    /// The signing key must come from an environment variable or secret store —
    /// never from a checked-in appsettings file.
    /// </summary>
    public sealed class JwtOptions
    {
        public const string SectionName = "Jwt";

        [Required] public string Issuer { get; set; } = string.Empty;
        [Required] public string Audience { get; set; } = string.Empty;

        /// <summary>Primary HMAC signing key. Minimum 32 bytes (256 bits).</summary>
        [Required] public string SigningKey { get; set; } = string.Empty;

        /// <summary>
        /// Previously-active signing keys, still accepted for *validation* only.
        /// Populate during a key rotation, then remove once all old access tokens
        /// have expired (i.e. after <see cref="AccessTokenMinutes"/>).
        /// </summary>
        public string[] PreviousSigningKeys { get; set; } = Array.Empty<string>();

        /// <summary>Access token lifetime. Capped at 15 minutes.</summary>
        [Range(1, 15)] public int AccessTokenMinutes { get; set; } = 15;

        /// <summary>Refresh token (and therefore session) lifetime.</summary>
        [Range(1, 90)] public int RefreshTokenDays { get; set; } = 14;

        /// <summary>Permitted clock drift. Capped at 60 seconds.</summary>
        [Range(0, 60)] public int ClockSkewSeconds { get; set; } = 30;

        public const int MinimumSigningKeyBytes = 32; // 256-bit
    }

    /// <summary>
    /// Password policy, lockout and session settings. Bound from section <c>Auth</c>.
    /// </summary>
    public sealed class AuthOptions
    {
        public const string SectionName = "Auth";

        // ---- Password policy (OWASP ASVS v4 §2.1) ----
        [Range(12, 128)] public int PasswordMinLength { get; set; } = 12;
        [Range(12, 256)] public int PasswordMaxLength { get; set; } = 128;
        public bool RequireUppercase { get; set; } = true;
        public bool RequireLowercase { get; set; } = true;
        public bool RequireDigit { get; set; } = true;
        public bool RequireNonAlphanumeric { get; set; } = true;

        /// <summary>How many previous passwords may not be reused.</summary>
        [Range(0, 24)] public int PasswordHistoryCount { get; set; } = 5;

        // ---- Lockout with exponential backoff ----
        [Range(1, 20)] public int MaxFailedAccessAttempts { get; set; } = 5;

        /// <summary>
        /// Base lockout in seconds. Actual lockout is
        /// <c>BaseLockoutSeconds * 2^(failures - MaxFailedAccessAttempts)</c>, capped at
        /// <see cref="MaxLockoutMinutes"/>.
        /// </summary>
        [Range(5, 3600)] public int BaseLockoutSeconds { get; set; } = 30;

        [Range(1, 1440)] public int MaxLockoutMinutes { get; set; } = 60;

        // ---- Sessions ----
        /// <summary>Maximum concurrent refresh-token families (≈ devices) per user.</summary>
        [Range(1, 20)] public int MaxActiveSessionsPerUser { get; set; } = 5;

        /// <summary>Name of the HttpOnly cookie that carries the refresh token.</summary>
        public string RefreshCookieName { get; set; } = "__Host-rmcms-rt";

        // ---- CORS ----
        /// <summary>
        /// Explicit allow-list. Must never be <c>*</c> — credentials are enabled.
        /// </summary>
        public string[] AllowedOrigins { get; set; } = Array.Empty<string>();

        // ---- Machine callers (no JWT possible) ----

        /// <summary>
        /// Pre-shared key for the external cron scheduler, sent as <c>X-Service-Key</c>.
        /// Supply via <c>Auth__ServiceApiKey</c>. Leave empty to require an Admin JWT instead.
        /// </summary>
        public string ServiceApiKey { get; set; } = string.Empty;

        /// <summary>
        /// Telegram's <c>secret_token</c>, echoed back on every webhook delivery.
        /// Supply via <c>Auth__TelegramWebhookSecret</c>. When empty, the webhook fails closed.
        /// </summary>
        public string TelegramWebhookSecret { get; set; } = string.Empty;

        // ---- First-run bootstrap ----
        public BootstrapOptions Bootstrap { get; set; } = new();

        public sealed class BootstrapOptions
        {
            /// <summary>
            /// When true and <c>auth_users</c> contains no Admin, an administrator is
            /// created at startup from <see cref="Username"/>/<see cref="Password"/>.
            /// Both must come from environment variables. Turn this off after first run.
            /// </summary>
            public bool Enabled { get; set; }
            public string Username { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
            public string DisplayName { get; set; } = "System Administrator";
        }
    }

    /// <summary>
    /// Fail-fast validation. A misconfigured signing key must stop the process at
    /// startup rather than silently degrade to an insecure default.
    /// </summary>
    public sealed class JwtOptionsValidator : IValidateOptions<JwtOptions>
    {
        public ValidateOptionsResult Validate(string? name, JwtOptions options)
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(options.Issuer))
                errors.Add("Jwt:Issuer is required.");

            if (string.IsNullOrWhiteSpace(options.Audience))
                errors.Add("Jwt:Audience is required.");

            if (string.IsNullOrWhiteSpace(options.SigningKey))
            {
                errors.Add(
                    "Jwt:SigningKey is required and must be supplied via the environment " +
                    "variable Jwt__SigningKey (or an equivalent secret store). It must never " +
                    "be committed to appsettings.json.");
            }
            else if (Encoding.UTF8.GetByteCount(options.SigningKey) < JwtOptions.MinimumSigningKeyBytes)
            {
                errors.Add(
                    $"Jwt:SigningKey must be at least {JwtOptions.MinimumSigningKeyBytes} bytes " +
                    $"({JwtOptions.MinimumSigningKeyBytes * 8}-bit) for HMAC-SHA256. " +
                    "Generate one with: openssl rand -base64 48");
            }

            for (var i = 0; i < options.PreviousSigningKeys.Length; i++)
            {
                var key = options.PreviousSigningKeys[i];
                if (string.IsNullOrWhiteSpace(key) ||
                    Encoding.UTF8.GetByteCount(key) < JwtOptions.MinimumSigningKeyBytes)
                {
                    errors.Add($"Jwt:PreviousSigningKeys[{i}] is shorter than the {JwtOptions.MinimumSigningKeyBytes}-byte minimum.");
                }
            }

            if (options.AccessTokenMinutes is < 1 or > 15)
                errors.Add("Jwt:AccessTokenMinutes must be between 1 and 15.");

            if (options.ClockSkewSeconds is < 0 or > 60)
                errors.Add("Jwt:ClockSkewSeconds must be between 0 and 60.");

            return errors.Count > 0
                ? ValidateOptionsResult.Fail(errors)
                : ValidateOptionsResult.Success;
        }
    }

    public sealed class AuthOptionsValidator : IValidateOptions<AuthOptions>
    {
        private readonly IHostEnvironment _environment;

        public AuthOptionsValidator(IHostEnvironment environment) => _environment = environment;

        public ValidateOptionsResult Validate(string? name, AuthOptions options)
        {
            var errors = new List<string>();

            if (options.PasswordMinLength < 12)
                errors.Add("Auth:PasswordMinLength must be at least 12.");

            if (options.PasswordMaxLength < options.PasswordMinLength)
                errors.Add("Auth:PasswordMaxLength must be >= Auth:PasswordMinLength.");

            if (options.AllowedOrigins.Any(o => o == "*"))
                errors.Add("Auth:AllowedOrigins must not contain '*' — credentials are enabled on the CORS policy.");

            foreach (var origin in options.AllowedOrigins)
            {
                if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                {
                    errors.Add($"Auth:AllowedOrigins contains an invalid absolute URI: '{origin}'.");
                    continue;
                }

                if (!_environment.IsDevelopment() && uri.Scheme != Uri.UriSchemeHttps)
                    errors.Add($"Auth:AllowedOrigins entry '{origin}' must use https outside Development.");
            }

            if (options.Bootstrap.Enabled)
            {
                if (string.IsNullOrWhiteSpace(options.Bootstrap.Username))
                    errors.Add("Auth:Bootstrap:Username is required when bootstrap is enabled.");

                if (string.IsNullOrWhiteSpace(options.Bootstrap.Password))
                    errors.Add("Auth:Bootstrap:Password is required when bootstrap is enabled (supply via Auth__Bootstrap__Password).");
            }

            return errors.Count > 0
                ? ValidateOptionsResult.Fail(errors)
                : ValidateOptionsResult.Success;
        }
    }
}
