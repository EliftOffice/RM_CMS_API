using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using RM_CMS.BLL.Admin;
using RM_CMS.Data.DTO;
using RM_CMS.Utilities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RM_CMS.Controllers.Admin
{
    [Route("api/systemconfig")]
    [ApiController]
    public class SystemConfigController : ControllerBase
    {
        private readonly ISystemConfigService _service;
        private readonly ILogger<SystemConfigController> _logger;

        public SystemConfigController(ISystemConfigService service, ILogger<SystemConfigController> logger)
        {
            _service = service;
            _logger = logger;
        }

        [HttpGet]
        public async Task<ActionResult<ApiResponse<IEnumerable<SystemConfigDto>>>> GetAll()
        {
            var result = await _service.GetAllConfigsAsync();
            if (result.ResponseType != ResponseType.Success)
            {
                return BadRequest(result);
            }
            return Ok(result);
        }

        [HttpPut]
        public async Task<ActionResult<ApiResponse<bool>>> Update([FromBody] IEnumerable<UpdateSystemConfigDto> configs)
        {
            var result = await _service.UpdateConfigsAsync(configs);
            if (result.ResponseType != ResponseType.Success)
            {
                return BadRequest(result);
            }
            return Ok(result);
        }
    }
}