using RM_CMS.DAL.Peoples;
using RM_CMS.DAL.Volunteers;
using RM_CMS.Data.DTO.Volunteers;
using RM_CMS.Data.Models;
using RM_CMS.Utilities;

namespace RM_CMS.BLL.Volunteers
{
    public interface IVolunteersBLL
    {
        Task<ApiResponse<AssignedVolunteerDTO>> AssignToVolunteerAsync(string personId);
        Task<ApiResponse<Volunteer>> GetVolunteerByIdAsync(string volunteerId);
        Task<ApiResponse<IEnumerable<People>>> GetVolunteerAssignmentsAsync(string volunteerId);
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
        public async Task<ApiResponse<IEnumerable<People>>> GetVolunteerAssignmentsAsync(string volunteerId)
        {
            try
            {
                return await _volunteersDAL.GetVolunteerAssignmentsAsync(volunteerId);
            }
            catch (Exception ex)
            {
                return new ApiResponse<IEnumerable<People>>(
                    ResponseType.Error,
                    $"Error retrieving assignments: {ex.Message}",
                    null
                );
            }
        }
    }
}