namespace RM_CMS.Modules.Identity.Domain
{
    /// <summary>
    /// A signed-in account, joined with the person it belongs to.
    ///
    /// The MVP stored a person's name, email and phone on the account row itself,
    /// duplicating them across five tables. Here <c>person</c> owns identity and
    /// <c>user_account</c> owns credentials; this type is the read model that joins
    /// the two for authentication.
    ///
    /// Never returned from a controller — map to a DTO first.
    /// </summary>
    public sealed class UserAccount
    {
        // ---- user_account ----
        /// <summary>Internal key. Never leaves the server.</summary>
        public long Id { get; set; }

        /// <summary>Public ULID. This is what the token and the API expose.</summary>
        public string PublicId { get; set; } = string.Empty;

        public string Username { get; set; } = string.Empty;
        public string NormalizedUsername { get; set; } = string.Empty;

        /// <summary>Empty means "no usable password" — provisioned but not activated.</summary>
        public string PasswordHash { get; set; } = string.Empty;

        public string SecurityStamp { get; set; } = string.Empty;
        public int TokenVersion { get; set; }

        public bool IsActive { get; set; }
        public bool MustChangePassword { get; set; }

        /// <summary>
        /// This account signs in with its mobile number and nothing else.
        /// </summary>
        /// <remarks>
        /// Granted per account by an administrator, for people who cannot read a
        /// password prompt. What it costs is not subtle: for these accounts the mobile
        /// number IS the credential, and mobile numbers are on posters and in group
        /// chats. It is a deliberate trade of security for access, and it is never the
        /// default — see <c>009_passwordless_login.sql</c>.
        ///
        /// Refused for administrators. An administrator can grant this to everyone
        /// else, so a passwordless one puts the whole system one known number away.
        /// </remarks>
        public bool AllowsPasswordlessLogin { get; set; }
        public int FailedAccessCount { get; set; }
        public DateTime? LockoutEndsAt { get; set; }
        public DateTime? LastLoginAt { get; set; }
        public DateTime? PasswordChangedAt { get; set; }

        public int RowVersion { get; set; }
        public DateTime CreatedAt { get; set; }

        // ---- person (joined) ----
        public long PersonId { get; set; }
        public string PersonPublicId { get; set; } = string.Empty;
        public string GivenName { get; set; } = string.Empty;
        public string? FamilyName { get; set; }

        /// <summary>Generated column on `person`.</summary>
        public string FullName { get; set; } = string.Empty;

        public long? CampusId { get; set; }
        public string? CampusPublicId { get; set; }

        // ---- related records (joined, optional) ----
        /// <summary>Set when this account also serves as a volunteer.</summary>
        public string? VolunteerPublicId { get; set; }

        /// <summary>The team this account leads, when it leads one.</summary>
        public string? TeamPublicId { get; set; }

        // ---- populated separately from user_role ----
        public List<UserRoleAssignment> Roles { get; set; } = new();

        public bool HasUsablePassword => !string.IsNullOrEmpty(PasswordHash);

        public bool IsLockedOut(DateTime utcNow) =>
            LockoutEndsAt.HasValue && LockoutEndsAt.Value > utcNow;

        public IEnumerable<string> RoleCodes => Roles.Select(r => r.RoleCode);

        public bool IsInRole(string roleCode) =>
            Roles.Any(r => string.Equals(r.RoleCode, roleCode, StringComparison.Ordinal));
    }

    /// <summary>
    /// One role grant. <see cref="CampusPublicId"/> is null for an organisation-wide
    /// grant, so a team lead can administer one campus without gaining rights over
    /// another.
    /// </summary>
    public sealed class UserRoleAssignment
    {
        public string RoleCode { get; set; } = string.Empty;
        public long? CampusId { get; set; }
        public string? CampusPublicId { get; set; }
        public DateTime GrantedAt { get; set; }
    }

    /// <summary>
    /// A row in <c>refresh_token</c>. Only the SHA-256 hash is persisted, so a
    /// database dump yields no usable tokens.
    /// </summary>
    public sealed class RefreshTokenRecord
    {
        public long Id { get; set; }
        public string PublicId { get; set; } = string.Empty;
        public long UserAccountId { get; set; }

        /// <summary>
        /// Groups one login's rotation chain. Presenting a spent token revokes the
        /// entire family — that is what makes theft detectable.
        /// </summary>
        public string FamilyId { get; set; } = string.Empty;

        public string TokenHash { get; set; } = string.Empty;
        public long? ParentId { get; set; }
        public long? ReplacedById { get; set; }

        public DateTime IssuedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public DateTime? RevokedAt { get; set; }
        public string? RevokedReason { get; set; }

        public string? DeviceLabel { get; set; }
        public string? UserAgentHash { get; set; }

        /// <summary>Read back through INET6_NTOA; stored as VARBINARY(16).</summary>
        public string? IssuedIp { get; set; }

        public bool IsRevoked => RevokedAt.HasValue;
        public bool IsExpired(DateTime utcNow) => ExpiresAt <= utcNow;
        public bool IsActive(DateTime utcNow) => !IsRevoked && !IsExpired(utcNow);
    }

    /// <summary>
    /// An entry for <c>security_event</c>. Outcomes only — never a password, token,
    /// hash or secret.
    /// </summary>
    public sealed class SecurityEventEntry
    {
        public long? UserAccountId { get; set; }
        public string? UsernameAttempted { get; set; }
        public string EventType { get; set; } = string.Empty;
        public bool Succeeded { get; set; }
        public string? Detail { get; set; }
        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }
        public string? CorrelationId { get; set; }
    }

    /// <summary>One active refresh-token family — i.e. one signed-in device.</summary>
    public sealed class ActiveSession
    {
        public string FamilyId { get; set; } = string.Empty;
        public string? DeviceLabel { get; set; }
        public string? IssuedIp { get; set; }
        public DateTime IssuedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
    }
}
