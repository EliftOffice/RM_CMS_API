namespace RM_CMS.Data.Models
{
    public class Event
    {
        public long Id { get; set; }
        public string Title { get; set; }
        public string VenueName { get; set; }
        public string Address { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public int Radius { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public bool IsActive { get; set; }
        public string RecurrenceType { get; set; }
        public string RecurrenceDay { get; set; }
        public DateTime? RepeatUntil { get; set; }
        public bool ReuseSameLocation { get; set; }
        public bool AutoActivateRecurring { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
