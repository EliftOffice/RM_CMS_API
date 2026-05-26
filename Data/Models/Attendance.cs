namespace RM_CMS.Data.Models
{
    public class Attendance
    {
        public long Id { get; set; }
        public long UserId { get; set; }
        public long EventId { get; set; }
        public string EventTitle { get; set; }
        public DateTime AttendanceDay { get; set; }
        public DateTime CheckinTime { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string DeviceInfo { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
