namespace RM_CMS.Data.DTO
{
    public class VolunteerResponseDto
    {
        public string VolunteerId { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string Status { get; set; } = string.Empty;
        public string Level { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public string CapacityBand { get; set; } = string.Empty;
        public int CapacityMin { get; set; }
        public int CapacityMax { get; set; }
        public int CurrentAssignments { get; set; }
        public int TotalCompleted { get; set; }
        public int TotalAssigned { get; set; }
        public decimal? CompletionRate { get; set; }
        public decimal? AvgResponseTime { get; set; }
        public DateTime? LastCheckIn { get; set; }
        public DateTime? NextCheckIn { get; set; }
        public string? EmotionalTone { get; set; }
        public int? VnpsScore { get; set; }
        public string? BurnoutRisk { get; set; }
        public string? TeamLead { get; set; }
        public string? Campus { get; set; }
        public DateTime? Level0Complete { get; set; }
        public DateTime? CrisisTrained { get; set; }
        public DateTime? ConfidentialitySigned { get; set; }
        public DateTime? BackgroundCheck { get; set; }
        public int BoundaryViolations { get; set; }
        public DateTime? LastViolationDate { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
