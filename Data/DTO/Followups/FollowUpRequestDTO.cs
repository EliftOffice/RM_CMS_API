namespace RM_CMS.Data.DTO.Followups
{
    public class FollowUpRequestDTO
    {
        
        public string? follow_up_id { get; set; }=string.Empty;
        public string person_id { get; set; } = string.Empty;
        public string volunteer_id { get; set; } = string.Empty;
        public string team_lead_id { get; set; } = string.Empty;
        public string contact_status { get; set; } = string.Empty;
        public string response_type { get; set; } = string.Empty;
        public string contact_method { get; set; } = string.Empty;
        public int? call_duration_min { get; set; }
        public string notes { get; set; } = string.Empty;
        public string tags { get; set; } = string.Empty;
        public string team_lead_name { get; set; } = string.Empty;
        public string telegram_chat_id { get; set; } = string.Empty;
        public string volunteer_name { get; set; } = string.Empty;
    }
}
