using RM_CMS.DAL.TeamLeads;
using RM_CMS.Data.DTO.TeamLeads;
using RM_CMS.Utilities;

namespace RM_CMS.BLL.TeamLeads
{
    public interface ICheckInBLL
    {
        Task<ApiResponse<string>> CreateCheckInAsync(CreateCheckInDTO dto);
    }
    public class CheckInBLL: ICheckInBLL
    {
        private readonly ICheckInDAL _checkInsDAL;
        public CheckInBLL(ICheckInDAL checkInsDAL)
        {
            _checkInsDAL = checkInsDAL;
        }
        public async Task<ApiResponse<string>> CreateCheckInAsync(CreateCheckInDTO dto)
        {
            try
            {
                return await _checkInsDAL.CreateCheckInAsync(dto);
            }
            catch (Exception ex)
            {
                return new ApiResponse<string>(
                    ResponseType.Error,
                    $"Error creating check-in: {ex.Message}",
                    null
                );
            }
        }
    }
}
