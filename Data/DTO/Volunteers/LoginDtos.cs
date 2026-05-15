namespace RM_CMS.Data.DTO.Volunteers
{
    public class LoginInitiateRequest
    {
        public string Mobile { get; set; }
    }

    public class LoginVerifyRequest
    {
        public string Mobile { get; set; }
        public string Otp { get; set; }
    }

    public class LoginResultDto
    {
        public bool Success { get; set; }
        public string Token { get; set; }
        public string Role { get; set; }
        public string Redirect { get; set; }
        public string UserId { get; set; }
        public string DisplayName { get; set; }
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
}
