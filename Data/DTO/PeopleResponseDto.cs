namespace RM_CMS.Data.DTO
{
    public class PeopleResponseDto
    {
        public string PersonId { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? AgeRange { get; set; }
        public string? HouseholdType { get; set; }
        public string? ZipCode { get; set; }
        public string VisitType { get; set; } = string.Empty;
        public DateTime FirstVisitDate { get; set; }
        public DateTime? LastVisitDate { get; set; }
        public int VisitCount { get; set; }
        public string? ConnectionSource { get; set; }
        public string? Campus { get; set; }
        public string FollowUpStatus { get; set; } = string.Empty;
        public string? FollowUpPriority { get; set; }
        public string? AssignedVolunteer { get; set; }
        public DateTime? AssignedDate { get; set; }
        public DateTime? LastContactDate { get; set; }
        public DateTime? NextActionDate { get; set; }
        public string? InterestedIn { get; set; }
        public string? PrayerRequests { get; set; }
        public string? SpecificNeeds { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
