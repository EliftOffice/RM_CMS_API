using Microsoft.Extensions.Options;
using RM_CMS.DAL.Auth;
using RM_CMS.Data.DTO.Auth;
using RM_CMS.Data.Models.Auth;
using RM_CMS.Security;
using RM_CMS.Utilities;

namespace RM_CMS.BLL.Auth
{
    /// <summary>Ambient request facts needed for auditing and device binding.</summary>
    public sealed record AuthRequestContext(string? IpAddress, string? UserAgent, string? CorrelationId);

    /// <summary>
    /// What the controller needs to complete a sign-in: the body payload plus the raw
    /// refresh token, which the controller places in an HttpOnly cookie (browser) or
    /// echoes in the body (non-browser client). The raw value never leaves this pair.
    /// </summary>
    public sealed class AuthIssueResult
    {
        public AuthResultDto Payload { get; init; } = new();
        public string RawRefreshToken { get; init; } = string.Empty;
        public DateTime RefreshExpiresUtc { get; init; }
    }

    public interface IAuthBLL
    {
        Task<ApiResponse<AuthIssueResult>> LoginAsync(LoginRequest request, AuthRequestContext context);
        Task<ApiResponse<AuthIssueResult>> RefreshAsync(string? rawRefreshToken, AuthRequestContext context);
        Task<ApiResponse<bool>> LogoutAsync(string? rawRefreshToken, string? userId, AuthRequestContext context);
        Task<ApiResponse<bool>> LogoutAllAsync(string userId, AuthRequestContext context);

        Task<ApiResponse<AuthUserDto>> GetCurrentUserAsync(string userId);
        Task<ApiResponse<IReadOnlyList<ActiveSessionDto>>> GetActiveSessionsAsync(string userId);
        Task<ApiResponse<bool>> ChangePasswordAsync(string userId, ChangePasswordRequest request, AuthRequestContext context);

        // ---- Administration ----
        Task<ApiResponse<PaginatedResultDto<AuthUserDto>>> ListUsersAsync(int page, int pageSize, string? search);
        Task<ApiResponse<CreatedCredentialDto>> CreateUserAsync(CreateAuthUserRequest request, string? actingUser, AuthRequestContext context);
        Task<ApiResponse<CreatedCredentialDto>> SetPasswordAsync(string userId, SetPasswordRequest request, string? actingUser, AuthRequestContext context);
        Task<ApiResponse<bool>> UpdateRolesAsync(string userId, UpdateRolesRequest request, string? actingUser, AuthRequestContext context);
        Task<ApiResponse<bool>> SetAccountStatusAsync(string userId, SetAccountStatusRequest request, string? actingUser, AuthRequestContext context);

        /// <summary>Creates the first administrator at startup when none exists. Idempotent.</summary>
        Task EnsureBootstrapAdminAsync();
    }

    /// <summary>Simple paging envelope for admin listings.</summary>
    public sealed class PaginatedResultDto<T>
    {
        public IReadOnlyList<T> Items { get; set; } = Array.Empty<T>();
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
        public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    }

    public sealed class AuthBLL : IAuthBLL
    {
        /// <summary>
        /// Returned for every login failure regardless of cause. Distinguishing
        /// "no such user" from "wrong password" from "disabled" would let an attacker
        /// enumerate accounts (OWASP ASVS 2.2.1).
        /// </summary>
        private const string GenericLoginFailure = "Invalid username or password.";

        private const string GenericRefreshFailure = "Your session has expired. Please sign in again.";

        private readonly IAuthUsersDAL _users;
        private readonly IRefreshTokenDAL _refreshTokens;
        private readonly IAuthAuditDAL _audit;
        private readonly ITokenService _tokens;
        private readonly IPasswordHashingService _hasher;
        private readonly IPasswordPolicy _passwordPolicy;
        private readonly AuthOptions _options;
        private readonly TimeProvider _clock;
        private readonly ILogger<AuthBLL> _logger;

        public AuthBLL(
            IAuthUsersDAL users,
            IRefreshTokenDAL refreshTokens,
            IAuthAuditDAL audit,
            ITokenService tokens,
            IPasswordHashingService hasher,
            IPasswordPolicy passwordPolicy,
            IOptions<AuthOptions> options,
            TimeProvider clock,
            ILogger<AuthBLL> logger)
        {
            _users = users;
            _refreshTokens = refreshTokens;
            _audit = audit;
            _tokens = tokens;
            _hasher = hasher;
            _passwordPolicy = passwordPolicy;
            _options = options.Value;
            _clock = clock;
            _logger = logger;
        }

        // ==========================================================
        // Login
        // ==========================================================
        public async Task<ApiResponse<AuthIssueResult>> LoginAsync(LoginRequest request, AuthRequestContext context)
        {
            var now = _clock.GetUtcNow().UtcDateTime;
            var normalized = Normalize(request.Username);

            try
            {
                var user = await _users.GetByUsernameAsync(normalized);

                if (user is null)
                {
                    // Equalise response time so a missing account cannot be spotted by timing.
                    _hasher.Verify(new AuthUser(), DummyHash, request.Password, out _);

                    await AuditAsync(context, AuthAuditEvents.LoginFailed, false,
                        usernameAttempted: request.Username, detail: "Unknown username");

                    return Fail<AuthIssueResult>(GenericLoginFailure);
                }

                if (user.IsLockedOut(now))
                {
                    await AuditAsync(context, AuthAuditEvents.LoginLockedOut, false,
                        userId: user.UserId, usernameAttempted: request.Username,
                        detail: $"Locked until {user.LockoutEndUtc:O}");

                    _logger.LogWarning(
                        "Login blocked for locked account {UserId} until {LockoutEnd}",
                        user.UserId, user.LockoutEndUtc);

                    return Fail<AuthIssueResult>(
                        "This account is temporarily locked due to repeated failed sign-in attempts. Please try again later.");
                }

                if (!user.IsActive || !user.HasUsablePassword)
                {
                    _hasher.Verify(user, DummyHash, request.Password, out _);

                    await AuditAsync(context, AuthAuditEvents.LoginFailed, false,
                        userId: user.UserId, usernameAttempted: request.Username,
                        detail: user.IsActive ? "No usable password set" : "Account disabled");

                    return Fail<AuthIssueResult>(GenericLoginFailure);
                }

                if (!_hasher.Verify(user, user.PasswordHash, request.Password, out var needsRehash))
                {
                    await RegisterFailedAttemptAsync(user, request.Username, context, now);
                    return Fail<AuthIssueResult>(GenericLoginFailure);
                }

                // Opportunistically upgrade an outdated hash format now that we hold the
                // plaintext and know it is correct.
                if (needsRehash)
                {
                    try
                    {
                        await _users.UpgradePasswordHashAsync(user.UserId, _hasher.Hash(user, request.Password));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Password hash upgrade failed for {UserId}", user.UserId);
                    }
                }

                await _users.RecordLoginSuccessAsync(user.UserId, user.RowVersion, now);
                user.LastLoginUtc = now;

                var issued = await IssueSessionAsync(user, familyId: null, parentTokenId: null,
                    deviceLabel: request.DeviceLabel, context, now);

                if (issued is null)
                {
                    // Unreachable on the login path (rotation is not involved), but a null
                    // here must never be surfaced as a successful sign-in.
                    _logger.LogError("Session issue returned null during login for {UserId}", user.UserId);
                    return Fail<AuthIssueResult>("Unable to sign in at the moment. Please try again.");
                }

                await AuditAsync(context, AuthAuditEvents.LoginSucceeded, true,
                    userId: user.UserId, usernameAttempted: request.Username);

                _logger.LogInformation(
                    "Login succeeded for {UserId} with roles {Roles}",
                    user.UserId, string.Join(",", user.Roles));

                return new ApiResponse<AuthIssueResult>(ResponseType.Success, "Signed in successfully", issued);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during login for {Username}", normalized);
                return Fail<AuthIssueResult>("Unable to sign in at the moment. Please try again.");
            }
        }

        /// <summary>
        /// A well-formed PBKDF2 hash of a random value, used purely to burn the same CPU
        /// time as a real verification when the account does not exist or has no password.
        /// </summary>
        private static readonly string DummyHash =
            "AQAAAAIAAYagAAAAEJ8Zx0Qw3nQm4kZq8yQx1cVQY2bK5xW9d0mJ3sF7hT2nRp6vL8cA1eG4dH5jK9wS0g==";

        private async Task RegisterFailedAttemptAsync(AuthUser user, string attemptedUsername, AuthRequestContext context, DateTime now)
        {
            var failedCount = user.AccessFailedCount + 1;
            DateTime? lockoutEnd = null;

            if (failedCount >= _options.MaxFailedAccessAttempts)
            {
                // Exponential backoff: base * 2^(overshoot), capped.
                var overshoot = failedCount - _options.MaxFailedAccessAttempts;
                var seconds = _options.BaseLockoutSeconds * Math.Pow(2, Math.Min(overshoot, 16));
                var capped = Math.Min(seconds, _options.MaxLockoutMinutes * 60d);

                lockoutEnd = now.AddSeconds(capped);

                _logger.LogWarning(
                    "Account {UserId} locked out until {LockoutEnd} after {FailedCount} failed attempts",
                    user.UserId, lockoutEnd, failedCount);
            }

            await _users.RecordLoginFailureAsync(user.UserId, user.RowVersion, failedCount, lockoutEnd);

            await AuditAsync(context, AuthAuditEvents.LoginFailed, false,
                userId: user.UserId, usernameAttempted: attemptedUsername,
                detail: $"Failed attempt {failedCount}");
        }

        // ==========================================================
        // Refresh (rotation + reuse detection)
        // ==========================================================
        public async Task<ApiResponse<AuthIssueResult>> RefreshAsync(string? rawRefreshToken, AuthRequestContext context)
        {
            var now = _clock.GetUtcNow().UtcDateTime;

            if (string.IsNullOrWhiteSpace(rawRefreshToken))
                return Fail<AuthIssueResult>(GenericRefreshFailure);

            try
            {
                var hash = _tokens.HashRefreshToken(rawRefreshToken);
                var stored = await _refreshTokens.GetByHashAsync(hash);

                if (stored is null)
                {
                    await AuditAsync(context, AuthAuditEvents.RefreshFailed, false, detail: "Unknown refresh token");
                    return Fail<AuthIssueResult>(GenericRefreshFailure);
                }

                // ---- Reuse detection ----
                // A token that has already been rotated must never be presented again.
                // Seeing one means either a stolen token is being replayed or the legitimate
                // client's replacement was intercepted. Either way the whole family is burned.
                if (stored.IsRevoked)
                {
                    var revokedCount = await _refreshTokens.RevokeFamilyAsync(
                        stored.FamilyId, RevocationReasons.ReuseDetected, now);

                    await AuditAsync(context, AuthAuditEvents.RefreshReuseDetected, false,
                        userId: stored.UserId,
                        detail: $"Family {stored.FamilyId} revoked ({revokedCount} tokens); original reason {stored.RevokedReason}");

                    _logger.LogWarning(
                        "Refresh token reuse detected for user {UserId}; revoked {Count} tokens in family {FamilyId}",
                        stored.UserId, revokedCount, stored.FamilyId);

                    return Fail<AuthIssueResult>(GenericRefreshFailure);
                }

                if (stored.IsExpired(now))
                {
                    await AuditAsync(context, AuthAuditEvents.RefreshFailed, false,
                        userId: stored.UserId, detail: "Expired refresh token");
                    return Fail<AuthIssueResult>(GenericRefreshFailure);
                }

                var user = await _users.GetByIdAsync(stored.UserId);

                if (user is null || !user.IsActive)
                {
                    await _refreshTokens.RevokeAllForUserAsync(stored.UserId, RevocationReasons.AccountDisabled, now);

                    await AuditAsync(context, AuthAuditEvents.RefreshFailed, false,
                        userId: stored.UserId, detail: "Account missing or disabled");

                    return Fail<AuthIssueResult>(GenericRefreshFailure);
                }

                var issued = await IssueSessionAsync(user, stored.FamilyId, stored.TokenId,
                    stored.DeviceLabel, context, now, presentedTokenId: stored.TokenId);

                if (issued is null)
                {
                    // RotateAsync lost the race: the row was revoked between our read and
                    // our write, which is exactly the reuse signature.
                    await _refreshTokens.RevokeFamilyAsync(stored.FamilyId, RevocationReasons.ReuseDetected, now);

                    await AuditAsync(context, AuthAuditEvents.RefreshReuseDetected, false,
                        userId: stored.UserId, detail: $"Concurrent rotation on family {stored.FamilyId}");

                    return Fail<AuthIssueResult>(GenericRefreshFailure);
                }

                await AuditAsync(context, AuthAuditEvents.TokenRefreshed, true, userId: user.UserId);

                return new ApiResponse<AuthIssueResult>(ResponseType.Success, "Token refreshed", issued);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during token refresh");
                return Fail<AuthIssueResult>(GenericRefreshFailure);
            }
        }

        /// <summary>
        /// Issues an access token + refresh token pair. When <paramref name="presentedTokenId"/>
        /// is supplied this is a rotation and returns null if the rotation was lost.
        /// </summary>
        private async Task<AuthIssueResult?> IssueSessionAsync(
            AuthUser user, string? familyId, string? parentTokenId, string? deviceLabel,
            AuthRequestContext context, DateTime now, string? presentedTokenId = null)
        {
            var refresh = _tokens.CreateRefreshToken();

            var record = new RefreshTokenRecord
            {
                TokenId = Guid.NewGuid().ToString(),
                UserId = user.UserId,
                FamilyId = familyId ?? Guid.NewGuid().ToString(),
                TokenHash = refresh.Hash,
                ParentTokenId = parentTokenId,
                ExpiresUtc = refresh.ExpiresUtc,
                CreatedUtc = now,
                DeviceLabel = Sanitize(deviceLabel, 120),
                UserAgentHash = _tokens.HashUserAgent(context.UserAgent),
                CreatedIp = context.IpAddress
            };

            if (presentedTokenId is not null)
            {
                if (!await _refreshTokens.RotateAsync(presentedTokenId, record))
                    return null;
            }
            else
            {
                await _refreshTokens.InsertAsync(record);

                // Enforce the concurrent-device cap for brand new sessions only, so a
                // routine refresh can never evict the session doing the refreshing.
                var activeFamilies = await _refreshTokens.CountActiveFamiliesAsync(user.UserId, now);
                if (activeFamilies > _options.MaxActiveSessionsPerUser)
                {
                    await _refreshTokens.RevokeOldestFamiliesAsync(
                        user.UserId, _options.MaxActiveSessionsPerUser, RevocationReasons.SessionLimit, now);
                }
            }

            var access = _tokens.CreateAccessToken(user);

            return new AuthIssueResult
            {
                RawRefreshToken = refresh.RawValue,
                RefreshExpiresUtc = refresh.ExpiresUtc,
                Payload = new AuthResultDto
                {
                    AccessToken = access.Value,
                    TokenType = "Bearer",
                    ExpiresAtUtc = access.ExpiresUtc,
                    ExpiresInSeconds = (int)Math.Max(0, (access.ExpiresUtc - now).TotalSeconds),
                    MustChangePassword = user.MustChangePassword,
                    User = ToDto(user)
                }
            };
        }

        // ==========================================================
        // Logout
        // ==========================================================
        public async Task<ApiResponse<bool>> LogoutAsync(string? rawRefreshToken, string? userId, AuthRequestContext context)
        {
            var now = _clock.GetUtcNow().UtcDateTime;

            try
            {
                if (!string.IsNullOrWhiteSpace(rawRefreshToken))
                {
                    var stored = await _refreshTokens.GetByHashAsync(_tokens.HashRefreshToken(rawRefreshToken));

                    if (stored is not null)
                    {
                        // Revoke the whole family, not just this token: the point of logout
                        // is to end the session on this device, and the family *is* the session.
                        await _refreshTokens.RevokeFamilyAsync(stored.FamilyId, RevocationReasons.Logout, now);

                        await AuditAsync(context, AuthAuditEvents.Logout, true, userId: stored.UserId);

                        return new ApiResponse<bool>(ResponseType.Success, "Signed out", true);
                    }
                }

                // No usable cookie (already expired, or a non-browser client). Still a success
                // from the caller's point of view — the client discards its access token.
                await AuditAsync(context, AuthAuditEvents.Logout, true, userId: userId, detail: "No active refresh token");

                return new ApiResponse<bool>(ResponseType.Success, "Signed out", true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during logout");
                return new ApiResponse<bool>(ResponseType.Success, "Signed out", true);
            }
        }

        public async Task<ApiResponse<bool>> LogoutAllAsync(string userId, AuthRequestContext context)
        {
            var now = _clock.GetUtcNow().UtcDateTime;

            try
            {
                var count = await _refreshTokens.RevokeAllForUserAsync(userId, RevocationReasons.LogoutAll, now);

                await AuditAsync(context, AuthAuditEvents.LogoutAll, true,
                    userId: userId, detail: $"Revoked {count} tokens");

                return new ApiResponse<bool>(ResponseType.Success, "Signed out of all devices", true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during logout-all for {UserId}", userId);
                return new ApiResponse<bool>(ResponseType.Error, "Unable to sign out of all devices.", false);
            }
        }

        // ==========================================================
        // Profile & sessions
        // ==========================================================
        public async Task<ApiResponse<AuthUserDto>> GetCurrentUserAsync(string userId)
        {
            var user = await _users.GetByIdAsync(userId);

            return user is null
                ? new ApiResponse<AuthUserDto>(ResponseType.Warning, "Account not found", null!)
                : new ApiResponse<AuthUserDto>(ResponseType.Success, "Account loaded", ToDto(user));
        }

        public async Task<ApiResponse<IReadOnlyList<ActiveSessionDto>>> GetActiveSessionsAsync(string userId)
        {
            var sessions = await _refreshTokens.GetActiveSessionsAsync(userId, _clock.GetUtcNow().UtcDateTime);
            return new ApiResponse<IReadOnlyList<ActiveSessionDto>>(ResponseType.Success, "Active sessions", sessions);
        }

        // ==========================================================
        // Change password
        // ==========================================================
        public async Task<ApiResponse<bool>> ChangePasswordAsync(string userId, ChangePasswordRequest request, AuthRequestContext context)
        {
            var now = _clock.GetUtcNow().UtcDateTime;

            try
            {
                var user = await _users.GetByIdAsync(userId);

                if (user is null || !user.IsActive)
                    return new ApiResponse<bool>(ResponseType.Error, "Account not found or disabled.", false);

                if (!user.HasUsablePassword ||
                    !_hasher.Verify(user, user.PasswordHash, request.CurrentPassword, out _))
                {
                    await AuditAsync(context, AuthAuditEvents.PasswordChanged, false,
                        userId: userId, detail: "Current password incorrect");

                    return new ApiResponse<bool>(ResponseType.Error, "Your current password is incorrect.", false);
                }

                var violations = _passwordPolicy.Validate(request.NewPassword, user);
                if (violations.Count > 0)
                    return new ApiResponse<bool>(ResponseType.Warning, string.Join(" ", violations), false);

                if (string.Equals(request.CurrentPassword, request.NewPassword, StringComparison.Ordinal))
                    return new ApiResponse<bool>(ResponseType.Warning, "The new password must be different from your current password.", false);

                if (await IsPasswordReusedAsync(user, request.NewPassword))
                {
                    return new ApiResponse<bool>(ResponseType.Warning,
                        $"You cannot reuse any of your last {_options.PasswordHistoryCount} passwords.", false);
                }

                var newHash = _hasher.Hash(user, request.NewPassword);

                var updated = await _users.UpdatePasswordAsync(
                    user.UserId, user.RowVersion, newHash, Guid.NewGuid().ToString(),
                    mustChangePassword: false, now);

                if (!updated)
                {
                    return new ApiResponse<bool>(ResponseType.Warning,
                        "Your account was modified concurrently. Please try again.", false);
                }

                await _users.AddPasswordHistoryAsync(user.UserId, newHash, now, _options.PasswordHistoryCount);

                // Every existing session dies: the rotated security stamp kills outstanding
                // access tokens, and this kills the refresh tokens behind them.
                await _refreshTokens.RevokeAllForUserAsync(user.UserId, RevocationReasons.PasswordChanged, now);

                await AuditAsync(context, AuthAuditEvents.PasswordChanged, true, userId: userId);

                _logger.LogInformation("Password changed for {UserId}; all sessions revoked", userId);

                return new ApiResponse<bool>(ResponseType.Success,
                    "Password changed. Please sign in again with your new password.", true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error changing password for {UserId}", userId);
                return new ApiResponse<bool>(ResponseType.Error, "Unable to change password at the moment.", false);
            }
        }

        private async Task<bool> IsPasswordReusedAsync(AuthUser user, string candidate)
        {
            if (_options.PasswordHistoryCount <= 0) return false;

            var history = await _users.GetRecentPasswordHashesAsync(user.UserId, _options.PasswordHistoryCount);

            return history.Any(oldHash => _hasher.Verify(user, oldHash, candidate, out _));
        }

        // ==========================================================
        // Administration
        // ==========================================================
        public async Task<ApiResponse<PaginatedResultDto<AuthUserDto>>> ListUsersAsync(int page, int pageSize, string? search)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);

            var total = await _users.CountAsync(search);
            var users = await _users.ListAsync((page - 1) * pageSize, pageSize, search);

            return new ApiResponse<PaginatedResultDto<AuthUserDto>>(ResponseType.Success, "Users loaded",
                new PaginatedResultDto<AuthUserDto>
                {
                    Items = users.Select(ToDto).ToList(),
                    Page = page,
                    PageSize = pageSize,
                    TotalCount = total
                });
        }

        public async Task<ApiResponse<CreatedCredentialDto>> CreateUserAsync(
            CreateAuthUserRequest request, string? actingUser, AuthRequestContext context)
        {
            try
            {
                var invalidRoles = request.Roles.Where(r => !Security.Roles.IsKnown(r)).ToList();
                if (invalidRoles.Count > 0)
                {
                    return new ApiResponse<CreatedCredentialDto>(ResponseType.Warning,
                        $"Unknown role(s): {string.Join(", ", invalidRoles)}", null!);
                }

                var normalized = Normalize(request.Username);

                if (await _users.UsernameExistsAsync(normalized))
                    return new ApiResponse<CreatedCredentialDto>(ResponseType.Warning, "That username is already taken.", null!);

                var generated = string.IsNullOrWhiteSpace(request.InitialPassword);
                var password = generated ? PasswordGenerator.Generate() : request.InitialPassword!;

                var user = new AuthUser
                {
                    UserId = Guid.NewGuid().ToString(),
                    Username = request.Username.Trim(),
                    NormalizedUsername = normalized,
                    Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
                    MobileNumber = request.MobileNumber.Trim(),
                    DisplayName = request.DisplayName.Trim(),
                    SecurityStamp = Guid.NewGuid().ToString(),
                    TokenVersion = 1,
                    IsActive = true,
                    MustChangePassword = true,
                    VolunteerId = Sanitize(request.VolunteerId, 20),
                    TeamLeadId = Sanitize(request.TeamLeadId, 20),
                    Roles = request.Roles
                };

                var violations = _passwordPolicy.Validate(password, user);
                if (violations.Count > 0)
                    return new ApiResponse<CreatedCredentialDto>(ResponseType.Warning, string.Join(" ", violations), null!);

                user.PasswordHash = _hasher.Hash(user, password);

                await _users.CreateAsync(user, request.Roles, actingUser);
                await _users.AddPasswordHistoryAsync(user.UserId, user.PasswordHash,
                    _clock.GetUtcNow().UtcDateTime, _options.PasswordHistoryCount);

                await AuditAsync(context, AuthAuditEvents.AccountCreated, true,
                    userId: user.UserId, detail: $"Roles: {string.Join(",", request.Roles)}; by {actingUser}");

                _logger.LogInformation("Account {UserId} created by {ActingUser}", user.UserId, actingUser);

                return new ApiResponse<CreatedCredentialDto>(ResponseType.Success, "Account created", new CreatedCredentialDto
                {
                    User = ToDto(user),
                    GeneratedPassword = generated ? password : null
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating account");
                return new ApiResponse<CreatedCredentialDto>(ResponseType.Error, "Unable to create the account.", null!);
            }
        }

        public async Task<ApiResponse<CreatedCredentialDto>> SetPasswordAsync(
            string userId, SetPasswordRequest request, string? actingUser, AuthRequestContext context)
        {
            var now = _clock.GetUtcNow().UtcDateTime;

            try
            {
                var user = await _users.GetByIdAsync(userId);
                if (user is null)
                    return new ApiResponse<CreatedCredentialDto>(ResponseType.Warning, "Account not found.", null!);

                var generated = string.IsNullOrWhiteSpace(request.NewPassword);
                var password = generated ? PasswordGenerator.Generate() : request.NewPassword!;

                var violations = _passwordPolicy.Validate(password, user);
                if (violations.Count > 0)
                    return new ApiResponse<CreatedCredentialDto>(ResponseType.Warning, string.Join(" ", violations), null!);

                var newHash = _hasher.Hash(user, password);

                var updated = await _users.UpdatePasswordAsync(
                    user.UserId, user.RowVersion, newHash, Guid.NewGuid().ToString(),
                    request.MustChangePassword, now);

                if (!updated)
                {
                    return new ApiResponse<CreatedCredentialDto>(ResponseType.Warning,
                        "The account was modified concurrently. Please try again.", null!);
                }

                await _users.AddPasswordHistoryAsync(user.UserId, newHash, now, _options.PasswordHistoryCount);
                await _refreshTokens.RevokeAllForUserAsync(user.UserId, RevocationReasons.PasswordChanged, now);

                await AuditAsync(context, AuthAuditEvents.PasswordSetByAdmin, true,
                    userId: user.UserId, detail: $"By {actingUser}");

                _logger.LogInformation("Password reset for {UserId} by {ActingUser}", userId, actingUser);

                user.MustChangePassword = request.MustChangePassword;

                return new ApiResponse<CreatedCredentialDto>(ResponseType.Success, "Password updated", new CreatedCredentialDto
                {
                    User = ToDto(user),
                    GeneratedPassword = generated ? password : null
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting password for {UserId}", userId);
                return new ApiResponse<CreatedCredentialDto>(ResponseType.Error, "Unable to set the password.", null!);
            }
        }

        public async Task<ApiResponse<bool>> UpdateRolesAsync(
            string userId, UpdateRolesRequest request, string? actingUser, AuthRequestContext context)
        {
            var now = _clock.GetUtcNow().UtcDateTime;

            try
            {
                var invalidRoles = request.Roles.Where(r => !Security.Roles.IsKnown(r)).ToList();
                if (invalidRoles.Count > 0)
                    return new ApiResponse<bool>(ResponseType.Warning, $"Unknown role(s): {string.Join(", ", invalidRoles)}", false);

                var user = await _users.GetByIdAsync(userId);
                if (user is null)
                    return new ApiResponse<bool>(ResponseType.Warning, "Account not found.", false);

                // Never let the last active administrator drop their own Admin role —
                // that would lock everyone out of user management permanently.
                if (user.Roles.Contains(Security.Roles.Admin) && !request.Roles.Contains(Security.Roles.Admin))
                {
                    var otherAdminExists = await AnyOtherActiveAdminAsync(user.UserId);
                    if (!otherAdminExists)
                    {
                        return new ApiResponse<bool>(ResponseType.Warning,
                            "This is the last active administrator. Grant Admin to another account first.", false);
                    }
                }

                var updated = await _users.ReplaceRolesAsync(
                    userId, user.RowVersion, request.Roles, Guid.NewGuid().ToString(), actingUser);

                if (!updated)
                    return new ApiResponse<bool>(ResponseType.Warning, "The account was modified concurrently. Please try again.", false);

                // A role change must not be usable until the user gets a new token.
                await _refreshTokens.RevokeAllForUserAsync(userId, RevocationReasons.RolesChanged, now);

                await AuditAsync(context, AuthAuditEvents.RolesChanged, true, userId: userId,
                    detail: $"{string.Join(",", user.Roles)} -> {string.Join(",", request.Roles)}; by {actingUser}");

                _logger.LogInformation(
                    "Roles for {UserId} changed from {OldRoles} to {NewRoles} by {ActingUser}",
                    userId, string.Join(",", user.Roles), string.Join(",", request.Roles), actingUser);

                return new ApiResponse<bool>(ResponseType.Success, "Roles updated", true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating roles for {UserId}", userId);
                return new ApiResponse<bool>(ResponseType.Error, "Unable to update roles.", false);
            }
        }

        public async Task<ApiResponse<bool>> SetAccountStatusAsync(
            string userId, SetAccountStatusRequest request, string? actingUser, AuthRequestContext context)
        {
            var now = _clock.GetUtcNow().UtcDateTime;

            try
            {
                var user = await _users.GetByIdAsync(userId);
                if (user is null)
                    return new ApiResponse<bool>(ResponseType.Warning, "Account not found.", false);

                if (!request.IsActive && user.Roles.Contains(Security.Roles.Admin))
                {
                    var otherAdminExists = await AnyOtherActiveAdminAsync(user.UserId);
                    if (!otherAdminExists)
                    {
                        return new ApiResponse<bool>(ResponseType.Warning,
                            "This is the last active administrator and cannot be disabled.", false);
                    }
                }

                var updated = await _users.SetActiveAsync(userId, user.RowVersion, request.IsActive, Guid.NewGuid().ToString());

                if (!updated)
                    return new ApiResponse<bool>(ResponseType.Warning, "The account was modified concurrently. Please try again.", false);

                if (!request.IsActive)
                    await _refreshTokens.RevokeAllForUserAsync(userId, RevocationReasons.AccountDisabled, now);

                var eventType = request.IsActive ? AuthAuditEvents.AccountEnabled : AuthAuditEvents.AccountDisabled;
                await AuditAsync(context, eventType, true, userId: userId,
                    detail: $"By {actingUser}. {Sanitize(request.Reason, 150)}");

                _logger.LogInformation("Account {UserId} {Status} by {ActingUser}",
                    userId, request.IsActive ? "enabled" : "disabled", actingUser);

                return new ApiResponse<bool>(ResponseType.Success,
                    request.IsActive ? "Account enabled" : "Account disabled", true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting status for {UserId}", userId);
                return new ApiResponse<bool>(ResponseType.Error, "Unable to update the account.", false);
            }
        }

        private async Task<bool> AnyOtherActiveAdminAsync(string excludingUserId)
        {
            // Cheap enough: admin counts are tiny.
            var candidates = await _users.ListAsync(0, 200, null);

            return candidates.Any(u =>
                u.IsActive &&
                !string.Equals(u.UserId, excludingUserId, StringComparison.Ordinal) &&
                u.Roles.Contains(Security.Roles.Admin));
        }

        // ==========================================================
        // First-run bootstrap
        // ==========================================================
        public async Task EnsureBootstrapAdminAsync()
        {
            if (!_options.Bootstrap.Enabled) return;

            try
            {
                if (await _users.AnyAdminExistsAsync())
                {
                    _logger.LogInformation("Bootstrap skipped: an active administrator already exists.");
                    return;
                }

                var normalized = Normalize(_options.Bootstrap.Username);

                if (await _users.UsernameExistsAsync(normalized))
                {
                    _logger.LogWarning(
                        "Bootstrap skipped: username already exists but holds no active Admin role. " +
                        "Grant the Admin role manually.");
                    return;
                }

                var user = new AuthUser
                {
                    UserId = Guid.NewGuid().ToString(),
                    Username = _options.Bootstrap.Username.Trim(),
                    NormalizedUsername = normalized,
                    MobileNumber = _options.Bootstrap.Username.Trim(),
                    DisplayName = _options.Bootstrap.DisplayName,
                    SecurityStamp = Guid.NewGuid().ToString(),
                    TokenVersion = 1,
                    IsActive = true,
                    MustChangePassword = true,
                    Roles = new List<string> { Security.Roles.Admin }
                };

                var violations = _passwordPolicy.Validate(_options.Bootstrap.Password, user);
                if (violations.Count > 0)
                {
                    _logger.LogError(
                        "Bootstrap administrator NOT created: the supplied password fails policy. {Violations}",
                        string.Join(" ", violations));
                    return;
                }

                user.PasswordHash = _hasher.Hash(user, _options.Bootstrap.Password);

                await _users.CreateAsync(user, user.Roles, "bootstrap");

                // Seed password history, otherwise the bootstrap password is absent from
                // the reuse check and the admin can cycle straight back to it later.
                await _users.AddPasswordHistoryAsync(
                    user.UserId, user.PasswordHash, _clock.GetUtcNow().UtcDateTime, _options.PasswordHistoryCount);

                _logger.LogWarning(
                    "Bootstrap administrator '{Username}' created and must change password at first sign-in. " +
                    "Disable Auth:Bootstrap:Enabled and clear the password environment variable now.",
                    user.Username);
            }
            catch (Exception ex)
            {
                // Never prevent the application from starting.
                _logger.LogError(ex, "Bootstrap administrator creation failed.");
            }
        }

        // ==========================================================
        // Helpers
        // ==========================================================
        private static string Normalize(string username) => username.Trim().ToUpperInvariant();

        private static string? Sanitize(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;

            // Strip control characters so nothing can forge a line in a log or audit row.
            var cleaned = new string(value.Where(c => !char.IsControl(c)).ToArray()).Trim();

            return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength];
        }

        private static ApiResponse<T> Fail<T>(string message) =>
            new(ResponseType.Error, message, default!);

        private static AuthUserDto ToDto(AuthUser user) => new()
        {
            UserId = user.UserId,
            Username = user.Username,
            DisplayName = user.DisplayName,
            MobileNumber = user.MobileNumber,
            Email = user.Email,
            Roles = user.Roles.ToList(),
            VolunteerId = user.VolunteerId,
            TeamLeadId = user.TeamLeadId,
            IsActive = user.IsActive,
            MustChangePassword = user.MustChangePassword,
            LastLoginUtc = user.LastLoginUtc,
            CreatedAt = user.CreatedAt
        };

        private Task AuditAsync(
            AuthRequestContext context, string eventType, bool succeeded,
            string? userId = null, string? usernameAttempted = null, string? detail = null) =>
            _audit.WriteAsync(new AuthAuditEntry
            {
                UserId = userId,
                UsernameAttempted = Sanitize(usernameAttempted, 100),
                EventType = eventType,
                Succeeded = succeeded,
                Detail = Sanitize(detail, 300),
                IpAddress = context.IpAddress,
                UserAgent = context.UserAgent,
                CorrelationId = context.CorrelationId
            });
    }
}
