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
       

       


    }
}