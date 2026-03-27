namespace RM_CMS.Data.DTO
{
    public class UpdateVolunteerDto
    {
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Status { get; set; }
        public string? Level { get; set; }
        public DateTime? EndDate { get; set; }
        public string? CapacityBand { get; set; }
        public int? CapacityMin { get; set; }
        public int? CapacityMax { get; set; }
        public int? CurrentAssignments { get; set; }
        public decimal? CompletionRate { get; set; }
        public string? EmotionalTone { get; set; }
        public int? VnpsScore { get; set; }
        public string? BurnoutRisk { get; set; }
        public DateTime? NextCheckIn { get; set; }
        public string? Campus { get; set; }
    }
}
