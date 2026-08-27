using System.ComponentModel.DataAnnotations;

namespace RM_CMS.Modules.Huddle.Api
{
    /// <summary>
    /// A batch of verdicts from one huddle sitting.
    ///
    /// Deliberately a batch. The MVP saved one row at a time behind a confirmation
    /// dialog, and in three months of production not a single verdict was recorded —
    /// the mechanics, not the idea, were what failed.
    /// </summary>
    public sealed class SubmitVerdictsRequest
    {
        [Required]
        [MinLength(1, ErrorMessage = "Nothing to record.")]
        [MaxLength(200, ErrorMessage = "Too many verdicts in one submission.")]
        public List<VerdictInput> Verdicts { get; set; } = new();
    }

    public sealed class VerdictInput
    {
        [Required][StringLength(26, MinimumLength = 26)]
        public string InteractionId { get; set; } = string.Empty;

        /// <summary>CORRECT / UNDER_ESCALATED / OVER_ESCALATED.</summary>
        [Required][StringLength(20)]
        public string Assessment { get; set; } = string.Empty;

        [StringLength(500)] public string? Note { get; set; }
    }

    public sealed class HuddleAgendaDto
    {
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        public bool IsHuddleDay { get; set; }
        public int HuddleDayOfWeek { get; set; }

        public IReadOnlyList<HuddleItemDto> Items { get; set; } = Array.Empty<HuddleItemDto>();

        public int OlderUnassessedCount { get; set; }
        public int AssessedThisWeek { get; set; }
    }

    public sealed class HuddleItemDto
    {
        public string InteractionId { get; set; } = string.Empty;
        public string? CaseReference { get; set; }
        public string VolunteerName { get; set; } = string.Empty;
        public string PersonName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? Outcome { get; set; }
        public string? Intent { get; set; }
        public DateTime? OccurredAt { get; set; }
        public string? Notes { get; set; }

        /// <summary>Whether this contact actually raised an escalation.</summary>
        public bool RaisedEscalation { get; set; }
    }

    public sealed class VerdictResultDto
    {
        public int Submitted { get; set; }
        public int Recorded { get; set; }

        /// <summary>
        /// Verdicts that named a contact outside the caller's teams, or one that is
        /// not a completed contact. Reported rather than silently dropped.
        /// </summary>
        public int Rejected { get; set; }

        public int UnderEscalated { get; set; }
        public int OverEscalated { get; set; }
    }
}
