namespace RM_CMS.Modules.Identity.Domain
{
    /// <summary>
    /// Role codes. These mirror the seed rows in <c>app_role</c> exactly — the
    /// database is the source of truth for which roles exist, this class is the
    /// compile-time handle for the ones the code branches on.
    ///
    /// Every account is STAFF. A visitor or member is a <c>person</c> row and never
    /// has a login, so there is no "member" role.
    /// </summary>
    public static class RoleCodes
    {
        public const string Admin = "ADMIN";
        public const string Pastor = "PASTOR";
        public const string TeamLead = "TEAM_LEAD";
        public const string Volunteer = "VOLUNTEER";

        /// <summary>Records visitors at intake. No case or volunteer access.</summary>
        public const string DataEntry = "DATA_ENTRY";

        /// <summary>
        /// Reviews what the public website collects. A side role like DataEntry, not a
        /// rung on the pastoral ladder: someone working the enquiry list is not partway
        /// to being a pastor.
        /// </summary>
        public const string WebCoordinator = "WEB_COORDINATOR";

        public static readonly string[] All =
        {
            Admin, Pastor, TeamLead, Volunteer, DataEntry, WebCoordinator
        };

        public static bool IsKnown(string? code) =>
            !string.IsNullOrWhiteSpace(code) && All.Contains(code, StringComparer.Ordinal);
    }

    /// <summary>
    /// Authorization policy names. Endpoints reference a policy, never a role list,
    /// so the role-to-capability mapping lives in exactly one place.
    /// </summary>
    public static class PolicyNames
    {
        /// <summary>Any authenticated, active account.</summary>
        public const string Authenticated = "Authenticated";

        public const string AdminOnly = "AdminOnly";
        public const string PastorOrAdmin = "PastorOrAdmin";
        public const string TeamLeadOrAbove = "TeamLeadOrAbove";
        public const string VolunteerOrAbove = "VolunteerOrAbove";

        /// <summary>Intake: data-entry operators, and anyone above them.</summary>
        public const string CanRecordVisitors = "CanRecordVisitors";

        /// <summary>
        /// Reads and triages website enquiries. Deliberately narrow — the website
        /// coordinator and administrators only. This list is unfiltered public input,
        /// so widening it widens who sees whatever the internet typed.
        /// </summary>
        public const string CanReviewWebEnquiries = "CanReviewWebEnquiries";

        /// <summary>Scheduled jobs: an Admin token or the scheduler's service key.</summary>
        public const string JobRunner = "JobRunner";
    }

    /// <summary>
    /// Claims carried in the access token.
    ///
    /// <c>MapInboundClaims</c> is disabled, so what is issued is exactly what is
    /// read — no WS-Federation URI rewriting.
    ///
    /// Note that only PUBLIC identifiers appear here. A token never carries an
    /// internal database id.
    /// </summary>
    public static class ClaimNames
    {
        /// <summary>The user account's public ULID.</summary>
        public const string Subject = "sub";

        public const string TokenId = "jti";
        public const string DisplayName = "name";
        public const string Role = "role";

        /// <summary>Rotates on any credential or role change; re-checked per request.</summary>
        public const string SecurityStamp = "sstamp";
        public const string TokenVersion = "tver";

        /// <summary>Public ULID of the person this account belongs to.</summary>
        public const string PersonId = "pid";

        /// <summary>Public ULID of the volunteer record, when the account has one.</summary>
        public const string VolunteerId = "vid";

        /// <summary>Public ULID of the team the account leads or belongs to.</summary>
        public const string TeamId = "tid";

        /// <summary>
        /// Public ULID of the campus this account is scoped to. Organisation-wide
        /// accounts omit it. This is the tenancy boundary.
        /// </summary>
        /// <summary>
        /// The tenancy SCOPE. Absent means organisation-wide, so it is omitted for an
        /// account holding an org-wide Admin or Pastor grant.
        /// </summary>
        public const string CampusId = "cid";

        /// <summary>
        /// The caller's HOME campus — the one their own person record sits at. Always
        /// present, and never a permission: it is only the default campus for records
        /// they create. Scope and default are different questions, and collapsing them
        /// into one claim is what made an administrator accidentally site-locked.
        /// </summary>
        public const string HomeCampusId = "hcid";

        public const string MustChangePassword = "pwd_reset";
    }

    /// <summary>Values written to <c>security_event.event_type</c>.</summary>
    public static class SecurityEventTypes
    {
        public const string LoginSucceeded = "LOGIN_SUCCEEDED";
        public const string LoginFailed = "LOGIN_FAILED";
        public const string LoginLockedOut = "LOGIN_LOCKED_OUT";
        public const string TokenRefreshed = "TOKEN_REFRESHED";
        public const string RefreshReuseDetected = "REFRESH_REUSE_DETECTED";
        public const string RefreshFailed = "REFRESH_FAILED";
        public const string Logout = "LOGOUT";
        public const string LogoutAll = "LOGOUT_ALL";
        public const string PasswordChanged = "PASSWORD_CHANGED";
        public const string PasswordSetByAdmin = "PASSWORD_SET_BY_ADMIN";
        public const string RolesChanged = "ROLES_CHANGED";
        public const string AccountCreated = "ACCOUNT_CREATED";
        public const string AccountEnabled = "ACCOUNT_ENABLED";
        public const string AccountDisabled = "ACCOUNT_DISABLED";
        public const string AuthorizationDenied = "AUTHORIZATION_DENIED";
    }

    /// <summary>Values written to <c>refresh_token.revoked_reason</c>.</summary>
    public static class RevocationReasons
    {
        public const string Rotated = "ROTATED";
        public const string Logout = "LOGOUT";
        public const string LogoutAll = "LOGOUT_ALL";
        public const string ReuseDetected = "REUSE_DETECTED";
        public const string PasswordChanged = "PASSWORD_CHANGED";
        public const string AccountDisabled = "ACCOUNT_DISABLED";
        public const string RolesChanged = "ROLES_CHANGED";
        public const string SessionLimit = "SESSION_LIMIT";
    }
}
