namespace RM_CMS.Modules.Care.Domain
{
    /// <summary>
    /// One engagement journey with a person.
    ///
    /// The MVP put follow-up state directly on the person, which made a journey a
    /// single overwritable slot: someone who visits, goes quiet and returns two
    /// years later overwrote their own history. A person may have several cases
    /// over time, and each is a separate story with its own outcome.
    /// </summary>
    public sealed class CareCase
    {
        public long Id { get; set; }
        public string PublicId { get; set; } = string.Empty;
        public string? ReferenceCode { get; set; }

        // ---- person (joined) ----
        public long PersonId { get; set; }
        public string PersonPublicId { get; set; } = string.Empty;
        public string PersonName { get; set; } = string.Empty;
        public string? PersonPhone { get; set; }
        public bool PersonDoNotContact { get; set; }

        // ---- placement ----
        public long CampusId { get; set; }
        public string? CampusPublicId { get; set; }
        public string? CampusName { get; set; }

        public long? AssignedVolunteerId { get; set; }
        public string? AssignedVolunteerPublicId { get; set; }
        public string? AssignedVolunteerName { get; set; }

        public long? TeamId { get; set; }
        public string? TeamName { get; set; }

        // ---- state ----
        public string Stage { get; set; } = CaseStage.Intake;
        public string Status { get; set; } = CaseStatus.Open;
        public string Priority { get; set; } = CasePriority.Normal;

        public string? VisitType { get; set; }
        public string? ConnectionSourceCode { get; set; }
        public DateTime? FirstVisitOn { get; set; }

        // ---- nurture position ----
        public long? NurturePlanId { get; set; }
        public int CurrentStepNumber { get; set; }
        public DateTime? NextStepDueOn { get; set; }
        public DateTime? AwaitingReviewSince { get; set; }

        // ---- timeline ----
        public DateTime OpenedAt { get; set; }
        public DateTime? AssignedAt { get; set; }
        public DateTime? FirstContactAt { get; set; }
        public DateTime? LastContactAt { get; set; }
        public DateTime? NextActionOn { get; set; }
        public DateTime? ClosedAt { get; set; }
        public string? CloseReason { get; set; }
        public string? CloseNotes { get; set; }

        // ---- counters ----
        public int ContactAttemptCount { get; set; }
        public int ConsecutiveNoContact { get; set; }

        public DateTime CreatedAt { get; set; }
        public int RowVersion { get; set; }

        public bool IsOpen => !string.Equals(Status, CaseStatus.Closed, StringComparison.Ordinal);

        /// <summary>
        /// True while an escalation is holding this case. The scheduler creates no
        /// further contacts until it is resolved.
        /// </summary>
        public bool IsPaused => string.Equals(Status, CaseStatus.Escalated, StringComparison.Ordinal);

        public bool NeedsAssignment => AssignedVolunteerId is null && IsOpen;
    }

    /// <summary>
    /// One planned or completed contact within a case.
    ///
    /// This single type covers both the initial follow-up and every nurture step.
    /// The MVP had two near-identical tables for them, each with its own DAL, DTOs
    /// and endpoints; the only real difference is that a nurture step is scheduled
    /// in advance, which is just <see cref="ScheduledOn"/> being populated.
    /// </summary>
    public sealed class CareInteraction
    {
        public long Id { get; set; }
        public string PublicId { get; set; } = string.Empty;

        public long CareCaseId { get; set; }
        public string? CareCasePublicId { get; set; }

        public long? VolunteerId { get; set; }
        public string? VolunteerPublicId { get; set; }
        public string? VolunteerName { get; set; }

        public string Stage { get; set; } = InteractionStage.InitialFollowUp;
        public int SequenceNumber { get; set; }

        public string? MethodCode { get; set; }
        public DateTime? ScheduledOn { get; set; }
        public DateTime? OccurredAt { get; set; }

        public string Status { get; set; } = InteractionStatus.Pending;
        public bool? MadeContact { get; set; }
        public string? OutcomeCode { get; set; }
        public string? OutcomeLabel { get; set; }
        public string? IntentCode { get; set; }
        public string? IntentLabel { get; set; }

        /// <summary>
        /// Steps in the plan this case is following, so a screen can render
        /// "Step 2 of 5" rather than assuming a length. Null when the case is not on
        /// a nurture plan.
        /// </summary>
        public int? NurtureTotalSteps { get; set; }

        public int? DurationMinutes { get; set; }
        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; }
        public int RowVersion { get; set; }

        public bool IsPending => string.Equals(Status, InteractionStatus.Pending, StringComparison.Ordinal);

        public bool IsOverdue(DateTime today) =>
            IsPending && ScheduledOn.HasValue && ScheduledOn.Value.Date < today.Date;
    }

    /// <summary>
    /// A concern raised out of an interaction and handed to a team lead.
    ///
    /// An escalation PAUSES its case: no further contact is scheduled while it is
    /// open. Closing it resumes the sequence from where it stopped. It is not a
    /// branch and not a terminator.
    /// </summary>
    public sealed class Escalation
    {
        public long Id { get; set; }
        public string PublicId { get; set; } = string.Empty;
        public string? ReferenceCode { get; set; }

        public long CareCaseId { get; set; }
        public string? CareCasePublicId { get; set; }
        public string? PersonName { get; set; }

        public long? CareInteractionId { get; set; }
        public long? RaisedByVolunteerId { get; set; }
        public string? RaisedByName { get; set; }

        public long? AssignedToUserId { get; set; }
        public string? AssignedToPublicId { get; set; }
        public string? AssignedToName { get; set; }

        public long CampusId { get; set; }
        public string? CampusPublicId { get; set; }

        public string ReasonCode { get; set; } = string.Empty;
        public string? ReasonLabel { get; set; }
        public bool ReasonRequiresProtocol { get; set; }

        public string Tier { get; set; } = EscalationTier.Standard;
        public string Status { get; set; } = EscalationStatus.New;
        public string Description { get; set; } = string.Empty;

        public DateTime RaisedAt { get; set; }
        public DateTime? NotifiedAt { get; set; }
        public DateTime? AcknowledgedAt { get; set; }
        public DateTime? ResolvedAt { get; set; }

        public string? OutcomeCode { get; set; }
        public string? ResolutionNotes { get; set; }
        public string? ResourceConnected { get; set; }

        public bool ResumeCaseOnClose { get; set; } = true;
        public int ReminderCount { get; set; }
        public DateTime? LastReminderAt { get; set; }
        public DateTime? PastorAlertedAt { get; set; }

        public bool? ProtocolFollowed { get; set; }
        public bool? AuthoritiesContacted { get; set; }
        public bool? VolunteerDebriefed { get; set; }

        public int RowVersion { get; set; }

        public bool IsOpen =>
            !string.Equals(Status, EscalationStatus.Resolved, StringComparison.Ordinal) &&
            !string.Equals(Status, EscalationStatus.Closed, StringComparison.Ordinal);

        public bool IsAcknowledged => AcknowledgedAt.HasValue;

        /// <summary>Hours this has been waiting. Drives the chase-up ladder.</summary>
        public double HoursWaiting(DateTime utcNow) => (utcNow - RaisedAt).TotalHours;
    }

    /// <summary>Free-text documentation attached to any entity.</summary>
    public sealed class CareNote
    {
        public long Id { get; set; }
        public string PublicId { get; set; } = string.Empty;

        public string EntityType { get; set; } = string.Empty;
        public long EntityId { get; set; }

        public string? NoteTypeCode { get; set; }
        public string? NoteTypeLabel { get; set; }
        public string Body { get; set; } = string.Empty;
        public string? Tags { get; set; }

        public bool IsPrivate { get; set; }
        public string? VisibleToRoleCode { get; set; }

        public DateTime CreatedAt { get; set; }
        public string? CreatedByName { get; set; }
    }

    /// <summary>Who has held a case, and when.</summary>
    public sealed class CaseAssignment
    {
        public long Id { get; set; }
        public long CareCaseId { get; set; }
        public long VolunteerId { get; set; }
        public string? VolunteerName { get; set; }
        public DateTime AssignedAt { get; set; }
        public DateTime? UnassignedAt { get; set; }
        public string Reason { get; set; } = AssignmentReason.Auto;
        public string? AssignedByName { get; set; }
    }

    // =====================================================================
    // Vocabularies
    //
    // These mirror CHECK constraints, not lookup tables: the code branches on
    // them, so an administrator renaming one would silently break routing.
    // =====================================================================

    public static class CaseStage
    {
        public const string Intake = "INTAKE";
        public const string InitialFollowUp = "INITIAL_FOLLOW_UP";
        public const string Nurture = "NURTURE";
        public const string Review = "REVIEW";
        public const string Closed = "CLOSED";

        public static readonly string[] All = { Intake, InitialFollowUp, Nurture, Review, Closed };

        public static bool IsKnown(string? v) => v is not null && All.Contains(v, StringComparer.Ordinal);
    }

    public static class CaseStatus
    {
        public const string Open = "OPEN";
        public const string AwaitingAssignment = "AWAITING_ASSIGNMENT";
        public const string InProgress = "IN_PROGRESS";

        /// <summary>Paused by an open escalation.</summary>
        public const string Escalated = "ESCALATED";

        public const string OnHold = "ON_HOLD";
        public const string Closed = "CLOSED";

        public static readonly string[] All =
            { Open, AwaitingAssignment, InProgress, Escalated, OnHold, Closed };

        public static bool IsKnown(string? v) => v is not null && All.Contains(v, StringComparer.Ordinal);
    }

    public static class CasePriority
    {
        public const string Low = "LOW";
        public const string Normal = "NORMAL";
        public const string High = "HIGH";
        public const string Urgent = "URGENT";

        public static readonly string[] All = { Low, Normal, High, Urgent };

        public static bool IsKnown(string? v) => v is null || All.Contains(v, StringComparer.Ordinal);
    }

    public static class CaseCloseReason
    {
        public const string BecameMember = "BECAME_MEMBER";
        public const string Declined = "DECLINED";
        public const string Unreachable = "UNREACHABLE";
        public const string MovedAway = "MOVED_AWAY";
        public const string Duplicate = "DUPLICATE";
        public const string ReferredOut = "REFERRED_OUT";
        public const string AlreadyChurched = "ALREADY_CHURCHED";
        public const string DoNotContact = "DO_NOT_CONTACT";
        public const string Other = "OTHER";

        public static readonly string[] All =
        {
            BecameMember, Declined, Unreachable, MovedAway, Duplicate,
            ReferredOut, AlreadyChurched, DoNotContact, Other
        };

        public static bool IsKnown(string? v) => v is not null && All.Contains(v, StringComparer.Ordinal);
    }

    public static class InteractionStage
    {
        public const string InitialFollowUp = "INITIAL_FOLLOW_UP";
        public const string Nurture = "NURTURE";
        public const string AdHoc = "AD_HOC";

        public static readonly string[] All = { InitialFollowUp, Nurture, AdHoc };

        public static bool IsKnown(string? v) => v is not null && All.Contains(v, StringComparer.Ordinal);
    }

    public static class InteractionStatus
    {
        public const string Pending = "PENDING";
        public const string Completed = "COMPLETED";
        public const string Missed = "MISSED";
        public const string Cancelled = "CANCELLED";

        public static readonly string[] All = { Pending, Completed, Missed, Cancelled };

        public static bool IsKnown(string? v) => v is not null && All.Contains(v, StringComparer.Ordinal);
    }

    public static class EscalationTier
    {
        public const string Standard = "STANDARD";
        public const string Urgent = "URGENT";
        public const string Emergency = "EMERGENCY";

        public static readonly string[] All = { Standard, Urgent, Emergency };

        public static bool IsKnown(string? v) => v is null || All.Contains(v, StringComparer.Ordinal);
    }

    public static class EscalationStatus
    {
        public const string New = "NEW";
        public const string Acknowledged = "ACKNOWLEDGED";
        public const string InProgress = "IN_PROGRESS";
        public const string Resolved = "RESOLVED";
        public const string ReferredOut = "REFERRED_OUT";
        public const string Closed = "CLOSED";

        public static readonly string[] All =
            { New, Acknowledged, InProgress, Resolved, ReferredOut, Closed };

        public static bool IsKnown(string? v) => v is not null && All.Contains(v, StringComparer.Ordinal);

        /// <summary>Statuses that end the escalation and release the case.</summary>
        public static bool IsTerminal(string? v) =>
            string.Equals(v, Resolved, StringComparison.Ordinal) ||
            string.Equals(v, Closed, StringComparison.Ordinal) ||
            string.Equals(v, ReferredOut, StringComparison.Ordinal);
    }

    public static class AssignmentReason
    {
        public const string Auto = "AUTO";
        public const string Manual = "MANUAL";
        public const string Reassigned = "REASSIGNED";
        public const string VolunteerInactive = "VOLUNTEER_INACTIVE";
        public const string Capacity = "CAPACITY";
        public const string Escalation = "ESCALATION";

        public static readonly string[] All =
            { Auto, Manual, Reassigned, VolunteerInactive, Capacity, Escalation };

        public static bool IsKnown(string? v) => v is null || All.Contains(v, StringComparer.Ordinal);
    }

    public static class NoteEntityTypes
    {
        public const string Person = "PERSON";
        public const string Volunteer = "VOLUNTEER";
        public const string CareCase = "CARE_CASE";
        public const string CareInteraction = "CARE_INTERACTION";
        public const string CheckIn = "CHECK_IN";
        public const string Escalation = "ESCALATION";
        public const string Team = "TEAM";

        public static readonly string[] All =
            { Person, Volunteer, CareCase, CareInteraction, CheckIn, Escalation, Team };

        public static bool IsKnown(string? v) => v is not null && All.Contains(v, StringComparer.Ordinal);
    }

    // =====================================================================
    // Progression
    // =====================================================================

    /// <summary>
    /// What a <c>care_progression_rule</c> says to do when an interaction completes.
    /// Mirrors the CHECK on <c>care_progression_rule.action</c>.
    /// </summary>
    public static class ProgressionAction
    {
        public const string StartNurture = "START_NURTURE";
        public const string ContinueNurture = "CONTINUE_NURTURE";
        public const string JumpToStep = "JUMP_TO_STEP";
        public const string ScheduleRetry = "SCHEDULE_RETRY";

        /// <summary>Raise an escalation and pause the case until it is closed.</summary>
        public const string Escalate = "ESCALATE";

        /// <summary>Hand to a team lead for the Permanent/Failed decision.</summary>
        public const string SendToReview = "SEND_TO_REVIEW";

        public const string CloseCase = "CLOSE_CASE";

        /// <summary>No rule matched. Queue for a human rather than guess.</summary>
        public const string ManualReview = "MANUAL_REVIEW";

        public static readonly string[] All =
        {
            StartNurture, ContinueNurture, JumpToStep, ScheduleRetry,
            Escalate, SendToReview, CloseCase, ManualReview
        };
    }

    /// <summary>One row of <c>care_progression_rule</c>, as matched.</summary>
    public sealed class ProgressionRule
    {
        public long Id { get; set; }
        public string PublicId { get; set; } = string.Empty;

        public string? FromStage { get; set; }
        public int? FromStepNumber { get; set; }
        public string? OutcomeCode { get; set; }
        public string? IntentCode { get; set; }

        public string Action { get; set; } = ProgressionAction.ManualReview;
        public int? JumpToStep { get; set; }
        public string? CloseReason { get; set; }
        public int? OverrideGapDays { get; set; }

        public int Priority { get; set; }
        public string? Description { get; set; }
    }

    /// <summary>A nurture plan and its steps.</summary>
    public sealed class NurturePlan
    {
        public long Id { get; set; }
        public string PublicId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;

        /// <summary>When false, progression advances regardless of the last outcome.</summary>
        public bool EvaluateOutcome { get; set; } = true;

        public string AssignmentMode { get; set; } = NurtureAssignmentMode.SameIfAvailable;
        public int DefaultGapDays { get; set; } = 7;
        public int MissedAfterDays { get; set; } = 3;

        public List<NurturePlanStep> Steps { get; set; } = new();

        public NurturePlanStep? StepNumber(int number) =>
            Steps.FirstOrDefault(s => s.StepNumber == number);

        public int MaxStepNumber => Steps.Count == 0 ? 0 : Steps.Max(s => s.StepNumber);
    }

    public sealed class NurturePlanStep
    {
        public long Id { get; set; }
        public int StepNumber { get; set; }
        public string? MethodCode { get; set; }

        /// <summary>Null falls back to the plan's default gap.</summary>
        public int? GapDays { get; set; }

        public string? Label { get; set; }
        public string? Guidance { get; set; }
    }

    /// <summary>Who runs the next nurture step. Mirrors the CHECK on the plan.</summary>
    public static class NurtureAssignmentMode
    {
        /// <summary>Always the volunteer already on the case.</summary>
        public const string SameVolunteer = "SAME_VOLUNTEER";

        /// <summary>Keep them if active and under capacity, else reassign.</summary>
        public const string SameIfAvailable = "SAME_IF_AVAILABLE";

        /// <summary>Always pick the least-loaded eligible volunteer.</summary>
        public const string ReassignLeastLoaded = "REASSIGN_LEAST_LOADED";
    }

    /// <summary>
    /// The outcome of evaluating a rule and applying it — what actually happened to
    /// the case, so the caller can tell the user rather than guess.
    /// </summary>
    public sealed class ProgressionResult
    {
        public string Action { get; set; } = ProgressionAction.ManualReview;
        public string Explanation { get; set; } = string.Empty;

        /// <summary>Set when the action created the next scheduled contact.</summary>
        public DateTime? NextContactDue { get; set; }
        public int? NextStepNumber { get; set; }

        /// <summary>Set when the action raised an escalation.</summary>
        public string? EscalationPublicId { get; set; }

        public bool CaseClosed { get; set; }
        public string? CloseReason { get; set; }

        /// <summary>True when the case now needs a human decision.</summary>
        public bool NeedsHumanDecision { get; set; }

        /// <summary>The rule that matched, for traceability.</summary>
        public string? MatchedRule { get; set; }
    }
}
