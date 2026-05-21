namespace RM_CMS.Data.DTO.Peoples
{
    public class PeoplesFilterDTO
    {
        public string? Status { get; set; }
        public string? Priority { get; set; }
        public string? AssignedTo { get; set; }
        public string? Campus { get; set; }

        // Optional (future ready 🚀)
        public int Limit { get; set; } = 100;

        public DateTime StartDate { get; set; } = DateTime.Now.AddDays(-7);
        public DateTime EndDate { get; set; } = DateTime.Now.AddDays(1);
    }
}
