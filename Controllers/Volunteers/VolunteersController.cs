using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RM_CMS.BLL.Volunteers;
using RM_CMS.Data.DTO;
using RM_CMS.Data.DTO.Volunteers;

namespace RM_CMS.Controllers.Volunteers
{
    [Route("api/[controller]")]
    [ApiController]
    public class VolunteersController : ControllerBase
    {
        private readonly IVolunteerService _volunteerService;
        private readonly ILogger<VolunteersController> _logger;

        public VolunteersController(IVolunteerService volunteerService, ILogger<VolunteersController> logger)
        {
            _volunteerService = volunteerService;
            _logger = logger;
        }
    }

    // Helper DTOs for PATCH endpoints
    public class UpdateCapacityDto
    {
        public string CapacityBand { get; set; } = string.Empty;
        public int CapacityMin { get; set; }
        public int CapacityMax { get; set; }
    }

    public class UpdatePerformanceDto
    {
        public int TotalCompleted { get; set; }
        public int TotalAssigned { get; set; }
    }

    public class UpdateCheckInDto
    {
        public DateTime? LastCheckIn { get; set; }
        public DateTime? NextCheckIn { get; set; }
    }
}
