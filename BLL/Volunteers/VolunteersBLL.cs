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
    }
}