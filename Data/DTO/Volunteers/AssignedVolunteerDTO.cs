namespace RM_CMS.Data.DTO.Volunteers
{
    public class AssignedVolunteerDTO
    {
        public string volunteer_id { get; set; } = string.Empty;
        public string first_name { get; set; } = string.Empty;
        public string last_name { get; set; } = string.Empty;
        public string capacity_max { get; set; } = string.Empty;
        public string current_assignments { get; set; } = string.Empty;

        public string people_id { get; set; } = string.Empty;
        public string people_name { get;set; } = string.Empty;  

    }
}