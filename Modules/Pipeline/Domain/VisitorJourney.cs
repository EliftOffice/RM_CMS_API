namespace RM_CMS.Modules.Pipeline.Domain
{
    /// <summary>
    /// Everything known about one visitor, and everything that has happened to them.
    ///
    /// This exists because the system could only answer the question one CASE at a
    /// time. <c>GET /api/cases/{id}</c> returns a case with its contacts, escalations
    /// and notes — but a person who visited, went quiet, and came back a year later has
    /// two cases, and reading them separately loses the very thing you opened the record
    /// to see: the shape of the whole relationship.
    ///
    /// So the unit here is the PERSON. Every case they have ever had is folded into one
    /// chronological timeline, and the profile that a volunteer needs before picking up
    /// the phone sits above it.
    /// </summary>
    public sealed class VisitorJourney
    {
        public VisitorProfile Profile { get; set; } = new();
        public JourneyStats Stats { get; set; } = new();

        /// <summary>Newest first — the case they are on now is the one you want.</summary>
        public List<JourneyCase> Cases { get; set; } = new();

        /// <summary>Every event across every case, newest first.</summary>
        public List<JourneyEvent> Events { get; set; } = new();
    }

    /// <summary>
    /// Who they are. The detail a volunteer wants before making contact, in the order
    /// they want it — who, how to reach them, where they live, and anything that
    /// changes how they must be approached.
    /// </summary>
    public sealed class VisitorProfile
    {
        public string PersonId { get; set; } = string.Empty;
        public string? ReferenceCode { get; set; }
        public string FullName { get; set; } = string.Empty;

        public string? AgeBand { get; set; }
        public string? Gender { get; set; }
        public string? HouseholdType { get; set; }

        public string? Mobile { get; set; }
        public string? Email { get; set; }

        /// <summary>True when a verified Telegram contact exists — alerts can reach them.</summary>
        public bool HasTelegram { get; set; }

        public string? AddressLine { get; set; }

        /// <summary>The controlled area, when one was recorded.</summary>
        public string? AreaName { get; set; }

        /// <summary>Free text, used for someone from out of town.</summary>
        public string? Locality { get; set; }
        public string? PostalCode { get; set; }
        public bool IsLocal { get; set; }

        public string? CampusName { get; set; }

        public string LifecycleStatus { get; set; } = string.Empty;
        public DateTime? BecameMemberOn { get; set; }

        /// <summary>
        /// Consent. When set, nobody may contact them — this is shown at the top of the
        /// record, not buried, because the whole page is otherwise an invitation to
        /// pick up the phone.
        /// </summary>
        public bool DoNotContact { get; set; }
        public DateTime? DoNotContactAt { get; set; }
        public string? DoNotContactNote { get; set; }

        public string? Notes { get; set; }

        public DateTime RecordedAt { get; set; }
        public string? RecordedBy { get; set; }
    }

    /// <summary>
    /// The numbers that say how this relationship has actually gone, as opposed to
    /// what stage a label claims it is at.
    /// </summary>
    public sealed class JourneyStats
    {
        public int CaseCount { get; set; }

        /// <summary>Contacts actually logged — attempted, whether or not they connected.</summary>
        public int ContactsLogged { get; set; }

        /// <summary>Of those, how many reached the person.</summary>
        public int ContactsMade { get; set; }

        /// <summary>Planned contacts that were never completed.</summary>
        public int ContactsMissed { get; set; }

        public int EscalationsRaised { get; set; }
        public int OpenEscalations { get; set; }

        public DateTime? FirstSeenOn { get; set; }
        public DateTime? LastContactAt { get; set; }

        /// <summary>Days since anyone reached them. Null when nobody ever has.</summary>
        public int? DaysSinceContact { get; set; }

        /// <summary>Days from first visit to today, or to the day they became a member.</summary>
        public int? DaysInJourney { get; set; }

        /// <summary>
        /// Reached-vs-attempted, as a percentage. The single most honest number on the
        /// page: a case can sit in NURTURE for months while every call goes unanswered.
        /// Null when nothing has been attempted.
        /// </summary>
        public int? ContactSuccessRate =>
            ContactsLogged == 0 ? null : (int)Math.Round(ContactsMade * 100.0 / ContactsLogged);
    }

    /// <summary>One care case in the person's history.</summary>
    public sealed class JourneyCase
    {
        public string CaseId { get; set; } = string.Empty;
        public string? ReferenceCode { get; set; }

        public string Stage { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? Priority { get; set; }

        public string? VolunteerName { get; set; }
        public string? TeamName { get; set; }

        public int CurrentStepNumber { get; set; }
        public int? PlanStepCount { get; set; }
        public string? PlanName { get; set; }
        public DateTime? NextStepDueOn { get; set; }

        public DateTime? FirstVisitOn { get; set; }
        public DateTime OpenedAt { get; set; }
        public DateTime? FirstContactAt { get; set; }
        public DateTime? LastContactAt { get; set; }
        public DateTime? ClosedAt { get; set; }
        public string? CloseReason { get; set; }
        public string? CloseReasonLabel { get; set; }

        public int ContactAttemptCount { get; set; }
        public bool IsOpen => !string.Equals(Status, "CLOSED", StringComparison.Ordinal);
    }

    /// <summary>
    /// One thing that happened, from any source. The timeline is the point of this
    /// screen: contacts, escalations, reassignments and notes interleaved in the order
    /// they occurred, because that order is the story. Four separate tables would make
    /// the reader reconstruct it.
    /// </summary>
    public sealed class JourneyEvent
    {
        public DateTime OccurredAt { get; set; }

        /// <summary>CASE_OPENED | CONTACT | ESCALATION | ASSIGNMENT | NOTE | CASE_CLOSED.</summary>
        public string Kind { get; set; } = string.Empty;

        /// <summary>One line naming what happened, already readable.</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>The substance — what was said, why it was escalated, the note body.</summary>
        public string? Detail { get; set; }

        /// <summary>Who did it. Null for something the system did.</summary>
        public string? Actor { get; set; }

        public string? CaseReference { get; set; }

        /// <summary>
        /// Colours the entry: POSITIVE reached them, NEGATIVE did not, ALERT is an
        /// escalation, NEUTRAL is everything else.
        /// </summary>
        public string Tone { get; set; } = "NEUTRAL";

        /// <summary>Short labels shown as chips — method, outcome, intent, tier.</summary>
        public List<string> Tags { get; set; } = new();
    }
}
