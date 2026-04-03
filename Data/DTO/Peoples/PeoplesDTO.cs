namespace RM_CMS.Data.DTO.Peoples
{
    public class PeoplesDTO
    {
        public string first_name { get; set; }
        public string last_name { get; set; }
        public string email { get; set; }
        public string phone { get; set; }
        public string prayer_requests { get; set; }
        public string specific_needs { get; set; }
        public string interested_in { get; set; }
    }
    public class CreatePeopleDto
    {
        public string first_name { get; set; } = string.Empty;
        public string last_name { get; set; } = string.Empty;
        public string? email { get; set; }
        public string? phone { get; set; }
        public string? age_range { get; set; }
        public string? household_type { get; set; }
        public string? zip_code { get; set; }
        public string visit_type { get; set; } = string.Empty;       
        public string? connection_source { get; set; }
        public string? campus { get; set; } = "Ongole";
        public string follow_up_status { get; set; } = string.Empty;
        public string? follow_up_priority { get; set; }       
        public string? interested_in { get; set; }
        public string? prayer_requests { get; set; }
        public string? specific_needs { get; set; }       
    }
}
