using System.ComponentModel.DataAnnotations;

namespace RM_CMS.Modules.Identity.Api
{
    // -------------------------------------------------------------------------
    // Requests
    // -------------------------------------------------------------------------

    /// <summary>
    /// Promotes an existing person up the ladder.
    ///
    /// Only the person's public id and the target role are required — everything the
    /// person already has (their record, contacts, cases, follow-ups, nurture history)
    /// is left exactly as it is. Nothing here can create a second person.
    /// </summary>
    public sealed class PromoteRequest
    {
        [Required][StringLength(30)]
        public string TargetRole { get; set; } = string.Empty;

        /// <summary>Scopes the granted role to one campus. Null grants it organisation-wide.</summary>
        [StringLength(26)]
        public string? CampusId { get; set; }

        /// <summary>
        /// Required when promoting to VOLUNTEER and the person has no volunteer record
        /// yet — a volunteer without a capacity band cannot be assigned work.
        /// </summary>
        [StringLength(20)]
        public string? CapacityBandCode { get; set; }

        [StringLength(26)]
        public string? TeamId { get; set; }

        /// <summary>
        /// When promoting to TEAM_LEAD, puts them in charge of this team
        /// (<c>team.lead_user_id</c>). Optional — a team can be assigned later.
        /// </summary>
        [StringLength(26)]
        public string? LeadsTeamId { get; set; }

        /// <summary>
        /// Volunteers may work without system access. Every other role is meaningless
        /// without a login, so this is forced true for them.
        /// </summary>
        public bool GrantSystemAccess { get; set; } = true;

        /// <summary>Omit to have a compliant password generated and returned once.</summary>
        [StringLength(128, MinimumLength = 12)]
        public string? InitialPassword { get; set; }
    }

    /// <summary>
    /// Creates a user directly, without the person having to exist as a visitor first.
    ///
    /// <see cref="PersonId"/> attaches to somebody already on file; leave it null and
    /// supply the name and mobile to record a new person at the same time.
    /// </summary>
    public sealed class CreateUserRequest
    {
        /// <summary>Existing person to attach to. Null creates one.</summary>
        [StringLength(26)]
        public string? PersonId { get; set; }

        [StringLength(80)] public string? GivenName { get; set; }
        [StringLength(80)] public string? FamilyName { get; set; }

        /// <summary>
        /// Also becomes the username, by convention: every account in this system signs
        /// in with a 10-digit mobile number, and the sign-in screen enforces that shape.
        /// </summary>
        [StringLength(20)] public string? Mobile { get; set; }

        [StringLength(255)] public string? Email { get; set; }

        [Required][StringLength(30)]
        public string RoleCode { get; set; } = string.Empty;

        [StringLength(26)] public string? CampusId { get; set; }
        [StringLength(20)] public string? CapacityBandCode { get; set; }
        [StringLength(26)] public string? TeamId { get; set; }
        [StringLength(26)] public string? LeadsTeamId { get; set; }

        public bool GrantSystemAccess { get; set; } = true;

        [StringLength(128, MinimumLength = 12)]
        public string? InitialPassword { get; set; }

        /// <summary>Force a password change at first sign-in.</summary>
        public bool MustChangePassword { get; set; } = true;
    }

    /// <summary>Replaces the whole role set — the "Change role" action.</summary>
    public sealed class ChangeRolesRequest
    {
        [Required][MinLength(1, ErrorMessage = "At least one role is required.")]
        public List<RoleGrantRequest> Roles { get; set; } = new();
    }

    // -------------------------------------------------------------------------
    // Responses
    // -------------------------------------------------------------------------

    /// <summary>
    /// One person's standing in the system: who they are, whether they can sign in,
    /// what authority they hold, and whether they carry cases.
    /// </summary>
    public sealed class DirectoryUserDto
    {
        public string PersonId { get; set; } = string.Empty;
        public string? PersonReferenceCode { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string LifecycleStatus { get; set; } = string.Empty;
        public string? Mobile { get; set; }
        public string? Email { get; set; }
        public string? CampusId { get; set; }
        public string? CampusName { get; set; }

        /// <summary>Null when this person has no login.</summary>
        public string? AccountId { get; set; }
        public string? Username { get; set; }
        public bool HasAccount { get; set; }
        public bool IsActive { get; set; }
        public bool MustChangePassword { get; set; }
        public DateTime? LastLoginAt { get; set; }
        public int AccountRowVersion { get; set; }

        public List<string> Roles { get; set; } = new();

        /// <summary>The highest rung they hold, for display and for promotion checks.</summary>
        public string? HighestRole { get; set; }

        /// <summary>What they could be promoted to next. Empty when already at the top.</summary>
        public List<string> PromotableTo { get; set; } = new();

        // ---- volunteer record, when they hold one ----
        public bool IsVolunteer { get; set; }
        public string? VolunteerId { get; set; }
        public string? VolunteerReferenceCode { get; set; }
        public string? VolunteerStatus { get; set; }
        public string? CapacityBandCode { get; set; }
        public int? CurrentCaseLoad { get; set; }
        public string? TeamName { get; set; }

        public DateTime CreatedAt { get; set; }
    }

    /// <summary>Detail view: the row plus role history.</summary>
    public sealed class DirectoryUserDetailDto
    {
        public DirectoryUserDto User { get; set; } = new();
        public List<RoleHistoryDto> RoleHistory { get; set; } = new();
    }

    public sealed class RoleHistoryDto
    {
        public string RoleCode { get; set; } = string.Empty;
        public string? CampusName { get; set; }
        public DateTime GrantedAt { get; set; }
        public string? GrantedBy { get; set; }
    }

    /// <summary>
    /// What a promotion or direct creation actually did. Spelled out rather than
    /// returning a bare success, because "promoted" can mean several different
    /// combinations of record changes and an administrator should see which.
    /// </summary>
    public sealed class UserChangeResultDto
    {
        public DirectoryUserDto User { get; set; } = new();

        public bool PersonCreated { get; set; }
        public bool AccountCreated { get; set; }
        public bool VolunteerRecordCreated { get; set; }
        public bool RoleGranted { get; set; }
        public bool TeamLeadAssigned { get; set; }

        /// <summary>Returned once, when the server generated the password.</summary>
        public string? GeneratedPassword { get; set; }

        public List<string> Notes { get; set; } = new();
    }
}
