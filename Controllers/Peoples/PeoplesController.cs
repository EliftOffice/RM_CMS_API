using Microsoft.AspNetCore.Mvc;
using RM_CMS.BLL.Peoples;
using RM_CMS.Data.DTO;
using RM_CMS.Data.DTO.Peoples;
using RM_CMS.Data.DTO.Volunteers;
using RM_CMS.Data.Models;
using RM_CMS.Utilities;

namespace RM_CMS.Controllers.Peoples
{
    [Route("api/[controller]")]
    [ApiController]
    public class PeoplesController : ControllerBase
    {
        private readonly IPeoplesBLL _peoplesBLL;
        private readonly ILogger<PeoplesController> _logger;

        public PeoplesController(IPeoplesBLL peoplesBLL, ILogger<PeoplesController> logger)
        {
            _peoplesBLL = peoplesBLL;
            _logger = logger;
        }

        [HttpPost("/api/people")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<ApiResponse<AssignedVolunteerDTO>>> SaveNewVisitor([FromBody] CreatePeopleDto createPeopleDto)
        {
            try
            {
                var result = await _peoplesBLL.SaveAndAssignePeople(createPeopleDto);

                // _logger.LogInformation($"Processed save new visitor request for {createDto?.email ?? createDto?.phone}");
                return HttpResponseHelper.CreateHttpResponse(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving new visitor");
                return StatusCode(500, new ApiResponse<AssignedVolunteerDTO>(
                    ResponseType.Error,
                    "An error occurred while saving visitor",
                    new AssignedVolunteerDTO()
                ));
            }
        }

        [HttpGet("/api/people/{personId}")]
        public async Task<ActionResult<ApiResponse<People>>> GetPersonByIdAsync(string personId)
        {
            try
            {
                var result = await _peoplesBLL.GetPersonByIdAsync(personId);
                return HttpResponseHelper.CreateHttpResponse(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving person by ID");
                return StatusCode(500, new ApiResponse<People>(
                    ResponseType.Error,
                    "An error occurred while retrieving person",
                    new People()
                ));
            }
        }

        [HttpGet("/api/people")]
        public async Task<ActionResult<ApiResponse<List<People>>>> GetPeopleAsync([FromQuery] PeoplesFilterDTO filter)
        {
            try
            {
                var result = await _peoplesBLL.GetPeopleByFilterAsync(filter);
                return HttpResponseHelper.CreateHttpResponse(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving people with filters");

                return StatusCode(500, new ApiResponse<IEnumerable<People>>(
                    ResponseType.Error,
                    "An error occurred while retrieving people",
                    new List<People>()
                ));
            }
        }


    }
}