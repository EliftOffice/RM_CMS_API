using Microsoft.AspNetCore.Mvc;
using RM_CMS.BLL.Followups;
using RM_CMS.Data.DTO.Followups;

namespace RM_CMS.Controllers.Followups
{
    [Route("api/[controller]")]
    [ApiController]
    public class FollowupsController : ControllerBase
    {
        private readonly IFollowUpService _followUpService;
        private readonly ILogger<FollowupsController> _logger;

        public FollowupsController(IFollowUpService followUpService, ILogger<FollowupsController> logger)
        {
            _followUpService = followUpService;
            _logger = logger;
        }
    }
}
