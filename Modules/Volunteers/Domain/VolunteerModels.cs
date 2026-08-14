namespace RM_CMS.Modules.Volunteers.Domain
{
    /// <summary>
    /// The volunteer-specific facts about a person. One row per person who serves.
    ///
    /// A volunteer IS a person — name, contact details and campus live on
    /// <c>person</c>. This record only holds what is true because they serve.
    /// </summary>
    public sealed class Volunteer
    {
        public long Id { get; set; }
        public string PublicId { get; set; } = string.Empty;

        /// <summary>Human-facing handle ('V001'). Display only — never a key.</summary>
        public string? ReferenceCode { get; set; }

        // ---- person (joined) ----
        public long PersonId { get; set; }
        public string PersonPublicId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string? PrimaryPhone { get; set; }
        public string? PrimaryEmail { get; set; }

        // ---- placement ----
        public long CampusId { get; set; }
        public string? CampusPublicId { get; set; }
        public string? CampusName { get; set; }

        public long? TeamId { get; set; }
        public string? TeamPublicId { get; set; }
        public string? TeamName { get; set; }

        public string Status { get; set; } = VolunteerStatus.Active;
        public string ServiceLevel { get; set; } = "LEVEL_0";

        // ---- capacity ----
        public string CapacityBandCode { get; set; } = string.Empty;
        public string? CapacityBandLabel { get; set; }
        public int CapacityMinPerWeek { get; set; }
        public int CapacityMaxPerWeek { get; set; }

        /// <summary>
        /// Live workload. Maintained in the same transaction as the case that changes
        /// it — the MVP kept this counter AND derived the same figure from another
        /// table, and the two drifted.
        /// </summary>
        public int CurrentCaseLoad { get; set; }

        public int LifetimeCasesAssigned { get; set; }
        public int LifetimeCasesClosed { get; set; }
        public DateTime? LastAssignedAt { get; set; }

        public DateTime StartedOn { get; set; }
        public DateTime? EndedOn { get; set; }

        // ---- wellbeing ----
        public string? BurnoutRisk { get; set; }
        public DateTime? LastCheckInOn { get; set; }
        public DateTime? NextCheckInOn { get; set; }

        // ---- safeguarding ----
        public DateTime? BackgroundCheckedOn { get; set; }
        public DateTime? ConfidentialitySignedOn { get; set; }
        public DateTime? CrisisTrainedOn { get; set; }
        public int BoundaryViolationCount { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public int RowVersion { get; set; }

        /// <summary>True when this volunteer can currently be given work.</summary>
        public bool IsAvailable =>
            string.Equals(Status, VolunteerStatus.Active, StringComparison.Ordinal);

        /// <summary>Spare capacity before the band's weekly ceiling is reached.</summary>
        public int RemainingCapacity => Math.Max(0, CapacityMaxPerWeek - CurrentCaseLoad);

        public bool HasSpareCapacity => RemainingCapacity > 0;

        /// <summary>
        /// Whether this volunteer may be handed a crisis case.
        ///
        /// Crisis work means hearing disclosures of abuse, self-harm and family
        /// breakdown. Assigning that to someone without training, a background check
        /// and a signed confidentiality agreement is a safeguarding failure, not a
        /// scheduling inconvenience — so it is a hard gate, checked separately from
        /// ordinary availability.
        /// </summary>
        public bool IsCrisisEligible =>
            IsAvailable &&
            CrisisTrainedOn.HasValue &&
            BackgroundCheckedOn.HasValue &&
            ConfidentialitySignedOn.HasValue;

        /// <summary>Names the missing safeguarding requirements, for an explainable refusal.</summary>
        public IReadOnlyList<string> MissingSafeguarding()
        {
            var missing = new List<string>();

            if (!CrisisTrainedOn.HasValue) missing.Add("crisis training");
            if (!BackgroundCheckedOn.HasValue) missing.Add("background check");
            if (!ConfidentialitySignedOn.HasValue) missing.Add("confidentiality agreement");

            return missing;
        }
    }

    /// <summary>
    /// A team of volunteers led by one person. The MVP had no team entity —
    /// <c>volunteers.team_lead</c> pointed straight at a team lead, so a team could
    /// not be renamed, retired or handed over without rewriting every volunteer row.
    /// </summary>
    public sealed class Team
    {
        public long Id { get; set; }
        public string PublicId { get; set; } = string.Empty;

        public long CampusId { get; set; }
        public string? CampusPublicId { get; set; }
        public string? CampusName { get; set; }

        public string Name { get; set; } = string.Empty;

        /// <summary>The lead's user account. Nullable so a team outlives a departure.</summary>
        public long? LeadUserId { get; set; }
        public string? LeadUserPublicId { get; set; }
        public string? LeadName { get; set; }

        public int MaxMembers { get; set; } = 12;
        public bool IsActive { get; set; } = true;

        /// <summary>Derived, not stored — counted at read time.</summary>
        public int MemberCount { get; set; }

        public DateTime CreatedAt { get; set; }
        public int RowVersion { get; set; }

        public bool HasRoom => MemberCount < MaxMembers;
    }

    /// <summary>One entry in a volunteer's capacity history. Append-only.</summary>
    public sealed class CapacityChange
    {
        public long Id { get; set; }
        public long VolunteerId { get; set; }
        public string? FromBandCode { get; set; }
        public string ToBandCode { get; set; } = string.Empty;
        public string? Reason { get; set; }
        public string? Notes { get; set; }
        public DateTime ChangedAt { get; set; }
        public string? ChangedByName { get; set; }
    }

    /// <summary>A capacity tier. Mirrors the <c>capacity_band</c> lookup.</summary>
    public sealed class CapacityBand
    {
        public string Code { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public int MinPerWeek { get; set; }
        public int MaxPerWeek { get; set; }
        public string? Description { get; set; }
    }

    /// <summary>
    /// Mirrors the CHECK on <c>volunteer.status</c>. Code branches on these, so they
    /// are a fixed vocabulary rather than an admin-editable lookup.
    /// </summary>
    public static class VolunteerStatus
    {
        /// <summary>Serving and eligible for new work.</summary>
        public const string Active = "ACTIVE";

        /// <summary>Temporarily not taking new cases; keeps existing ones.</summary>
        public const string Paused = "PAUSED";

        /// <summary>Away for a defined period.</summary>
        public const string OnLeave = "ON_LEAVE";

        /// <summary>Not serving, but the record is retained.</summary>
        public const string Inactive = "INACTIVE";

        /// <summary>Left the ministry. Terminal.</summary>
        public const string Exited = "EXITED";

        public static readonly string[] All = { Active, Paused, OnLeave, Inactive, Exited };

        /// <summary>Statuses that keep existing cases assigned rather than releasing them.</summary>
        public static readonly string[] RetainsCases = { Active, Paused, OnLeave };

        public static bool IsKnown(string? value) =>
            !string.IsNullOrWhiteSpace(value) && All.Contains(value, StringComparer.Ordinal);

        /// <summary>
        /// True when moving to this status should hand the volunteer's open cases to
        /// someone else. A pause is temporary; leaving is not.
        /// </summary>
        public static bool RequiresCaseHandover(string? status) =>
            string.Equals(status, Inactive, StringComparison.Ordinal) ||
            string.Equals(status, Exited, StringComparison.Ordinal);
    }

    /// <summary>Mirrors the CHECK on <c>volunteer.burnout_risk</c>.</summary>
    public static class BurnoutRisk
    {
        public const string Low = "LOW";
        public const string Medium = "MEDIUM";
        public const string High = "HIGH";

        public static readonly string[] All = { Low, Medium, High };

        public static bool IsKnown(string? value) =>
            value is null || All.Contains(value, StringComparer.Ordinal);
    }

    /// <summary>Why a volunteer's capacity band changed. Recorded for the wellbeing trail.</summary>
    public static class CapacityChangeReasons
    {
        public const string CheckIn = "CHECK_IN";
        public const string VolunteerRequest = "VOLUNTEER_REQUEST";
        public const string BurnoutRisk = "BURNOUT_RISK";
        public const string PerformanceReview = "PERFORMANCE_REVIEW";
        public const string Onboarding = "ONBOARDING";
        public const string Other = "OTHER";

        public static readonly string[] All =
        {
            CheckIn, VolunteerRequest, BurnoutRisk, PerformanceReview, Onboarding, Other
        };

        public static bool IsKnown(string? value) =>
            value is null || All.Contains(value, StringComparer.Ordinal);
    }
}
