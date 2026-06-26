namespace RM_CMS.Data.DTO.Volunteers
{
    public class VolunteerDetailsDto
    {
        public string VolunteerId { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Phone { get; set; }
        public string TeamLeadId { get; set; }
        public string TeamLeadName { get; set; }
        public string CapacityBand { get; set; }
        public int? CapacityMin { get; set; }
        public int? CapacityMax { get; set; }
    }
}
