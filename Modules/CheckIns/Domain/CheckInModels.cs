namespace RM_CMS.Modules.CheckIns.Domain
{
    /// <summary>
    /// A pastoral conversation between a team lead and one of their volunteers.
    /// Mirrors <c>volunteer_check_in</c>.
    ///
    /// This is the only part of the system that looks after the PEOPLE DOING THE WORK
    /// rather than the people being cared for. Volunteers hear disclosures of abuse,
    /// self-harm and family breakdown; a check-in is where that lands somewhere before
    /// it turns into burnout or a boundary being crossed. That is why the record keeps
    /// emotional tone, concerns and boundary issues as first-class columns rather than
    /// as free text nobody queries.
    /// </summary>
    public sealed class VolunteerCheckIn
    {
        public long Id { get; set; }
        public string PublicId { get; set; } = string.Empty;

        public long VolunteerId { get; set; }
        public string? VolunteerPublicId { get; set; }
        public string? VolunteerName { get; set; }

        /// <summary>The team lead who held it, as a user account.</summary>
        public long ConductedBy { get; set; }
        public string? ConductedByName { get; set; }

        public DateTime HeldOn { get; set; }
        public int? DurationMinutes { get; set; }
        public string MeetingType { get; set; } = CheckInMeetingType.Monthly;

        /// <summary>GREEN / AMBER / RED. Null when it was not asked.</summary>
        public string? EmotionalTone { get; set; }

        public string? Concerns { get; set; }
        public string? TrainingNeeds { get; set; }
        public string? ActionItems { get; set; }

        public bool CapacityReviewed { get; set; }
        public bool BoundaryIssuesRaised { get; set; }
        public bool FollowUpRequired { get; set; }
        public DateTime? NextCheckInOn { get; set; }

        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Whether this check-in should not simply be filed and forgotten. A RED tone,
        /// a boundary issue or an explicit follow-up flag all mean somebody has to come
        /// back to this volunteer — which is exactly what a monthly rhythm tends to
        /// lose track of.
        /// </summary>
        public bool NeedsAttention =>
            FollowUpRequired ||
            BoundaryIssuesRaised ||
            string.Equals(EmotionalTone, CheckInTone.Red, StringComparison.Ordinal);
    }

    /// <summary>A volunteer whose check-in has fallen due, for the team lead's list.</summary>
    public sealed class CheckInDue
    {
        public string VolunteerId { get; set; } = string.Empty;
        public string? ReferenceCode { get; set; }
        public string VolunteerName { get; set; } = string.Empty;
        public string? TeamName { get; set; }

        public DateTime? LastCheckInOn { get; set; }
        public DateTime? NextCheckInOn { get; set; }

        /// <summary>Null when they have never had one — which is its own kind of overdue.</summary>
        public int? DaysSinceLastCheckIn { get; set; }

        public bool IsOverdue { get; set; }

        /// <summary>The tone recorded last time, so a RED is not lost between meetings.</summary>
        public string? LastEmotionalTone { get; set; }
    }

    public static class CheckInMeetingType
    {
        public const string Monthly = "MONTHLY";
        public const string AdHoc = "AD_HOC";
        public const string Onboarding = "ONBOARDING";
        public const string Exit = "EXIT";

        /// <summary>Held because something was wrong, not because it was due.</summary>
        public const string Welfare = "WELFARE";

        public static readonly string[] All = { Monthly, AdHoc, Onboarding, Exit, Welfare };

        public static bool IsKnown(string? v) => v is not null && All.Contains(v, StringComparer.Ordinal);
    }

    public static class CheckInTone
    {
        public const string Green = "GREEN";
        public const string Amber = "AMBER";
        public const string Red = "RED";

        public static readonly string[] All = { Green, Amber, Red };

        public static bool IsKnown(string? v) => v is null || All.Contains(v, StringComparer.Ordinal);
    }
}
