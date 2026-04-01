using System.ComponentModel.DataAnnotations.Schema;

namespace RM_CMS.Data.DTO.Volunteers
{
    public class CreateVolunteerDto
    {
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
        [Column("capacity_band")]
        public string CapacityBand { get; set; } = string.Empty;
        [Column("capacity_min")]
        public int CapacityMin { get; set; }
        [Column("capacity_max")]
        public int CapacityMax { get; set; }
        [Column("team_lead")]
        public string? TeamLead { get; set; }
        [Column("campus")]
        public string? Campus { get; set; } = "Ongole";
        [Column("level_0_complete")]
        public DateTime? Level0Complete { get; set; }
        [Column("crisis_trained")]
        public DateTime? CrisisTrained { get; set; }
        [Column("confidentiality_signed")]
        public DateTime? ConfidentialitySigned { get; set; }
        [Column("background_check")]
        public DateTime? BackgroundCheck { get; set; }
    }
}
