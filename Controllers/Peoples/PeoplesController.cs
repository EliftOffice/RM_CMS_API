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
        public async Task<ActionResult<ApiResponse<AssignedVolunteerDTO>>> SaveNewVisitor([FromBody] CreatePersonDto createPersonDto)
        {
            try
            {
                var result = await _peoplesBLL.SaveAndAssignPeople(createPersonDto);

                return HttpResponseHelper.CreateHttpResponse(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving new visitor");

                return StatusCode(500, new ApiResponse<AssignedVolunteerDTO>(
                    ResponseType.Error,
                    "An error occurred while saving visitor",
                    null
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


        [HttpGet("/api/GetBasicPeopleAsync")]
        public async Task<ActionResult<ApiResponse<List<People>>>> GetBasicPeopleAsync()
        {
            try
            {
                var result = await _peoplesBLL.GetBasicPeopleAsync();
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


        [HttpPut("/api/UpdateVisitor")]
        public async Task<ActionResult<ApiResponse<People>>> UpdateVisitor([FromBody] CreatePeopleDto updateDto)
        {
            try
            {
                var result = await _peoplesBLL.UpdateVisitorAsync(updateDto);

                return HttpResponseHelper.CreateHttpResponse(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating visitor");

                return StatusCode(500, new ApiResponse<People>(
                    ResponseType.Error,
                    "An error occurred while updating visitor",
                    new People()
                ));
            }


        }
    }
}