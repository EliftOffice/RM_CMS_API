using RM_CMS.DAL.Peoples;
using RM_CMS.DAL.Volunteers;
using RM_CMS.Data.DTO;
using RM_CMS.Data.DTO.Jobs;
using RM_CMS.Data.DTO.Volunteers;
using RM_CMS.Data.Models;
using RM_CMS.Utilities;

namespace RM_CMS.BLL.Volunteers
{
    public interface IVolunteersBLL
    {
        Task<ApiResponse<AssignedVolunteerDTO>> AssignToVolunteerAsync(string personId);
        Task<ApiResponse<AssignedVolunteerDTO>> ManualAssignToVolunteerAsync(string personId, string volunteerId);
        Task<ApiResponse<Volunteer>> GetVolunteerByIdAsync(string volunteerId);
        Task<ApiResponse<List<People>>> GetVolunteerAssignmentsAsync(string volunteerId);

        Task<ApiResponse<VolunteerResponseDto>> CreateVolunteerAsync(CreateVolunteerDto dto);
        Task<ApiResponse<List<Volunteer>>> GetVolunteersAsync();
        Task<ApiResponse<List<VolunteerLookupDto>>> GetVolunteersByMobileAsync(string mobile);
        Task<ApiResponse<List<UserLookupDto>>> GetVolunteersByMobileAsyncV1(string mobile);
        Task<ApiResponse<string>> UpdateVolunteerMobileAsync(UpdateVolunteerMobileDto dto);

        // New methods
        Task<ApiResponse<TelegramChatDto>> GetLatestTelegramChatAsync();
        Task<ApiResponse<bool>> UpdateVolunteerTelegramAsync(UpdateVolunteerTelegramDto dto);

        Task<ApiResponse<List<VolunteerPendingAssignmentDto>>> GetVolunteersWithPendingAssignmentsAsync();
        Task<ApiResponse<bool>> SendTelegramMessageAsync(string chatId, string message);
    }
    public class VolunteersBLL : IVolunteersBLL
    {
        private readonly IVolunteersDAL _volunteersDAL;

        public VolunteersBLL(IVolunteersDAL volunteersDAL)
        {
            _volunteersDAL = volunteersDAL;
        }

        public async Task<ApiResponse<AssignedVolunteerDTO>> AssignToVolunteerAsync(string personId)
        {
            try
            {
                return await _volunteersDAL.AssignToVolunteerAsync(personId);
            }
            catch (Exception)
            {
                return new ApiResponse<AssignedVolunteerDTO>(
                    ResponseType.Error,
                    "Error assigning volunteer",
                    new AssignedVolunteerDTO()
                );
            }
        }

        public async Task<ApiResponse<AssignedVolunteerDTO>> ManualAssignToVolunteerAsync(string personId, string volunteerId)
        {
            try
            {
                return await _volunteersDAL.ManualAssignToVolunteerAsync(personId, volunteerId);
            }
            catch (Exception ex)
            {
                return new ApiResponse<AssignedVolunteerDTO>(ResponseType.Error, $"Error performing manual assign: {ex.Message}", null);
            }
        }

        public async Task<ApiResponse<Volunteer>> GetVolunteerByIdAsync(string volunteerId)
        {
            try
            {
                return await _volunteersDAL.GetVolunteerByIdAsync(volunteerId);
            }
            catch (Exception ex)
            {
                return new ApiResponse<Volunteer>(
                    ResponseType.Error,
                    $"Error retrieving volunteer: {ex.Message}",
                    new Volunteer()
                );
            }
        }

        public async Task<ApiResponse<List<Volunteer>>> GetVolunteersAsync()
        {
            try
            {
                return await _volunteersDAL.GetVolunteersAsync();
            }
            catch (Exception ex)
            {
                return new ApiResponse<List<Volunteer>>(
                    ResponseType.Error,
                    $"Error retrieving volunteers: {ex.Message}",
                    new List<Volunteer>()
                );
            }
        }
        public async Task<ApiResponse<List<People>>> GetVolunteerAssignmentsAsync(string volunteerId)
        {
            try
            {
                return await _volunteersDAL.GetVolunteerAssignmentsAsync(volunteerId);
            }
            catch (Exception ex)
            {
                return new ApiResponse<List<People>>(
                    ResponseType.Error,
                    $"Error retrieving assignments: {ex.Message}",
                    null
                );
            }
        }

        public async Task<ApiResponse<VolunteerResponseDto>> CreateVolunteerAsync(CreateVolunteerDto dto)
        {
            try
            {
                // 🔹 1. Validate DTO
                if (dto == null)
                {
                    return new ApiResponse<VolunteerResponseDto>(
                        ResponseType.Warning,
                        "Invalid payload",
                        null
                    );
                }

                if (string.IsNullOrWhiteSpace(dto.FirstName) ||
                    string.IsNullOrWhiteSpace(dto.Email))
                {
                    return new ApiResponse<VolunteerResponseDto>(
                        ResponseType.Warning,
                        "First Name and Email are required",
                        null
                    );
                }

                // 🔹 2. Normalize
                dto.Email = dto.Email.Trim().ToLower();
                dto.FirstName = dto.FirstName.Trim();
                dto.LastName = dto.LastName?.Trim();

                // 🔹 3. Defaults (align with DB)
                dto.CapacityBand = string.IsNullOrWhiteSpace(dto.CapacityBand)
                    ? "Balanced"   // ✅ FIXED (matches DB)
                    : dto.CapacityBand;

                if (dto.StartDate == default)
                    dto.StartDate = DateTime.Now;

                // 🔹 4. Duplicate Check
                var exists = await _volunteersDAL.ExistsByEmailAsync(dto.Email);
                if (exists.Data)
                {
                    return new ApiResponse<VolunteerResponseDto>(
                        ResponseType.Warning,
                        "Email already exists",
                        null
                    );
                }

                // 🔹 5. Call DAL (NO business logic here)
                return await _volunteersDAL.CreateVolunteerAsync(dto);
            }
            catch (Exception ex)
            {
                return new ApiResponse<VolunteerResponseDto>(
                    ResponseType.Error,
                    ex.Message,
                    null
                );
            }
        }

        public async Task<ApiResponse<List<UserLookupDto>>> GetVolunteersByMobileAsyncV1(string mobile)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(mobile))
                {
                    return new ApiResponse<List<UserLookupDto>>(
                        ResponseType.Warning,
                        "Mobile number is required",
                        new List<UserLookupDto>()
                    );
                }

                // Volunteers
                var volunteerResult = await _volunteersDAL.GetVolunteersAsyncByMobile(mobile);

                if (volunteerResult.ResponseType == ResponseType.Success &&
                    volunteerResult.Data != null &&
                    volunteerResult.Data.Any())
                {
                    return new ApiResponse<List<UserLookupDto>>(
                        ResponseType.Success,
                        "Volunteer found",
                        volunteerResult.Data.Select(v => new UserLookupDto
                        {
                            Id = v.VolunteerId,
                            FirstName = v.FirstName,
                            LastName = v.LastName,
                            Phone = v.Phone,
                            Role = "volunteer",
                            OTP = v.OTP
                        }).ToList()
                    );
                }

                // Team Lead fallback
                var teamLeadResult = await _volunteersDAL.GetTeamLeadByMobileAsync(mobile);

                if (teamLeadResult.ResponseType == ResponseType.Success &&
                    teamLeadResult.Data != null)
                {
                    return new ApiResponse<List<UserLookupDto>>(
                        ResponseType.Success,
                        "Team lead found",
                        new List<UserLookupDto>
                        {
                    new UserLookupDto
                    {
                        Id = teamLeadResult.Data.TeamLeadId,
                        FirstName = teamLeadResult.Data.FirstName,
                        LastName = teamLeadResult.Data.LastName,
                        Phone = teamLeadResult.Data.Phone,
                        Role =teamLeadResult.Data.RoleType,
                        OTP = teamLeadResult.Data.OTP
                    }
                        }
                    );
                }
               

                // User fallback
                var user = await _volunteersDAL.GetUserByMobileAsync(mobile);

                if (user.ResponseType == ResponseType.Success &&
                    user.Data != null)
                {
                    return new ApiResponse<List<UserLookupDto>>(
                        ResponseType.Success,
                        "User found",
                        new List<UserLookupDto>
                        {
                    new UserLookupDto
                    {
                        Id = user.Data.TeamLeadId,
                        FirstName = user.Data.FirstName,
                        LastName = user.Data.LastName,
                        Phone = user.Data.Phone,
                        Role =user.Data.RoleType,
                        OTP = user.Data.OTP
                    }
                        }
                    );
                }

                return new ApiResponse<List<UserLookupDto>>(
                    ResponseType.Warning,
                    "No user found with this mobile number",
                    new List<UserLookupDto>()
                );
            }
            catch (Exception ex)
            {
                return new ApiResponse<List<UserLookupDto>>(
                    ResponseType.Error,
                    $"Error retrieving user: {ex.Message}",
                    new List<UserLookupDto>()
                );
            }
        }
        public async Task<ApiResponse<List<VolunteerLookupDto>>> GetVolunteersByMobileAsync(string mobile)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(mobile))
                {
                    return new ApiResponse<List<VolunteerLookupDto>>(
                        ResponseType.Warning,
                        "Mobile number is required",
                        new List<VolunteerLookupDto>()
                    );
                }

                // ✅ Call DAL
                return await _volunteersDAL.GetVolunteersAsyncByMobile(mobile);
            }
            catch (Exception ex)
            {
                return new ApiResponse<List<VolunteerLookupDto>>(
                    ResponseType.Error,
                    $"Error retrieving volunteer: {ex.Message}",
                    new List<VolunteerLookupDto>()
                );
            }
        }

        public async Task<ApiResponse<string>> UpdateVolunteerMobileAsync(UpdateVolunteerMobileDto dto)
        {
            try
            {
                // 🔹 1. Basic validation
                if (dto == null)
                {
                    return new ApiResponse<string>(
                        ResponseType.Warning,
                        "Invalid payload",
                        null
                    );
                }

                if (string.IsNullOrWhiteSpace(dto.VolunteerId) ||
                    string.IsNullOrWhiteSpace(dto.NewMobile))
                {
                    return new ApiResponse<string>(
                        ResponseType.Warning,
                        "VolunteerId and Mobile are required",
                        null
                    );
                }

                // 🔹 2. Normalize
                dto.VolunteerId = dto.VolunteerId.Trim();
                dto.NewMobile = dto.NewMobile.Trim();

                // 🔹 3. Validate mobile (10 digits)
                if (!System.Text.RegularExpressions.Regex.IsMatch(dto.NewMobile, @"^\d{10}$"))
                {
                    return new ApiResponse<string>(
                        ResponseType.Warning,
                        "Mobile number must be exactly 10 digits",
                        null
                    );
                }

                // 🔹 4. Call DAL (correct signature)
                return await _volunteersDAL.UpdateVolunteerMobileAsync(
                    dto.VolunteerId,
                    dto.NewMobile
                );
            }
            catch (Exception ex)
            {
                return new ApiResponse<string>(
                    ResponseType.Error,
                    $"Error updating mobile: {ex.Message}",
                    null
                );
            }
        }

        public async Task<ApiResponse<TelegramChatDto>> GetLatestTelegramChatAsync()
        {
            try
            {
                return await ((VolunteersDAL)_volunteersDAL).GetLatestTelegramChatAsync();
            }
            catch (Exception ex)
            {
                return new ApiResponse<TelegramChatDto>(ResponseType.Error, $"Error fetching latest telegram chat: {ex.Message}", null);
            }
        }

        public async Task<ApiResponse<bool>> UpdateVolunteerTelegramAsync(UpdateVolunteerTelegramDto dto)
        {
            try
            {
                return await ((VolunteersDAL)_volunteersDAL).UpdateVolunteerTelegramAsync(dto);
            }
            catch (Exception ex)
            {
                return new ApiResponse<bool>(ResponseType.Error, $"Error updating volunteer telegram: {ex.Message}", false);
            }
        }

        public async Task<ApiResponse<List<VolunteerPendingAssignmentDto>>>GetVolunteersWithPendingAssignmentsAsync()
        {
            try
            {
                return await _volunteersDAL
                    .GetVolunteersWithPendingAssignmentsAsync();
            }
            catch (Exception ex)
            {
                return new ApiResponse<List<VolunteerPendingAssignmentDto>>(
                    ResponseType.Error,
                    $"Error retrieving volunteers with pending assignments: {ex.Message}",
                    new List<VolunteerPendingAssignmentDto>()
                );
            }
        }
       

        public async Task<ApiResponse<bool>> SendTelegramMessageAsync(string chatId, string message)
        {
            try
            {
                // Validations

                if (string.IsNullOrWhiteSpace(chatId))
                {
                    return new ApiResponse<bool>(
                        ResponseType.Warning,
                        "Chat id is required",
                        false
                    );
                }

                if (string.IsNullOrWhiteSpace(message))
                {
                    return new ApiResponse<bool>(
                        ResponseType.Warning,
                        "Message is required",
                        false
                    );
                }

                if (message.Length > 4000)
                {
                    return new ApiResponse<bool>(
                        ResponseType.Warning,
                        "Message exceeds telegram limit",
                        false
                    );
                }

                await _volunteersDAL.SendTelegramMessageAsync(chatId, message);

                return new ApiResponse<bool>(
                    ResponseType.Success,
                    "Telegram message sent successfully",
                    true
                );
            }
            catch (Exception ex)
            {
                return new ApiResponse<bool>(
                    ResponseType.Error,
                    $"Error sending telegram message: {ex.Message}",
                    false
                );
            }
        }
    }
}