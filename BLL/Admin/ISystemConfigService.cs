using RM_CMS.Data.DTO;
using RM_CMS.Utilities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RM_CMS.BLL.Admin
{
    public interface ISystemConfigService
    {
        Task<ApiResponse<IEnumerable<SystemConfigDto>>> GetAllConfigsAsync();
        Task<ApiResponse<bool>> UpdateConfigsAsync(IEnumerable<UpdateSystemConfigDto> configs);
    }
}