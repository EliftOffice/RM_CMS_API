using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace RM_CMS.Data.Models
{
    [Table("follow_ups")]
    public class FollowUp
    {
        [Column("follow_up_id")]
        public string FollowUpId { get; set; } = string.Empty;
        [Column("person_id")]
        public string PersonId { get; set; } = string.Empty;
        [Column("volunteer_id")]
        public string VolunteerId { get; set; } = string.Empty;
        [Column("attempt_number")]
        public int AttemptNumber { get; set; }
        [Column("attempt_date")]
        public DateTime AttemptDate { get; set; }
        [Column("contact_method")]
        public string ContactMethod { get; set; } = string.Empty;
        [Column("contact_status")]
        public string ContactStatus { get; set; } = string.Empty; // e.g. Contacted / Not Contacted
        [Column("response_type")]
        public string ResponseType { get; set; } = string.Empty; // e.g. Normal / Crisis
        [Column("call_duration_min")]
        public int? CallDurationMin { get; set; }
        [Column("notes")]
        public string? Notes { get; set; }
        [Column("week_number")]
        public int? WeekNumber { get; set; }
        [Column("created_at")]
        public DateTime CreatedAt { get; set; }
    }
}
