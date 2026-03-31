using System;

namespace RM_CMS.Data.Models
{
    public class FollowUp
    {
        public string FollowUpId { get; set; } = string.Empty;
        public string PersonId { get; set; } = string.Empty;
        public string VolunteerId { get; set; } = string.Empty;
        public int AttemptNumber { get; set; }
        public DateTime AttemptDate { get; set; }
        public string ContactMethod { get; set; } = string.Empty;
        public string ContactStatus { get; set; } = string.Empty; // e.g. Contacted / Not Contacted
        public string ResponseType { get; set; } = string.Empty; // e.g. Normal / Crisis
        public int? CallDurationMin { get; set; }
        public string? Notes { get; set; }
        public int? WeekNumber { get; set; }
        public string EscalationAppropriate { get; set; } = "Not-Assessed";
        public DateTime CreatedAt { get; set; }
    }
}
