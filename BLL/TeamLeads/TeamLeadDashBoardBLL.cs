using RM_CMS.DAL.TeamLeads;
using RM_CMS.Data.DTO.TeamLeads;
using RM_CMS.Utilities;

namespace RM_CMS.BLL.TeamLeads
{
    public interface ITeamLeadDashBoardBLL
    {
        Task<ApiResponse<TeamLeadMetricsDTO>> GetTeamHealthMetricsAsync(string teamLeadId);
        Task<ApiResponse<bool>> SaveTeamLeadAsync(TeamLeadDTO teamLead);
    }
    public class TeamLeadDashBoardBLL : ITeamLeadDashBoardBLL
    {
        private readonly ITeamLeadDashBoardDAL _dal;

        public TeamLeadDashBoardBLL(ITeamLeadDashBoardDAL dal)
        {
            _dal = dal;
        }

        public async Task<ApiResponse<TeamLeadMetricsDTO>> GetTeamHealthMetricsAsync(string teamLeadId)
        {
            try
            {
                return await _dal.GetTeamHealthMetricsAsync(teamLeadId);
            }
            catch (Exception)
            {
                return new ApiResponse<TeamLeadMetricsDTO>(
                    ResponseType.Error,
                    "Error processing team metrics",
                    null
                );
            }
        }

        public async Task<ApiResponse<bool>> SaveTeamLeadAsync(TeamLeadDTO teamLead)
        {
            try
            {
                // 🔹 1. Basic Validations
                

                if (string.IsNullOrWhiteSpace(teamLead.FirstName))
                {
                    return new ApiResponse<bool>(
                        ResponseType.Error,
                        "First Name is required",
                        false
                    );
                }

                if (string.IsNullOrWhiteSpace(teamLead.LastName))
                {
                    return new ApiResponse<bool>(
                        ResponseType.Error,
                        "Last Name is required",
                        false
                    );
                }

                if (string.IsNullOrWhiteSpace(teamLead.Phone))
                {
                    return new ApiResponse<bool>(
                        ResponseType.Error,
                        "Mobile number  is required",
                        false
                    );
                }

              
                // 🔹 4. Call DAL
                return await _dal.SaveTeamLeadAsync(teamLead);
            }
            catch (Exception ex)
            {
                return new ApiResponse<bool>(
                    ResponseType.Error,
                    $"BLL Error: {ex.Message}",
                    false
                );
            }
        }

        // 🔹 Helper Method
        private bool IsValidEmail(string email)
        {
            try
            {
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == email;
            }
            catch
            {
                return false;
            }
        }


    }
}