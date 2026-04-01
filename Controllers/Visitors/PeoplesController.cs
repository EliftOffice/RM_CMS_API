using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RM_CMS.BLL.Visitors;
using RM_CMS.Data.DTO.Visitors;
using RM_CMS.Utilities;

namespace RM_CMS.Controllers.visitors
{
    [Route("api/[controller]")]
    [ApiController]
    public class PeoplesController : ControllerBase
    {
        private readonly IPeopleService _peopleService;
        private readonly ILogger<PeoplesController> _logger;

        public PeoplesController(IPeopleService peopleService, ILogger<PeoplesController> logger)
        {
            _peopleService = peopleService;
            _logger = logger;
        }

        /// <summary>
        /// Assigns a person to an available volunteer based on capacity and campus
        /// </summary>
        /// <param name="personId">The ID of the person to assign</param>
        /// <returns>ApiResponse with assignment details</returns>
        [HttpPost("{personId}/assign")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<ApiResponse<object>>> AssignPersonToVolunteer(string personId)
        {
            try
            {
                _logger.LogInformation($"Attempting to assign person {personId} to a volunteer");

                var result = await _peopleService.AssignPersonToVolunteerAsync(personId);

                _logger.LogInformation($"Processed assignment request for person {personId}");
                return HttpResponseHelper.CreateHttpResponse(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error assigning person {personId}");
                return HttpResponseHelper.CreateHttpResponse(new ApiResponse<object>(
                    ResponseType.Error,
                    "An error occurred during assignment process",
                    null
                ));
            }
        }
    }

    public class UpdateContactDto
    {
        public DateTime LastContactDate { get; set; }
        public DateTime? NextActionDate { get; set; }
    }
}
