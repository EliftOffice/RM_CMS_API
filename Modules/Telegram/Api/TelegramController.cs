using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using RM_CMS.Modules.Identity.Domain;
using RM_CMS.Modules.Identity.Services;
using RM_CMS.Modules.Telegram.Domain;
using RM_CMS.Modules.Telegram.Services;
using RM_CMS.Security;
using RM_CMS.Utilities;

namespace RM_CMS.Modules.Telegram.Api
{
    /// <summary>
    /// Where Telegram delivers updates.
    ///
    /// Anonymous to ASP.NET's authorization, because Telegram carries no user identity —
    /// the caller is proved by <see cref="TelegramWebhookSecretAttribute"/>, which
    /// checks the secret Telegram was registered with and 404s anything else so a
    /// prober cannot even confirm the route exists.
    ///
    /// The handler always returns 200. Telegram retries any non-2xx indefinitely, so a
    /// malformed update that made this endpoint fail would become an infinite loop
    /// against the API.
    /// </summary>
    [ApiController]
    [Route("api/telegram")]
    [Produces("application/json")]
    public sealed class TelegramWebhookController : ControllerBase
    {
        private readonly ITelegramLinkService _linking;
        private readonly ITelegramClient _client;

        /// <summary>Resolves the sign-in confirmation buttons. See HandleSignInCallbackAsync.</summary>
        private readonly IIdentityService _identity;

        private readonly ILogger<TelegramWebhookController> _logger;

        public TelegramWebhookController(
            ITelegramLinkService linking,
            ITelegramClient client,
            IIdentityService identity,
            ILogger<TelegramWebhookController> logger)
        {
            _linking = linking;
            _client = client;
            _identity = identity;
            _logger = logger;
        }

        [HttpPost("webhook")]
        [AllowAnonymous]
        [TelegramWebhookSecret]
        [DisableRateLimiting]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Webhook([FromBody] JsonElement update)
        {
            try
            {
                // A tapped button, not a typed message. This is how a sign-in is
                // confirmed: a callback button works in an existing chat, where a
                // t.me deep link would only re-send its payload for a NEW chat and so
                // would do nothing for anybody who had already linked.
                if (TryParseCallback(update, out var callback))
                {
                    await HandleSignInCallbackAsync(callback);
                    return Ok(new { ok = true });
                }

                var message = Parse(update);

                // Anything that is not a /start is ignored for now: two-way chat is a
                // separate feature with its own message-retention question.
                if (message is not null && message.IsStartCommand)
                {
                    var (outcome, reply) = await _linking.HandleStartAsync(message);

                    await _client.SendMessageAsync(message.ChatId, reply);

                    _logger.LogInformation("Telegram /start from chat {ChatId}: {Outcome}",
                        message.ChatId, outcome);
                }
            }
            catch (Exception ex)
            {
                // Swallowed on purpose. Telling Telegram this failed only makes it send
                // the same broken update again.
                _logger.LogError(ex, "Telegram webhook could not process an update.");
            }

            return Ok(new { ok = true });
        }

        /// <summary>A button somebody tapped, reduced to the three things that matter.</summary>
        private sealed record TelegramCallback(string Id, long ChatId, string Data);

        /// <summary>
        /// Pulls a <c>callback_query</c> out of the update envelope.
        /// </summary>
        /// <remarks>
        /// The chat comes from the MESSAGE the button is attached to, which is the chat
        /// this server sent the prompt to. It is compared against the challenge's own
        /// stored chat before anything is approved.
        /// </remarks>
        private static bool TryParseCallback(JsonElement update, out TelegramCallback callback)
        {
            callback = default!;

            if (update.ValueKind != JsonValueKind.Object) return false;
            if (!update.TryGetProperty("callback_query", out var query)) return false;
            if (!query.TryGetProperty("id", out var id)) return false;

            var data = query.TryGetProperty("data", out var d) ? d.GetString() : null;

            if (string.IsNullOrWhiteSpace(data)) return false;

            long chatId = 0;

            if (query.TryGetProperty("message", out var message) &&
                message.TryGetProperty("chat", out var chat) &&
                chat.TryGetProperty("id", out var cid))
            {
                cid.TryGetInt64(out chatId);
            }

            if (chatId == 0) return false;

            callback = new TelegramCallback(id.GetString() ?? string.Empty, chatId, data!);
            return true;
        }

        /// <summary>
        /// Handles a sign-in confirmation button.
        /// </summary>
        /// <remarks>
        /// The callback data is <c>lv:a:token</c> to confirm and <c>lv:d:token</c> to
        /// refuse. It came back from Telegram unchanged, so it is treated as untrusted
        /// input: the token is only ever looked up by hash, and anything that does not
        /// match this shape is ignored rather than guessed at.
        ///
        /// The spinner on the button is always closed, even on a refusal. Telegram
        /// retries an update that is never answered, which would replay the tap.
        /// </remarks>
        private async Task HandleSignInCallbackAsync(TelegramCallback callback)
        {
            const string prefix = "lv:";

            if (!callback.Data.StartsWith(prefix, StringComparison.Ordinal))
            {
                await _client.AnswerCallbackAsync(callback.Id);
                return;
            }

            var rest = callback.Data[prefix.Length..];
            var separator = rest.IndexOf(':');

            if (separator <= 0)
            {
                await _client.AnswerCallbackAsync(callback.Id);
                return;
            }

            var action = rest[..separator];
            var token = rest[(separator + 1)..];

            if (token.Length == 0 || (action != "a" && action != "d"))
            {
                await _client.AnswerCallbackAsync(callback.Id);
                return;
            }

            var context = new RequestContext(
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                Request.Headers.UserAgent.ToString(),
                HttpContext.TraceIdentifier);

            var (handled, reply) = await _identity.ResolveVerificationAsync(
                token, approved: action == "a", callback.ChatId, context);

            // The toast is short by necessity — Telegram caps it — so the full
            // sentence goes in a follow-up message where there is room for it.
            await _client.AnswerCallbackAsync(callback.Id, handled ? "Done" : "No longer valid");
            await _client.SendMessageAsync(callback.ChatId, reply);

            _logger.LogInformation(
                "Sign-in confirmation from chat {ChatId}: {Action}, handled={Handled}",
                callback.ChatId, action == "a" ? "approved" : "declined", handled);
        }

        /// <summary>
        /// Pulls out only the fields this application uses. Telegram's envelope is large
        /// and changes over time, so anything unexpected is ignored rather than binding
        /// into a strict model that would throw.
        /// </summary>
        private static TelegramMessage? Parse(JsonElement update)
        {
            if (update.ValueKind != JsonValueKind.Object) return null;
            if (!update.TryGetProperty("message", out var message)) return null;
            if (!message.TryGetProperty("chat", out var chat)) return null;
            if (!chat.TryGetProperty("id", out var chatId)) return null;

            var from = message.TryGetProperty("from", out var f) ? f : default;

            return new TelegramMessage
            {
                MessageId = message.TryGetProperty("message_id", out var m) && m.TryGetInt64(out var mid) ? mid : 0,
                ChatId = chatId.TryGetInt64(out var cid) ? cid : 0,
                FromId = from.ValueKind == JsonValueKind.Object &&
                         from.TryGetProperty("id", out var fid) && fid.TryGetInt64(out var fidv) ? fidv : null,
                FirstName = Text(from, "first_name"),
                LastName = Text(from, "last_name"),
                Username = Text(from, "username"),
                Text = message.TryGetProperty("text", out var t) ? t.GetString() : null
            };
        }

        private static string? Text(JsonElement element, string property) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
                ? value.GetString()
                : null;
    }

    /// <summary>
    /// Connect and disconnect a Telegram account.
    ///
    /// A signed-in person manages their own link. Acting on somebody else's is
    /// administrator-only, because issuing an invitation for another person creates a
    /// link that would connect THEIR alerts to whoever opens it.
    /// </summary>
    [ApiController]
    [Route("api/telegram")]
    [Produces("application/json")]
    [Authorize(Policy = PolicyNames.Authenticated)]
    public sealed class TelegramLinkController : ControllerBase
    {
        private readonly ITelegramLinkService _linking;
        private readonly ITelegramClient _client;
        private readonly ICurrentIdentity _current;
        private readonly AuthOptions _auth;

        public TelegramLinkController(
            ITelegramLinkService linking,
            ITelegramClient client,
            ICurrentIdentity current,
            IOptions<AuthOptions> auth)
        {
            _linking = linking;
            _client = client;
            _current = current;
            _auth = auth.Value;
        }

        /// <summary>Whether this person is connected, and whether they must be.</summary>
        [HttpGet("status")]
        [ProducesResponseType(typeof(ApiResponse<TelegramLinkStatus>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Status([FromQuery] string? personId = null)
        {
            if (!MayActFor(personId)) return Forbid();

            return Ok(await _linking.GetStatusAsync(personId));
        }

        /// <summary>
        /// Issues the one-time deep link to open in Telegram. Rate-limited: each call
        /// mints a credential, and the previous one is retired.
        /// </summary>
        [HttpPost("invitation")]
        [EnableRateLimiting(RateLimitPolicies.Sensitive)]
        [ProducesResponseType(typeof(ApiResponse<TelegramLinkInvitation>), StatusCodes.Status200OK)]
        public async Task<IActionResult> CreateInvitation([FromBody] DisconnectTelegramRequest? request = null)
        {
            var personId = request?.PersonId;

            if (!MayActFor(personId)) return Forbid();

            return Ok(await _linking.CreateInvitationAsync(personId));
        }

        [HttpPost("disconnect")]
        [EnableRateLimiting(RateLimitPolicies.Sensitive)]
        [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Disconnect([FromBody] DisconnectTelegramRequest? request = null)
        {
            var personId = request?.PersonId;

            if (!MayActFor(personId)) return Forbid();

            return Ok(await _linking.DisconnectAsync(personId));
        }

        /// <summary>
        /// Links somebody using a chat id the administrator already holds, skipping
        /// the deep-link handshake.
        ///
        /// Administrators only, and deliberately not folded into the self-service
        /// routes above: those prove ownership through Telegram, this one asserts it.
        /// The chat id is checked against Telegram before anything is written.
        /// </summary>
        [HttpPost("link")]
        [Authorize(Policy = PolicyNames.AdminOnly)]
        [EnableRateLimiting(RateLimitPolicies.Sensitive)]
        [ProducesResponseType(typeof(ApiResponse<TelegramLinkStatus>), StatusCodes.Status200OK)]
        public async Task<ActionResult<ApiResponse<TelegramLinkStatus>>> LinkManually(
            [FromBody] AdminLinkTelegramRequest request)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            return HttpResponseHelper.CreateHttpResponse(await _linking.LinkManuallyAsync(request));
        }

        /// <summary>
        /// Sends a short test message to somebody's linked chat.
        ///
        /// The point of the manual path is that nobody proved they own the chat, so
        /// the administrator needs a way to find out before a real escalation is the
        /// thing that discovers a wrong digit.
        /// </summary>
        [HttpPost("test-message")]
        [Authorize(Policy = PolicyNames.AdminOnly)]
        [EnableRateLimiting(RateLimitPolicies.Sensitive)]
        [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
        public async Task<ActionResult<ApiResponse<bool>>> TestMessage(
            [FromBody] DisconnectTelegramRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.PersonId))
            {
                return BadRequest(new ApiResponse<bool>(
                    ResponseType.Warning, "Say whose link to test.", false));
            }

            return HttpResponseHelper.CreateHttpResponse(
                await _linking.SendTestMessageAsync(request.PersonId));
        }

        /// <summary>
        /// What is configured, and what Telegram currently believes.
        ///
        /// Reports the token as a BOOLEAN and never its value. A setup screen needs to
        /// know whether a secret is present, never what it is — putting it in a
        /// response would land it in browser history, proxy logs and any error report.
        /// </summary>
        [HttpGet("setup")]
        [Authorize(Policy = PolicyNames.AdminOnly)]
        [ProducesResponseType(typeof(ApiResponse<TelegramSetupDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Setup()
        {
            var setup = new TelegramSetupDto
            {
                BotTokenConfigured = _client.HasToken,
                BotUsername = _client.BotUsername,
                WebhookSecretConfigured = !string.IsNullOrWhiteSpace(_auth.TelegramWebhookSecret),
                SuggestedWebhookUrl = $"{Request.Scheme}://{Request.Host}/api/telegram/webhook",
                LinkedPeople = await _linking.CountLinkedAsync()
            };

            // Only ask Telegram anything if there is a token to ask with.
            if (setup.BotTokenConfigured)
            {
                var (meOk, meDetail) = await _client.GetMeAsync();

                setup.ConnectionOk = meOk;
                setup.ConnectionDetail = meOk ? ReadBotName(meDetail) : Summarise(meDetail);

                var (hookOk, hookDetail) = await _client.GetWebhookInfoAsync();

                if (hookOk) ApplyWebhookInfo(setup, hookDetail);
                else setup.WebhookDetail = Summarise(hookDetail);
            }

            return Ok(new ApiResponse<TelegramSetupDto>(ResponseType.Success, "Telegram setup", setup));
        }

        /// <summary>Pulls the bot's @username out of a getMe response.</summary>
        private static string ReadBotName(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("result", out var result) &&
                    result.TryGetProperty("username", out var username))
                {
                    return "@" + username.GetString();
                }
            }
            catch (JsonException) { /* fall through to the generic answer */ }

            return "Connected.";
        }

        /// <summary>
        /// Flattens getWebhookInfo into the few fields an administrator acts on:
        /// where Telegram is delivering, how much is stuck, and why.
        /// </summary>
        private static void ApplyWebhookInfo(TelegramSetupDto setup, string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("result", out var result)) return;

                setup.WebhookUrl = result.TryGetProperty("url", out var url) ? url.GetString() : null;

                setup.WebhookRegistered = !string.IsNullOrWhiteSpace(setup.WebhookUrl);

                setup.PendingUpdates = result.TryGetProperty("pending_update_count", out var pending)
                    && pending.TryGetInt32(out var count) ? count : 0;

                // Telegram keeps reporting the last failure long after it is fixed, so
                // this is shown as history rather than as a current fault.
                setup.LastError = result.TryGetProperty("last_error_message", out var error)
                    ? error.GetString()
                    : null;
            }
            catch (JsonException)
            {
                setup.WebhookDetail = "Telegram returned an unreadable response.";
            }
        }

        /// <summary>Keeps a Telegram error short enough to show without dumping raw JSON.</summary>
        private static string Summarise(string detail)
        {
            if (string.IsNullOrWhiteSpace(detail)) return "No detail.";

            try
            {
                using var doc = JsonDocument.Parse(detail);

                if (doc.RootElement.TryGetProperty("description", out var description))
                    return description.GetString() ?? detail;
            }
            catch (JsonException) { /* not JSON; fall through */ }

            return detail.Length > 200 ? detail[..200] : detail;
        }

        /// <summary>
        /// Registers the webhook with Telegram, using the secret the application
        /// already validates against. Administrator-only setup action.
        /// </summary>
        [HttpPost("webhook/register")]
        [Authorize(Policy = PolicyNames.AdminOnly)]
        [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
        public async Task<IActionResult> RegisterWebhook([FromQuery] string? baseUrl = null)
        {
            if (string.IsNullOrWhiteSpace(_auth.TelegramWebhookSecret))
            {
                return Ok(new ApiResponse<string>(ResponseType.Warning,
                    "Auth__TelegramWebhookSecret is not configured, so the webhook cannot be secured.", null!));
            }

            // Telegram requires a public HTTPS URL; localhost will be rejected.
            var origin = string.IsNullOrWhiteSpace(baseUrl)
                ? $"{Request.Scheme}://{Request.Host}"
                : baseUrl.TrimEnd('/');

            var (ok, detail) = await _client.SetWebhookAsync(
                $"{origin}/api/telegram/webhook", _auth.TelegramWebhookSecret);

            return Ok(new ApiResponse<string>(
                ok ? ResponseType.Success : ResponseType.Warning,
                ok ? "Webhook registered." : "Telegram refused the webhook.", detail));
        }

        /// <summary>What Telegram currently believes about the webhook.</summary>
        [HttpGet("webhook/status")]
        [Authorize(Policy = PolicyNames.AdminOnly)]
        [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
        public async Task<IActionResult> WebhookStatus()
        {
            var (ok, detail) = await _client.GetWebhookInfoAsync();

            return Ok(new ApiResponse<string>(
                ok ? ResponseType.Success : ResponseType.Warning,
                ok ? "Webhook status" : "Could not read the webhook status.", detail));
        }

        /// <summary>
        /// Own person, or anybody if an administrator. Without this an ordinary user
        /// could mint an invitation that links someone else's alerts to their phone.
        /// </summary>
        private bool MayActFor(string? personId) =>
            string.IsNullOrWhiteSpace(personId)
            || _current.IsAdmin
            || string.Equals(personId, _current.PersonId, StringComparison.Ordinal);
    }
}
