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
    public class TeamLeadLookupDto
    {
        public string TeamLeadId { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string RoleType { get; set; }
        public string Phone { get; set; }
        public string Email { get; set; }
        public string OTP { get; set; }
        public string ChatID { get; set; }
    }

    public class UserLookupDto
    {
        public string Id { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Phone { get; set; }
        public string Role { get; set; }
        public string OTP { get; set; }
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
