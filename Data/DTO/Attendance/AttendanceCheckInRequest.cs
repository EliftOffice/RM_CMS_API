namespace RM_CMS.Data.DTO.Attendance
{
    public class AttendanceCheckInRequest
    {
        public long UserId { get; set; }
        public long EventId { get; set; }
        public string EventTitle { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string DeviceInfo { get; set; } = string.Empty;
        public bool IsSynced { get; set; }
    }
}
