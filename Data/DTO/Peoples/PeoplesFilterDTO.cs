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
    }
}
