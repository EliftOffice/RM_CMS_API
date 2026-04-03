namespace RM_CMS.Data.DTO.TeamLeads
{
    public class CreateCheckInDTO
    {
        public string VolunteerId { get; set; }
        public string TeamLeadId { get; set; }

        public DateTime? CheckInDate { get; set; }
        public int? DurationMin { get; set; }
        public string? MeetingType { get; set; }

        public string? EmotionalTone { get; set; }
        public bool CapacityAdjustment { get; set; }
        public string? NewCapacityBand { get; set; }

        public string? ConcernsNoted { get; set; }
        public bool FollowUpNeeded { get; set; }

        public bool CompletionRateDiscussed { get; set; }
        public bool BoundaryIssues { get; set; }

        public string? TrainingNeeds { get; set; }
        public string? ActionItems { get; set; }

        public DateTime? NextCheckInDate { get; set; }
    }
}
