using Microsoft.AspNetCore.Mvc;
using RM_CMS.BLL.Events;
using RM_CMS.Data.DTO.Events;
using RM_CMS.Utilities;
using System.Text.Json;

namespace RM_CMS.Controllers.Admin
{
    [ApiController]
    [Route("api/admin/events")]
    public class AdminEventsController : ControllerBase
    {
        private readonly IEventsBLL _eventsBLL;
        private readonly ILogger<AdminEventsController> _logger;

        public AdminEventsController(IEventsBLL eventsBLL, ILogger<AdminEventsController> logger)
        {
            _eventsBLL = eventsBLL;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            try
            {
                _logger.LogInformation("Fetching all events for admin");

                var result = await _eventsBLL.GetAdminEventsAsync();
                return Ok(new { success = true, data = result.Data });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching all events");
                return StatusCode(500, new { success = false, message = "An error occurred while fetching events" });
            }
        }

        [HttpGet("{id:long}")]
        public async Task<IActionResult> GetById(long id)
        {
            try
            {
                _logger.LogInformation("Fetching event: {EventId}", id);

                var result = await _eventsBLL.GetEventByIdAsync(id);

                if (result.ResponseType != ResponseType.Success)
                {
                    return NotFound(new ApiResponse<object>(ResponseType.Warning, "Event not found", null));
                }

                return Ok(new { success = true, data = result.Data });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching event");
                return StatusCode(500, new { success = false, message = "An error occurred while fetching event" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] AdminEventRequest request)
        {
            try
            {
                _logger.LogInformation("Creating event: {Title}", request?.Title);

                if (request == null)
                {
                    return BadRequest(new ApiResponse<object>(ResponseType.Warning, "Invalid request", null));
                }

                var result = await _eventsBLL.CreateEventAsync(request);

                if (result.ResponseType == ResponseType.Success)
                {
                    return Ok(new ApiResponse<object>(ResponseType.Success, "Event created successfully", new { id = result.Data }));
                }

                return BadRequest(new ApiResponse<object>(ResponseType.Warning, result.Message, null));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating event");
                return StatusCode(500, new ApiResponse<object>(ResponseType.Error, "An error occurred while creating event", null));
            }
        }

        [HttpPut("{id:long}")]
        public async Task<IActionResult> Update(long id, [FromBody] AdminEventRequest request)
        {
            try
            {
                _logger.LogInformation("Updating event: {EventId}", id);

                if (request == null)
                {
                    return BadRequest(new ApiResponse<object>(ResponseType.Warning, "Invalid request", null));
                }

                var result = await _eventsBLL.UpdateEventAsync(id, request);

                if (result.ResponseType == ResponseType.Success)
                {
                    return Ok(new ApiResponse<object>(ResponseType.Success, "Event updated successfully", new { id }));
                }

                return BadRequest(new ApiResponse<object>(ResponseType.Warning, result.Message, null));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating event");
                return StatusCode(500, new ApiResponse<object>(ResponseType.Error, "An error occurred while updating event", null));
            }
        }

        [HttpPatch("{id:long}/status")]
        public async Task<IActionResult> UpdateStatus(long id, [FromBody] JsonElement body)
        {
            try
            {
                _logger.LogInformation("Updating event status: {EventId}", id);

                if (!body.TryGetProperty("isActive", out var isActiveElement))
                {
                    return BadRequest(new ApiResponse<object>(ResponseType.Warning, "isActive is required", null));
                }

                var isActive = isActiveElement.GetBoolean();
                var result = await _eventsBLL.UpdateEventStatusAsync(id, isActive);

                if (result.ResponseType == ResponseType.Success)
                {
                    return Ok(new ApiResponse<object>(ResponseType.Success, "Event status updated", new { id }));
                }

                return BadRequest(new ApiResponse<object>(ResponseType.Warning, result.Message, null));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating event status");
                return StatusCode(500, new ApiResponse<object>(ResponseType.Error, "An error occurred while updating event status", null));
            }
        }
    }
}
