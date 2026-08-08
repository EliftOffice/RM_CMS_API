using System.ComponentModel.DataAnnotations;

namespace RM_CMS.Data.DTO.Auth
{
    // ------------------------------------------------------------------
    // Requests
    //
    // These are deliberately narrow: only the fields a caller is allowed to
    // set. Domain entities (AuthUser) are never model-bound, which is what
    // prevents mass assignment of is_active / roles / row_version.
    // ------------------------------------------------------------------

    public sealed class LoginRequest
    {
        [Required(ErrorMessage = "Username is required.")]
        [StringLength(100, MinimumLength = 3)]
        public string Username { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password is required.")]
        [StringLength(128, MinimumLength = 1)]
        public string Password { get; set; } = string.Empty;

        /// <summary>Optional friendly device name, shown in the user's session list.</summary>
        [StringLength(120)]
        public string? DeviceLabel { get; set; }
    }

    /// <summary>
    /// Body is optional — the refresh token normally arrives in the HttpOnly cookie.
    /// The body field exists only for non-browser clients that cannot hold cookies.
    /// </summary>
    public sealed class RefreshTokenRequest
    {
        [StringLength(200)]
        public string? RefreshToken { get; set; }
    }

    public sealed class ChangePasswordRequest
    {
        [Required] [StringLength(128)]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required] [StringLength(128, MinimumLength = 12)]
        public string NewPassword { get; set; } = string.Empty;

        [Required]
        [Compare(nameof(NewPassword), ErrorMessage = "New password and confirmation do not match.")]
        [StringLength(128)]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public sealed class CreateAuthUserRequest
    {
        [Required] [StringLength(100, MinimumLength = 3)]
        public string Username { get; set; } = string.Empty;

        [Required] [StringLength(150, MinimumLength = 2)]
        public string DisplayName { get; set; } = string.Empty;

        [Required] [StringLength(30, MinimumLength = 6)]
        [RegularExpression(@"^[0-9+\-\s()]{6,30}$", ErrorMessage = "Mobile number contains invalid characters.")]
        public string MobileNumber { get; set; } = string.Empty;

        [EmailAddress] [StringLength(150)]
        public string? Email { get; set; }

        /// <summary>When omitted, a compliant random password is generated and returned once.</summary>
        [StringLength(128, MinimumLength = 12)]
        public string? InitialPassword { get; set; }

        [Required] [MinLength(1, ErrorMessage = "At least one role is required.")]
        public List<string> Roles { get; set; } = new();

        [StringLength(20)] public string? VolunteerId { get; set; }
        [StringLength(20)] public string? TeamLeadId { get; set; }
    }

    public sealed class SetPasswordRequest
    {
        /// <summary>When omitted, a compliant random password is generated and returned once.</summary>
        [StringLength(128, MinimumLength = 12)]
        public string? NewPassword { get; set; }

        /// <summary>Force the user to choose their own password at next login. Default true.</summary>
        public bool MustChangePassword { get; set; } = true;
    }

    public sealed class UpdateRolesRequest
    {
        [Required] [MinLength(1, ErrorMessage = "At least one role is required.")]
        public List<string> Roles { get; set; } = new();
    }

    public sealed class SetAccountStatusRequest
    {
        [Required] public bool IsActive { get; set; }

        [StringLength(200)] public string? Reason { get; set; }
    }

    // ------------------------------------------------------------------
    // Responses
    // ------------------------------------------------------------------

    /// <summary>
    /// Returned by login and refresh. The refresh token is intentionally NOT in this
    /// payload for browser clients — it is delivered as an HttpOnly cookie so that
    /// XSS cannot read it. <see cref="RefreshToken"/> is populated only for clients
    /// that explicitly opt out of cookies.
    /// </summary>
    public sealed class AuthResultDto
    {
        public string AccessToken { get; set; } = string.Empty;
        public string TokenType { get; set; } = "Bearer";
        public int ExpiresInSeconds { get; set; }
        public DateTime ExpiresAtUtc { get; set; }

        public string? RefreshToken { get; set; }

        public bool MustChangePassword { get; set; }
        public AuthUserDto User { get; set; } = new();
    }

    /// <summary>Safe projection of an account. Never includes hash, stamp or row version.</summary>
    public sealed class AuthUserDto
    {
        public string UserId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string MobileNumber { get; set; } = string.Empty;
        public string? Email { get; set; }
        public List<string> Roles { get; set; } = new();
        public string? VolunteerId { get; set; }
        public string? TeamLeadId { get; set; }
        public bool IsActive { get; set; }
        public bool MustChangePassword { get; set; }
        public DateTime? LastLoginUtc { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>Returned once, at creation or admin reset. Never persisted or logged.</summary>
    public sealed class CreatedCredentialDto
    {
        public AuthUserDto User { get; set; } = new();

        /// <summary>
        /// Present only when the server generated the password. Shown to the administrator
        /// a single time — it cannot be retrieved again.
        /// </summary>
        public string? GeneratedPassword { get; set; }
    }

    /// <summary>One active refresh-token family — i.e. one signed-in device.</summary>
    public sealed class ActiveSessionDto
    {
        public string FamilyId { get; set; } = string.Empty;
        public string? DeviceLabel { get; set; }
        public string? CreatedIp { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime ExpiresUtc { get; set; }
        public bool IsCurrent { get; set; }
    }
}
