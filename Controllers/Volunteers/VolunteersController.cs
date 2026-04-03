using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RM_CMS.BLL.Peoples;
using RM_CMS.BLL.Volunteers;
using RM_CMS.Data.DTO;
using RM_CMS.Data.DTO.Volunteers;
using RM_CMS.Data.Models;
using RM_CMS.Utilities;

namespace RM_CMS.Controllers.Volunteers
{
    [Route("api/[controller]")]
    [ApiController]
    public class VolunteersController : ControllerBase
    {
        private readonly IVolunteersBLL _VolunteersBLL;
        private readonly ILogger<VolunteersController> _logger;
        public VolunteersController(IVolunteersBLL volunteersBLL, ILogger<VolunteersController> logger)
        {
            _VolunteersBLL = volunteersBLL;
            _logger = logger;
        }

        [HttpPost("assign-volunteer")]
        public async Task<ActionResult<ApiResponse<AssignedVolunteerDTO>>> AssignToVolunteer([FromQuery] string personId)
        {
            try
            {
                _logger.LogInformation("Assigning volunteer for PersonId: {PersonId}", personId);

                var result = await _VolunteersBLL.AssignToVolunteerAsync(personId);

                return HttpResponseHelper.CreateHttpResponse(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error assigning volunteer");

                return HttpResponseHelper.CreateHttpResponse(new ApiResponse<AssignedVolunteerDTO>(
                    ResponseType.Error,
                    "An error occurred while assigning volunteer",
                    new AssignedVolunteerDTO()
                ));
            }
        }

        [HttpGet("/api/volunteers/{id}")]
        public async Task<ActionResult<ApiResponse<Volunteer>>> GetVolunteerByIdAsync(string id)
        {
            try
            {
                var result = await _VolunteersBLL.GetVolunteerByIdAsync(id);
                return HttpResponseHelper.CreateHttpResponse(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving volunteer by ID");

                return StatusCode(500, new ApiResponse<Volunteer>(
                    ResponseType.Error,
                    "An error occurred while retrieving volunteer",
                    new Volunteer()
                ));
            }
        }
        [HttpGet("/api/volunteers/{id}/assignments")]
        public async Task<ActionResult<ApiResponse<IEnumerable<People>>>> GetVolunteerAssignmentsAsync(string id)
        {
            try
            {
                var result = await _VolunteersBLL.GetVolunteerAssignmentsAsync(id);
                return HttpResponseHelper.CreateHttpResponse(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving volunteer assignments");

                return StatusCode(500, new ApiResponse<IEnumerable<People>>(
                    ResponseType.Error,
                    "An error occurred while retrieving assignments",
                    new List<People>()
                ));
            }
        }

    }
   
}
