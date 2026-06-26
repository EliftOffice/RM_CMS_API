namespace RM_CMS.Data.DTO.Volunteers
{
    public class TelegramChatDto
    {
        public string ChatId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    public class UpdateVolunteerTelegramDto
    {
        public string VolunteerId { get; set; } = string.Empty;
        public string TelegramChatId { get; set; } = string.Empty;
    }
}
