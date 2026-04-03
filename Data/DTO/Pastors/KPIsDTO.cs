namespace RM_CMS.Data.DTO.Pastors
{
    public class KPIsDTO
    {
        public KPIItemDTO CompletionRate { get; set; }
        public KPIItemDTO FirstContact48h { get; set; }
        public KPIItemDTO EscalationRate { get; set; }
        public KPIItemDTO CrisisHandledSafely { get; set; }
        public KPIItemDTO VolunteerRetention { get; set; }
        public KPIItemDTO SystemVnps { get; set; }

        public int OnTargetCount { get; set; }
        public int TotalCount { get; set; }
    }
}
