namespace RM_CMS.Security
{
    /// <summary>
    /// The fixed set of application roles. Mirrors the seed rows in
    /// <c>Database/SQL_Scripts/auth_schema.sql</c> (table <c>auth_roles</c>).
    /// </summary>
    public static class Roles
    {
        public const string Admin = "Admin";
        public const string Pastor = "Pastor";
        public const string TeamLead = "TeamLead";
        public const string Volunteer = "Volunteer";
        public const string Member = "Member";

        public static readonly string[] All = { Admin, Pastor, TeamLead, Volunteer, Member };

        public static bool IsKnown(string? role) =>
            !string.IsNullOrWhiteSpace(role) &&
            All.Contains(role, StringComparer.Ordinal);
    }

    /// <summary>
    /// Authorization policy names. Every protected endpoint references one of these
    /// rather than listing roles inline, so the role-to-capability mapping lives in
    /// exactly one place (<see cref="AuthorizationPolicies"/> registration in Program.cs).
    /// </summary>
    public static class Policies
    {
        /// <summary>Any authenticated, active account.</summary>
        public const string Authenticated = "Authenticated";

        /// <summary>Administrators only — configuration, user management, jobs.</summary>
        public const string AdminOnly = "AdminOnly";

        /// <summary>Pastors and admins — cross-team oversight.</summary>
        public const string PastorOrAdmin = "PastorOrAdmin";

        /// <summary>Team leads and above — escalations, check-ins, nurture review.</summary>
        public const string TeamLeadOrAbove = "TeamLeadOrAbove";

        /// <summary>Volunteers and above — day-to-day follow-up work.</summary>
        public const string VolunteerOrAbove = "VolunteerOrAbove";

        /// <summary>Any signed-in user including plain members — events, own attendance.</summary>
        public const string MemberOrAbove = "MemberOrAbove";

        /// <summary>
        /// Scheduled-job endpoints: satisfied by an Admin JWT or the scheduler's
        /// <c>X-Service-Key</c> header.
        /// </summary>
        public const string JobRunner = "JobRunner";
    }

    /// <summary>
    /// Custom claim types carried in the access token. Kept as short, stable strings
    /// because <c>MapInboundClaims</c> is disabled — what is issued is what is read.
    /// </summary>
    public static class AppClaimTypes
    {
        public const string UserId = "sub";
        public const string TokenId = "jti";
        public const string DisplayName = "name";
        public const string Role = "role";
        public const string SecurityStamp = "sstamp";
        public const string TokenVersion = "tver";
        public const string VolunteerId = "vol_id";
        public const string TeamLeadId = "tl_id";
        public const string MustChangePassword = "pwd_reset";
    }
}
