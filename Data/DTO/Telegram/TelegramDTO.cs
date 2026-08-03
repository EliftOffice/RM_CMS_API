namespace RM_CMS.Data.DTO.Telegram
{
   
    public class TelegramUpdate
    {
        public TelegramMessage Message { get; set; }
    }

    public class TelegramMessage
    {
        public TelegramChat Chat { get; set; }

        public string Text { get; set; }
    }

    public class TelegramChat
    {
        public long Id { get; set; }

        public string Username { get; set; }

        public string First_Name { get; set; }

        public string Last_Name { get; set; }
    }
}
