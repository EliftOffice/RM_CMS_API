namespace RM_CMS.Modules.Huddle.Domain
{
    /// <summary>
    /// One contact awaiting the team lead's verdict at the weekly huddle.
    ///
    /// The huddle exists to calibrate JUDGEMENT, not to check work was done. For each
    /// contact the volunteer logged, the lead says whether the escalation decision was
    /// right — and the case that matters is the one where a volunteer heard something
    /// serious and did not pass it on. Nothing else in the system catches that: the
    /// escalation chase-up can only chase escalations that exist.
    /// </summary>
    public sealed class HuddleItem
    {
        public string InteractionId { get; set; } = string.Empty;
        public string? CaseReference { get; set; }

        public string VolunteerId { get; set; } = string.Empty;
        public string VolunteerName { get; set; } = string.Empty;
        public string PersonName { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;
        public string? OutcomeCode { get; set; }
        public string? OutcomeLabel { get; set; }
        public string? IntentCode { get; set; }
        public string? IntentLabel { get; set; }

        public DateTime? OccurredAt { get; set; }
        public DateTime? ScheduledOn { get; set; }
        public string? Notes { get; set; }

        /// <summary>True when this contact actually raised an escalation.</summary>
        public bool RaisedEscalation { get; set; }

        public string? EscalationAssessment { get; set; }
        public string? AssessmentNote { get; set; }
    }

    /// <summary>
    /// One verdict, as submitted. The huddle saves many of these at once: the MVP
    /// made the lead click Update and confirm a dialog per row, and with hundreds of
    /// rows that is why no verdict was ever recorded in three months of production.
    /// </summary>
    public sealed class HuddleVerdict
    {
        public string InteractionId { get; set; } = string.Empty;
        public string Assessment { get; set; } = string.Empty;
        public string? Note { get; set; }
    }

    /// <summary>
    /// What the huddle screen needs in one call: the week under review, the items,
    /// and how much has piled up outside it.
    /// </summary>
    public sealed class HuddleAgenda
    {
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }

        /// <summary>True when today is the configured huddle day.</summary>
        public bool IsHuddleDay { get; set; }

        /// <summary>1 = Monday ... 7 = Sunday, as the church says it aloud.</summary>
        public int HuddleDayOfWeek { get; set; }

        public IReadOnlyList<HuddleItem> Items { get; set; } = Array.Empty<HuddleItem>();

        /// <summary>
        /// Unassessed contacts OLDER than this week's window. Shown as a number rather
        /// than loaded into the table: the MVP put every unassessed row ever into one
        /// unpaginated list, which is how the screen became unusable.
        /// </summary>
        public int OlderUnassessedCount { get; set; }

        /// <summary>Verdicts recorded in this window, for a sense of progress.</summary>
        public int AssessedThisWeek { get; set; }
    }

    public static class HuddleAssessment
    {
        public const string Correct = "CORRECT";

        /// <summary>Should have escalated and did not. The dangerous one.</summary>
        public const string UnderEscalated = "UNDER_ESCALATED";

        /// <summary>Escalated something routine — costs the lead time, signals a training gap.</summary>
        public const string OverEscalated = "OVER_ESCALATED";

        public static readonly string[] All = { Correct, UnderEscalated, OverEscalated };

        public static bool IsKnown(string? v) => v is not null && All.Contains(v, StringComparer.Ordinal);

        /// <summary>
        /// Whether this verdict says something went wrong that a person should hear
        /// about. Used to decide what the volunteer's next check-in should cover.
        /// </summary>
        public static bool IsMiscalibration(string? v) =>
            string.Equals(v, UnderEscalated, StringComparison.Ordinal) ||
            string.Equals(v, OverEscalated, StringComparison.Ordinal);
    }
}
