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
    
}
