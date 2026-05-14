namespace RM_CMS.Data.DTO.Volunteers
{
    public class ManualAssignDto
    {
        public string PersonId { get; set; }
        public string VolunteerId { get; set; }
    }


    public class TelegramMessageRequest
    {
        public string TargetPhoneNumber { get; set; }

        public string Message { get; set; }

        // First Time Only
        public string OTP { get; set; }

        // Optional
        public string TwoFactorPassword { get; set; }
    }
    public class TelegramRequestDTO
    {
        public string PhoneNumber { get; set; }
        public string Message { get; set; }
    }

    public class TelegramApiConfigModel
    {
        public string ApiId { get; set; }
        public string ApiHash { get; set; }
        public string PhoneNumber { get; set; }
    }
}
