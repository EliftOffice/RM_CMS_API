namespace RM_CMS.Data.Models
{
    public class NurtureStep
    {
        public string StepId { get; set; } = string.Empty;
        public string SequenceId { get; set; } = string.Empty;
        public string PersonId { get; set; } = string.Empty;
        public string VolunteerId { get; set; } = string.Empty;
        public int StepNumber { get; set; }           // 1–7
        public string Method { get; set; } = string.Empty; // Call | Visit
        public DateTime ScheduledDate { get; set; }
        public string Status { get; set; } = "Pending"; // Pending | Done | Missed
        public string? ContactStatus { get; set; }
        public string? ResponseType { get; set; }
        public string? Notes { get; set; }
        public DateTime? CompletedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
