using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RM_CMS.Data.DTO.Telegram;
using RM_CMS.Security;

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

        /// <summary>
        /// Receives bot updates from Telegram's servers.
        ///
        /// Telegram cannot present a JWT, so this endpoint is anonymous to the auth system
        /// and instead authenticated by the shared <c>secret_token</c> that Telegram echoes
        /// on every delivery — enforced by <see cref="TelegramWebhookSecretAttribute"/>.
        /// Without that filter this route is world-callable and anyone can inject fake chats.
        /// </summary>
        [HttpPost("webhook")]
        [AllowAnonymous]
        [TelegramWebhookSecret]
        public async Task<IActionResult> Webhook([FromBody] TelegramUpdate update)
        {
            _logger.LogInformation("Telegram webhook received.");

            if (update?.Message != null)
            {
                var chatId = update.Message.Chat.Id;
                var text = update.Message.Text;

                // Chat ids and message bodies are user data — log the fact, not the content.
                _logger.LogDebug("Telegram update accepted for chat {ChatId}", chatId);

                if (!string.IsNullOrEmpty(text) && text.StartsWith("/start"))
                {
                    var parts = text.Split(' ');

                    string token = parts.Length > 1 ? parts[1] : "";

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