using System;

namespace RM_CMS.Data.DTO.Followups
{
    public class FollowUpResponseDto
    {
        public string FollowUpId { get; set; } = string.Empty;
        public string PersonId { get; set; } = string.Empty;
        public string VolunteerId { get; set; } = string.Empty;
        public int AttemptNumber { get; set; }
        public DateTime AttemptDate { get; set; }
        public string ContactMethod { get; set; } = string.Empty;
        public string ContactStatus { get; set; } = string.Empty;
        public string ResponseType { get; set; } = string.Empty;
        public int? CallDurationMin { get; set; }
        public string? Notes { get; set; }
        public string EscalationAppropriate { get; set; } = "Not-Assessed";
        public DateTime CreatedAt { get; set; }
    }
}
