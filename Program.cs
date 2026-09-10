using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using RM_CMS.Middleware;
using RM_CMS.Modules.Identity.Services;
using RM_CMS.Modules.Identity.Domain;
using RM_CMS.Security;

namespace RM_CMS
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // ==========================================================
            // Configuration & secrets
            //
            // Secrets (Jwt:SigningKey, ConnectionStrings:DefaultConnection,
            // Auth:Bootstrap:Password) come from environment variables, which Coolify
            // injects at deploy time. Nothing sensitive is committed to appsettings.json.
            // Environment variables use the double-underscore form, e.g. Jwt__SigningKey.
            // ==========================================================
            builder.Configuration.AddEnvironmentVariables();

            ConfigureLogging(builder);
            ConfigureOptions(builder);
            ConfigureRequestLimits(builder);
            ConfigureAuthentication(builder);
            ConfigureAuthorization(builder);
            ConfigureCors(builder);
            ConfigureRateLimiting(builder);
            ConfigureMvc(builder);
            RegisterApplicationServices(builder);

            var app = builder.Build();

            // Fail fast: touching the validated options forces JwtOptionsValidator /
            // AuthOptionsValidator to run now rather than on the first request.
            _ = app.Services.GetRequiredService<IOptions<JwtOptions>>().Value;
            _ = app.Services.GetRequiredService<IOptions<AuthOptions>>().Value;

            ConfigurePipeline(app);

            await BootstrapAsync(app);

            await app.RunAsync();
        }

        // ==========================================================
        // Logging
        // ==========================================================
        private static void ConfigureLogging(WebApplicationBuilder builder)
        {
            builder.Logging.ClearProviders();

            if (builder.Environment.IsDevelopment())
            {
                builder.Logging.AddSimpleConsole(options =>
                {
                    options.IncludeScopes = true;
                    options.SingleLine = false;
                    options.TimestampFormat = "HH:mm:ss ";
                });
            }
            else
            {
                // Structured JSON so the Coolify/host log pipeline can index fields
                // (CorrelationId, UserId, StatusCode) rather than grep free text.
                builder.Logging.AddJsonConsole(options =>
                {
                    options.IncludeScopes = true;
                    options.JsonWriterOptions = new JsonWriterOptions { Indented = false };
                });
            }
        }

        // ==========================================================
        // Options
        // ==========================================================
        private static void ConfigureOptions(WebApplicationBuilder builder)
        {
            builder.Services.AddSingleton(TimeProvider.System);

            builder.Services.AddOptions<JwtOptions>()
                .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
                .ValidateOnStart();

            builder.Services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>();

            builder.Services.AddOptions<AuthOptions>()
                .Bind(builder.Configuration.GetSection(AuthOptions.SectionName))
                .ValidateOnStart();

            builder.Services.AddSingleton<IValidateOptions<AuthOptions>, AuthOptionsValidator>();

            builder.Services.Configure<AccountStateCacheOptions>(
                builder.Configuration.GetSection(AccountStateCacheOptions.SectionName));

            builder.Services.Configure<RM_CMS.Modules.Telegram.Domain.TelegramOptions>(
                builder.Configuration.GetSection(RM_CMS.Modules.Telegram.Domain.TelegramOptions.SectionName));

            builder.Services.Configure<RM_CMS.Modules.Notifications.Domain.NotificationOptions>(
                builder.Configuration.GetSection(RM_CMS.Modules.Notifications.Domain.NotificationOptions.SectionName));

            // Named client so Telegram calls get their own timeout: an unreachable bot
            // must not hold a request thread while a volunteer waits.
            builder.Services.AddHttpClient(nameof(RM_CMS.Modules.Telegram.Services.TelegramClient), client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            // CRITICAL: HttpClient's default logging writes the full request URI, and
            // Telegram carries the bot token IN THE PATH
            // (api.telegram.org/bot<TOKEN>/getMe). Left on, every call publishes the
            // credential to the log pipeline. TelegramClient does its own logging,
            // by method name only.
            .RemoveAllLoggers();

            // ASP.NET Identity's PBKDF2 hasher. V3 = HMAC-SHA512; the iteration count is
            // raised above the framework default in line with current OWASP guidance.
            builder.Services.Configure<PasswordHasherOptions>(options =>
            {
                options.CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3;
                options.IterationCount = 210_000;
            });
        }

        // ==========================================================
        // Request size limits (DoS surface)
        // ==========================================================
        private static void ConfigureRequestLimits(WebApplicationBuilder builder)
        {
            const long maxBodyBytes = 2 * 1024 * 1024; // 2 MB — this API only accepts JSON/form data

            builder.Services.Configure<KestrelServerOptions>(options =>
            {
                options.Limits.MaxRequestBodySize = maxBodyBytes;
                options.Limits.MaxRequestHeadersTotalSize = 32 * 1024;
                options.Limits.MaxRequestLineSize = 8 * 1024;
                options.AddServerHeader = false; // do not advertise Kestrel
            });

            builder.Services.Configure<FormOptions>(options =>
            {
                options.MultipartBodyLengthLimit = maxBodyBytes;
                options.ValueLengthLimit = 256 * 1024;
                options.KeyLengthLimit = 2 * 1024;
                options.ValueCountLimit = 256;
            });
        }

        // ==========================================================
        // Authentication
        // ==========================================================
        private static void ConfigureAuthentication(WebApplicationBuilder builder)
        {
            builder.Services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                // Keep the claim names exactly as issued. Without this, .NET rewrites
                // "sub"/"role" into long WS-Federation URIs and claim lookups silently miss.
                options.MapInboundClaims = false;
                options.SaveToken = false; // no need to keep the raw token in the auth properties
                options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();

                // Validation parameters (issuer, audience, lifetime, signing key, algorithm,
                // clock skew) are built by TokenService so issuing and validating can never
                // drift apart.
                var serviceProvider = builder.Services.BuildServiceProvider();
                options.TokenValidationParameters = serviceProvider
                    .GetRequiredService<ITokenIssuer>()
                    .BuildValidationParameters();

                options.Events = new JwtBearerEvents
                {
                    // Re-check the account state on every request: logout, password change,
                    // role change and account disable must take effect immediately, not when
                    // the access token happens to expire.
                    OnTokenValidated = async context =>
                    {
                        var validator = context.HttpContext.RequestServices
                            .GetRequiredService<AccessTokenValidator>();

                        await validator.ValidateAsync(context);
                    },

                    // Generic 401 as ProblemDetails. The reason is never disclosed —
                    // "signature invalid" vs "expired" is information an attacker can use.
                    // Answers in whichever form the caller can use: ProblemDetails for a
                    // page script, the error page for somebody who navigated here in a
                    // browser. It used to write JSON unconditionally, so an expired
                    // session on a page request filled the window with raw JSON.
                    //
                    // Note this is also what answers an unknown path — the fallback
                    // policy denies anonymous callers before routing can 404 — which is
                    // deliberate: a 401 for everything unknown tells a prober nothing
                    // about which paths exist.
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();

                        var environment = context.HttpContext.RequestServices
                            .GetRequiredService<IWebHostEnvironment>();

                        await ErrorPages.WriteAsync(
                            context.HttpContext, StatusCodes.Status401Unauthorized, environment);
                    },

                    OnForbidden = async context =>
                    {
                        var logger = context.HttpContext.RequestServices
                            .GetRequiredService<ILoggerFactory>()
                            .CreateLogger("Authorization");

                        logger.LogWarning(
                            "Authorization denied for {UserId} on {Method} {Path}",
                            context.Principal?.FindFirst(ClaimNames.Subject)?.Value ?? "unknown",
                            context.Request.Method,
                            context.Request.Path);

                        var environment = context.HttpContext.RequestServices
                            .GetRequiredService<IWebHostEnvironment>();

                        await ErrorPages.WriteAsync(
                            context.HttpContext, StatusCodes.Status403Forbidden, environment);
                    }
                };
            });
        }

        // ==========================================================
        // Authorization
        // ==========================================================
        private static void ConfigureAuthorization(WebApplicationBuilder builder)
        {
            builder.Services.AddAuthorization(options =>
            {
                // DEFAULT DENY.
                // Any endpoint that does not opt out with [AllowAnonymous] requires an
                // authenticated caller — so a controller that forgets [Authorize] is still
                // protected. This is what closes the "unintentionally public" class of bug
                // permanently rather than one controller at a time.
                options.FallbackPolicy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .Build();

                options.DefaultPolicy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .Build();

                options.AddPolicy(PolicyNames.Authenticated, policy =>
                    policy.RequireAuthenticatedUser());

                options.AddPolicy(PolicyNames.AdminOnly, policy =>
                    policy.RequireAuthenticatedUser()
                          .RequireClaim(ClaimNames.Role, RoleCodes.Admin));

                options.AddPolicy(PolicyNames.PastorOrAdmin, policy =>
                    policy.RequireAuthenticatedUser()
                          .RequireClaim(ClaimNames.Role, RoleCodes.Admin, RoleCodes.Pastor));

                options.AddPolicy(PolicyNames.TeamLeadOrAbove, policy =>
                    policy.RequireAuthenticatedUser()
                          .RequireClaim(ClaimNames.Role, RoleCodes.Admin, RoleCodes.Pastor, RoleCodes.TeamLead));

                options.AddPolicy(PolicyNames.VolunteerOrAbove, policy =>
                    policy.RequireAuthenticatedUser()
                          .RequireClaim(ClaimNames.Role,
                              RoleCodes.Admin, RoleCodes.Pastor, RoleCodes.TeamLead, RoleCodes.Volunteer));

                // Intake. Data-entry operators record visitors but see nothing else.
                options.AddPolicy(PolicyNames.CanRecordVisitors, policy =>
                    policy.RequireAuthenticatedUser()
                          .RequireClaim(ClaimNames.Role,
                              RoleCodes.Admin, RoleCodes.Pastor, RoleCodes.TeamLead,
                              RoleCodes.Volunteer, RoleCodes.DataEntry));

                // The website enquiry list. Narrow on purpose: this is unfiltered public
                // input, and everyone who can open it can read whatever the internet
                // typed into a form.
                options.AddPolicy(PolicyNames.CanReviewWebEnquiries, policy =>
                    policy.RequireAuthenticatedUser()
                          .RequireClaim(ClaimNames.Role,
                              RoleCodes.Admin, RoleCodes.WebCoordinator));

                // The church calendar on the public website.
                options.AddPolicy(PolicyNames.CanManageEvents, policy =>
                    policy.RequireAuthenticatedUser()
                          .RequireClaim(ClaimNames.Role,
                              RoleCodes.Admin, RoleCodes.Pastor, RoleCodes.WebCoordinator));

                // Scheduled jobs: an Admin token OR the scheduler's service key. Note the
                // absence of RequireAuthenticatedUser — the machine caller has no identity.
                options.AddPolicy(PolicyNames.JobRunner, policy =>
                    policy.AddRequirements(new ServiceKeyOrAdminRequirement()));
            });

            builder.Services.AddSingleton<IAuthorizationHandler, ServiceKeyOrAdminHandler>();
        }

        // ==========================================================
        // CORS
        // ==========================================================
        private static void ConfigureCors(WebApplicationBuilder builder)
        {
            var allowedOrigins = builder.Configuration
                .GetSection($"{AuthOptions.SectionName}:AllowedOrigins")
                .Get<string[]>() ?? Array.Empty<string>();

            builder.Services.AddCors(options =>
            {
                options.AddPolicy(CorsPolicyName, policy =>
                {
                    if (allowedOrigins.Length == 0)
                    {
                        // No origins configured means same-origin only, which is the correct
                        // default here: the frontend is served by this very application.
                        policy.WithOrigins(Array.Empty<string>());
                    }
                    else
                    {
                        // Explicit allow-list. AllowAnyOrigin + AllowCredentials is forbidden
                        // by the CORS spec and by AuthOptionsValidator.
                        policy.WithOrigins(allowedOrigins);
                    }

                    policy.AllowAnyHeader()
                          .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")
                          .AllowCredentials()          // required for the refresh cookie
                          .WithExposedHeaders(CorrelationIdMiddleware.HeaderName)
                          .SetPreflightMaxAge(TimeSpan.FromMinutes(10));
                });
            });
        }

        private const string CorsPolicyName = "RmCmsCors";

        // ==========================================================
        // Rate limiting
        // ==========================================================
        private static void ConfigureRateLimiting(WebApplicationBuilder builder)
        {
            builder.Services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

                options.OnRejected = async (context, cancellationToken) =>
                {
                    var logger = context.HttpContext.RequestServices
                        .GetRequiredService<ILoggerFactory>()
                        .CreateLogger("RateLimiting");

                    logger.LogWarning(
                        "Rate limit hit on {Path} from {RemoteIp}",
                        context.HttpContext.Request.Path,
                        context.HttpContext.Connection.RemoteIpAddress);

                    if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    {
                        context.HttpContext.Response.Headers.RetryAfter =
                            ((int)retryAfter.TotalSeconds).ToString();
                    }

                    // Retry-After was set above; ErrorPages preserves it across the
                    // response reset, because it is the only actionable thing a
                    // throttled caller is given.
                    var environment = context.HttpContext.RequestServices
                        .GetRequiredService<IWebHostEnvironment>();

                    await ErrorPages.WriteAsync(
                        context.HttpContext, StatusCodes.Status429TooManyRequests, environment);
                };

                // Login: 5 attempts per 5 minutes per IP+username pair. Partitioning by
                // username as well as IP means one attacker cannot lock out a whole NAT,
                // and cannot spray many usernames from one address either.
                options.AddPolicy(RateLimitPolicies.Login, context =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        LoginPartitionKey(context),
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 5,
                            Window = TimeSpan.FromMinutes(5),
                            QueueLimit = 0,
                            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                        }));

                // The login screen's "does this number need a password?" question.
                //
                // Four times the login allowance, on the same IP partition. It carries no
                // credential, so there is nothing to guess at; the limit is here to stop a
                // script sweeping numbers to find the passwordless ones, and to keep one
                // client from making the endpoint expensive.
                //
                // NOTE, pre-dating this policy: LoginPartitionKey is IP-only, and behind
                // Coolify every request arrives from the proxy — so in production this
                // bucket, like the login one, is shared by everybody. That is deliberate
                // (X-Forwarded-For is caller-supplied, and trusting it would let an
                // attacker mint a fresh bucket per request), but it means the ceiling has
                // to be generous enough for a whole church office at once.
                options.AddPolicy(RateLimitPolicies.LoginMethod, context =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        LoginPartitionKey(context),
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 20,
                            Window = TimeSpan.FromMinutes(5),
                            QueueLimit = 0,
                            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                        }));

                // Refresh: a normal client refreshes about once per access-token lifetime.
                options.AddPolicy(RateLimitPolicies.Refresh, context =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        ClientPartitionKey(context),
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 30,
                            Window = TimeSpan.FromMinutes(5),
                            QueueLimit = 0
                        }));

                options.AddPolicy(RateLimitPolicies.Sensitive, context =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        ClientPartitionKey(context),
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 20,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0
                        }));

                // The public website's form. Anonymous and reachable by anyone, so it
                // gets its own bucket rather than sharing the global one — a bot
                // hammering the prayer form must not be able to exhaust the budget
                // that signed-in staff are also drawing from.
                //
                // Partitioned on the forwarded address, because behind Coolify's proxy
                // the connection address is the proxy for every visitor alike. The
                // header is spoofable, which only ever splits an attacker across more
                // buckets; the service applies a second per-fingerprint throttle that
                // a spoofed header does not escape.
                options.AddPolicy(RateLimitPolicies.PublicForm, context =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        PublicFormPartitionKey(context),
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 12,
                            Window = TimeSpan.FromMinutes(5),
                            QueueLimit = 0,
                            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                        }));

                // Global ceiling for everything else.
                options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                    RateLimitPartition.GetTokenBucketLimiter(
                        ClientPartitionKey(context),
                        _ => new TokenBucketRateLimiterOptions
                        {
                            TokenLimit = 240,
                            TokensPerPeriod = 120,
                            ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                            QueueLimit = 0,
                            AutoReplenishment = true
                        }));
            });
        }

        /// <summary>Partition key for authenticated-or-anonymous callers.</summary>
        private static string ClientPartitionKey(HttpContext context)
        {
            var userId = context.User.FindFirst(ClaimNames.Subject)?.Value;

            return !string.IsNullOrWhiteSpace(userId)
                ? $"user:{userId}"
                : $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
        }

        /// <summary>
        /// Partition key for the public form. Prefers X-Forwarded-For so visitors are
        /// told apart behind a reverse proxy; falls back to the connection address.
        /// Never used for authorization — see the note on the policy.
        /// </summary>
        private static string PublicFormPartitionKey(HttpContext context)
        {
            var forwarded = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(forwarded))
            {
                var first = forwarded.Split(',')[0].Trim();
                if (first.Length is > 0 and <= 64) return $"web:{first}";
            }

            return $"web:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
        }

        /// <summary>Partition key for the login endpoint: IP plus the username being tried.</summary>
        private static string LoginPartitionKey(HttpContext context)
        {
            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            // The username is only available after model binding, so fall back to IP alone
            // for the pre-binding partition. The per-account exponential backoff in IdentityService
            // covers the username dimension.
            return $"login:{ip}";
        }

        // ==========================================================
        // MVC / Swagger
        // ==========================================================
        private static void ConfigureMvc(WebApplicationBuilder builder)
        {
            // NOTE: no PropertyNamingPolicy override here, deliberately.
            // The original application never configured JSON, so it used ASP.NET Core's
            // camelCase default, and every page script reads `res.data` / `res.message`.
            // Forcing PascalCase would break all 18 frontend pages at once, so the default
            // is left exactly as it was — backward compatibility over tidiness.
            builder.Services.AddControllers();

            // Model-binding failures become RFC 7807 ValidationProblemDetails automatically,
            // with the correlation id attached so a user can quote it.
            builder.Services.Configure<ApiBehaviorOptions>(options =>
            {
                options.InvalidModelStateResponseFactory = context =>
                {
                    var problem = new ValidationProblemDetails(context.ModelState)
                    {
                        Status = StatusCodes.Status400BadRequest,
                        Title = "One or more validation errors occurred.",
                        Instance = context.HttpContext.Request.Path
                    };

                    problem.Extensions["correlationId"] = context.HttpContext.TraceIdentifier;

                    return new BadRequestObjectResult(problem)
                    {
                        ContentTypes = { "application/problem+json" }
                    };
                };
            });

            builder.Services.AddHttpContextAccessor();
            builder.Services.AddEndpointsApiExplorer();

            builder.Services.AddSwaggerGen(options =>
            {
                options.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "RM_CMS API",
                    Version = "v1",
                    Description = "Church management system API. All endpoints require a Bearer access token unless explicitly marked anonymous."
                });

                options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    Name = "Authorization",
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    In = ParameterLocation.Header,
                    Description = "Paste the access token returned by POST /api/auth/login."
                });

                options.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference
                            {
                                Type = ReferenceType.SecurityScheme,
                                Id = "Bearer"
                            }
                        },
                        Array.Empty<string>()
                    }
                });
            });
        }

        // ==========================================================
        // Application services
        // ==========================================================
        private static void RegisterApplicationServices(WebApplicationBuilder builder)
        {
            // ---- Identity module ----
            builder.Services.AddSingleton<ITokenIssuer, TokenIssuer>();
            builder.Services.AddSingleton<IPasswordService, PasswordService>();
            builder.Services.AddSingleton<IAccountStateCache, AccountStateCache>();
            builder.Services.AddScoped<AccessTokenValidator>();
            builder.Services.AddScoped<ICurrentIdentity, CurrentIdentity>();

            // ---- Data access ----
            builder.Services.AddScoped<RM_CMS.Data.IDbConnectionFactory, RM_CMS.Data.DbConnectionFactory>();

            builder.Services.AddScoped<RM_CMS.Modules.Identity.Data.IUserAccountRepository,
                                       RM_CMS.Modules.Identity.Data.UserAccountRepository>();
            builder.Services.AddScoped<RM_CMS.Modules.Identity.Data.IRefreshTokenRepository,
                                       RM_CMS.Modules.Identity.Data.RefreshTokenRepository>();
            builder.Services.AddScoped<RM_CMS.Modules.Identity.Data.ISecurityEventRepository,
                                       RM_CMS.Modules.Identity.Data.SecurityEventRepository>();
            builder.Services.AddScoped<IIdentityService, IdentityService>();
            builder.Services.AddScoped<IIdentityBootstrapper, IdentityBootstrapper>();

            // User management. Spans person, account, roles and volunteer, so it is
            // registered after the modules it orchestrates rather than owning copies
            // of their rules.
            builder.Services.AddScoped<RM_CMS.Modules.Identity.Data.IUserDirectoryRepository,
                                       RM_CMS.Modules.Identity.Data.UserDirectoryRepository>();
            builder.Services.AddScoped<IUserDirectoryService, UserDirectoryService>();

            // ---- Common DAL ----

            // ---- People module (new architecture) ----
            builder.Services.AddScoped<RM_CMS.Modules.People.Data.IPersonRepository,
                                       RM_CMS.Modules.People.Data.PersonRepository>();
            builder.Services.AddScoped<RM_CMS.Modules.People.Services.IPeopleService,
                                       RM_CMS.Modules.People.Services.PeopleService>();

            // ---- Care module (new architecture) ----
            builder.Services.AddScoped<RM_CMS.Modules.Care.Data.ICareCaseRepository,
                                       RM_CMS.Modules.Care.Data.CareCaseRepository>();
            builder.Services.AddScoped<RM_CMS.Modules.Care.Data.ICareInteractionRepository,
                                       RM_CMS.Modules.Care.Data.CareInteractionRepository>();
            builder.Services.AddScoped<RM_CMS.Modules.Care.Data.IEscalationRepository,
                                       RM_CMS.Modules.Care.Data.EscalationRepository>();
            builder.Services.AddScoped<RM_CMS.Modules.Care.Data.ICareLookupRepository,
                                       RM_CMS.Modules.Care.Data.CareLookupRepository>();
            builder.Services.AddScoped<RM_CMS.Modules.Care.Services.IProgressionEngine,
                                       RM_CMS.Modules.Care.Services.ProgressionEngine>();
            builder.Services.AddScoped<RM_CMS.Modules.Care.Services.ICareService,
                                       RM_CMS.Modules.Care.Services.CareService>();

            // ---- Volunteers module (new architecture) ----
            builder.Services.AddScoped<RM_CMS.Modules.Volunteers.Data.IVolunteerRepository,
                                       RM_CMS.Modules.Volunteers.Data.VolunteerRepository>();
            builder.Services.AddScoped<RM_CMS.Modules.Volunteers.Data.ITeamRepository,
                                       RM_CMS.Modules.Volunteers.Data.TeamRepository>();
            builder.Services.AddScoped<RM_CMS.Modules.Volunteers.Services.IVolunteerService,
                                       RM_CMS.Modules.Volunteers.Services.VolunteerService>();

            // ---- Notifications module (new architecture) ----
            // Queue and sender are separate on purpose. Raising an alert only writes a
            // row, so a Telegram outage cannot roll back the domain transaction that
            // raised it; the sender then drains that queue on its own sweep, where a
            // failure costs a retry rather than the escalation itself.
            builder.Services.AddScoped<RM_CMS.Modules.Notifications.Data.INotificationRepository,
                                       RM_CMS.Modules.Notifications.Data.NotificationRepository>();
            builder.Services.AddScoped<RM_CMS.Modules.Notifications.Services.INotificationQueue,
                                       RM_CMS.Modules.Notifications.Services.NotificationQueue>();
            builder.Services.AddScoped<RM_CMS.Modules.Notifications.Services.INotificationComposer,
                                       RM_CMS.Modules.Notifications.Services.NotificationComposer>();
            builder.Services.AddScoped<RM_CMS.Modules.Notifications.Services.INotificationSender,
                                       RM_CMS.Modules.Notifications.Services.NotificationSender>();

            // Pending sign-ins awaiting their Telegram confirmation. Registered
            // beside Identity because that is what creates and consumes them.
            builder.Services.AddScoped<RM_CMS.Modules.Identity.Data.ILoginChallengeRepository,
                                       RM_CMS.Modules.Identity.Data.LoginChallengeRepository>();

            // ---- Message templates module ----
            // The wording of every Telegram message, editable by an administrator.
            // It used to be string literals scattered through the composer and the
            // link service, so changing a word meant a deployment — and the people
            // who know how these should read are pastors, not whoever can rebuild
            // the application.
            //
            // Registered BEFORE Telegram and Notifications because both take it: the
            // link service renders the /start replies through it, and the composer
            // renders every queued alert.
            builder.Services.AddScoped<RM_CMS.Modules.MessageTemplates.Data.ITemplateRepository,
                                       RM_CMS.Modules.MessageTemplates.Data.TemplateRepository>();
            builder.Services.AddScoped<RM_CMS.Modules.MessageTemplates.Services.ITemplateService,
                                       RM_CMS.Modules.MessageTemplates.Services.TemplateService>();

            // ---- Telegram module (new architecture) ----
            // The bot token comes from Telegram__BotToken in the environment, never the
            // database — the MVP kept the live token in a settings row that a generic
            // API served to anyone who could reach it.
            builder.Services.AddScoped<RM_CMS.Modules.Telegram.Data.ITelegramRepository,
                                       RM_CMS.Modules.Telegram.Data.TelegramRepository>();
            builder.Services.AddScoped<RM_CMS.Modules.Telegram.Services.ITelegramLinkService,
                                       RM_CMS.Modules.Telegram.Services.TelegramLinkService>();
            builder.Services.AddSingleton<RM_CMS.Modules.Telegram.Services.ITelegramClient,
                                          RM_CMS.Modules.Telegram.Services.TelegramClient>();

            // ---- Settings module (new architecture) ----
            // Replaces the legacy SystemConfig slice. Owns the bounds checking the
            // schema documents but cannot enforce on a VARCHAR value column.
            builder.Services.AddScoped<RM_CMS.Modules.Settings.Data.ISettingRepository,
                                       RM_CMS.Modules.Settings.Data.SettingRepository>();
            builder.Services.AddScoped<RM_CMS.Modules.Settings.Services.ISettingService,
                                       RM_CMS.Modules.Settings.Services.SettingService>();

            // ---- Campuses module ----
            // The tenancy boundary everything else scopes on. The table shipped with
            // the first schema and a single seeded row, but nothing could create a
            // second one, so the boundary was never exercised.
            builder.Services.AddScoped<RM_CMS.Modules.Campuses.Data.ICampusRepository,
                                       RM_CMS.Modules.Campuses.Data.CampusRepository>();
            builder.Services.AddScoped<RM_CMS.Modules.Campuses.Services.ICampusService,
                                       RM_CMS.Modules.Campuses.Services.CampusService>();

            // ---- Areas module ----
            // The controlled locality list behind the area picker on the intake and
            // add-user screens. Registered before People because PeopleService takes
            // IAreaService to do the find-or-create when an operator types an area
            // that does not exist yet.
            builder.Services.AddScoped<RM_CMS.Modules.Areas.Data.IAreaRepository,
                                       RM_CMS.Modules.Areas.Data.AreaRepository>();
            builder.Services.AddScoped<RM_CMS.Modules.Areas.Services.IAreaService,
                                       RM_CMS.Modules.Areas.Services.AreaService>();

            // ---- Web enquiries module ----
            // Everything the public website collects, held apart from the pastoral
            // records until a coordinator decides what it becomes. The submit endpoint
            // is the only anonymous write in the application.
            builder.Services.AddScoped<RM_CMS.Modules.WebEnquiries.Data.IWebEnquiryRepository,
                                       RM_CMS.Modules.WebEnquiries.Data.WebEnquiryRepository>();
            builder.Services.AddScoped<RM_CMS.Modules.WebEnquiries.Services.IWebEnquiryService,
                                       RM_CMS.Modules.WebEnquiries.Services.WebEnquiryService>();

            // ---- Events module ----
            // The church calendar the public website lists. Drafts are invisible to the
            // public; publishing is its own action so an edit cannot push one live.
            builder.Services.AddScoped<RM_CMS.Modules.Events.Data.IEventRepository,
                                       RM_CMS.Modules.Events.Data.EventRepository>();
            builder.Services.AddScoped<RM_CMS.Modules.Events.Services.IEventService,
                                       RM_CMS.Modules.Events.Services.EventService>();

            // ---- Jobs module (new architecture) ----
            // Replaces the legacy CornJobs slice. Triggered by an external cron over
            // the JobRunner policy — there is no in-process scheduler, so a second
            // instance does not double every sweep.
            builder.Services.AddScoped<RM_CMS.Modules.Jobs.Data.IJobRunRepository,
                                       RM_CMS.Modules.Jobs.Data.JobRunRepository>();
            builder.Services.AddScoped<RM_CMS.Modules.Jobs.Services.IJobService,
                                       RM_CMS.Modules.Jobs.Services.JobService>();

            // ---- Dashboards module (new architecture) ----
            // Read-only role landing pages. Purpose-built aggregate queries rather than
            // calls into Care and Volunteers: a dashboard wants counts, not lists it
            // then counts in memory. Scoped to the teams the CALLER leads, resolved from
            // their token — the page this replaces trusted a query-string team lead id.
            builder.Services.AddScoped<RM_CMS.Modules.Dashboards.Data.ITeamLeadDashboardRepository,
                                       RM_CMS.Modules.Dashboards.Data.TeamLeadDashboardRepository>();
            builder.Services.AddScoped<RM_CMS.Modules.Dashboards.Services.ITeamLeadDashboardService,
                                       RM_CMS.Modules.Dashboards.Services.TeamLeadDashboardService>();

            // The pastor dashboard reuses the team lead read model for cases,
            // contacts, nurture and volunteer load — a pastor's scope is just a
            // longer team-id list — and adds only the three aggregates unique to
            // overseeing several teams: pastor-alerted escalations, the per-team
            // leaderboard, and huddle compliance by team.
            builder.Services.AddScoped<RM_CMS.Modules.Dashboards.Data.IPastorDashboardRepository,
                                       RM_CMS.Modules.Dashboards.Data.PastorDashboardRepository>();
            builder.Services.AddScoped<RM_CMS.Modules.Dashboards.Services.IPastorDashboardService,
                                       RM_CMS.Modules.Dashboards.Services.PastorDashboardService>();

            // ---- Pipeline module (new architecture) ----
            // Every visitor and where their journey has reached. Scoped by role in
            // the service: team lead to their own teams, pastor to their campus,
            // administrator to everything. There is no scope parameter.
            builder.Services.AddScoped<RM_CMS.Modules.Pipeline.Data.IPipelineRepository,
                                       RM_CMS.Modules.Pipeline.Data.PipelineRepository>();
            builder.Services.AddScoped<RM_CMS.Modules.Pipeline.Services.IPipelineService,
                                       RM_CMS.Modules.Pipeline.Services.PipelineService>();

            // ---- Huddle module (new architecture) ----
            // The weekly team meeting where a lead assesses their volunteers'
            // escalation judgement. The only mechanism that can catch an
            // UNDER-escalation: the chase-up job can only chase escalations that
            // were actually raised.
            builder.Services.AddScoped<RM_CMS.Modules.Huddle.Data.IHuddleRepository,
                                       RM_CMS.Modules.Huddle.Data.HuddleRepository>();
            builder.Services.AddScoped<RM_CMS.Modules.Huddle.Services.IHuddleService,
                                       RM_CMS.Modules.Huddle.Services.HuddleService>();

            // ---- Check-ins module (new architecture) ----
            // The team lead's pastoral duty toward their own volunteers. Scoped to the
            // teams the caller leads; the conductor comes from the token, never the
            // payload, so the record of who held a conversation is trustworthy.
            builder.Services.AddScoped<RM_CMS.Modules.CheckIns.Data.ICheckInRepository,
                                       RM_CMS.Modules.CheckIns.Data.CheckInRepository>();
            builder.Services.AddScoped<RM_CMS.Modules.CheckIns.Services.ICheckInService,
                                       RM_CMS.Modules.CheckIns.Services.CheckInService>();

            Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;
        }

        // ==========================================================
        // HTTP pipeline
        //
        // Order is deliberate:
        //   exception handling wraps everything, so nothing escapes as a stack trace;
        //   correlation id is assigned before anything logs;
        //   security headers are set before any body is written;
        //   CORS precedes auth so preflight requests are answered without a token;
        //   rate limiting precedes auth so unauthenticated floods are cheap to reject.
        // ==========================================================
        private static void ConfigurePipeline(WebApplication app)
        {
            app.UseMiddleware<ExceptionHandlingMiddleware>();
            app.UseMiddleware<CorrelationIdMiddleware>();
            app.UseMiddleware<SecurityHeadersMiddleware>();

            // Gives a body to every failure that would otherwise have none.
            //
            // A 404 for a missing page, a 403 from the authorization policies, a 429
            // from the rate limiter: all of these came back with a status and an EMPTY
            // BODY, so a browser showed its own blank "cannot reach this page" and the
            // whole site looked down rather than one address being wrong.
            //
            // This runs only when nothing else wrote a body, so a controller returning
            // its own ProblemDetails or an ApiResponse is left exactly as it was.
            // ErrorPages then decides page or JSON by the path and the Accept header.
            //
            // Placed here, outside routing, so it also covers what never reaches a
            // controller — which is most of the cases above.
            app.UseStatusCodePages(context =>
                ErrorPages.WriteAsync(
                    context.HttpContext,
                    context.HttpContext.Response.StatusCode,
                    app.Environment));

            if (!app.Environment.IsDevelopment())
            {
                // 180 days, applies to subdomains, and eligible for browser preload lists.
                app.UseHsts();
            }

            app.UseHttpsRedirection();

            app.UseMiddleware<RequestLoggingMiddleware>();

            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseStaticFiles(new StaticFileOptions
            {
                // ServeUnknownFileTypes was previously enabled, which lets any extension be
                // served with a caller-influenced content type. Turning it off means unknown
                // types are simply not served.
                ServeUnknownFileTypes = false,
                OnPrepareResponse = context =>
                {
                    var name = context.File.Name;

                    // HTML shells must not be cached, or a signed-out user can be shown a
                    // stale authenticated page from the browser cache.
                    if (name.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                    {
                        context.Context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
                        context.Context.Response.Headers.Pragma = "no-cache";
                        return;
                    }

                    // Scripts and styles carry no version in their filenames, so a browser
                    // that caches them keeps running the previous deployment's code against
                    // the new API — which fails in ways that look like application bugs
                    // rather than staleness.
                    //
                    // "no-cache" means revalidate, not "do not store": the ETag still gives
                    // a 304 when nothing changed, so the cost is one conditional request per
                    // file rather than a re-download.
                    if (name.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
                        name.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
                    {
                        context.Context.Response.Headers.CacheControl = "no-cache, must-revalidate";
                    }
                }
            });

            app.UseRouting();

            app.UseCors(CorsPolicyName);

            app.UseRateLimiter();

            app.UseAuthentication();
            app.UseAuthorization();

            // Must run after authentication so the claims exist, and after authorization so
            // it only ever affects callers who were otherwise allowed through.
            app.UseMiddleware<PasswordChangeRequiredMiddleware>();

            // After the password gate: somebody who must change their password has a
            // more urgent problem, and being told about both at once helps nobody.
            app.UseMiddleware<TelegramLinkRequiredMiddleware>();

            app.MapControllers();

            // The login shell itself must stay reachable without a token, otherwise nobody
            // can ever sign in. It contains no data — only the form.
            app.MapGet("/", async context =>
            {
                context.Response.ContentType = "text/html";
                context.Response.Headers.CacheControl = "no-store";

                await context.Response.SendFileAsync(
                    Path.Combine(app.Environment.ContentRootPath, "wwwroot", "pages", "auth", "login.html"));
            })
            .AllowAnonymous();

            // Liveness probe for Coolify. Deliberately returns no version or build details.
            app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
               .AllowAnonymous()
               .DisableRateLimiting();
        }

        // ==========================================================
        // Startup bootstrap
        // ==========================================================
        private static async Task BootstrapAsync(WebApplication app)
        {
            using var scope = app.Services.CreateScope();

            var bootstrapper = scope.ServiceProvider.GetRequiredService<IIdentityBootstrapper>();

            await bootstrapper.EnsureAdministratorAsync();
        }
    }
}
