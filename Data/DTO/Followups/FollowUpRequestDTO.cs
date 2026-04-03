namespace RM_CMS.Data.DTO.Followups
{
    public class FollowUpRequestDTO
    {
        public string person_id { get; set; }
        public string volunteer_id { get; set; }
        public string team_lead_id { get; set; }
        public string contact_status { get; set; }
        public string response_type { get; set; }
        public string contact_method { get; set; }
        public int? call_duration_min { get; set; }
        public string notes { get; set; }
        public string tags { get; set; }
    }
}
