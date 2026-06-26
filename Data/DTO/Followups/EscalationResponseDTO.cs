namespace RM_CMS.Data.DTO.Followups
{
    public class EscalationResponseDTO
    {
        public string EscalationId { get; set; }
        public string PersonId { get; set; }
        public string VolunteerId { get; set; }
        public string TeamLeadId { get; set; }
        public string Status { get; set; }
        public string EscalationTier { get; set; }
        public DateTime EscalationDate { get; set; }

        public string PersonName { get; set; }
        public string VolunteerName { get; set; }
    }
}
