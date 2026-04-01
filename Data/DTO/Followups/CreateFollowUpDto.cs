using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace RM_CMS.Data.DTO.Followups
{
    public class CreateFollowUpDto
    {
        [Column("person_id")]
        public string PersonId { get; set; } = string.Empty;
        [Column("volunteer_id")]
        public string VolunteerId { get; set; } = string.Empty;
        [Column("contact_method")]
        public string ContactMethod { get; set; } = string.Empty;
        [Column("contact_status")]
        public string ContactStatus { get; set; } = string.Empty; // Contacted / Not Contacted
        [Column("response_type")]
        public string ResponseType { get; set; } = string.Empty; // Normal / Crisis
        [Column("call_duration_min")]
        public int? CallDurationMin { get; set; }
        [Column("notes")]
        public string? Notes { get; set; }
    }
}
