using System.ComponentModel.DataAnnotations.Schema;

namespace RM_CMS.Data.DTO.Volunteers
{
    public class UpdateVolunteerDto
    {
        [Column("first_name")]
        public string? FirstName { get; set; }
        [Column("last_name")]
        public string? LastName { get; set; }
        [Column("email")]
        public string? Email { get; set; }
        [Column("phone")]
        public string? Phone { get; set; }
        [Column("status")]
        public string? Status { get; set; }
        [Column("level")]
        public string? Level { get; set; }
        [Column("end_date")]
        public DateTime? EndDate { get; set; }
        [Column("capacity_band")]
        public string? CapacityBand { get; set; }
        [Column("capacity_min")]
        public int? CapacityMin { get; set; }
        [Column("capacity_max")]
        public int? CapacityMax { get; set; }
        [Column("current_assignments")]
        public int? CurrentAssignments { get; set; }
        [Column("completion_rate")]
        public decimal? CompletionRate { get; set; }
        [Column("emotional_tone")]
        public string? EmotionalTone { get; set; }
        [Column("vnps_score")]
        public int? VnpsScore { get; set; }
        [Column("burnout_risk")]
        public string? BurnoutRisk { get; set; }
        [Column("next_check_in")]
        public DateTime? NextCheckIn { get; set; }
        [Column("campus")]
        public string? Campus { get; set; }
    }
}
