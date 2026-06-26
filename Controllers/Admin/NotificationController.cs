using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using RM_CMS.BLL.Admin;
using RM_CMS.Data.DTO;
using RM_CMS.Utilities;
using System.Threading.Tasks;

namespace RM_CMS.Controllers.Admin
{
    [Route("api/notifications")]
    [ApiController]
    public class NotificationController : ControllerBase
    {
        private readonly INotificationService _notificationService;
        private readonly ILogger<NotificationController> _logger;

        public NotificationController(INotificationService notificationService, ILogger<NotificationController> logger)
        {
            _notificationService = notificationService;
            _logger = logger;
        }

        [HttpPost("broadcast/volunteers")]
        public async Task<ActionResult<ApiResponse<object>>> BroadcastToVolunteers([FromBody] BroadcastMessageDto dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Message))
            {
                return BadRequest(new ApiResponse<object>(ResponseType.Error, "Message payload is required.", null));
            }

            _logger.LogInformation("Attempting to broadcast Telegram message to all volunteers.");
            var result = await _notificationService.BroadcastTelegramToVolunteersAsync(dto.Message);

            if (result.ResponseType == ResponseType.Error)
            {
                return BadRequest(result);
            }

            return Ok(result);
        }
    }
}