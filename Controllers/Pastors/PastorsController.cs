using Microsoft.AspNetCore.Mvc;
using RM_CMS.BLL.Pastors;
using RM_CMS.Data.DTO;
using RM_CMS.Data.DTO.Pastors;
using RM_CMS.Utilities;

namespace RM_CMS.Controllers.Pastors
{
    [Route("api/[controller]")]
    [ApiController]
    public class PastorsController : ControllerBase
    {
        private readonly IPastorDashboardBLL _pastorDashboardBLL;
        private readonly ILogger<PastorsController> _logger;

        public PastorsController(IPastorDashboardBLL pastorDashboardBLL, ILogger<PastorsController> logger)
        {
            _pastorDashboardBLL = pastorDashboardBLL;
            _logger = logger;
        }

       
        [HttpGet("dashboard")]
        public async Task<ActionResult<ApiResponse<PastorDTO>>> GetPastorDashboardAsync()
        {
            try
            {
                var result = await _pastorDashboardBLL.GetPastorDashboardAsync();
                return HttpResponseHelper.CreateHttpResponse(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving pastor dashboard");

                return StatusCode(500, new ApiResponse<PastorDTO>(
                    ResponseType.Error,
                    "An error occurred while retrieving pastor dashboard",
                    new PastorDTO()
                ));
            }
        }
    }
}