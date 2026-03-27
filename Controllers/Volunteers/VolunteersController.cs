using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RM_CMS.BLL.Volunteers;
using RM_CMS.Data.DTO;

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

        /// <summary>
        /// Get all volunteers
        /// </summary>
        [HttpGet]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<IEnumerable<VolunteerResponseDto>>> GetAll()
        {
            try
            {
                _logger.LogInformation("Getting all volunteers");
                var result = await _volunteerService.GetAllAsync();
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching all volunteers");
                return StatusCode(500, new { message = "An error occurred while fetching volunteers", error = ex.Message });
            }
        }

        /// <summary>
        /// Get volunteer by ID
        /// </summary>
        [HttpGet("{volunteerId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<VolunteerResponseDto>> GetById(string volunteerId)
        {
            try
            {
                _logger.LogInformation($"Getting volunteer with ID: {volunteerId}");
                var result = await _volunteerService.GetByIdAsync(volunteerId);
                
                if (result == null)
                {
                    return NotFound(new { message = $"Volunteer with ID {volunteerId} not found" });
                }
                
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error fetching volunteer with ID: {volunteerId}");
                return StatusCode(500, new { message = "An error occurred while fetching the volunteer", error = ex.Message });
            }
        }

        /// <summary>
        /// Get volunteers by status
        /// </summary>
        [HttpGet("status/{status}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<IEnumerable<VolunteerResponseDto>>> GetByStatus(string status)
        {
            try
            {
                _logger.LogInformation($"Getting volunteers with status: {status}");
                var result = await _volunteerService.GetByStatusAsync(status);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error fetching volunteers with status: {status}");
                return StatusCode(500, new { message = "An error occurred while fetching volunteers by status", error = ex.Message });
            }
        }

        /// <summary>
        /// Get volunteers by team lead
        /// </summary>
        [HttpGet("team-lead/{teamLeadId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<IEnumerable<VolunteerResponseDto>>> GetByTeamLead(string teamLeadId)
        {
            try
            {
                _logger.LogInformation($"Getting volunteers for team lead: {teamLeadId}");
                var result = await _volunteerService.GetByTeamLeadAsync(teamLeadId);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error fetching volunteers for team lead: {teamLeadId}");
                return StatusCode(500, new { message = "An error occurred while fetching volunteers by team lead", error = ex.Message });
            }
        }

        /// <summary>
        /// Get volunteers by capacity band
        /// </summary>
        [HttpGet("capacity/{capacityBand}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<IEnumerable<VolunteerResponseDto>>> GetByCapacityBand(string capacityBand)
        {
            try
            {
                _logger.LogInformation($"Getting volunteers with capacity band: {capacityBand}");
                var result = await _volunteerService.GetByCapacityBandAsync(capacityBand);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error fetching volunteers with capacity band: {capacityBand}");
                return StatusCode(500, new { message = "An error occurred while fetching volunteers by capacity band", error = ex.Message });
            }
        }

        /// <summary>
        /// Get paginated volunteers
        /// </summary>
        [HttpGet("paginated/data")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<object>> GetPaginated([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10)
        {
            try
            {
                _logger.LogInformation($"Getting paginated volunteers - Page: {pageNumber}, Size: {pageSize}");
                var (data, totalCount) = await _volunteerService.GetPaginatedAsync(pageNumber, pageSize);
                
                return Ok(new
                {
                    pageNumber,
                    pageSize,
                    totalCount,
                    totalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
                    data
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching paginated volunteers");
                return StatusCode(500, new { message = "An error occurred while fetching paginated volunteers", error = ex.Message });
            }
        }

        /// <summary>
        /// Create new volunteer
        /// </summary>
        [HttpPost]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<VolunteerResponseDto>> Create([FromBody] CreateVolunteerDto dto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                _logger.LogInformation($"Creating new volunteer: {dto.FirstName} {dto.LastName}");
                var result = await _volunteerService.CreateAsync(dto);

                if (result == null)
                {
                    return BadRequest(new { message = "Failed to create volunteer" });
                }

                return CreatedAtAction(nameof(GetById), new { volunteerId = result.VolunteerId }, result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating new volunteer");
                return StatusCode(500, new { message = "An error occurred while creating the volunteer", error = ex.Message });
            }
        }

        /// <summary>
        /// Update volunteer
        /// </summary>
        [HttpPut("{volunteerId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<object>> Update(string volunteerId, [FromBody] UpdateVolunteerDto dto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                _logger.LogInformation($"Updating volunteer: {volunteerId}");
                var success = await _volunteerService.UpdateAsync(volunteerId, dto);

                if (!success)
                {
                    return NotFound(new { message = $"Volunteer with ID {volunteerId} not found" });
                }

                var updated = await _volunteerService.GetByIdAsync(volunteerId);
                return Ok(new { message = "Volunteer updated successfully", data = updated });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating volunteer: {volunteerId}");
                return StatusCode(500, new { message = "An error occurred while updating the volunteer", error = ex.Message });
            }
        }

        /// <summary>
        /// Delete volunteer
        /// </summary>
        [HttpDelete("{volunteerId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<object>> Delete(string volunteerId)
        {
            try
            {
                _logger.LogInformation($"Deleting volunteer: {volunteerId}");
                var success = await _volunteerService.DeleteAsync(volunteerId);

                if (!success)
                {
                    return NotFound(new { message = $"Volunteer with ID {volunteerId} not found" });
                }

                return Ok(new { message = "Volunteer deleted successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting volunteer: {volunteerId}");
                return StatusCode(500, new { message = "An error occurred while deleting the volunteer", error = ex.Message });
            }
        }

        /// <summary>
        /// Update volunteer status
        /// </summary>
        [HttpPatch("{volunteerId}/status/{status}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<object>> UpdateStatus(string volunteerId, string status)
        {
            try
            {
                _logger.LogInformation($"Updating status for volunteer {volunteerId} to {status}");
                var success = await _volunteerService.UpdateStatusAsync(volunteerId, status);

                if (!success)
                {
                    return NotFound(new { message = $"Volunteer with ID {volunteerId} not found" });
                }

                var updated = await _volunteerService.GetByIdAsync(volunteerId);
                return Ok(new { message = "Volunteer status updated successfully", data = updated });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating status for volunteer: {volunteerId}");
                return StatusCode(500, new { message = "An error occurred while updating the status", error = ex.Message });
            }
        }

        /// <summary>
        /// Update volunteer capacity
        /// </summary>
        [HttpPatch("{volunteerId}/capacity")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<object>> UpdateCapacity(string volunteerId, [FromBody] UpdateCapacityDto dto)
        {
            try
            {
                _logger.LogInformation($"Updating capacity for volunteer {volunteerId}");
                var success = await _volunteerService.UpdateCapacityAsync(volunteerId, dto.CapacityBand, dto.CapacityMin, dto.CapacityMax);

                if (!success)
                {
                    return NotFound(new { message = $"Volunteer with ID {volunteerId} not found" });
                }

                var updated = await _volunteerService.GetByIdAsync(volunteerId);
                return Ok(new { message = "Volunteer capacity updated successfully", data = updated });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating capacity for volunteer: {volunteerId}");
                return StatusCode(500, new { message = "An error occurred while updating the capacity", error = ex.Message });
            }
        }

        /// <summary>
        /// Update volunteer performance
        /// </summary>
        [HttpPatch("{volunteerId}/performance")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<object>> UpdatePerformance(string volunteerId, [FromBody] UpdatePerformanceDto dto)
        {
            try
            {
                _logger.LogInformation($"Updating performance for volunteer {volunteerId}");
                var success = await _volunteerService.UpdatePerformanceAsync(volunteerId, dto.TotalCompleted, dto.TotalAssigned);

                if (!success)
                {
                    return NotFound(new { message = $"Volunteer with ID {volunteerId} not found" });
                }

                var updated = await _volunteerService.GetByIdAsync(volunteerId);
                return Ok(new { message = "Volunteer performance updated successfully", data = updated });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating performance for volunteer: {volunteerId}");
                return StatusCode(500, new { message = "An error occurred while updating the performance", error = ex.Message });
            }
        }

        /// <summary>
        /// Update volunteer check-in dates
        /// </summary>
        [HttpPatch("{volunteerId}/check-in")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<object>> UpdateCheckIn(string volunteerId, [FromBody] UpdateCheckInDto dto)
        {
            try
            {
                _logger.LogInformation($"Updating check-in for volunteer {volunteerId}");
                var success = await _volunteerService.UpdateCheckInAsync(volunteerId, dto.LastCheckIn, dto.NextCheckIn);

                if (!success)
                {
                    return NotFound(new { message = $"Volunteer with ID {volunteerId} not found" });
                }

                var updated = await _volunteerService.GetByIdAsync(volunteerId);
                return Ok(new { message = "Volunteer check-in updated successfully", data = updated });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating check-in for volunteer: {volunteerId}");
                return StatusCode(500, new { message = "An error occurred while updating the check-in", error = ex.Message });
            }
        }

        /// <summary>
        /// Get active volunteers count
        /// </summary>
        [HttpGet("count/active")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<object>> GetActiveCount()
        {
            try
            {
                _logger.LogInformation("Getting active volunteers count");
                var count = await _volunteerService.GetActiveVolunteerCountAsync();
                return Ok(new { activeCount = count });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting active count");
                return StatusCode(500, new { message = "An error occurred while getting the active count", error = ex.Message });
            }
        }

        /// <summary>
        /// Get volunteers with low completion rate
        /// </summary>
        [HttpGet("alert/low-completion/{threshold}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<IEnumerable<VolunteerResponseDto>>> GetLowCompletionRate(decimal threshold)
        {
            try
            {
                _logger.LogInformation($"Getting volunteers with completion rate below {threshold}");
                var result = await _volunteerService.GetWithLowCompletionRateAsync(threshold);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching volunteers with low completion rate");
                return StatusCode(500, new { message = "An error occurred while fetching volunteers with low completion rate", error = ex.Message });
            }
        }

        /// <summary>
        /// Get total volunteers count
        /// </summary>
        [HttpGet("count/total")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<object>> GetTotalCount()
        {
            try
            {
                _logger.LogInformation("Getting total volunteers count");
                var count = await _volunteerService.GetTotalCountAsync();
                return Ok(new { totalCount = count });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting total count");
                return StatusCode(500, new { message = "An error occurred while getting the total count", error = ex.Message });
            }
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
