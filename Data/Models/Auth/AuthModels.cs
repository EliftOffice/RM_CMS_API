namespace RM_CMS.Data.Models.Auth
{
    /// <summary>
    /// Internal representation of a row in <c>auth_users</c>.
    /// Never returned from a controller — map to a DTO first (mass-assignment safety).
    /// </summary>
    public sealed class AuthUser
    {
        public string UserId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string NormalizedUsername { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string MobileNumber { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>Empty string means "no usable password" — login must be refused.</summary>
        public string PasswordHash { get; set; } = string.Empty;

        public string SecurityStamp { get; set; } = string.Empty;
        public int TokenVersion { get; set; }

        public bool IsActive { get; set; }
        public bool MustChangePassword { get; set; }
        public int AccessFailedCount { get; set; }
        public DateTime? LockoutEndUtc { get; set; }
        public DateTime? LastLoginUtc { get; set; }
        public DateTime? PasswordChangedUtc { get; set; }

        public string? VolunteerId { get; set; }
        public string? TeamLeadId { get; set; }
        public string? PersonId { get; set; }

        public long RowVersion { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        /// <summary>Populated by the DAL from <c>auth_user_roles</c>; not a column.</summary>
        public List<string> Roles { get; set; } = new();

        public bool HasUsablePassword => !string.IsNullOrEmpty(PasswordHash);

        public bool IsLockedOut(DateTime utcNow) => LockoutEndUtc.HasValue && LockoutEndUtc.Value > utcNow;
    }

    /// <summary>Row in <c>auth_refresh_tokens</c>. Only the hash is ever persisted.</summary>
    public sealed class RefreshTokenRecord
    {
        public string TokenId { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string FamilyId { get; set; } = string.Empty;
        public string TokenHash { get; set; } = string.Empty;
        public string? ParentTokenId { get; set; }
        public string? ReplacedByTokenId { get; set; }

        public DateTime ExpiresUtc { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime? RevokedUtc { get; set; }
        public string? RevokedReason { get; set; }

        public string? DeviceLabel { get; set; }
        public string? UserAgentHash { get; set; }
        public string? CreatedIp { get; set; }

        public bool IsRevoked => RevokedUtc.HasValue;
        public bool IsExpired(DateTime utcNow) => ExpiresUtc <= utcNow;
        public bool IsActive(DateTime utcNow) => !IsRevoked && !IsExpired(utcNow);
    }

    /// <summary>Reasons recorded in <c>auth_refresh_tokens.revoked_reason</c>.</summary>
    public static class RevocationReasons
    {
        public const string Rotated = "Rotated";
        public const string Logout = "Logout";
        public const string LogoutAll = "LogoutAll";
        public const string ReuseDetected = "ReuseDetected";
        public const string PasswordChanged = "PasswordChanged";
        public const string AccountDisabled = "AccountDisabled";
        public const string RolesChanged = "RolesChanged";
        public const string SessionLimit = "SessionLimit";
    }

    /// <summary>Values written to <c>auth_login_audit.event_type</c>.</summary>
    public static class AuthAuditEvents
    {
        public const string LoginSucceeded = "LoginSucceeded";
        public const string LoginFailed = "LoginFailed";
        public const string LoginLockedOut = "LoginLockedOut";
        public const string TokenRefreshed = "TokenRefreshed";
        public const string RefreshReuseDetected = "RefreshReuseDetected";
        public const string RefreshFailed = "RefreshFailed";
        public const string Logout = "Logout";
        public const string LogoutAll = "LogoutAll";
        public const string PasswordChanged = "PasswordChanged";
        public const string PasswordSetByAdmin = "PasswordSetByAdmin";
        public const string RolesChanged = "RolesChanged";
        public const string AccountDisabled = "AccountDisabled";
        public const string AccountEnabled = "AccountEnabled";
        public const string AccountCreated = "AccountCreated";
        public const string AuthorizationDenied = "AuthorizationDenied";
    }
}
