using System.ComponentModel.DataAnnotations;

namespace RM_CMS.Modules.CheckIns.Api
{
    /// <summary>
    /// What a team lead records after a check-in.
    ///
    /// Note what is NOT here: who conducted it. That comes from the caller's token.
    /// The MVP form posted a <c>teamLeadId</c> alongside the volunteer, which meant the
    /// record of who held a pastoral conversation was whatever the browser said it was.
    /// </summary>
    public sealed class RecordCheckInRequest
    {
        [Required][StringLength(26, MinimumLength = 26)]
        public string VolunteerId { get; set; } = string.Empty;

        /// <summary>Defaults to today when the form leaves it blank.</summary>
        public DateTime? HeldOn { get; set; }

        [Range(1, 480)] public int? DurationMinutes { get; set; }

        [Required][StringLength(20)]
        public string MeetingType { get; set; } = "MONTHLY";

        /// <summary>
        /// GREEN / AMBER / RED. Required: it is the single most useful field on the
        /// form and the one a hurried lead would otherwise skip.
        /// </summary>
        [Required][StringLength(20)]
        public string EmotionalTone { get; set; } = string.Empty;

        [StringLength(4000)] public string? Concerns { get; set; }
        [StringLength(4000)] public string? TrainingNeeds { get; set; }
        [StringLength(4000)] public string? ActionItems { get; set; }

        public bool CapacityReviewed { get; set; }
        public bool BoundaryIssuesRaised { get; set; }
        public bool FollowUpRequired { get; set; }

        public DateTime? NextCheckInOn { get; set; }

        /// <summary>
        /// Set to move the volunteer to a different capacity band as part of this
        /// conversation. Applied through the volunteer service, so the capacity
        /// history row is written the same way it is everywhere else.
        /// </summary>
        [StringLength(20)] public string? NewCapacityBandCode { get; set; }
    }

    public sealed class CheckInDto
    {
        public string Id { get; set; } = string.Empty;
        public string VolunteerId { get; set; } = string.Empty;
        public string VolunteerName { get; set; } = string.Empty;
        public string? ConductedByName { get; set; }

        public DateTime HeldOn { get; set; }
        public int? DurationMinutes { get; set; }
        public string MeetingType { get; set; } = string.Empty;
        public string? EmotionalTone { get; set; }

        public string? Concerns { get; set; }
        public string? TrainingNeeds { get; set; }
        public string? ActionItems { get; set; }

        public bool CapacityReviewed { get; set; }
        public bool BoundaryIssuesRaised { get; set; }
        public bool FollowUpRequired { get; set; }
        public DateTime? NextCheckInOn { get; set; }

        /// <summary>True when this one should not just be filed — RED, a boundary issue, or flagged.</summary>
        public bool NeedsAttention { get; set; }
    }

    public sealed class CheckInDueDto
    {
        public string VolunteerId { get; set; } = string.Empty;
        public string? ReferenceCode { get; set; }
        public string VolunteerName { get; set; } = string.Empty;
        public string? TeamName { get; set; }

        public DateTime? LastCheckInOn { get; set; }
        public DateTime? NextCheckInOn { get; set; }
        public int? DaysSinceLastCheckIn { get; set; }
        public string? LastEmotionalTone { get; set; }

        /// <summary>True when they have never had a check-in recorded.</summary>
        public bool NeverCheckedIn { get; set; }
    }
}
