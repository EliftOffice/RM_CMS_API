namespace RM_CMS.Data.DTO.Followups
{
    public class FollowUpsFilterDTO
    {
        public string? VolunteerId { get; set; }
        public string? PersonId { get; set; }
        public int? Week { get; set; }
        public string? Status { get; set; }
        public int Limit { get; set; } = 100;
    }
}
