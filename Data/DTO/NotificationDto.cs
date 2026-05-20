namespace RM_CMS.Data.DTO
{
    public class BroadcastMessageDto
    {
        public string Message { get; set; }
    }

    public class VolunteerChatInfoDto
    {
        public string VolunteerId { get; set; }
        public string FirstName { get; set; }
        public string TelegramChatId { get; set; }
        public string Phone { get; set; }
    }
}