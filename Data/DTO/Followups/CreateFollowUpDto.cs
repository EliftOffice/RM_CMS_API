using System;

namespace RM_CMS.Data.DTO.Followups
{
    public class CreateFollowUpDto
    {
        public string PersonId { get; set; } = string.Empty;
        public string VolunteerId { get; set; } = string.Empty;
        public string ContactMethod { get; set; } = string.Empty;
        public string ContactStatus { get; set; } = string.Empty; // Contacted / Not Contacted
        public string ResponseType { get; set; } = string.Empty; // Normal / Crisis
        public int? CallDurationMin { get; set; }
        public string? Notes { get; set; }
    }
}
