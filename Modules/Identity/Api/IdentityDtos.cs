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

        [Required(ErrorMessage = "Password is required.")]
        [StringLength(128, MinimumLength = 1)]
        public string Password { get; set; } = string.Empty;

        [StringLength(120)]
        public string? DeviceLabel { get; set; }
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

    public sealed class PagedResult<T>
    {
        public IReadOnlyList<T> Items { get; set; } = Array.Empty<T>();
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
        public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    }
}
