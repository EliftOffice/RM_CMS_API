using RM_CMS.DAL.TeamLeads;
using RM_CMS.Data.DTO.TeamLeads;
using RM_CMS.Utilities;

namespace RM_CMS.BLL.TeamLeads
{
    public interface ITeamLeadDashBoardBLL
    {
        Task<ApiResponse<TeamLeadMetricsDTO>> GetTeamHealthMetricsAsync(string teamLeadId);
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
    }
}