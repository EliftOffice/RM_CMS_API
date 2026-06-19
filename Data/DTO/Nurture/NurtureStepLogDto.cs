namespace RM_CMS.Data.DTO.Nurture
{
    /// <summary>Volunteer submits outcome for a nurture step.</summary>
    public class NurtureStepLogDto
    {
        public string StepId { get; set; } = string.Empty;
        public string SequenceId { get; set; } = string.Empty;
        public string PersonId { get; set; } = string.Empty;
        public string VolunteerId { get; set; } = string.Empty;
        public string ContactStatus { get; set; } = string.Empty;  // Contacted | Not Contacted
        public string? ResponseType { get; set; }                   // Normal | No Response | Needs Follow-Up | Crisis
        public string? Notes { get; set; }
        public string? Method { get; set; } 
        public int? CallDurationMin { get; set; } = 0;
    }

    /// <summary>Team Lead closes the sequence after Step 7.</summary>
    public class CloseSequenceDto
    {
        public string SequenceId { get; set; } = string.Empty;
        public string TeamLeadId { get; set; } = string.Empty;
        public string FinalStatus { get; set; } = string.Empty;     // Permanent | Failed
        public string? FinalNotes { get; set; }
    }

    /// <summary>Summary card used in Team Lead dashboard and huddle view.</summary>
    public class NurtureSequenceSummaryDto
    {
        public string SequenceId { get; set; } = string.Empty;
        public string PersonId { get; set; } = string.Empty;
        public string PersonName { get; set; } = string.Empty;
        public string PersonPhone { get; set; } = string.Empty;
        public string VolunteerId { get; set; } = string.Empty;
        public string VolunteerName { get; set; } = string.Empty;
        public int CurrentStep { get; set; }
        public string Status { get; set; } = string.Empty;
        public string NextMethod { get; set; } = string.Empty;      // Call | Visit
        public DateTime? NextScheduledDate { get; set; }
        public string NextStepStatus { get; set; } = string.Empty;  // Pending | Overdue
        public DateTime StartedAt { get; set; }
    }

    /// <summary>Full step detail for a sequence — used in volunteer assignment card.</summary>
    public class NurtureStepDetailDto
    {
        public string StepId { get; set; } = string.Empty;
        public string SequenceId { get; set; } = string.Empty;
        public string PersonId { get; set; } = string.Empty;
        public string PersonName { get; set; } = string.Empty;
        public string PersonPhone { get; set; } = string.Empty;
        public int StepNumber { get; set; }
        public string Method { get; set; } = string.Empty;
        public DateTime ScheduledDate { get; set; }
        public string Status { get; set; } = string.Empty;
    }

    /// <summary>
    /// Nurture section shown in the Team Lead's team huddle / check-in screen.
    /// Gives TL a snapshot of all active sequences + anything awaiting their final call.
    /// </summary>
    public class HuddleNurtureReviewDto
    {
        public int TotalActive { get; set; }
        public int OverdueSteps { get; set; }
        public int AwaitingFinalDecision { get; set; }
        public IEnumerable<NurtureSequenceSummaryDto> ActiveSequences { get; set; } = new List<NurtureSequenceSummaryDto>();
        public IEnumerable<NurtureSequenceSummaryDto> AwaitingReview { get; set; } = new List<NurtureSequenceSummaryDto>();
    }
}
