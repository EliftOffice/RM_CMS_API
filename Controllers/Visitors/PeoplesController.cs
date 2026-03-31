using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RM_CMS.BLL.Visitors;
using RM_CMS.Data.DTO.Visitors;

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
        /// Get all people
        /// </summary>
        [HttpGet]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<IEnumerable<PeopleResponseDto>>> GetAll()
        {
            try
            {
                _logger.LogInformation("Getting all people");
                var result = await _peopleService.GetAllAsync();
                if (result.Type != RM_CMS.Data.ResponseType.Success)
                {
                    return StatusCode(result.StatusCode == 0 ? 500 : result.StatusCode, result);
                }
                return Ok(result.Data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching all people");
                return StatusCode(500, new { message = "An error occurred while fetching people", error = ex.Message });
            }
        }

        /// <summary>
        /// Get person by ID
        /// </summary>
        [HttpGet("{personId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<PeopleResponseDto>> GetById(string personId)
        {
            try
            {
                _logger.LogInformation($"Getting person with ID: {personId}");
                var result = await _peopleService.GetByIdAsync(personId);
                if (result.Type != RM_CMS.Data.ResponseType.Success)
                {
                    return StatusCode(result.StatusCode == 0 ? 404 : result.StatusCode, result);
                }
                return Ok(result.Data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error fetching person with ID: {personId}");
                return StatusCode(500, new { message = "An error occurred while fetching the person", error = ex.Message });
            }
        }

        /// <summary>
        /// Get people by follow-up status
        /// </summary>
        [HttpGet("status/{followUpStatus}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<IEnumerable<PeopleResponseDto>>> GetByStatus(string followUpStatus)
        {
            try
            {
                _logger.LogInformation($"Getting people with status: {followUpStatus}");
                var result = await _peopleService.GetByStatusAsync(followUpStatus);
                if (result.Type != RM_CMS.Data.ResponseType.Success)
                {
                    return StatusCode(result.StatusCode == 0 ? 500 : result.StatusCode, result);
                }
                return Ok(result.Data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error fetching people with status: {followUpStatus}");
                return StatusCode(500, new { message = "An error occurred while fetching people by status", error = ex.Message });
            }
        }

        /// <summary>
        /// Get people by assigned volunteer
        /// </summary>
        [HttpGet("volunteer/{volunteerId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<IEnumerable<PeopleResponseDto>>> GetByAssignedVolunteer(string volunteerId)
        {
            try
            {
                _logger.LogInformation($"Getting people assigned to volunteer: {volunteerId}");
                var result = await _peopleService.GetByAssignedVolunteerAsync(volunteerId);
                if (result.Type != RM_CMS.Data.ResponseType.Success)
                {
                    return StatusCode(result.StatusCode == 0 ? 500 : result.StatusCode, result);
                }
                return Ok(result.Data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error fetching people for volunteer: {volunteerId}");
                return StatusCode(500, new { message = "An error occurred while fetching people by volunteer", error = ex.Message });
            }
        }

        /// <summary>
        /// Get people by priority level
        /// </summary>
        [HttpGet("priority/{priority}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<IEnumerable<PeopleResponseDto>>> GetByPriority(string priority)
        {
            try
            {
                _logger.LogInformation($"Getting people with priority: {priority}");
                var result = await _peopleService.GetByPriorityAsync(priority);
                if (result.Type != RM_CMS.Data.ResponseType.Success)
                {
                    return StatusCode(result.StatusCode == 0 ? 500 : result.StatusCode, result);
                }
                return Ok(result.Data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error fetching people with priority: {priority}");
                return StatusCode(500, new { message = "An error occurred while fetching people by priority", error = ex.Message });
            }
        }

        /// <summary>
        /// Get paginated people
        /// </summary>
        [HttpGet("paginated/data")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<object>> GetPaginated([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10)
        {
            try
            {
                _logger.LogInformation($"Getting paginated people - Page: {pageNumber}, Size: {pageSize}");
                var result = await _peopleService.GetPaginatedAsync(pageNumber, pageSize);
                if (result.Type != RM_CMS.Data.ResponseType.Success)
                {
                    return StatusCode(result.StatusCode == 0 ? 500 : result.StatusCode, result);
                }

                var paged = result.Data!;
                return Ok(new
                {
                    pageNumber,
                    pageSize,
                    totalCount = paged.TotalCount,
                    totalPages = (int)Math.Ceiling(paged.TotalCount / (double)pageSize),
                    data = paged.Data
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching paginated people");
                return StatusCode(500, new { message = "An error occurred while fetching paginated people", error = ex.Message });
            }
        }

        /// <summary>
        /// Create new person
        /// </summary>
        [HttpPost]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<PeopleResponseDto>> Create([FromBody] CreatePeopleDto dto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                _logger.LogInformation($"Creating new person: {dto.FirstName} {dto.LastName}");
                var result = await _peopleService.CreateAsync(dto);
                if (result.Type != RM_CMS.Data.ResponseType.Success)
                {
                    return StatusCode(result.StatusCode == 0 ? 400 : result.StatusCode, result);
                }

                return CreatedAtAction(nameof(GetById), new { personId = result.Data?.PersonId }, result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating new person");
                return StatusCode(500, new { message = "An error occurred while creating the person", error = ex.Message });
            }
        }

        /// <summary>
        /// Update person
        /// </summary>
        [HttpPut("{personId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<object>> Update(string personId, [FromBody] UpdatePeopleDto dto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                _logger.LogInformation($"Updating person: {personId}");
                var result = await _peopleService.UpdateAsync(personId, dto);
                if (result.Type != RM_CMS.Data.ResponseType.Success)
                {
                    return StatusCode(result.StatusCode == 0 ? 404 : result.StatusCode, result);
                }

                var updated = await _peopleService.GetByIdAsync(personId);
                return Ok(new { message = "Person updated successfully", data = updated.Data });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating person: {personId}");
                return StatusCode(500, new { message = "An error occurred while updating the person", error = ex.Message });
            }
        }

        /// <summary>
        /// Delete person
        /// </summary>
        [HttpDelete("{personId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<object>> Delete(string personId)
        {
            try
            {
                _logger.LogInformation($"Deleting person: {personId}");
                var result = await _peopleService.DeleteAsync(personId);
                if (result.Type != RM_CMS.Data.ResponseType.Success)
                {
                    return StatusCode(result.StatusCode == 0 ? 404 : result.StatusCode, result);
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting person: {personId}");
                return StatusCode(500, new { message = "An error occurred while deleting the person", error = ex.Message });
            }
        }

        /// <summary>
        /// Update follow-up status
        /// </summary>
        [HttpPatch("{personId}/status/{status}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<object>> UpdateFollowUpStatus(string personId, string status)
        {
            try
            {
                _logger.LogInformation($"Updating follow-up status for person {personId} to {status}");
                var result = await _peopleService.UpdateFollowUpStatusAsync(personId, status);
                if (result.Type != RM_CMS.Data.ResponseType.Success)
                {
                    return StatusCode(result.StatusCode == 0 ? 404 : result.StatusCode, result);
                }

                var updated = await _peopleService.GetByIdAsync(personId);
                return Ok(new { message = "Follow-up status updated successfully", data = updated.Data });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating status for person: {personId}");
                return StatusCode(500, new { message = "An error occurred while updating the status", error = ex.Message });
            }
        }

        /// <summary>
        /// Assign volunteer to person
        /// </summary>
        [HttpPatch("{personId}/assign-volunteer/{volunteerId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<object>> AssignVolunteer(string personId, string volunteerId)
        {
            try
            {
                _logger.LogInformation($"Assigning volunteer {volunteerId} to person {personId}");
                var result = await _peopleService.AssignVolunteerAsync(personId, volunteerId);
                if (result.Type != RM_CMS.Data.ResponseType.Success)
                {
                    return StatusCode(result.StatusCode == 0 ? 404 : result.StatusCode, result);
                }

                var updated = await _peopleService.GetByIdAsync(personId);
                return Ok(new { message = "Volunteer assigned successfully", data = updated.Data });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error assigning volunteer to person: {personId}");
                return StatusCode(500, new { message = "An error occurred while assigning the volunteer", error = ex.Message });
            }
        }

        /// <summary>
        /// Update last contact information
        /// </summary>
        [HttpPatch("{personId}/contact")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<object>> UpdateContact(string personId, [FromBody] UpdateContactDto dto)
        {
            try
            {
                _logger.LogInformation($"Updating contact information for person {personId}");
                var result = await _peopleService.UpdateContactAsync(personId, dto.LastContactDate, dto.NextActionDate);
                if (result.Type != RM_CMS.Data.ResponseType.Success)
                {
                    return StatusCode(result.StatusCode == 0 ? 404 : result.StatusCode, result);
                }

                var updated = await _peopleService.GetByIdAsync(personId);
                return Ok(new { message = "Contact information updated successfully", data = updated.Data });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating contact for person: {personId}");
                return StatusCode(500, new { message = "An error occurred while updating contact information", error = ex.Message });
            }
        }

        /// <summary>
        /// Get total count of people
        /// </summary>
        [HttpGet("count/total")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<object>> GetTotalCount()
        {
            try
            {
                _logger.LogInformation("Getting total count of people");
                var result = await _peopleService.GetTotalCountAsync();
                if (result.Type != RM_CMS.Data.ResponseType.Success)
                {
                    return StatusCode(result.StatusCode == 0 ? 500 : result.StatusCode, result);
                }

                return Ok(new { totalCount = result.Data });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting total count");
                return StatusCode(500, new { message = "An error occurred while getting the total count", error = ex.Message });
            }
        }
    }

    public class UpdateContactDto
    {
        public DateTime LastContactDate { get; set; }
        public DateTime? NextActionDate { get; set; }
    }
}
