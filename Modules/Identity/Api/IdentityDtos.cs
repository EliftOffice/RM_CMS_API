using System.ComponentModel.DataAnnotations;

namespace RM_CMS.Modules.Identity.Api
{
    // -------------------------------------------------------------------------
    // Requests
    //
    // Deliberately narrow: only fields a caller may set. Domain models are never
    // model-bound, which is what prevents mass assignment of is_active, roles or
    // row_version.
    // -------------------------------------------------------------------------

    public sealed class LoginRequest
    {
        [Required(ErrorMessage = "Username is required.")]
        [StringLength(100, MinimumLength = 3)]
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// Omitted by accounts an administrator has allowed to sign in with their
        /// mobile number alone.
        /// </summary>
        /// <remarks>
        /// No longer <c>[Required]</c>. The check moved into the service, which is the
        /// only place that knows whether THIS account is allowed to omit it — a
        /// model-level Required would reject a passwordless sign-in before anything
        /// had looked the account up. An account without the grant that sends no
        /// password still fails, with the same generic message as a wrong one.
        /// </remarks>
        [StringLength(128)]
        public string? Password { get; set; }

        [StringLength(120)]
        public string? DeviceLabel { get; set; }
    }

    /// <summary>
    /// Asks what a given mobile number needs in order to sign in, so the login screen
    /// can show a password box only to accounts that have one.
    /// </summary>
    public sealed class LoginMethodRequest
    {
        [Required(ErrorMessage = "Enter your mobile number.")]
        [StringLength(100, MinimumLength = 3)]
        public string Username { get; set; } = string.Empty;
    }

    /// <summary>
    /// The answer to that question, and nothing else.
    /// </summary>
    /// <remarks>
    /// Deliberately holds no name, no role and no "account exists" flag. An unknown
    /// number gets <c>RequiresPassword = true</c>, exactly like an ordinary account,
    /// so this endpoint cannot be used to test whether somebody has an account here —
    /// the caller has to go on and fail a real sign-in, which is rate-limited and
    /// audited.
    ///
    /// It does still reveal which numbers are passwordless, and that cannot be
    /// designed away: telling the browser to skip the password box IS the answer. That
    /// is a cost of the feature, not of this shape.
    /// </remarks>
    public sealed class LoginMethodDto
    {
        public bool RequiresPassword { get; set; } = true;
    }

    /// <summary>Grants or revokes mobile-number-only sign-in for one account.</summary>
    public sealed class SetPasswordlessLoginRequest
    {
        [Required] public bool Allowed { get; set; }

        [StringLength(200)] public string? Reason { get; set; }
    }

    /// <summary>
    /// The refresh token normally arrives in the HttpOnly cookie. This body field
    /// exists only for non-browser clients that cannot hold cookies.
    /// </summary>
    public sealed class RefreshRequest
    {
        [StringLength(200)]
        public string? RefreshToken { get; set; }
    }

    public sealed class ChangePasswordRequest
    {
        [Required][StringLength(128)]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required][StringLength(128, MinimumLength = 12)]
        public string NewPassword { get; set; } = string.Empty;

        [Required]
        [Compare(nameof(NewPassword), ErrorMessage = "New password and confirmation do not match.")]
        [StringLength(128)]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    /// <summary>
    /// Grants access to a person who already exists.
    ///
    /// Accounts attach to people; they do not create them. That separation is what
    /// removed the five-way duplication of names and phone numbers in the MVP, so
    /// the caller supplies a person's public id rather than their details.
    /// </summary>
    public sealed class CreateAccountRequest
    {
        [Required][StringLength(26, MinimumLength = 26, ErrorMessage = "A valid person id is required.")]
        public string PersonId { get; set; } = string.Empty;

        [Required][StringLength(100, MinimumLength = 3)]
        public string Username { get; set; } = string.Empty;

        /// <summary>Omit to have a compliant password generated and returned once.</summary>
        [StringLength(128, MinimumLength = 12)]
        public string? InitialPassword { get; set; }

        /// <summary>
        /// Whether the initial password is a one-time credential. Defaults to true:
        /// an account whose password the administrator knows is the unsafe case, so
        /// it has to be asked for rather than fallen into.
        /// </summary>
        public bool MustChangePassword { get; set; } = true;

        [Required][MinLength(1, ErrorMessage = "At least one role is required.")]
        public List<RoleGrantRequest> Roles { get; set; } = new();
    }

    public sealed class RoleGrantRequest
    {
        [Required][StringLength(30)]
        public string RoleCode { get; set; } = string.Empty;

        /// <summary>Null grants the role organisation-wide.</summary>
        [StringLength(26)]
        public string? CampusId { get; set; }
    }

    public sealed class SetPasswordRequest
    {
        /// <summary>Omit to have a compliant password generated and returned once.</summary>
        [StringLength(128, MinimumLength = 12)]
        public string? NewPassword { get; set; }

        public bool MustChangePassword { get; set; } = true;
    }

    public sealed class UpdateRolesRequest
    {
        [Required][MinLength(1, ErrorMessage = "At least one role is required.")]
        public List<RoleGrantRequest> Roles { get; set; } = new();
    }

    public sealed class SetAccountStatusRequest
    {
        [Required] public bool IsActive { get; set; }

        [StringLength(200)] public string? Reason { get; set; }
    }

    // -------------------------------------------------------------------------
    // Responses
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returned by login and refresh.
    ///
    /// <see cref="RefreshToken"/> stays null for browser clients — the refresh token
    /// is delivered as an HttpOnly cookie so that page script cannot read it.
    /// </summary>
    public sealed class AuthResultDto
    {
        public string AccessToken { get; set; } = string.Empty;
        public string TokenType { get; set; } = "Bearer";
        public int ExpiresInSeconds { get; set; }
        public DateTime ExpiresAtUtc { get; set; }

        public string? RefreshToken { get; set; }

        public bool MustChangePassword { get; set; }
        public AccountDto Account { get; set; } = new();

        /// <summary>
        /// True when the credential was correct but the sign-in is not finished: the
        /// person still has to confirm it on Telegram.
        /// </summary>
        /// <remarks>
        /// When this is set there is NO access token and no refresh cookie — nothing
        /// that could be used as a session. <see cref="ChallengeId"/> names the pending
        /// sign-in, and the screen exchanges it for a session once the tap arrives.
        ///
        /// Carried on this DTO rather than reported as a failure because it is not one:
        /// a client that stopped here would be abandoning a sign-in that is going
        /// perfectly well.
        /// </remarks>
        public bool RequiresTelegramVerification { get; set; }

        /// <summary>The pending sign-in to poll. Null unless verification is required.</summary>
        public string? ChallengeId { get; set; }
    }

    /// <summary>Names a pending sign-in awaiting its Telegram tap.</summary>
    public sealed class LoginChallengeRequest
    {
        [Required]
        [StringLength(26, MinimumLength = 26)]
        public string ChallengeId { get; set; } = string.Empty;
    }

    /// <summary>What the waiting screen is told.</summary>
    public sealed class LoginChallengeStatusDto
    {
        /// <summary>WAITING, APPROVED or FAILED.</summary>
        public string Outcome { get; set; } = string.Empty;

        /// <summary>Seconds left before the prompt stops working. Zero once it has.</summary>
        public int ExpiresInSeconds { get; set; }

        /// <summary>
        /// Present only on APPROVED, and only on the ONE poll that won the race to
        /// consume the challenge. This is the session.
        /// </summary>
        public AuthResultDto? Session { get; set; }
    }

    /// <summary>
    /// Safe projection of an account. Carries public ids only — never an internal
    /// key, a password hash, a security stamp or a row version.
    /// </summary>
    public sealed class AccountDto
    {
        public string Id { get; set; } = string.Empty;          // account public ULID
        public string PersonId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string? CampusId { get; set; }
        public string? VolunteerId { get; set; }
        public string? TeamId { get; set; }
        public List<RoleGrantDto> Roles { get; set; } = new();
        public bool IsActive { get; set; }
        public bool MustChangePassword { get; set; }

        /// <summary>Signs in with a mobile number alone. Shown as a warning badge.</summary>
        public bool AllowsPasswordlessLogin { get; set; }

        /// <summary>
        /// False for an account the server will not let become passwordless, so the
        /// screen can disable the control rather than offer a button that always
        /// fails. Administrators are the only case today.
        /// </summary>
        public bool CanAllowPasswordlessLogin { get; set; }

        public DateTime? LastLoginAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public sealed class RoleGrantDto
    {
        public string RoleCode { get; set; } = string.Empty;
        public string? CampusId { get; set; }
    }

    /// <summary>Returned once, at creation or admin reset. Never stored or logged.</summary>
    public sealed class CreatedCredentialDto
    {
        public AccountDto Account { get; set; } = new();

        /// <summary>
        /// Present only when the server generated the password. Shown to the
        /// administrator a single time; it cannot be retrieved again.
        /// </summary>
        public string? GeneratedPassword { get; set; }
    }

    /// <summary>One signed-in device.</summary>
    public sealed class SessionDto
    {
        public string SessionId { get; set; } = string.Empty;   // the token family
        public string? DeviceLabel { get; set; }
        public string? IpAddress { get; set; }
        public DateTime SignedInAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public bool IsCurrent { get; set; }
    }

    /// <summary>
    /// The password rules, for screens that set or change a password.
    ///
    /// Carries both the raw flags (for client-side checking as the user types) and a
    /// ready-made <see cref="Rules"/> list (for display), so a screen does not have to
    /// re-derive the wording and get it subtly different from the server's.
    /// </summary>
    public sealed class PasswordPolicyDto
    {
        public int MinLength { get; set; }
        public int MaxLength { get; set; }
        public bool RequireUppercase { get; set; }
        public bool RequireLowercase { get; set; }
        public bool RequireDigit { get; set; }
        public bool RequireNonAlphanumeric { get; set; }

        /// <summary>How many previous passwords are blocked. 0 means no history check.</summary>
        public int HistoryCount { get; set; }

        public List<string> Rules { get; set; } = new();
    }

    public sealed class PagedResult<T>
    {
        public IReadOnlyList<T> Items { get; set; } = Array.Empty<T>();
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
        public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    }
}
