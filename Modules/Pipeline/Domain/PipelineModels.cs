namespace RM_CMS.Modules.Pipeline.Domain
{
    /// <summary>
    /// One person and where their journey has reached — the whole visitor flow in
    /// one row.
    ///
    /// This is the view nothing else in the system gives: the dashboard answers
    /// "what needs me today", the case screens answer "what about this one person".
    /// Neither answers "where is everybody", which is the question you ask when you
    /// want to know whether the funnel is working rather than whether today is busy.
    /// </summary>
    public sealed class PipelineRow
    {
        public string PersonId { get; set; } = string.Empty;
        public string PersonName { get; set; } = string.Empty;
        public string? Phone { get; set; }

        public string? CaseId { get; set; }
        public string? CaseReference { get; set; }

        /// <summary>INTAKE / INITIAL_FOLLOW_UP / NURTURE / REVIEW / CLOSED.</summary>
        public string? Stage { get; set; }
        public string? Status { get; set; }

        public string? VolunteerName { get; set; }
        public string? TeamName { get; set; }
        public string? CampusName { get; set; }

        // ---- nurture position ----
        public int CurrentStepNumber { get; set; }
        public int? PlanStepCount { get; set; }
        public DateTime? NextStepDueOn { get; set; }

        public DateTime? FirstVisitOn { get; set; }
        public DateTime OpenedAt { get; set; }
        public DateTime? LastContactAt { get; set; }
        public DateTime? ClosedAt { get; set; }
        public string? CloseReason { get; set; }

        /// <summary>An open escalation is pausing this case.</summary>
        public bool HasOpenEscalation { get; set; }

        public int ContactAttemptCount { get; set; }

        /// <summary>
        /// How far through the plan, as text. Only meaningful in the nurture stage —
        /// a case in initial follow-up has no plan position yet, and showing "0 of 7"
        /// would imply it is behind rather than not yet started.
        /// </summary>
        public string? NurtureProgress =>
            string.Equals(Stage, "NURTURE", StringComparison.Ordinal) && PlanStepCount is > 0
                ? $"{CurrentStepNumber} of {PlanStepCount}"
                : null;

        /// <summary>
        /// Days since the last contact, or since the case opened when there has been
        /// none. This is the number that says "nobody has touched this person in a
        /// month", which no stage label conveys on its own.
        /// </summary>
        public int? DaysSinceContact { get; set; }
    }

    /// <summary>
    /// The funnel counts. Ordered as the journey runs, so the shape of the drop-off
    /// is visible rather than having to be reconstructed from a list.
    /// </summary>
    public sealed class PipelineSummary
    {
        public int Intake { get; set; }
        public int InitialFollowUp { get; set; }
        public int Nurture { get; set; }
        public int Review { get; set; }
        public int Closed { get; set; }

        /// <summary>Open cases paused by an escalation, across every stage.</summary>
        public int Escalated { get; set; }

        /// <summary>People on file with no case at all — recorded but never started.</summary>
        public int NoCase { get; set; }

        public int TotalPeople { get; set; }

        /// <summary>Closed cases that reached membership, for the only ratio that matters.</summary>
        public int BecameMembers { get; set; }
    }

    /// <summary>What the caller asked to see.</summary>
    public sealed class PipelineQuery
    {
        public string? Search { get; init; }
        public string? Stage { get; init; }
        public string? Status { get; init; }

        /// <summary>True to show people with no case as well.</summary>
        public bool IncludeUnstarted { get; init; } = true;

        public int Skip { get; init; }
        public int Take { get; init; } = 50;
    }
}
