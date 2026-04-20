namespace RM_CMS.Data.DTO.Peoples
{
    public class CreatePersonDto
    {
        public string FirstName { get; set; } = null!;
        public string LastName { get; set; } = null!;
        public string Phone { get; set; } = null!;
        public string? Email { get; set; }
        public string? AgeRange { get; set; }
        public string? Address { get; set; }
        public string? HouseholdType { get; set; }
        public string? ConnectionSource { get; set; }
        public string? RefName { get; set; }
        public string? RefPhone { get; set; }
        public string? PrayerRequests { get; set; }
       
    }
    public class CreatePeopleDto
    {
        public string first_name { get; set; } = string.Empty;
        public string last_name { get; set; } = string.Empty;
        public string? email { get; set; } 
        public string? phone { get; set; }
        public string? age_range { get; set; } = "18-25";
        public string? household_type { get; set; } = "Single";
        public string? zip_code { get; set; } = "522147";
        public string visit_type { get; set; } = "First-Time Visitor";
        public string? connection_source { get; set; } = "Other";
        public string? campus { get; set; } = "Ongole";
        public string follow_up_status { get; set; } = string.Empty;
        public string? follow_up_priority { get; set; } 
        public string? interested_in { get; set; }
        public string? prayer_requests { get; set; }
        public string? specific_needs { get; set; }       
    }
}
