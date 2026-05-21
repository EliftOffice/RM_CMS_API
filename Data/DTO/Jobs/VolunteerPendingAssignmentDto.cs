namespace RM_CMS.Data.DTO.Jobs
{
    public class VolunteerPendingAssignmentDto
    {
        public string VolunteerId { get; set; } = string.Empty;

        public string FirstName { get; set; } = string.Empty;

        public string LastName { get; set; } = string.Empty;

        public string? Phone { get; set; }

        public string? Email { get; set; }

        public long? TelegramChatId { get; set; }

        public int PendingAssignmentsCount { get; set; }
    }

    public class TeamLeadPendingAssignmentDto
    {
        public string TeamLeadName { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;      

        public long? TelegramChatId { get; set; }

        public int PendingAssignmentsCount { get; set; }
    }
}
