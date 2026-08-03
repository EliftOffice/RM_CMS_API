using Microsoft.AspNetCore.Mvc;
using RM_CMS.Data.DTO.Telegram;

namespace RM_CMS.Controllers.Telegram
{
    [Route("api/[controller]")]
    [ApiController]
    public class TelegramController : ControllerBase
    {
        private readonly ILogger<TelegramController> _logger;

        public TelegramController(ILogger<TelegramController> logger)
        {
            _logger = logger;
        }

        [HttpPost("webhook")]
        public async Task<IActionResult> Webhook([FromBody] TelegramUpdate update)
        {
            _logger.LogInformation("Telegram webhook received.");

            if (update?.Message != null)
            {
                var chatId = update.Message.Chat.Id;
                var text = update.Message.Text;

                _logger.LogInformation($"ChatId: {chatId}");
                _logger.LogInformation($"Message: {text}");

                if (!string.IsNullOrEmpty(text) && text.StartsWith("/start"))
                {
                    var parts = text.Split(' ');

                    string token = parts.Length > 1 ? parts[1] : "";

                    _logger.LogInformation($"Start Token: {token}");

                    // TODO:
                    // 1. Validate token
                    // 2. Find Person
                    // 3. Save chatId to database
                    // 4. Mark token as used
                    // 5. Send welcome message
                }
            }

            return Ok();
        }
    }
}