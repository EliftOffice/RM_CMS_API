using RM_CMS.Data.DTO;
using RM_CMS.Utilities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RM_CMS.DAL.Admin
{
    public interface ISystemConfigRepository
    {
        Task<ApiResponse<IEnumerable<SystemConfigDto>>> GetAllAsync();
        Task<ApiResponse<bool>> UpdateAsync(IEnumerable<UpdateSystemConfigDto> configs);
    }
}