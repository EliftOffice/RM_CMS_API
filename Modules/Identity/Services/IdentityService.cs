using Microsoft.Extensions.Options;
using RM_CMS.Modules.Identity.Api;
using RM_CMS.Modules.Identity.Data;
using RM_CMS.Modules.Identity.Domain;
using RM_CMS.Security;
using RM_CMS.Utilities;

namespace RM_CMS.Modules.Identity.Services
{
    /// <summary>Ambient request facts needed for auditing and device binding.</summary>
    public sealed record RequestContext(string? IpAddress, string? UserAgent, string? CorrelationId);

    /// <summary>
    /// What the controller needs to complete a sign-in: the response payload plus the
    /// raw refresh token, which the controller puts in an HttpOnly cookie. The raw
    /// value never leaves this pair.
    /// </summary>
    public sealed class IssuedSession
    {
        public AuthResultDto Payload { get; init; } = new();
        public string RawRefreshToken { get; init; } = string.Empty;
        public DateTime RefreshExpiresAt { get; init; }
    }

    public interface IIdentityService
    {
        Task<ApiResponse<IssuedSession>> LoginAsync(LoginRequest request, RequestContext context);
        Task<ApiResponse<IssuedSession>> RefreshAsync(string? rawRefreshToken, RequestContext context);
        Task<ApiResponse<bool>> LogoutAsync(string? rawRefreshToken, string? accountPublicId, RequestContext context);
        Task<ApiResponse<bool>> LogoutAllAsync(string accountPublicId, RequestContext context);

        Task<ApiResponse<AccountDto>> GetAccountAsync(string accountPublicId);
        Task<ApiResponse<IReadOnlyList<SessionDto>>> GetSessionsAsync(string accountPublicId);
        Task<ApiResponse<bool>> ChangePasswordAsync(string accountPublicId, ChangePasswordRequest request, RequestContext context);

        // ---- Administration ----
        Task<ApiResponse<PagedResult<AccountDto>>> ListAccountsAsync(int page, int pageSize, string? search, string? roleCode);
        Task<ApiResponse<CreatedCredentialDto>> CreateAccountAsync(CreateAccountRequest request, string? actingPublicId, RequestContext context);
        Task<ApiResponse<CreatedCredentialDto>> SetPasswordAsync(string accountPublicId, SetPasswordRequest request, string? actingPublicId, RequestContext context);
        Task<ApiResponse<bool>> UpdateRolesAsync(string accountPublicId, UpdateRolesRequest request, string? actingPublicId, RequestContext context);
        Task<ApiResponse<bool>> SetAccountStatusAsync(string accountPublicId, SetAccountStatusRequest request, string? actingPublicId, RequestContext context);
    }

    public sealed class IdentityService : IIdentityService
    {
        /// <summary>
        /// Returned for every login failure regardless of cause. Distinguishing
        /// "no such user" from "wrong password" from "disabled" lets an attacker
        /// enumerate accounts (OWASP ASVS 2.2.1).
        /// </summary>
        private const string GenericLoginFailure = "Invalid username or password.";
        private const string GenericSessionFailure = "Your session has expired. Please sign in again.";

        private readonly IUserAccountRepository _accounts;
        private readonly IRefreshTokenRepository _tokens;
        private readonly ISecurityEventRepository _audit;
        private readonly ITokenIssuer _issuer;
        private readonly IPasswordService _passwords;
        private readonly AuthOptions _options;
        private readonly TimeProvider _clock;
        private readonly ILogger<IdentityService> _logger;

        public IdentityService(
            IUserAccountRepository accounts,
            IRefreshTokenRepository tokens,
            ISecurityEventRepository audit,
            ITokenIssuer issuer,
            IPasswordService passwords,
            IOptions<AuthOptions> options,
            TimeProvider clock,
            ILogger<IdentityService> logger)
        {
            _accounts = accounts;
            _tokens = tokens;
            _audit = audit;
            _issuer = issuer;
            _passwords = passwords;
            _options = options.Value;
            _clock = clock;
            _logger = logger;
        }

        // =====================================================================
        // Login
        // =====================================================================
        public async Task<ApiResponse<IssuedSession>> LoginAsync(LoginRequest request, RequestContext context)
        {
            var now = _clock.GetUtcNow().UtcDateTime;
            var normalized = Normalize(request.Username);

            try
            {
                var account = await _accounts.GetByUsernameAsync(normalized);

                if (account is null)
                {
                    // Equalise timing so a missing account cannot be spotted by latency.
                    _passwords.BurnVerificationTime(request.Password);

                    await AuditAsync(context, SecurityEventTypes.LoginFailed, false,
                        usernameAttempted: request.Username, detail: "Unknown username");

                    return Fail<IssuedSession>(GenericLoginFailure);
                }

                if (account.IsLockedOut(now))
                {
                    await AuditAsync(context, SecurityEventTypes.LoginLockedOut, false,
                        accountId: account.Id, usernameAttempted: request.Username,
                        detail: $"Locked until {account.LockoutEndsAt:O}");

                    _logger.LogWarning("Login blocked for locked account {AccountId} until {LockoutEnd}",
                        account.PublicId, account.LockoutEndsAt);

                    return Fail<IssuedSession>(
                        "This account is temporarily locked after repeated failed sign-in attempts. Please try again later.");
                }

                if (!account.IsActive || !account.HasUsablePassword)
                {
                    _passwords.BurnVerificationTime(request.Password);

                    await AuditAsync(context, SecurityEventTypes.LoginFailed, false,
                        accountId: account.Id, usernameAttempted: request.Username,
                        detail: account.IsActive ? "No usable password set" : "Account disabled");

                    return Fail<IssuedSession>(GenericLoginFailure);
                }

                if (!_passwords.Verify(account, account.PasswordHash, request.Password, out var needsRehash))
                {
                    await RegisterFailedAttemptAsync(account, request.Username, context, now);
                    return Fail<IssuedSession>(GenericLoginFailure);
                }

                // Opportunistically upgrade an outdated hash now that we hold the
                // plaintext and know it is correct.
                if (needsRehash)
                {
                    try
                    {
                        await _accounts.UpgradePasswordHashAsync(account.Id, _passwords.Hash(account, request.Password));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Password hash upgrade failed for {AccountId}", account.PublicId);
                    }
                }

                await _accounts.RecordLoginSuccessAsync(account.Id, account.RowVersion, now);
                account.LastLoginAt = now;

                var issued = await IssueSessionAsync(account, familyId: null, parentId: null,
                    deviceLabel: request.DeviceLabel, context, now);

                if (issued is null)
                {
                    _logger.LogError("Session issue returned null during login for {AccountId}", account.PublicId);
                    return Fail<IssuedSession>("Unable to sign in at the moment. Please try again.");
                }

                await AuditAsync(context, SecurityEventTypes.LoginSucceeded, true,
                    accountId: account.Id, usernameAttempted: request.Username);

                _logger.LogInformation("Login succeeded for {AccountId} with roles {Roles}",
                    account.PublicId, string.Join(",", account.RoleCodes));

                return new ApiResponse<IssuedSession>(ResponseType.Success, "Signed in successfully", issued);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during login for {Username}", normalized);
                return Fail<IssuedSession>("Unable to sign in at the moment. Please try again.");
            }
        }

        private async Task RegisterFailedAttemptAsync(UserAccount account, string attempted, RequestContext context, DateTime now)
        {
            var failures = account.FailedAccessCount + 1;
            DateTime? lockoutEnds = null;

            if (failures >= _options.MaxFailedAccessAttempts)
            {
                // Exponential backoff: base * 2^(overshoot), capped.
                var overshoot = failures - _options.MaxFailedAccessAttempts;
                var seconds = _options.BaseLockoutSeconds * Math.Pow(2, Math.Min(overshoot, 16));
                var capped = Math.Min(seconds, _options.MaxLockoutMinutes * 60d);

                lockoutEnds = now.AddSeconds(capped);

                _logger.LogWarning("Account {AccountId} locked until {LockoutEnd} after {Failures} failed attempts",
                    account.PublicId, lockoutEnds, failures);
            }

            await _accounts.RecordLoginFailureAsync(account.Id, account.RowVersion, failures, lockoutEnds);

            await AuditAsync(context, SecurityEventTypes.LoginFailed, false,
                accountId: account.Id, usernameAttempted: attempted, detail: $"Failed attempt {failures}");
        }

        // =====================================================================
        // Refresh — rotation with reuse detection
        // =====================================================================
        public async Task<ApiResponse<IssuedSession>> RefreshAsync(string? rawRefreshToken, RequestContext context)
        {
            var now = _clock.GetUtcNow().UtcDateTime;

            if (string.IsNullOrWhiteSpace(rawRefreshToken))
                return Fail<IssuedSession>(GenericSessionFailure);

            try
            {
                var stored = await _tokens.GetByHashAsync(_issuer.HashRefreshToken(rawRefreshToken));

                if (stored is null)
                {
                    await AuditAsync(context, SecurityEventTypes.RefreshFailed, false, detail: "Unknown refresh token");
                    return Fail<IssuedSession>(GenericSessionFailure);
                }

                // REUSE DETECTION. A token that has already been rotated must never be
                // presented again. Seeing one means a stolen token is being replayed, or
                // the legitimate client's replacement was intercepted. Either way the
                // whole family is burned.
                if (stored.IsRevoked)
                {
                    var revoked = await _tokens.RevokeFamilyAsync(stored.FamilyId, RevocationReasons.ReuseDetected, now);

                    await AuditAsync(context, SecurityEventTypes.RefreshReuseDetected, false,
                        accountId: stored.UserAccountId,
                        detail: $"Family {stored.FamilyId} revoked ({revoked} tokens); prior reason {stored.RevokedReason}");

                    _logger.LogWarning("Refresh token reuse detected for account {AccountId}; revoked {Count} tokens in family {FamilyId}",
                        stored.UserAccountId, revoked, stored.FamilyId);

                    return Fail<IssuedSession>(GenericSessionFailure);
                }

                if (stored.IsExpired(now))
                {
                    await AuditAsync(context, SecurityEventTypes.RefreshFailed, false,
                        accountId: stored.UserAccountId, detail: "Expired refresh token");
                    return Fail<IssuedSession>(GenericSessionFailure);
                }

                // The token row carries the internal id, so this is the one place that
                // looks an account up by key rather than by public id.
                var account = await _accounts.GetByIdAsync(stored.UserAccountId);

                if (account is null || !account.IsActive)
                {
                    await _tokens.RevokeAllForAccountAsync(stored.UserAccountId, RevocationReasons.AccountDisabled, now);

                    await AuditAsync(context, SecurityEventTypes.RefreshFailed, false,
                        accountId: stored.UserAccountId, detail: "Account missing or disabled");

                    return Fail<IssuedSession>(GenericSessionFailure);
                }

                var issued = await IssueSessionAsync(account, stored.FamilyId, stored.Id,
                    stored.DeviceLabel, context, now, presentedTokenId: stored.Id);

                if (issued is null)
                {
                    // RotateAsync lost the race: the row was revoked between our read and
                    // our write, which is exactly the reuse signature.
                    await _tokens.RevokeFamilyAsync(stored.FamilyId, RevocationReasons.ReuseDetected, now);

                    await AuditAsync(context, SecurityEventTypes.RefreshReuseDetected, false,
                        accountId: stored.UserAccountId, detail: $"Concurrent rotation on family {stored.FamilyId}");

                    return Fail<IssuedSession>(GenericSessionFailure);
                }

                await AuditAsync(context, SecurityEventTypes.TokenRefreshed, true, accountId: account.Id);

                return new ApiResponse<IssuedSession>(ResponseType.Success, "Token refreshed", issued);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during token refresh");
                return Fail<IssuedSession>(GenericSessionFailure);
            }
        }

        /// <summary>
        /// Issues an access + refresh pair. When <paramref name="presentedTokenId"/> is
        /// supplied this is a rotation, and returns null if the rotation was lost.
        /// </summary>
        private async Task<IssuedSession?> IssueSessionAsync(
            UserAccount account, string? familyId, long? parentId, string? deviceLabel,
            RequestContext context, DateTime now, long? presentedTokenId = null)
        {
            var refresh = _issuer.CreateRefreshToken();

            var record = new RefreshTokenRecord
            {
                PublicId = Ulid.NewUlid(),
                UserAccountId = account.Id,
                FamilyId = familyId ?? Ulid.NewUlid(),
                TokenHash = refresh.Hash,
                ParentId = parentId,
                IssuedAt = now,
                ExpiresAt = refresh.ExpiresAt,
                DeviceLabel = Sanitize(deviceLabel, 120),
                UserAgentHash = _issuer.HashUserAgent(context.UserAgent),
                IssuedIp = context.IpAddress
            };

            if (presentedTokenId is not null)
            {
                if (!await _tokens.RotateAsync(presentedTokenId.Value, record))
                    return null;
            }
            else
            {
                await _tokens.InsertAsync(record);

                // Enforce the device cap for NEW sessions only, so a routine refresh can
                // never evict the session doing the refreshing.
                var families = await _tokens.CountActiveFamiliesAsync(account.Id, now);
                if (families > _options.MaxActiveSessionsPerUser)
                {
                    await _tokens.RevokeOldestFamiliesAsync(
                        account.Id, _options.MaxActiveSessionsPerUser, RevocationReasons.SessionLimit, now);
                }
            }

            var access = _issuer.CreateAccessToken(account);

            return new IssuedSession
            {
                RawRefreshToken = refresh.RawValue,
                RefreshExpiresAt = refresh.ExpiresAt,
                Payload = new AuthResultDto
                {
                    AccessToken = access.Value,
                    TokenType = "Bearer",
                    ExpiresAtUtc = access.ExpiresAt,
                    ExpiresInSeconds = (int)Math.Max(0, (access.ExpiresAt - now).TotalSeconds),
                    MustChangePassword = account.MustChangePassword,
                    Account = ToDto(account)
                }
            };
        }

        // =====================================================================
        // Logout
        // =====================================================================
        public async Task<ApiResponse<bool>> LogoutAsync(string? rawRefreshToken, string? accountPublicId, RequestContext context)
        {
            var now = _clock.GetUtcNow().UtcDateTime;

            try
            {
                if (!string.IsNullOrWhiteSpace(rawRefreshToken))
                {
                    var stored = await _tokens.GetByHashAsync(_issuer.HashRefreshToken(rawRefreshToken));

                    if (stored is not null)
                    {
                        // Revoke the whole family, not just this token: the point of logout
                        // is to end the session on this device, and the family IS the session.
                        await _tokens.RevokeFamilyAsync(stored.FamilyId, RevocationReasons.Logout, now);
                        await AuditAsync(context, SecurityEventTypes.Logout, true, accountId: stored.UserAccountId);

                        return new ApiResponse<bool>(ResponseType.Success, "Signed out", true);
                    }
                }

                await AuditAsync(context, SecurityEventTypes.Logout, true, detail: "No active refresh token");
                return new ApiResponse<bool>(ResponseType.Success, "Signed out", true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during logout");
                // Never fail a logout: the client discards its token regardless.
                return new ApiResponse<bool>(ResponseType.Success, "Signed out", true);
            }
        }

        public async Task<ApiResponse<bool>> LogoutAllAsync(string accountPublicId, RequestContext context)
        {
            var now = _clock.GetUtcNow().UtcDateTime;

            try
            {
                var account = await _accounts.GetByPublicIdAsync(accountPublicId);
                if (account is null)
                    return new ApiResponse<bool>(ResponseType.Warning, "Account not found.", false);

                var count = await _tokens.RevokeAllForAccountAsync(account.Id, RevocationReasons.LogoutAll, now);

                await AuditAsync(context, SecurityEventTypes.LogoutAll, true,
                    accountId: account.Id, detail: $"Revoked {count} tokens");

                return new ApiResponse<bool>(ResponseType.Success, "Signed out of all devices", true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during logout-all for {AccountId}", accountPublicId);
                return new ApiResponse<bool>(ResponseType.Error, "Unable to sign out of all devices.", false);
            }
        }

        // =====================================================================
        // Profile and sessions
        // =====================================================================
        public async Task<ApiResponse<AccountDto>> GetAccountAsync(string accountPublicId)
        {
            var account = await _accounts.GetByPublicIdAsync(accountPublicId);

            return account is null
                ? new ApiResponse<AccountDto>(ResponseType.Warning, "Account not found", null!)
                : new ApiResponse<AccountDto>(ResponseType.Success, "Account loaded", ToDto(account));
        }

        public async Task<ApiResponse<IReadOnlyList<SessionDto>>> GetSessionsAsync(string accountPublicId)
        {
            var account = await _accounts.GetByPublicIdAsync(accountPublicId);

            if (account is null)
                return new ApiResponse<IReadOnlyList<SessionDto>>(ResponseType.Warning, "Account not found", Array.Empty<SessionDto>());

            var sessions = await _tokens.GetActiveSessionsAsync(account.Id, _clock.GetUtcNow().UtcDateTime);

            var dtos = sessions.Select(s => new SessionDto
            {
                SessionId = s.FamilyId,
                DeviceLabel = s.DeviceLabel,
                IpAddress = s.IssuedIp,
                SignedInAt = s.IssuedAt,
                ExpiresAt = s.ExpiresAt
            }).ToList();

            return new ApiResponse<IReadOnlyList<SessionDto>>(ResponseType.Success, "Active sessions", dtos);
        }

        // =====================================================================
        // Change password
        // =====================================================================
        public async Task<ApiResponse<bool>> ChangePasswordAsync(string accountPublicId, ChangePasswordRequest request, RequestContext context)
        {
            var now = _clock.GetUtcNow().UtcDateTime;

            try
            {
                var account = await _accounts.GetByPublicIdAsync(accountPublicId);

                if (account is null || !account.IsActive)
                    return new ApiResponse<bool>(ResponseType.Error, "Account not found or disabled.", false);

                if (!account.HasUsablePassword ||
                    !_passwords.Verify(account, account.PasswordHash, request.CurrentPassword, out _))
                {
                    await AuditAsync(context, SecurityEventTypes.PasswordChanged, false,
                        accountId: account.Id, detail: "Current password incorrect");

                    return new ApiResponse<bool>(ResponseType.Error, "Your current password is incorrect.", false);
                }

                var violations = _passwords.Validate(request.NewPassword, account);
                if (violations.Count > 0)
                    return new ApiResponse<bool>(ResponseType.Warning, string.Join(" ", violations), false);

                if (string.Equals(request.CurrentPassword, request.NewPassword, StringComparison.Ordinal))
                    return new ApiResponse<bool>(ResponseType.Warning, "The new password must be different from your current password.", false);

                if (await IsPasswordReusedAsync(account, request.NewPassword))
                {
                    return new ApiResponse<bool>(ResponseType.Warning,
                        $"You cannot reuse any of your last {_options.PasswordHistoryCount} passwords.", false);
                }

                var hash = _passwords.Hash(account, request.NewPassword);

                var updated = await _accounts.UpdatePasswordAsync(
                    account.Id, account.RowVersion, hash, Ulid.NewUlid(), mustChangePassword: false, now);

                if (!updated)
                    return new ApiResponse<bool>(ResponseType.Warning, "Your account was modified concurrently. Please try again.", false);

                await _accounts.AddPasswordHistoryAsync(account.Id, hash, now, _options.PasswordHistoryCount);

                // Every session dies: the rotated stamp kills outstanding access tokens,
                // and this kills the refresh tokens behind them.
                await _tokens.RevokeAllForAccountAsync(account.Id, RevocationReasons.PasswordChanged, now);

                await AuditAsync(context, SecurityEventTypes.PasswordChanged, true, accountId: account.Id);

                _logger.LogInformation("Password changed for {AccountId}; all sessions revoked", account.PublicId);

                return new ApiResponse<bool>(ResponseType.Success,
                    "Password changed. Please sign in again with your new password.", true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error changing password for {AccountId}", accountPublicId);
                return new ApiResponse<bool>(ResponseType.Error, "Unable to change password at the moment.", false);
            }
        }

        private async Task<bool> IsPasswordReusedAsync(UserAccount account, string candidate)
        {
            if (_options.PasswordHistoryCount <= 0) return false;

            var history = await _accounts.GetRecentPasswordHashesAsync(account.Id, _options.PasswordHistoryCount);

            return history.Any(old => _passwords.Verify(account, old, candidate, out _));
        }

        // =====================================================================
        // Administration
        // =====================================================================
        public async Task<ApiResponse<PagedResult<AccountDto>>> ListAccountsAsync(int page, int pageSize, string? search, string? roleCode)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);

            var total = await _accounts.CountAsync(search, roleCode);
            var accounts = await _accounts.ListAsync((page - 1) * pageSize, pageSize, search, roleCode);

            return new ApiResponse<PagedResult<AccountDto>>(ResponseType.Success, "Accounts loaded",
                new PagedResult<AccountDto>
                {
                    Items = accounts.Select(ToDto).ToList(),
                    Page = page,
                    PageSize = pageSize,
                    TotalCount = total
                });
        }

        public async Task<ApiResponse<CreatedCredentialDto>> CreateAccountAsync(
            CreateAccountRequest request, string? actingPublicId, RequestContext context)
        {
            var now = _clock.GetUtcNow().UtcDateTime;

            try
            {
                var invalid = request.Roles.Where(r => !RoleCodes.IsKnown(r.RoleCode)).Select(r => r.RoleCode).ToList();
                if (invalid.Count > 0)
                    return new ApiResponse<CreatedCredentialDto>(ResponseType.Warning, $"Unknown role(s): {string.Join(", ", invalid)}", null!);

                // Accounts attach to existing people; they do not create them.
                var personId = await _accounts.ResolvePersonIdAsync(request.PersonId);
                if (personId is null)
                    return new ApiResponse<CreatedCredentialDto>(ResponseType.Warning, "That person was not found.", null!);

                var normalized = Normalize(request.Username);

                if (await _accounts.UsernameExistsAsync(normalized))
                    return new ApiResponse<CreatedCredentialDto>(ResponseType.Warning, "That username is already taken.", null!);

                var generated = string.IsNullOrWhiteSpace(request.InitialPassword);
                var password = generated ? _passwords.Generate() : request.InitialPassword!;

                var account = new UserAccount
                {
                    PublicId = Ulid.NewUlid(),
                    PersonId = personId.Value,
                    Username = request.Username.Trim(),
                    NormalizedUsername = normalized,
                    SecurityStamp = Ulid.NewUlid(),
                    TokenVersion = 1,
                    IsActive = true,

                    // Was hard-coded true, which silently overrode the caller. The
                    // add-user screen collects this as a checkbox and sent it all the
                    // way down, and it died here: unticking it changed nothing and
                    // every account came out forced to change at first sign-in.
                    MustChangePassword = request.MustChangePassword
                };

                var violations = _passwords.Validate(password, account);
                if (violations.Count > 0)
                    return new ApiResponse<CreatedCredentialDto>(ResponseType.Warning, string.Join(" ", violations), null!);

                account.PasswordHash = _passwords.Hash(account, password);

                var roles = new List<UserRoleAssignment>();
                foreach (var grant in request.Roles)
                {
                    roles.Add(new UserRoleAssignment
                    {
                        RoleCode = grant.RoleCode,
                        CampusId = await _accounts.ResolveCampusIdAsync(grant.CampusId)
                    });
                }

                var actingId = await ResolveAccountIdAsync(actingPublicId);
                var newId = await _accounts.CreateAsync(account, roles, actingId);

                await _accounts.AddPasswordHistoryAsync(newId, account.PasswordHash, now, _options.PasswordHistoryCount);

                await AuditAsync(context, SecurityEventTypes.AccountCreated, true,
                    accountId: newId, detail: $"Roles: {string.Join(",", roles.Select(r => r.RoleCode))}");

                _logger.LogInformation("Account {AccountId} created by {ActingUser}", account.PublicId, actingPublicId);

                var created = await _accounts.GetByPublicIdAsync(account.PublicId);

                return new ApiResponse<CreatedCredentialDto>(ResponseType.Success, "Account created", new CreatedCredentialDto
                {
                    Account = created is null ? ToDto(account) : ToDto(created),
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
            string accountPublicId, SetPasswordRequest request, string? actingPublicId, RequestContext context)
        {
            var now = _clock.GetUtcNow().UtcDateTime;

            try
            {
                var account = await _accounts.GetByPublicIdAsync(accountPublicId);
                if (account is null)
                    return new ApiResponse<CreatedCredentialDto>(ResponseType.Warning, "Account not found.", null!);

                var generated = string.IsNullOrWhiteSpace(request.NewPassword);
                var password = generated ? _passwords.Generate() : request.NewPassword!;

                var violations = _passwords.Validate(password, account);
                if (violations.Count > 0)
                    return new ApiResponse<CreatedCredentialDto>(ResponseType.Warning, string.Join(" ", violations), null!);

                var hash = _passwords.Hash(account, password);

                var updated = await _accounts.UpdatePasswordAsync(
                    account.Id, account.RowVersion, hash, Ulid.NewUlid(), request.MustChangePassword, now);

                if (!updated)
                    return new ApiResponse<CreatedCredentialDto>(ResponseType.Warning, "The account was modified concurrently. Please try again.", null!);

                await _accounts.AddPasswordHistoryAsync(account.Id, hash, now, _options.PasswordHistoryCount);
                await _tokens.RevokeAllForAccountAsync(account.Id, RevocationReasons.PasswordChanged, now);

                await AuditAsync(context, SecurityEventTypes.PasswordSetByAdmin, true,
                    accountId: account.Id, detail: $"By {actingPublicId}");

                _logger.LogInformation("Password reset for {AccountId} by {ActingUser}", accountPublicId, actingPublicId);

                account.MustChangePassword = request.MustChangePassword;

                return new ApiResponse<CreatedCredentialDto>(ResponseType.Success, "Password updated", new CreatedCredentialDto
                {
                    Account = ToDto(account),
                    GeneratedPassword = generated ? password : null
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting password for {AccountId}", accountPublicId);
                return new ApiResponse<CreatedCredentialDto>(ResponseType.Error, "Unable to set the password.", null!);
            }
        }

        public async Task<ApiResponse<bool>> UpdateRolesAsync(
            string accountPublicId, UpdateRolesRequest request, string? actingPublicId, RequestContext context)
        {
            var now = _clock.GetUtcNow().UtcDateTime;

            try
            {
                var invalid = request.Roles.Where(r => !RoleCodes.IsKnown(r.RoleCode)).Select(r => r.RoleCode).ToList();
                if (invalid.Count > 0)
                    return new ApiResponse<bool>(ResponseType.Warning, $"Unknown role(s): {string.Join(", ", invalid)}", false);

                var account = await _accounts.GetByPublicIdAsync(accountPublicId);
                if (account is null)
                    return new ApiResponse<bool>(ResponseType.Warning, "Account not found.", false);

                // Never let the last active administrator drop their own Admin role —
                // that would lock everyone out of user management permanently.
                var losingAdmin = account.IsInRole(RoleCodes.Admin) &&
                                  !request.Roles.Any(r => r.RoleCode == RoleCodes.Admin);

                if (losingAdmin && await _accounts.CountActiveAdminsAsync(account.Id) == 0)
                {
                    return new ApiResponse<bool>(ResponseType.Warning,
                        "This is the last active administrator. Grant Admin to another account first.", false);
                }

                var roles = new List<UserRoleAssignment>();
                foreach (var grant in request.Roles)
                {
                    roles.Add(new UserRoleAssignment
                    {
                        RoleCode = grant.RoleCode,
                        CampusId = await _accounts.ResolveCampusIdAsync(grant.CampusId)
                    });
                }

                var actingId = await ResolveAccountIdAsync(actingPublicId);

                var updated = await _accounts.ReplaceRolesAsync(account.Id, account.RowVersion, roles, Ulid.NewUlid(), actingId);

                if (!updated)
                    return new ApiResponse<bool>(ResponseType.Warning, "The account was modified concurrently. Please try again.", false);

                // A role change must not be usable until the user gets a new token.
                await _tokens.RevokeAllForAccountAsync(account.Id, RevocationReasons.RolesChanged, now);

                await AuditAsync(context, SecurityEventTypes.RolesChanged, true, accountId: account.Id,
                    detail: $"{string.Join(",", account.RoleCodes)} -> {string.Join(",", roles.Select(r => r.RoleCode))}");

                _logger.LogInformation("Roles for {AccountId} changed to {NewRoles} by {ActingUser}",
                    accountPublicId, string.Join(",", roles.Select(r => r.RoleCode)), actingPublicId);

                return new ApiResponse<bool>(ResponseType.Success, "Roles updated", true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating roles for {AccountId}", accountPublicId);
                return new ApiResponse<bool>(ResponseType.Error, "Unable to update roles.", false);
            }
        }

        public async Task<ApiResponse<bool>> SetAccountStatusAsync(
            string accountPublicId, SetAccountStatusRequest request, string? actingPublicId, RequestContext context)
        {
            var now = _clock.GetUtcNow().UtcDateTime;

            try
            {
                var account = await _accounts.GetByPublicIdAsync(accountPublicId);
                if (account is null)
                    return new ApiResponse<bool>(ResponseType.Warning, "Account not found.", false);

                if (!request.IsActive && account.IsInRole(RoleCodes.Admin) &&
                    await _accounts.CountActiveAdminsAsync(account.Id) == 0)
                {
                    return new ApiResponse<bool>(ResponseType.Warning,
                        "This is the last active administrator and cannot be disabled.", false);
                }

                var updated = await _accounts.SetActiveAsync(account.Id, account.RowVersion, request.IsActive, Ulid.NewUlid());

                if (!updated)
                    return new ApiResponse<bool>(ResponseType.Warning, "The account was modified concurrently. Please try again.", false);

                if (!request.IsActive)
                    await _tokens.RevokeAllForAccountAsync(account.Id, RevocationReasons.AccountDisabled, now);

                var eventType = request.IsActive ? SecurityEventTypes.AccountEnabled : SecurityEventTypes.AccountDisabled;

                await AuditAsync(context, eventType, true, accountId: account.Id,
                    detail: $"By {actingPublicId}. {Sanitize(request.Reason, 200)}");

                _logger.LogInformation("Account {AccountId} {Status} by {ActingUser}",
                    accountPublicId, request.IsActive ? "enabled" : "disabled", actingPublicId);

                return new ApiResponse<bool>(ResponseType.Success,
                    request.IsActive ? "Account enabled" : "Account disabled", true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting status for {AccountId}", accountPublicId);
                return new ApiResponse<bool>(ResponseType.Error, "Unable to update the account.", false);
            }
        }

        // =====================================================================
        // Helpers
        // =====================================================================
        private async Task<long?> ResolveAccountIdAsync(string? accountPublicId)
        {
            if (string.IsNullOrWhiteSpace(accountPublicId)) return null;
            var account = await _accounts.GetByPublicIdAsync(accountPublicId);
            return account?.Id;
        }

        private static string Normalize(string username) => username.Trim().ToUpperInvariant();

        private static string? Sanitize(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;

            // Strip control characters so nothing can forge a line in a log or audit row.
            var cleaned = new string(value.Where(c => !char.IsControl(c)).ToArray()).Trim();

            return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength];
        }

        private static ApiResponse<T> Fail<T>(string message) => new(ResponseType.Error, message, default!);

        private static AccountDto ToDto(UserAccount account) => new()
        {
            Id = account.PublicId,
            PersonId = account.PersonPublicId,
            Username = account.Username,
            FullName = account.FullName,
            CampusId = account.CampusPublicId,
            VolunteerId = account.VolunteerPublicId,
            TeamId = account.TeamPublicId,
            Roles = account.Roles.Select(r => new RoleGrantDto
            {
                RoleCode = r.RoleCode,
                CampusId = r.CampusPublicId
            }).ToList(),
            IsActive = account.IsActive,
            MustChangePassword = account.MustChangePassword,
            LastLoginAt = account.LastLoginAt,
            CreatedAt = account.CreatedAt
        };

        private Task AuditAsync(
            RequestContext context, string eventType, bool succeeded,
            long? accountId = null, string? usernameAttempted = null, string? detail = null) =>
            _audit.WriteAsync(new SecurityEventEntry
            {
                UserAccountId = accountId,
                UsernameAttempted = Sanitize(usernameAttempted, 100),
                EventType = eventType,
                Succeeded = succeeded,
                Detail = Sanitize(detail, 500),
                IpAddress = context.IpAddress,
                UserAgent = context.UserAgent,
                CorrelationId = context.CorrelationId
            });
    }
}
