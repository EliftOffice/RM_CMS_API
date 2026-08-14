using System.ComponentModel.DataAnnotations;

namespace RM_CMS.Modules.Care.Api
{
    // -------------------------------------------------------------------------
    // Requests
    // -------------------------------------------------------------------------

    /// <summary>Opens a care journey for an existing person.</summary>
    public sealed class OpenCaseRequest
    {
        [Required][StringLength(26, MinimumLength = 26)]
        public string PersonId { get; set; } = string.Empty;

        [StringLength(30)] public string? VisitType { get; set; }
        [StringLength(30)] public string? ConnectionSource { get; set; }
        public DateTime? FirstVisitOn { get; set; }

        [StringLength(10)] public string? Priority { get; set; }

        /// <summary>Assign immediately to the least-loaded eligible volunteer.</summary>
        public bool AutoAssign { get; set; } = true;
    }

    public sealed class AssignCaseRequest
    {
        [Required][StringLength(26, MinimumLength = 26)]
        public string VolunteerId { get; set; } = string.Empty;

        /// <summary>Why the case moved. Recorded in the assignment history.</summary>
        [StringLength(30)] public string? Reason { get; set; }

        /// <summary>
        /// A note explaining the handover to the incoming volunteer. Recommended on
        /// a manual reassignment — they are inheriting a relationship, not a ticket.
        /// </summary>
        [StringLength(2000)] public string? HandoverNote { get; set; }

        [Required] public int RowVersion { get; set; }
    }

    /// <summary>
    /// Logs a contact. This is the main funnel: the outcome and intent together
    /// decide, via the admin-configured rules, what happens to the case next.
    /// </summary>
    public sealed class LogInteractionRequest
    {
        [Required][StringLength(30)]
        public string OutcomeCode { get; set; } = string.Empty;

        /// <summary>
        /// What the visitor wants next. Separate from the outcome because a call can
        /// go perfectly well and still end with "please don't contact me again".
        /// </summary>
        [StringLength(30)] public string? IntentCode { get; set; }

        [StringLength(30)] public string? MethodCode { get; set; }

        public DateTime? OccurredAt { get; set; }

        [Range(0, 600)] public int? DurationMinutes { get; set; }

        [StringLength(4000)] public string? Notes { get; set; }

        [Required] public int RowVersion { get; set; }
    }

    /// <summary>Raises a concern by hand, outside the automatic routing.</summary>
    public sealed class RaiseEscalationRequest
    {
        [Required][StringLength(40)]
        public string ReasonCode { get; set; } = string.Empty;

        [Required][StringLength(4000, MinimumLength = 10,
            ErrorMessage = "Describe the concern in at least 10 characters.")]
        public string Description { get; set; } = string.Empty;

        [StringLength(20)] public string? Tier { get; set; }

        [Required] public int CaseRowVersion { get; set; }
    }

    public sealed class ResolveEscalationRequest
    {
        [Required][StringLength(40)]
        public string OutcomeCode { get; set; } = string.Empty;

        [StringLength(4000)] public string? ResolutionNotes { get; set; }
        [StringLength(150)]  public string? ResourceConnected { get; set; }

        /// <summary>Required when the reason is flagged as needing a documented protocol.</summary>
        public bool? ProtocolFollowed { get; set; }
        public bool? AuthoritiesContacted { get; set; }
        public bool? VolunteerDebriefed { get; set; }

        /// <summary>Days before the paused case resumes. Defaults to the plan gap.</summary>
        [Range(0, 90)] public int? ResumeInDays { get; set; }

        [StringLength(20)] public string? Status { get; set; }

        [Required] public int RowVersion { get; set; }
    }

    /// <summary>The team lead's decision when a case reaches review.</summary>
    public sealed class ReviewDecisionRequest
    {
        [Required][StringLength(30)]
        public string CloseReason { get; set; } = string.Empty;

        [StringLength(2000)] public string? Notes { get; set; }

        [Required] public int RowVersion { get; set; }
    }

    public sealed class AddNoteRequest
    {
        [Required][StringLength(4000, MinimumLength = 1)]
        public string Body { get; set; } = string.Empty;

        [StringLength(30)]  public string? NoteType { get; set; }
        [StringLength(255)] public string? Tags { get; set; }

        /// <summary>Restricts the note to a role — a pastoral confidence, for instance.</summary>
        public bool IsPrivate { get; set; }

        [StringLength(30)] public string? VisibleToRole { get; set; }
    }

    // -------------------------------------------------------------------------
    // Responses
    // -------------------------------------------------------------------------

    public sealed class CaseDto
    {
        public string Id { get; set; } = string.Empty;
        public string? ReferenceCode { get; set; }

        public string PersonId { get; set; } = string.Empty;
        public string PersonName { get; set; } = string.Empty;
        public string? PersonPhone { get; set; }
        public bool PersonDoNotContact { get; set; }

        public string? CampusName { get; set; }
        public string? VolunteerId { get; set; }
        public string? VolunteerName { get; set; }
        public string? TeamName { get; set; }

        public string Stage { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Priority { get; set; } = string.Empty;

        public int CurrentStepNumber { get; set; }
        public DateTime? NextStepDueOn { get; set; }
        public DateTime? NextActionOn { get; set; }
        public DateTime? AwaitingReviewSince { get; set; }

        public DateTime OpenedAt { get; set; }
        public DateTime? FirstContactAt { get; set; }
        public DateTime? LastContactAt { get; set; }
        public DateTime? ClosedAt { get; set; }
        public string? CloseReason { get; set; }

        public int ContactAttemptCount { get; set; }
        public int RowVersion { get; set; }

        /// <summary>Populated on the detail view only.</summary>
        public List<InteractionDto> Interactions { get; set; } = new();
        public List<EscalationDto> Escalations { get; set; } = new();
        public List<NoteDto> Notes { get; set; } = new();
        public List<AssignmentDto> AssignmentHistory { get; set; } = new();
    }

    public sealed class CaseSummaryDto
    {
        public string Id { get; set; } = string.Empty;
        public string? ReferenceCode { get; set; }
        public string PersonName { get; set; } = string.Empty;
        public string? PersonPhone { get; set; }
        public string? VolunteerName { get; set; }
        public string Stage { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Priority { get; set; } = string.Empty;
        public DateTime? NextActionOn { get; set; }
        public DateTime? LastContactAt { get; set; }
        public int ContactAttemptCount { get; set; }
        public DateTime OpenedAt { get; set; }
    }

    public sealed class InteractionDto
    {
        public string Id { get; set; } = string.Empty;
        public string? CaseId { get; set; }
        public string? CaseReference { get; set; }
        public string? PersonName { get; set; }
        public string? PersonPhone { get; set; }

        public string Stage { get; set; } = string.Empty;
        public int SequenceNumber { get; set; }
        public string? Method { get; set; }
        public DateTime? ScheduledOn { get; set; }
        public DateTime? OccurredAt { get; set; }
        public string Status { get; set; } = string.Empty;
        public bool? MadeContact { get; set; }
        public string? Outcome { get; set; }
        public string? OutcomeLabel { get; set; }
        public string? Intent { get; set; }
        public string? IntentLabel { get; set; }
        public int? DurationMinutes { get; set; }
        public string? Notes { get; set; }
        public string? VolunteerName { get; set; }
        public bool IsOverdue { get; set; }
        public int RowVersion { get; set; }
    }

    public sealed class EscalationDto
    {
        public string Id { get; set; } = string.Empty;
        public string? ReferenceCode { get; set; }
        public string? CaseId { get; set; }
        public string? PersonName { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string? ReasonLabel { get; set; }
        public bool RequiresProtocol { get; set; }
        public string Tier { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string? RaisedByName { get; set; }
        public string? AssignedToName { get; set; }
        public DateTime RaisedAt { get; set; }
        public DateTime? AcknowledgedAt { get; set; }
        public DateTime? ResolvedAt { get; set; }
        public string? Outcome { get; set; }
        public string? ResolutionNotes { get; set; }
        public double HoursWaiting { get; set; }
        public int ReminderCount { get; set; }
        public bool PastorAlerted { get; set; }
        public int RowVersion { get; set; }
    }

    public sealed class NoteDto
    {
        public string Id { get; set; } = string.Empty;
        public string? NoteType { get; set; }
        public string Body { get; set; } = string.Empty;
        public string? Tags { get; set; }
        public bool IsPrivate { get; set; }
        public string? VisibleToRole { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? CreatedBy { get; set; }
    }

    public sealed class AssignmentDto
    {
        public string VolunteerName { get; set; } = string.Empty;
        public DateTime AssignedAt { get; set; }
        public DateTime? UnassignedAt { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string? AssignedBy { get; set; }
    }

    /// <summary>
    /// What logging a contact actually did — so the volunteer is told the
    /// consequence rather than left guessing.
    /// </summary>
    public sealed class InteractionResultDto
    {
        public string Action { get; set; } = string.Empty;
        public string Explanation { get; set; } = string.Empty;
        public string? MatchedRule { get; set; }

        public DateTime? NextContactDue { get; set; }
        public int? NextStepNumber { get; set; }
        public string? EscalationId { get; set; }
        public bool CaseClosed { get; set; }
        public string? CloseReason { get; set; }
        public bool NeedsHumanDecision { get; set; }

        public CaseDto Case { get; set; } = new();
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
