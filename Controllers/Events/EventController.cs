using Microsoft.AspNetCore.Mvc;
using RM_CMS.BLL.Events;
using RM_CMS.Data.DTO.Events;
using RM_CMS.Utilities;

namespace RM_CMS.Controllers.Events
{
    [ApiController]
    [Route("api/events")]
    public class EventController : ControllerBase
    {
        private readonly IEventsBLL _eventsBLL;
        private readonly ILogger<EventController> _logger;

        public EventController(IEventsBLL eventsBLL, ILogger<EventController> logger)
        {
            _eventsBLL = eventsBLL;
            _logger = logger;
        }

        [HttpGet("active")]
        public async Task<IActionResult> Active([FromQuery] long? userId)
        {
            try
            {
                _logger.LogInformation("Fetching active events for userId: {UserId}", userId);

                var result = await _eventsBLL.GetActiveEventsAsync(userId);
                return Ok(new { success = true, data = result.Data });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching active events");
                return StatusCode(500, new { success = false, message = "An error occurred while fetching active events" });
            }
        }
    }
}
