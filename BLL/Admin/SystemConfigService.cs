using RM_CMS.DAL.Admin;
using RM_CMS.Data.DTO;
using RM_CMS.Utilities;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RM_CMS.BLL.Admin
{
    public class SystemConfigService : ISystemConfigService
    {
        private readonly ISystemConfigRepository _repository;

        public SystemConfigService(ISystemConfigRepository repository)
        {
            _repository = repository;
        }

        public async Task<ApiResponse<IEnumerable<SystemConfigDto>>> GetAllConfigsAsync()
        {
            // Pass through to the repository layer. More complex business logic could be added here in the future.
            return await _repository.GetAllAsync();
        }

        public async Task<ApiResponse<bool>> UpdateConfigsAsync(IEnumerable<UpdateSystemConfigDto> configs)
        {
            if (configs == null || !configs.Any())
            {
                return new ApiResponse<bool>(ResponseType.Warning, "No configuration data provided.", false);
            }

            return await _repository.UpdateAsync(configs);
        }
    }
}