namespace RM_CMS.Data.DTO.Events
{
    public class EventDto
    {
        public long Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string VenueName { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public int Radius { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public bool IsActive { get; set; }
        public string RecurrenceType { get; set; } = "once";
        public string RecurrenceDay { get; set; } = string.Empty;
        public DateTime? RepeatUntil { get; set; }
        public bool ReuseSameLocation { get; set; }
        public bool AutoActivateRecurring { get; set; }
        public bool IsAttended { get; set; }
    }

    public class GenerateSongsRequestDto
    {
        public DateTime ServiceDate { get; set; }

        public int ServiceType { get; set; }

        public string ServiceCategory { get; set; }
    }
}
