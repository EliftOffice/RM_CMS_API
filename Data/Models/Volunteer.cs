namespace RM_CMS.Data.Models
{
    public class Volunteer
    {
        public string VolunteerId { get; set; } = string.Empty;
        
        // Personal Information
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? Phone { get; set; }
        
        // Volunteer Status
        public string Status { get; set; } = string.Empty;
        public string Level { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        
        // Capacity Management
        public string CapacityBand { get; set; } = string.Empty;
        public int CapacityMin { get; set; }
        public int CapacityMax { get; set; }
        public int CurrentAssignments { get; set; }
        
        // Performance Metrics
        public int TotalCompleted { get; set; }
        public int TotalAssigned { get; set; }
        public decimal? CompletionRate { get; set; }
        public decimal? AvgResponseTime { get; set; }
        
        // Health Indicators
        public DateTime? LastCheckIn { get; set; }
        public DateTime? NextCheckIn { get; set; }
        public string? EmotionalTone { get; set; }
        public int? VnpsScore { get; set; }
        public string? BurnoutRisk { get; set; }
        
        // Team Assignment
        public string? TeamLead { get; set; }
        public string? Campus { get; set; }
        
        // Training & Compliance
        public DateTime? Level0Complete { get; set; }
        public DateTime? CrisisTrained { get; set; }
        public DateTime? ConfidentialitySigned { get; set; }
        public DateTime? BackgroundCheck { get; set; }
        
        // Boundary Tracking
        public int BoundaryViolations { get; set; }
        public DateTime? LastViolationDate { get; set; }
        
        // Metadata
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
