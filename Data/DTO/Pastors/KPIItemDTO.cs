namespace RM_CMS.Data.DTO.Pastors
{
    public class KPIItemDTO
    {
        public double Current { get; set; }
        public double Target { get; set; }
        public double? Trend { get; set; } // only for completion rate
        public string Status { get; set; }
    }
}
