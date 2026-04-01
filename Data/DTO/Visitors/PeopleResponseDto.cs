using System.ComponentModel.DataAnnotations.Schema;

namespace RM_CMS.Data.DTO.Visitors
{
    public class PeopleResponseDto
    {
        [Column("person_id")]
        public string PersonId { get; set; } = string.Empty;
        [Column("first_name")]
        public string FirstName { get; set; } = string.Empty;
        [Column("last_name")]
        public string LastName { get; set; } = string.Empty;
        [Column("email")]
        public string? Email { get; set; }
        [Column("phone")]
        public string? Phone { get; set; }
        [Column("age_range")]
        public string? AgeRange { get; set; }
        [Column("household_type")]
        public string? HouseholdType { get; set; }
        [Column("zip_code")]
        public string? ZipCode { get; set; }
        [Column("visit_type")]
        public string VisitType { get; set; } = string.Empty;
        [Column("first_visit_date")]
        public DateTime FirstVisitDate { get; set; }
        [Column("last_visit_date")]
        public DateTime? LastVisitDate { get; set; }
        [Column("visit_count")]
        public int VisitCount { get; set; }
        [Column("connection_source")]
        public string? ConnectionSource { get; set; }
        [Column("campus")]
        public string? Campus { get; set; }
        [Column("follow_up_status")]
        public string FollowUpStatus { get; set; } = string.Empty;
        [Column("follow_up_priority")]
        public string? FollowUpPriority { get; set; }
        [Column("assigned_volunteer")]
        public string? AssignedVolunteer { get; set; }
        [Column("assigned_date")]
        public DateTime? AssignedDate { get; set; }
        [Column("last_contact_date")]
        public DateTime? LastContactDate { get; set; }
        [Column("next_action_date")]
        public DateTime? NextActionDate { get; set; }
        [Column("interested_in")]
        public string? InterestedIn { get; set; }
        [Column("prayer_requests")]
        public string? PrayerRequests { get; set; }
        [Column("specific_needs")]
        public string? SpecificNeeds { get; set; }
        [Column("created_at")]
        public DateTime CreatedAt { get; set; }
        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; }
    }
}
