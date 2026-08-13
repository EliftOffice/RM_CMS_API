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
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();

                        if (context.Response.HasStarted) return;

                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        context.Response.ContentType = "application/problem+json";

                        var problem = new ProblemDetails
                        {
                            Status = StatusCodes.Status401Unauthorized,
                            Title = "Authentication required.",
                            Detail = "A valid access token is required to call this endpoint.",
                            Instance = context.Request.Path
                        };

                        problem.Extensions["correlationId"] = context.HttpContext.TraceIdentifier;

                        await context.Response.WriteAsync(
                            JsonSerializer.Serialize(problem, ProblemJsonOptions));
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

                        if (context.Response.HasStarted) return;

                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        context.Response.ContentType = "application/problem+json";

                        var problem = new ProblemDetails
                        {
                            Status = StatusCodes.Status403Forbidden,
                            Title = "Access denied.",
                            Detail = "Your account does not have permission to perform this action.",
                            Instance = context.Request.Path
                        };

                        problem.Extensions["correlationId"] = context.HttpContext.TraceIdentifier;

                        await context.Response.WriteAsync(
                            JsonSerializer.Serialize(problem, ProblemJsonOptions));
                    }
                };
            });
        }

        private static readonly JsonSerializerOptions ProblemJsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

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

                    context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    context.HttpContext.Response.ContentType = "application/problem+json";

                    var problem = new ProblemDetails
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "Too many requests.",
                        Detail = "You have made too many requests. Please wait and try again.",
                        Instance = context.HttpContext.Request.Path
                    };

                    problem.Extensions["correlationId"] = context.HttpContext.TraceIdentifier;

                    await context.HttpContext.Response.WriteAsync(
                        JsonSerializer.Serialize(problem, ProblemJsonOptions), cancellationToken);
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

            // ---- Common DAL ----
            builder.Services.AddScoped<RM_CMS.DAL.CommonDAL.ITelegram, RM_CMS.DAL.CommonDAL.Telegram>();

            // ---- Peoples ----
            builder.Services.AddScoped<RM_CMS.BLL.Peoples.IPeoplesBLL, RM_CMS.BLL.Peoples.PeoplesBLL>();
            builder.Services.AddScoped<RM_CMS.DAL.Peoples.IPeoplesDAL, RM_CMS.DAL.Peoples.PeoplesDAL>();

            // ---- Followups ----
            builder.Services.AddScoped<RM_CMS.BLL.Followups.IFollowupsBLL, RM_CMS.BLL.Followups.FollowupsBLL>();
            builder.Services.AddScoped<RM_CMS.DAL.Followups.IFollowupsDAL, RM_CMS.DAL.Followups.FollowupsDAL>();

            // ---- Escalations ----
            builder.Services.AddScoped<RM_CMS.BLL.Followups.IEscalationsBLL, RM_CMS.BLL.Followups.EscalationsBLL>();
            builder.Services.AddScoped<RM_CMS.DAL.Followups.IEscalationsDAL, RM_CMS.DAL.Followups.EscalationsDAL>();

            // ---- Volunteers ----
            builder.Services.AddScoped<RM_CMS.BLL.Volunteers.IVolunteersBLL, RM_CMS.BLL.Volunteers.VolunteersBLL>();
            builder.Services.AddScoped<RM_CMS.DAL.Volunteers.IVolunteersDAL, RM_CMS.DAL.Volunteers.VolunteersDAL>();

            // ---- Team Leads ----
            builder.Services.AddScoped<RM_CMS.DAL.TeamLeads.ITeamLeadDashBoardDAL, RM_CMS.DAL.TeamLeads.TeamLeadDashBoardDAL>();
            builder.Services.AddScoped<RM_CMS.BLL.TeamLeads.ITeamLeadDashBoardBLL, RM_CMS.BLL.TeamLeads.TeamLeadDashBoardBLL>();

            // ---- Check-in ----
            builder.Services.AddScoped<RM_CMS.DAL.TeamLeads.ICheckInDAL, RM_CMS.DAL.TeamLeads.CheckInDAL>();
            builder.Services.AddScoped<RM_CMS.BLL.TeamLeads.ICheckInBLL, RM_CMS.BLL.TeamLeads.CheckInBLL>();

            // ---- Pastors ----
            builder.Services.AddScoped<RM_CMS.DAL.Pastors.IPastorDashboardDAL, RM_CMS.DAL.Pastors.PastorDashboardDAL>();
            builder.Services.AddScoped<RM_CMS.BLL.Pastors.IPastorDashboardBLL, RM_CMS.BLL.Pastors.PastorDashBoardBLL>();

            // ---- Users (legacy lookup module) ----
            builder.Services.AddScoped<RM_CMS.DAL.Users.IUsersDAL, RM_CMS.DAL.Users.UsersDAL>();
            builder.Services.AddScoped<RM_CMS.BLL.Users.IUsersBLL, RM_CMS.BLL.Users.UsersBLL>();



            // ---- Nurture ----
            builder.Services.AddScoped<RM_CMS.DAL.Nurture.INurtureDAL, RM_CMS.DAL.Nurture.NurtureDAL>();
            builder.Services.AddScoped<RM_CMS.BLL.Nurture.INurtureBLL, RM_CMS.BLL.Nurture.NurtureBLL>();

            // ---- Scheduled jobs ----
            builder.Services.AddScoped<RM_CMS.BLL.Jobs.ICornJobsBLL, RM_CMS.BLL.Jobs.CornJobsBLL>();

            // ---- Admin / system config ----
            builder.Services.AddScoped<RM_CMS.DAL.Admin.ISystemConfigRepository, RM_CMS.DAL.Admin.SystemConfigRepository>();
            builder.Services.AddScoped<RM_CMS.BLL.Admin.ISystemConfigService, RM_CMS.BLL.Admin.SystemConfigService>();
            builder.Services.AddScoped<RM_CMS.BLL.Admin.INotificationService, RM_CMS.BLL.Admin.NotificationService>();

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
                    // HTML shells must not be cached, or a signed-out user can be shown a
                    // stale authenticated page from the browser cache.
                    if (context.File.Name.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                    {
                        context.Context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
                        context.Context.Response.Headers.Pragma = "no-cache";
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

            app.MapControllers();

            // The login shell itself must stay reachable without a token, otherwise nobody
            // can ever sign in. It contains no data — only the form.
            app.MapGet("/", async context =>
            {
                context.Response.ContentType = "text/html";
                context.Response.Headers.CacheControl = "no-store";

                await context.Response.SendFileAsync(
                    Path.Combine(app.Environment.ContentRootPath, "wwwroot", "templates", "Volunteers", "Login.html"));
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
