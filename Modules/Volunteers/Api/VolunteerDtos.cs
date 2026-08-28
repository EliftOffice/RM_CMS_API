using System.ComponentModel.DataAnnotations;

namespace RM_CMS.Modules.Volunteers.Api
{
    // -------------------------------------------------------------------------
    // Requests
    // -------------------------------------------------------------------------

    /// <summary>
    /// Enrols an existing person as a volunteer.
    ///
    /// Takes a person id rather than a name and number, for the same reason
    /// accounts do: the person record is the single source of contact details, and
    /// duplicating them here is what the redesign removed.
    /// </summary>
    public sealed class EnrolVolunteerRequest
    {
        [Required][StringLength(26, MinimumLength = 26, ErrorMessage = "A valid person id is required.")]
        public string PersonId { get; set; } = string.Empty;

        [Required][StringLength(20)]
        public string CapacityBandCode { get; set; } = string.Empty;

        /// <summary>Optional at enrolment — a volunteer can be placed on a team later.</summary>
        [StringLength(26)] public string? TeamId { get; set; }

        /// <summary>Defaults to the person's campus when omitted.</summary>
        [StringLength(26)] public string? CampusId { get; set; }

        public DateTime? StartedOn { get; set; }

        [StringLength(20)] public string? ServiceLevel { get; set; }
    }

    public sealed class UpdateVolunteerRequest
    {
        [Required][StringLength(20)]
        public string Status { get; set; } = string.Empty;

        [StringLength(26)] public string? TeamId { get; set; }
        [StringLength(20)] public string? ServiceLevel { get; set; }
        [StringLength(20)] public string? BurnoutRisk { get; set; }

        public DateTime? EndedOn { get; set; }

        [Required] public int RowVersion { get; set; }
    }

    /// <summary>
    /// Moves a volunteer to a different capacity band. Separate from the general
    /// update because every change is recorded in the wellbeing history — knowing a
    /// volunteer was stepped down twice for burnout risk is the point.
    /// </summary>
    public sealed class ChangeCapacityRequest
    {
        [Required][StringLength(20)]
        public string CapacityBandCode { get; set; } = string.Empty;

        [StringLength(40)] public string? Reason { get; set; }
        [StringLength(1000)] public string? Notes { get; set; }

        [Required] public int RowVersion { get; set; }
    }

    /// <summary>
    /// Records safeguarding milestones. Restricted to administrators: these gates
    /// decide who may be handed a crisis disclosure.
    /// </summary>
    public sealed class SafeguardingRequest
    {
        public DateTime? BackgroundCheckedOn { get; set; }
        public DateTime? ConfidentialitySignedOn { get; set; }
        public DateTime? CrisisTrainedOn { get; set; }

        [Required] public int RowVersion { get; set; }
    }

    public sealed class CreateTeamRequest
    {
        [Required][StringLength(120, MinimumLength = 2)]
        public string Name { get; set; } = string.Empty;

        [StringLength(26)] public string? CampusId { get; set; }

        /// <summary>Public id of the lead's user account.</summary>
        [StringLength(26)] public string? LeadAccountId { get; set; }

        [Range(1, 100)] public int MaxMembers { get; set; } = 12;
    }

    public sealed class UpdateTeamRequest
    {
        [Required][StringLength(120, MinimumLength = 2)]
        public string Name { get; set; } = string.Empty;

        [StringLength(26)] public string? LeadAccountId { get; set; }

        [Range(1, 100)] public int MaxMembers { get; set; } = 12;

        public bool IsActive { get; set; } = true;

        [Required] public int RowVersion { get; set; }
    }

    // -------------------------------------------------------------------------
    // Responses
    // -------------------------------------------------------------------------

    public sealed class VolunteerDto
    {
        public string Id { get; set; } = string.Empty;
        public string? ReferenceCode { get; set; }

        public string PersonId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string? PrimaryPhone { get; set; }
        public string? PrimaryEmail { get; set; }

        public string? CampusId { get; set; }
        public string? CampusName { get; set; }
        public string? TeamId { get; set; }
        public string? TeamName { get; set; }

        public string Status { get; set; } = string.Empty;
        public string ServiceLevel { get; set; } = string.Empty;

        public string CapacityBandCode { get; set; } = string.Empty;
        public string? CapacityBandLabel { get; set; }
        public int CapacityMaxPerWeek { get; set; }
        public int CurrentCaseLoad { get; set; }
        public int RemainingCapacity { get; set; }

        public int LifetimeCasesAssigned { get; set; }
        public int LifetimeCasesClosed { get; set; }

        public DateTime StartedOn { get; set; }
        public DateTime? EndedOn { get; set; }

        public string? BurnoutRisk { get; set; }
        public DateTime? LastCheckInOn { get; set; }
        public DateTime? NextCheckInOn { get; set; }

        public DateTime? BackgroundCheckedOn { get; set; }
        public DateTime? ConfidentialitySignedOn { get; set; }
        public DateTime? CrisisTrainedOn { get; set; }
        public int BoundaryViolationCount { get; set; }

        /// <summary>Whether this volunteer may take a crisis case.</summary>
        public bool IsCrisisEligible { get; set; }

        /// <summary>What is missing when they are not. Empty when eligible.</summary>
        public List<string> MissingSafeguarding { get; set; } = new();

        public int RowVersion { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>Row shape for lists — the columns a team lead scans.</summary>
    public sealed class VolunteerSummaryDto
    {
        public string Id { get; set; } = string.Empty;
        public string? ReferenceCode { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string? PrimaryPhone { get; set; }
        public string? TeamName { get; set; }
        public string Status { get; set; } = string.Empty;
        public string CapacityBandCode { get; set; } = string.Empty;
        public int CurrentCaseLoad { get; set; }
        public int CapacityMaxPerWeek { get; set; }
        public string? BurnoutRisk { get; set; }
        public bool IsCrisisEligible { get; set; }
        public DateTime? NextCheckInOn { get; set; }
    }

    public sealed class TeamDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? CampusId { get; set; }
        public string? CampusName { get; set; }
        public string? LeadAccountId { get; set; }
        public string? LeadName { get; set; }
        public int MaxMembers { get; set; }
        public int MemberCount { get; set; }
        public bool HasRoom { get; set; }
        public bool IsActive { get; set; }
        public int RowVersion { get; set; }
    }

    /// <summary>
    /// What the signed-in caller may do with teams.
    ///
    /// The screen renders itself from this rather than from the role claim, so
    /// there is one authority on what is allowed and it is the server. Hiding a
    /// button is a courtesy; <c>UpdateTeamAsync</c> is the actual check.
    /// </summary>
    public sealed class TeamAccessDto
    {
        public bool CanOpen { get; set; }
        public bool CanCreate { get; set; }

        /// <summary>Every team in scope — admin everywhere, pastor at their campus.</summary>
        public bool CanEditAnyTeam { get; set; }

        /// <summary>Only the team this caller leads, and only its name and size.</summary>
        public bool CanEditOwnTeam { get; set; }

        public bool CanReassignLead { get; set; }
        public bool CanDeactivate { get; set; }
        public bool CanSeeAllCampuses { get; set; }

        /// <summary>NONE, OWNTEAMONLY, CAMPUSWIDE or ADMINISTRATOR.</summary>
        public string Scope { get; set; } = "NONE";
    }

    /// <summary>An account that could be set as a team's lead.</summary>
    public sealed class LeadCandidateDto
    {
        public string AccountId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string RoleCode { get; set; } = string.Empty;
        public string? CampusName { get; set; }

        /// <summary>The team they already lead, so the picker can warn before a swap.</summary>
        public string? LeadsTeam { get; set; }
    }

    public sealed class CapacityChangeDto
    {
        public string? FromBandCode { get; set; }
        public string ToBandCode { get; set; } = string.Empty;
        public string? Reason { get; set; }
        public string? Notes { get; set; }
        public DateTime ChangedAt { get; set; }
        public string? ChangedBy { get; set; }
    }

    public sealed class CapacityBandDto
    {
        public string Code { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public int MinPerWeek { get; set; }
        public int MaxPerWeek { get; set; }
        public string? Description { get; set; }
    }

    /// <summary>
    /// A volunteer who could take a new case, with the figures the picker sorts on.
    /// Exposed so a team lead assigning by hand sees the same shortlist the
    /// scheduler would compute.
    /// </summary>
    public sealed class EligibleVolunteerDto
    {
        public string Id { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string? TeamName { get; set; }
        public int CurrentCaseLoad { get; set; }
        public int CapacityMaxPerWeek { get; set; }
        public int RemainingCapacity { get; set; }
        public bool IsCrisisEligible { get; set; }
        public DateTime? LastAssignedAt { get; set; }
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
