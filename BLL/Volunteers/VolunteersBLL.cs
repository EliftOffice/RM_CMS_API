using RM_CMS.DAL.Peoples;
using RM_CMS.DAL.Volunteers;
using RM_CMS.Data.DTO;
using RM_CMS.Data.DTO.Volunteers;
using RM_CMS.Data.Models;
using RM_CMS.Utilities;

namespace RM_CMS.BLL.Volunteers
{
    public interface IVolunteersBLL
    {
        Task<ApiResponse<AssignedVolunteerDTO>> AssignToVolunteerAsync(string personId);
        Task<ApiResponse<Volunteer>> GetVolunteerByIdAsync(string volunteerId);
        Task<ApiResponse<List<People>>> GetVolunteerAssignmentsAsync(string volunteerId);

        Task<ApiResponse<VolunteerResponseDto>> CreateVolunteerAsync(CreateVolunteerDto dto);
        Task<ApiResponse<List<Volunteer>>> GetVolunteersAsync();
        Task<ApiResponse<List<VolunteerLookupDto>>> GetVolunteersByMobileAsync(string mobile);
        Task<ApiResponse<string>> UpdateVolunteerMobileAsync(UpdateVolunteerMobileDto dto);
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
                    null
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
    }
}