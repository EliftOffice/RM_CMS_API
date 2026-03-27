namespace RM_CMS.Data.DTO
{
    public class CreateVolunteerDto
    {
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
        public string? TeamLead { get; set; }
        public string? Campus { get; set; }
        public DateTime? Level0Complete { get; set; }
        public DateTime? CrisisTrained { get; set; }
        public DateTime? ConfidentialitySigned { get; set; }
        public DateTime? BackgroundCheck { get; set; }
    }
}
