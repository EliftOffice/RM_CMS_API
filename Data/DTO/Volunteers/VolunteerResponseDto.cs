using System.ComponentModel.DataAnnotations.Schema;

namespace RM_CMS.Data.DTO
{
    public class VolunteerResponseDto
    {
        [Column("volunteer_id")]
        public string VolunteerId { get; set; } = string.Empty;
        [Column("first_name")]
        public string FirstName { get; set; } = string.Empty;
        [Column("last_name")]
        public string LastName { get; set; } = string.Empty;
        [Column("email")]
        public string Email { get; set; } = string.Empty;
        [Column("phone")]
        public string? Phone { get; set; }
        [Column("status")]
        public string Status { get; set; } = string.Empty;
        [Column("level")]
        public string Level { get; set; } = string.Empty;
        [Column("start_date")]
        public DateTime StartDate { get; set; }
        [Column("end_date")]
        public DateTime? EndDate { get; set; }
        [Column("capacity_band")]
        public string CapacityBand { get; set; } = string.Empty;
        [Column("capacity_min")]
        public int CapacityMin { get; set; }
        [Column("capacity_max")]
        public int CapacityMax { get; set; }
        [Column("current_assignments")]
        public int CurrentAssignments { get; set; }
        [Column("total_completed")]
        public int TotalCompleted { get; set; }
        [Column("total_assigned")]
        public int TotalAssigned { get; set; }
        [Column("completion_rate")]
        public decimal? CompletionRate { get; set; }
        [Column("avg_response_time")]
        public decimal? AvgResponseTime { get; set; }
        [Column("last_check_in")]
        public DateTime? LastCheckIn { get; set; }
        [Column("next_check_in")]
        public DateTime? NextCheckIn { get; set; }
        [Column("emotional_tone")]
        public string? EmotionalTone { get; set; }
        [Column("vnps_score")]
        public int? VnpsScore { get; set; }
        [Column("burnout_risk")]
        public string? BurnoutRisk { get; set; }
        [Column("team_lead")]
        public string? TeamLead { get; set; }
        [Column("campus")]
        public string? Campus { get; set; }
        [Column("level_0_complete")]
        public DateTime? Level0Complete { get; set; }
        [Column("crisis_trained")]
        public DateTime? CrisisTrained { get; set; }
        [Column("confidentiality_signed")]
        public DateTime? ConfidentialitySigned { get; set; }
        [Column("background_check")]
        public DateTime? BackgroundCheck { get; set; }
        [Column("boundary_violations")]
        public int BoundaryViolations { get; set; }
        [Column("last_violation_date")]
        public DateTime? LastViolationDate { get; set; }
        [Column("created_at")]
        public DateTime CreatedAt { get; set; }
        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; }
    }
}
