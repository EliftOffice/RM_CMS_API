namespace RM_CMS.Data.Models
{
    public class NurtureSequence
    {
        public string SequenceId { get; set; } = string.Empty;
        public string PersonId { get; set; } = string.Empty;
        public string VolunteerId { get; set; } = string.Empty;
        public string? TeamLeadId { get; set; }
        public int CurrentStep { get; set; } = 1;
        public string Status { get; set; } = "Active"; // Active | InReview | Permanent | Failed
        public DateTime StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string? FinalNotes { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
