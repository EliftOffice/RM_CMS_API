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

                if (string.IsNullOrWhiteSpace(personId))
                {
                    return BadRequest(new ApiResponse<object>(
                        ResponseType.Error,
                        "Person ID cannot be empty",
                        null
                    ));
                }

                var result = await _peopleService.AssignPersonToVolunteerAsync(personId);

                if (result.ResponseType == ResponseType.Error)
                {
                    _logger.LogWarning($"Assignment failed for person {personId}: {result.Message}");
                    return BadRequest(result);
                }

                _logger.LogInformation($"Successfully assigned person {personId}");
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error assigning person {personId}");
                return StatusCode(500, new ApiResponse<object>(
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
