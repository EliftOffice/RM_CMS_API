using Microsoft.Extensions.Options;
using RM_CMS.Modules.Identity.Api;
using RM_CMS.Modules.Identity.Data;
using RM_CMS.Modules.Identity.Domain;
using RM_CMS.Modules.MessageTemplates.Domain;
using RM_CMS.Modules.MessageTemplates.Services;
using RM_CMS.Modules.Settings.Data;
using RM_CMS.Modules.Telegram.Data;
using RM_CMS.Modules.Telegram.Services;
using RM_CMS.Security;
using RM_CMS.Utilities;

namespace RM_CMS.Modules.Identity.Services
{
    /// <summary>Ambient request facts needed for auditing and device binding.</summary>
    public sealed record RequestContext(string? IpAddress, string? UserAgent, string? CorrelationId);

    /// <summary>
    /// What polling a pending sign-in produced: the answer for the browser, and — on
    /// the one poll that won the race — the session the controller must turn into a
    /// token and a cookie.
    /// </summary>
    /// <remarks>
    /// Two fields rather than one because the raw refresh token must NEVER be part of
    /// the serialized response: it belongs in an HttpOnly cookie the page cannot read.
    /// Keeping it off the DTO makes that structural rather than a rule somebody has to
    /// remember.
    /// </remarks>
    public sealed record VerificationPollResult(
        ApiResponse<LoginChallengeStatusDto> Response, IssuedSession? Session);

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

        /// <summary>
        /// Whether this mobile number has to supply a password. Anonymous, so the
        /// login screen can show the password box only where it is needed.
        /// </summary>
        Task<ApiResponse<LoginMethodDto>> GetLoginMethodAsync(LoginMethodRequest request, RequestContext context);

        /// <summary>
        /// Sends (or re-sends) the Telegram confirmation prompt for a pending sign-in.
        /// </summary>
        Task<ApiResponse<bool>> SendVerificationPromptAsync(string challengePublicId, RequestContext context);

        /// <summary>
        /// Where a pending sign-in has got to, and — on the one poll that wins the race
        /// to consume it — the session itself.
        /// </summary>
        Task<VerificationPollResult> PollVerificationAsync(string challengePublicId, RequestContext context);

        /// <summary>
        /// Records a tap on the Telegram button. Called by the webhook, which has
        /// already checked the shared secret.
        /// </summary>
        Task<(bool Handled, string Reply)> ResolveVerificationAsync(
            string token, bool approved, long chatId, RequestContext context);
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
        Task<ApiResponse<bool>> SetPasswordlessLoginAsync(string accountPublicId, SetPasswordlessLoginRequest request, string? actingPublicId, RequestContext context);
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

        // ---- second factor ----
        // Identity reaches into Telegram for this, which is the one direction this
        // codebase's modules do not otherwise go. The alternative — an abstraction in
        // Identity implemented in Telegram — would be one interface and one class to
        // express "send a message to a chat id", and there is exactly one channel.
        // Revisit if a second ever appears.
        private readonly ILoginChallengeRepository _challenges;
        private readonly ITelegramRepository _telegramContacts;
        private readonly ITelegramClient _telegram;
        private readonly ITemplateService _templates;
        private readonly ISettingRepository _settings;
        private readonly ISecurityEventRepository _audit;
        private readonly ITokenIssuer _issuer;
        private readonly IPasswordService _passwords;
        private readonly AuthOptions _options;
        private readonly TimeProvider _clock;
        private readonly ILogger<IdentityService> _logger;

        public IdentityService(
            IUserAccountRepository accounts,
            IRefreshTokenRepository tokens,
            ILoginChallengeRepository challenges,
            ITelegramRepository telegramContacts,
            ITelegramClient telegram,
            ITemplateService templates,
            ISettingRepository settings,
            ISecurityEventRepository audit,
            ITokenIssuer issuer,
            IPasswordService passwords,
            IOptions<AuthOptions> options,
            TimeProvider clock,
            ILogger<IdentityService> logger)
        {
            _accounts = accounts;
            _tokens = tokens;
            _challenges = challenges;
            _telegramContacts = telegramContacts;
            _telegram = telegram;
            _templates = templates;
            _settings = settings;
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
                    // The password is nullable now that number-only accounts omit it;
                    // an empty string burns the same time as a real one.
                    _passwords.BurnVerificationTime(request.Password ?? string.Empty);

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

                // An account this administrator has marked passwordless signs in on the
                // strength of its mobile number alone. It skips the password checks
                // entirely — including HasUsablePassword, because such an account may
                // never have been given one — but NOT the disabled check, the lockout
                // above, or any of the auditing below.
                var passwordless = account.AllowsPasswordlessLogin;

                if (!account.IsActive || (!passwordless && !account.HasUsablePassword))
                {
                    _passwords.BurnVerificationTime(request.Password ?? string.Empty);

                    await AuditAsync(context, SecurityEventTypes.LoginFailed, false,
                        accountId: account.Id, usernameAttempted: request.Username,
                        detail: account.IsActive ? "No usable password set" : "Account disabled");

                    return Fail<IssuedSession>(GenericLoginFailure);
                }

                if (!passwordless)
                {
                    // A missing password on an account that needs one is a failure like
                    // any other, and it counts towards lockout. Treating it as a
                    // validation error instead would hand an attacker a free probe:
                    // "password required" means the account exists.
                    if (string.IsNullOrEmpty(request.Password))
                    {
                        await RegisterFailedAttemptAsync(account, request.Username, context, now);
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
                }

                // ---- second factor -------------------------------------------------
                // The credential is correct. Before any session exists, ask whether
                // this sign-in also has to be confirmed on Telegram.
                //
                // Deliberately AFTER the credential check and BEFORE RecordLoginSuccess:
                // a challenge raised earlier would let anyone spray mobile numbers and
                // make the church's phones buzz, and marking the login successful now
                // would record a sign-in that has not happened yet.
                var challenge = await StartTelegramVerificationAsync(account, context, now);

                if (challenge is not null)
                {
                    await AuditAsync(context, SecurityEventTypes.LoginVerificationRequired, true,
                        accountId: account.Id, usernameAttempted: request.Username);

                    _logger.LogInformation(
                        "Login for {AccountId} is waiting on Telegram confirmation", account.PublicId);

                    // No token, no cookie, nothing that could be used as a session.
                    return new ApiResponse<IssuedSession>(
                        ResponseType.Success,
                        "Confirm this sign-in on Telegram.",
                        new IssuedSession
                        {
                            Payload = new AuthResultDto
                            {
                                RequiresTelegramVerification = true,
                                ChallengeId = challenge.PublicId,
                                AccessToken = string.Empty
                            },
                            RawRefreshToken = string.Empty
                        });
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

                // Recorded under its own event type when no password was presented, so
                // the audit trail can tell a session that proved something apart from
                // one that proved only that somebody knew a mobile number.
                await AuditAsync(context,
                    passwordless ? SecurityEventTypes.PasswordlessLoginSucceeded : SecurityEventTypes.LoginSucceeded,
                    true, accountId: account.Id, usernameAttempted: request.Username);

                _logger.LogInformation("Login succeeded for {AccountId} with roles {Roles}{Passwordless}",
                    account.PublicId, string.Join(",", account.RoleCodes),
                    passwordless ? " [mobile number only]" : string.Empty);

                return new ApiResponse<IssuedSession>(ResponseType.Success, "Signed in successfully", issued);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during login for {Username}", normalized);
                return Fail<IssuedSession>("Unable to sign in at the moment. Please try again.");
            }
        }

        /// <summary>
        /// What this mobile number needs in order to sign in.
        ///
        /// The login screen asks before deciding whether to show a password box. Only
        /// an account an administrator has explicitly marked passwordless, and which is
        /// active and not locked out, gets an answer of "no password".
        ///
        /// EVERY OTHER CASE ANSWERS "password required" — unknown number, disabled
        /// account, locked account, or a failure reading the database. That default is
        /// the safe direction twice over: it never invites a sign-in that will not
        /// work, and it means this endpoint cannot be used to check whether somebody
        /// has an account here. A wrong number simply gets a password box and then a
        /// generic failure, exactly as it did before.
        /// </summary>
        public async Task<ApiResponse<LoginMethodDto>> GetLoginMethodAsync(
            LoginMethodRequest request, RequestContext context)
        {
            var normalized = Normalize(request.Username);

            try
            {
                var account = await _accounts.GetByUsernameAsync(normalized);

                var passwordless =
                    account is not null &&
                    account.AllowsPasswordlessLogin &&
                    account.IsActive &&
                    !account.IsLockedOut(_clock.GetUtcNow().UtcDateTime);

                return new ApiResponse<LoginMethodDto>(
                    ResponseType.Success,
                    "Sign-in method.",
                    new LoginMethodDto { RequiresPassword = !passwordless });
            }
            catch (Exception ex)
            {
                // Fails towards the password box. A screen that skipped it because the
                // database hiccuped would sign nobody in and explain nothing.
                _logger.LogError(ex, "Could not read the sign-in method for {Username}", normalized);

                return new ApiResponse<LoginMethodDto>(
                    ResponseType.Success, "Sign-in method.", new LoginMethodDto { RequiresPassword = true });
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

        /// <summary>
        /// Grants or revokes mobile-number-only sign-in for one account.
        /// </summary>
        /// <remarks>
        /// Refused for administrators, and that is the one place this feature is
        /// narrower than "any user". An administrator is the account that can grant
        /// this to everybody else, reset passwords and change roles — a passwordless
        /// one puts the entire system, including every pastoral record in it, behind a
        /// mobile number that is written on a poster. The people this feature exists
        /// for are volunteers and intake operators, none of whom need that role, so
        /// the guardrail costs nothing real. Lift it by deleting this block if the
        /// church decides otherwise.
        /// </remarks>
        public async Task<ApiResponse<bool>> SetPasswordlessLoginAsync(
            string accountPublicId, SetPasswordlessLoginRequest request,
            string? actingPublicId, RequestContext context)
        {
            var now = _clock.GetUtcNow().UtcDateTime;

            try
            {
                var account = await _accounts.GetByPublicIdAsync(accountPublicId);

                if (account is null)
                    return new ApiResponse<bool>(ResponseType.Warning, "Account not found.", false);

                if (request.Allowed && account.IsInRole(RoleCodes.Admin))
                {
                    return new ApiResponse<bool>(ResponseType.Warning,
                        "An administrator cannot sign in without a password. " +
                        "Anyone who knew the number would hold the whole system.", false);
                }

                if (request.Allowed == account.AllowsPasswordlessLogin)
                {
                    return new ApiResponse<bool>(ResponseType.Success,
                        request.Allowed
                            ? "Already signing in with a mobile number only."
                            : "Already requires a password.",
                        true);
                }

                var updated = await _accounts.SetPasswordlessLoginAsync(
                    account.Id, account.RowVersion, request.Allowed,
                    Ulid.NewUlid(), await ResolveAccountIdAsync(actingPublicId));

                if (!updated)
                {
                    return new ApiResponse<bool>(ResponseType.Warning,
                        "The account was modified concurrently. Please try again.", false);
                }

                // Every open session for this account goes, either way. Granting the
                // change mid-session would leave a session whose security stamp no
                // longer matches; revoking it and NOT cutting the sessions would leave
                // the account reachable for as long as one lasted, which is the exact
                // window the administrator was trying to close.
                await _tokens.RevokeAllForAccountAsync(account.Id, RevocationReasons.RolesChanged, now);

                var eventType = request.Allowed
                    ? SecurityEventTypes.PasswordlessEnabled
                    : SecurityEventTypes.PasswordlessDisabled;

                await AuditAsync(context, eventType, true, accountId: account.Id,
                    detail: $"By {actingPublicId}. {Sanitize(request.Reason, 200)}");

                _logger.LogWarning(
                    "Account {AccountId} passwordless sign-in {State} by {ActingUser}",
                    accountPublicId, request.Allowed ? "ENABLED" : "disabled", actingPublicId);

                return new ApiResponse<bool>(ResponseType.Success,
                    request.Allowed
                        ? "This person can now sign in with their mobile number alone."
                        : "This person must enter a password again.",
                    true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting passwordless sign-in for {AccountId}", accountPublicId);
                return new ApiResponse<bool>(ResponseType.Error, "Unable to update the account.", false);
            }
        }

        // =====================================================================
        // Telegram sign-in confirmation
        //
        // The credential proves what somebody knows. This proves they are holding the
        // phone the account is linked to — which matters most for the accounts that
        // sign in with a mobile number alone, where "what they know" is a number other
        // people also know.
        // =====================================================================

        private const string VerifyOnLoginSetting = "telegram.verify_on_login";

        /// <summary>How long a prompt stays tappable. Minutes, not hours.</summary>
        private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(3);

        /// <summary>
        /// Re-sends allowed per challenge. A message can genuinely be missed; without a
        /// ceiling this is a way to make somebody's phone buzz all afternoon.
        /// </summary>
        private const int MaxPromptSends = 3;

        /// <summary>
        /// Creates a pending sign-in when this account has to confirm on Telegram, or
        /// null when it does not and the session should be issued directly.
        /// </summary>
        /// <remarks>
        /// Returns null — meaning "let them in" — in three cases, and each one is a
        /// deliberate decision to fail OPEN rather than lock the church out:
        ///
        ///   the setting is off;
        ///   the account has no Telegram linked, so there is no phone to ask;
        ///   the setting could not be read, or the contact lookup threw.
        ///
        /// The alternative is a second factor that bricks every account the moment
        /// Telegram is misconfigured, including the administrator who would have to fix
        /// it. <c>telegram.require_linking</c> is the setting that makes case two rare,
        /// and the Settings screen says to use the two together.
        /// </remarks>
        private async Task<LoginChallenge?> StartTelegramVerificationAsync(
            UserAccount account, RequestContext context, DateTime now)
        {
            try
            {
                if (!await _settings.GetBoolAsync(VerifyOnLoginSetting, false)) return null;

                var contact = await _telegramContacts.GetContactAsync(account.PersonId);

                if (contact is null || contact.OptedOutAt is not null ||
                    !long.TryParse(contact.ChatId, out _))
                {
                    _logger.LogInformation(
                        "Sign-in confirmation is on, but {AccountId} has no usable Telegram link. Allowing.",
                        account.PublicId);

                    return null;
                }

                // Any earlier prompt for this account stops working now. Somebody who
                // starts signing in twice must not leave a live button behind them.
                await _challenges.ExpireOutstandingForAccountAsync(account.Id, now);

                var token = NewChallengeToken();

                var challenge = new LoginChallenge
                {
                    PublicId = Ulid.NewUlid(),
                    UserAccountId = account.Id,
                    TokenHash = Sha256Hex(token),
                    ChatId = contact.ChatId,
                    Status = LoginChallengeStatus.Pending,
                    RequestIp = Sanitize(context.IpAddress, 64),
                    UserAgent = Sanitize(context.UserAgent, 255),
                    CreatedAt = now,
                    ExpiresAt = now.Add(ChallengeLifetime)
                };

                challenge.Id = await _challenges.CreateAsync(challenge);

                // The raw token exists only here and inside the button. It is not
                // returned to the browser: the browser is the thing being verified, and
                // handing it the means to approve itself would make this theatre.
                _pendingTokens[challenge.PublicId] = token;

                return challenge;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Could not start Telegram confirmation for {AccountId}. Allowing the sign-in.",
                    account.PublicId);

                return null;
            }
        }

        /// <summary>
        /// Raw challenge tokens, held only between creating a challenge and sending its
        /// prompt — which is two HTTP requests apart.
        /// </summary>
        /// <remarks>
        /// Static and in-memory, which has one consequence worth stating: behind two
        /// instances, the send may land on the instance that did not create the
        /// challenge. That case is handled by minting a fresh token and re-hashing the
        /// row, so it self-heals rather than failing — see
        /// <see cref="SendVerificationPromptAsync"/>. The dictionary is only ever an
        /// optimisation that keeps the common single-instance path from rewriting a row.
        /// </remarks>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _pendingTokens = new();

        public async Task<ApiResponse<bool>> SendVerificationPromptAsync(
            string challengePublicId, RequestContext context)
        {
            var now = _clock.GetUtcNow().UtcDateTime;

            try
            {
                var challenge = await _challenges.GetByPublicIdAsync(challengePublicId);

                // One vague message for every reason a prompt cannot be sent. This
                // endpoint is anonymous, so a specific answer would let somebody probe
                // which challenge ids exist.
                if (challenge is null ||
                    challenge.Status != LoginChallengeStatus.Pending ||
                    challenge.HasExpired(now))
                {
                    return new ApiResponse<bool>(ResponseType.Warning,
                        "That sign-in is no longer waiting. Start again.", false);
                }

                if (challenge.SendCount >= MaxPromptSends)
                {
                    return new ApiResponse<bool>(ResponseType.Warning,
                        "The confirmation has already been sent several times. Start the sign-in again.", false);
                }

                if (!long.TryParse(challenge.ChatId, out var chatId))
                {
                    return new ApiResponse<bool>(ResponseType.Warning,
                        "This account's Telegram link is not usable. Contact an administrator.", false);
                }

                // Normally the token is the one minted with the challenge. When it is
                // not — another instance handled the login, or this one restarted — a
                // fresh one is minted and the row re-hashed, so the older button (which
                // was never sent) simply stops matching.
                if (!_pendingTokens.TryGetValue(challengePublicId, out var token))
                {
                    token = NewChallengeToken();

                    if (!await _challenges.RehashAsync(challenge.Id, Sha256Hex(token), now))
                    {
                        return new ApiResponse<bool>(ResponseType.Warning,
                            "That sign-in is no longer waiting. Start again.", false);
                    }

                    _pendingTokens[challengePublicId] = token;
                }

                var account = await _accounts.GetByIdAsync(challenge.UserAccountId);

                var body = await _templates.RenderForNameAsync(
                    TelegramTemplates.LoginVerification,
                    account?.FullName,
                    new Dictionary<string, string?>(StringComparer.Ordinal)
                    {
                        ["Minutes"] = ((int)ChallengeLifetime.TotalMinutes).ToString(),
                        // Shown so the person can tell their own sign-in from somebody
                        // else's. Deliberately not dressed up as a location: an IP is
                        // not one, and pretending otherwise would be worse than useless.
                        ["Device"] = ShortDevice(challenge.UserAgent)
                    });

                var sent = await _telegram.SendMessageWithButtonsAsync(
                    chatId,
                    body ?? "Confirm your sign-in.",
                    new[]
                    {
                        ("✅ Yes, this was me", $"lv:a:{token}"),
                        ("🚫 No, this was not me", $"lv:d:{token}")
                    });

                if (!sent)
                {
                    return new ApiResponse<bool>(ResponseType.Warning,
                        "Telegram would not deliver the confirmation. Try again in a moment.", false);
                }

                await _challenges.RecordSendAsync(challenge.Id);

                return new ApiResponse<bool>(ResponseType.Success, "Check Telegram and tap to confirm.", true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not send a sign-in confirmation for challenge {Challenge}", challengePublicId);

                return new ApiResponse<bool>(ResponseType.Error,
                    "The confirmation could not be sent. Try again.", false);
            }
        }

        public async Task<VerificationPollResult> PollVerificationAsync(
            string challengePublicId, RequestContext context)
        {
            var now = _clock.GetUtcNow().UtcDateTime;

            try
            {
                var challenge = await _challenges.GetByPublicIdAsync(challengePublicId);

                if (challenge is null || challenge.HasExpired(now) ||
                    challenge.Status is LoginChallengeStatus.Declined
                                     or LoginChallengeStatus.Expired
                                     or LoginChallengeStatus.Consumed)
                {
                    return Waiting(LoginChallengeOutcome.Failed, 0);
                }

                if (challenge.Status == LoginChallengeStatus.Pending)
                    return Waiting(LoginChallengeOutcome.Waiting, SecondsLeft(challenge, now));

                // APPROVED. Exactly one caller wins this, and only that one gets a
                // session — the conditional UPDATE is what makes a challenge single-use
                // even with two tabs polling at the same moment.
                if (!await _challenges.ConsumeAsync(challenge.Id, now))
                    return Waiting(LoginChallengeOutcome.Failed, 0);

                var account = await _accounts.GetByIdAsync(challenge.UserAccountId);

                // Re-checked here rather than trusted from the login a few minutes ago:
                // an administrator may have disabled the account while the person was
                // reaching for their phone.
                if (account is null || !account.IsActive)
                {
                    await AuditAsync(context, SecurityEventTypes.LoginFailed, false,
                        accountId: challenge.UserAccountId, detail: "Account unusable at confirmation");

                    return Waiting(LoginChallengeOutcome.Failed, 0);
                }

                await _accounts.RecordLoginSuccessAsync(account.Id, account.RowVersion, now);

                var issued = await IssueSessionAsync(account, familyId: null, parentId: null,
                    deviceLabel: null, context, now);

                if (issued is null) return Waiting(LoginChallengeOutcome.Failed, 0);

                await AuditAsync(context, SecurityEventTypes.LoginVerified, true,
                    accountId: account.Id);

                _logger.LogInformation(
                    "Sign-in for {AccountId} confirmed on Telegram", account.PublicId);

                return new VerificationPollResult(
                    new ApiResponse<LoginChallengeStatusDto>(
                        ResponseType.Success, "Signed in successfully",
                        new LoginChallengeStatusDto
                        {
                            Outcome = LoginChallengeOutcome.Approved,
                            ExpiresInSeconds = 0,
                            Session = issued.Payload
                        }),
                    issued);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not poll sign-in confirmation {Challenge}", challengePublicId);
                return Waiting(LoginChallengeOutcome.Failed, 0);
            }
        }

        public async Task<(bool Handled, string Reply)> ResolveVerificationAsync(
            string token, bool approved, long chatId, RequestContext context)
        {
            var now = _clock.GetUtcNow().UtcDateTime;

            try
            {
                var challenge = await _challenges.GetByTokenHashAsync(Sha256Hex(token));

                if (challenge is null)
                    return (false, "That confirmation is no longer valid.");

                // The button must be tapped in the chat it was SENT to. Telegram gives
                // the chat the callback came from, and comparing them is what stops a
                // forwarded message being used to approve somebody else's sign-in.
                if (!string.Equals(challenge.ChatId, chatId.ToString(), StringComparison.Ordinal))
                {
                    _logger.LogWarning(
                        "Sign-in confirmation tapped from chat {ChatId}, which is not the chat it was sent to.",
                        chatId);

                    return (false, "That confirmation was not sent to this chat.");
                }

                if (!approved)
                {
                    await _challenges.DeclineAsync(challenge.Id, now);

                    // Worth an audit row of its own: somebody had this account's
                    // credential and the owner says it was not them.
                    await AuditAsync(context, SecurityEventTypes.LoginVerificationDeclined, false,
                        accountId: challenge.UserAccountId, detail: "Declined on Telegram");

                    _logger.LogWarning(
                        "Sign-in for account {AccountId} was DECLINED on Telegram. The credential is known to somebody else.",
                        challenge.UserAccountId);

                    return (true, "Thank you. That sign-in has been refused. " +
                                  "If it was not you, tell the church office — your password may be known to somebody else.");
                }

                if (challenge.HasExpired(now) || challenge.Status != LoginChallengeStatus.Pending)
                    return (false, "That confirmation has expired. Please sign in again.");

                if (!await _challenges.ApproveAsync(challenge.Id, now))
                    return (false, "That confirmation has expired. Please sign in again.");

                return (true, "✅ Confirmed. You are being signed in.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not resolve a sign-in confirmation from chat {ChatId}", chatId);
                return (false, "Something went wrong. Please sign in again.");
            }
        }

        private static VerificationPollResult Waiting(string outcome, int secondsLeft) =>
            new(new ApiResponse<LoginChallengeStatusDto>(
                    ResponseType.Success, outcome,
                    new LoginChallengeStatusDto { Outcome = outcome, ExpiresInSeconds = secondsLeft }),
                null);

        private static int SecondsLeft(LoginChallenge challenge, DateTime now) =>
            Math.Max(0, (int)(challenge.ExpiresAt - now).TotalSeconds);

        /// <summary>
        /// 32 bytes of CSPRNG output in the alphabet Telegram allows in callback data,
        /// which is capped at 64 bytes total.
        /// </summary>
        private static string NewChallengeToken() =>
            Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24))
                .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        private static string Sha256Hex(string value) =>
            Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

        /// <summary>
        /// A user agent reduced to something a person can recognise. Never shown as a
        /// location — an IP address is not one.
        /// </summary>
        private static string ShortDevice(string? userAgent)
        {
            if (string.IsNullOrWhiteSpace(userAgent)) return "a device";

            var ua = userAgent;

            if (ua.Contains("Android", StringComparison.OrdinalIgnoreCase)) return "an Android phone";
            if (ua.Contains("iPhone", StringComparison.OrdinalIgnoreCase)) return "an iPhone";
            if (ua.Contains("iPad", StringComparison.OrdinalIgnoreCase)) return "an iPad";
            if (ua.Contains("Windows", StringComparison.OrdinalIgnoreCase)) return "a Windows computer";
            if (ua.Contains("Mac", StringComparison.OrdinalIgnoreCase)) return "a Mac";

            return "a device";
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
            AllowsPasswordlessLogin = account.AllowsPasswordlessLogin,

            // Mirrors the refusal in SetPasswordlessLoginAsync, so the screen can grey
            // the control out instead of offering a button that always fails. The
            // server still decides; this only saves the administrator a pointless click.
            CanAllowPasswordlessLogin = !account.IsInRole(RoleCodes.Admin),

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
