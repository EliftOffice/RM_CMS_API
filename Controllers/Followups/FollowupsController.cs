using Microsoft.AspNetCore.Mvc;
using RM_CMS.BLL.Followups;
using RM_CMS.Data;
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

        [HttpPost("assign/{personId}")]
        public async Task<IActionResult> AssignVolunteer(string personId)
        {
            try
            {
                var result = await _followUpService.AssignVolunteerAsync(personId);
                if (result.Type != RM_CMS.Data.ResponseType.Success)
                {
                    return StatusCode(result.StatusCode == 0 ? 400 : result.StatusCode, result);
                }

                return StatusCode(result.StatusCode == 0 ? 200 : result.StatusCode, result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error assigning volunteer to person {PersonId}", personId);
                return StatusCode(500, new { message = "An error occurred while assigning volunteer", error = ex.Message });
                
            }
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateFollowUpDto dto)
        {
            try
            {
                var result = await _followUpService.CreateAsync(dto);
                if (result.Type != RM_CMS.Data.ResponseType.Success)
                {
                    return StatusCode(result.StatusCode == 0 ? 400 : result.StatusCode, result);
                }

                var id = result.Data?.FollowUpId;
                if (result.StatusCode == 201)
                {
                    return CreatedAtAction(nameof(GetById), new { id = id }, result);
                }

                return StatusCode(result.StatusCode == 0 ? 200 : result.StatusCode, result);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Validation failed for follow-up creation");
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating follow-up");
                return StatusCode(500, new { message = "An error occurred while creating follow-up", error = ex.Message });
            }
        }

        [HttpGet("person/{personId}")]
        public async Task<IActionResult> GetByPerson(string personId)
        {
            var items = await _followUpService.GetByPersonAsync(personId);
            return Ok(items);
        }

        [HttpGet("volunteer/{volunteerId}")]
        public async Task<IActionResult> GetByVolunteer(string volunteerId)
        {
            var items = await _followUpService.GetByVolunteerAsync(volunteerId);
            return Ok(items);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(string id)
        {
            // For now, use repository via service layer not exposed; simple workaround: try to fetch by person/volunteer lists
            // Client should use person/volunteer endpoints. Return 404.
            return NotFound();
        }
    }
}
