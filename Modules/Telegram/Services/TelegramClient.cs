using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RM_CMS.Modules.Telegram.Domain;

namespace RM_CMS.Modules.Telegram.Services
{
    /// <summary>
    /// The only thing in the application that talks to the Telegram Bot API.
    ///
    /// Controllers and business logic call this; nothing else builds a Telegram URL.
    /// That matters because the bot token is in every one of those URLs — keeping the
    /// construction in one place is what stops it reaching a log, an error message or
    /// an exception trace somewhere else.
    /// </summary>
    public interface ITelegramClient
    {
        bool IsConfigured { get; }

        /// <summary>The t.me link that opens the bot, with a start payload.</summary>
        string BuildDeepLink(string startPayload);

        /// <summary>
        /// Sends a message. Returns false rather than throwing: a failed alert must be
        /// recorded and moved past, never allowed to roll back the domain transaction
        /// that raised it.
        /// </summary>
        Task<bool> SendMessageAsync(long chatId, string text, CancellationToken cancellationToken = default);

        /// <summary>Registers the webhook with Telegram. Used by the admin setup action.</summary>
        Task<(bool Ok, string Detail)> SetWebhookAsync(string url, string secretToken, CancellationToken cancellationToken = default);

        /// <summary>What Telegram believes about the current webhook, for diagnostics.</summary>
        Task<(bool Ok, string Detail)> GetWebhookInfoAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Asks Telegram to identify the bot. This is the only way to prove a token is
        /// actually valid without waiting for a real user to press Start.
        /// </summary>
        Task<(bool Ok, string Detail)> GetMeAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Asks Telegram what it knows about a chat id.
        ///
        /// This is what makes an administrator entering a chat id by hand safe enough
        /// to allow. A chat id is an opaque integer, so a mistyped one is not
        /// malformed — it is a VALID id belonging to somebody else, and the mistake
        /// only surfaces when that stranger starts receiving another person's
        /// pastoral alerts. Telegram answering for the id proves the chat exists and
        /// that this bot can reach it.
        /// </summary>
        Task<ChatLookup> GetChatAsync(long chatId, CancellationToken cancellationToken = default);

        /// <summary>The configured bot username, for display. Never the token.</summary>
        string? BotUsername { get; }

        /// <summary>
        /// Whether a token is present — reported separately from the username so the
        /// setup screen can say which half is missing. Only ever a boolean.
        /// </summary>
        bool HasToken { get; }
    }

    /// <summary>Why a chat lookup did or did not succeed.</summary>
    public enum ChatLookupOutcome
    {
        Ok,

        /// <summary>Telegram answered, and does not know that chat for this bot.</summary>
        ChatNotFound,

        /// <summary>Telegram rejected the token itself.</summary>
        BadToken,

        /// <summary>We never got an answer — DNS, firewall, proxy, or no connection.</summary>
        Unreachable,

        NotConfigured
    }

    public sealed record ChatLookup(
        ChatLookupOutcome Outcome,
        string Detail,
        string? Username = null,
        string? DisplayName = null)
    {
        public bool Ok => Outcome == ChatLookupOutcome.Ok;
    }

    public sealed class TelegramClient : ITelegramClient
    {
        private readonly IHttpClientFactory _httpFactory;
        private readonly TelegramOptions _options;
        private readonly ILogger<TelegramClient> _logger;

        public TelegramClient(
            IHttpClientFactory httpFactory,
            IOptions<TelegramOptions> options,
            ILogger<TelegramClient> logger)
        {
            _httpFactory = httpFactory;
            _options = options.Value;
            _logger = logger;
        }

        public bool IsConfigured => _options.IsConfigured;

        public string? BotUsername => _options.NormalizedBotUsername;

        /// <summary>True when a token is present. Never exposes the token itself.</summary>
        public bool HasToken => !string.IsNullOrWhiteSpace(_options.BotToken);

        public async Task<(bool Ok, string Detail)> GetMeAsync(CancellationToken cancellationToken = default)
        {
            if (!HasToken) return (false, "No bot token is configured.");

            return await PostAsync("getMe", "{}", cancellationToken);
        }

        public async Task<ChatLookup> GetChatAsync(long chatId, CancellationToken cancellationToken = default)
        {
            if (!HasToken)
                return new ChatLookup(ChatLookupOutcome.NotConfigured, "No bot token is configured.");

            var payload = JsonSerializer.Serialize(new { chat_id = chatId });
            var (ok, detail) = await PostAsync("getChat", payload, cancellationToken);

            // One retry, and only for a TRANSPORT failure (timeout, DNS, connection
            // reset) — never for an answer Telegram actually gave. getChat is
            // read-only, so retrying it duplicates nothing; the "sometimes it just
            // times out" reports were single slow round-trips to api.telegram.org,
            // not a real outage, and this alone clears most of them without the
            // administrator seeing anything.
            if (!ok && ClassifyFailure(detail) == ChatLookupOutcome.Unreachable)
            {
                _logger.LogInformation(
                    "Telegram getChat for {ChatId} failed once; retrying.", chatId);

                (ok, detail) = await PostAsync("getChat", payload, cancellationToken);
            }

            if (!ok)
                return new ChatLookup(ClassifyFailure(detail), Describe(detail));

            // Pull the handle and name back out so the caller can show the
            // administrator WHO they are about to link, rather than asking them to
            // trust a number they typed.
            try
            {
                using var doc = JsonDocument.Parse(detail);

                if (!doc.RootElement.TryGetProperty("result", out var result))
                    return new ChatLookup(ChatLookupOutcome.Ok, detail);

                var username = result.TryGetProperty("username", out var u) ? u.GetString() : null;

                var first = result.TryGetProperty("first_name", out var f) ? f.GetString() : null;
                var last = result.TryGetProperty("last_name", out var l) ? l.GetString() : null;
                var title = result.TryGetProperty("title", out var t) ? t.GetString() : null;

                var display = title ?? string.Join(' ',
                    new[] { first, last }.Where(s => !string.IsNullOrWhiteSpace(s)));

                return new ChatLookup(ChatLookupOutcome.Ok, detail, username,
                    string.IsNullOrWhiteSpace(display) ? null : display);
            }
            catch (JsonException)
            {
                // Telegram answered, which is the part that matters. A shape we cannot
                // parse costs the confirmation name, not the verification.
                return new ChatLookup(ChatLookupOutcome.Ok, detail);
            }
        }

        public string BuildDeepLink(string startPayload) =>
            $"https://t.me/{_options.NormalizedBotUsername}?start={Uri.EscapeDataString(startPayload)}";

        public async Task<bool> SendMessageAsync(long chatId, string text, CancellationToken cancellationToken = default)
        {
            if (!IsConfigured)
            {
                _logger.LogWarning("Telegram message not sent: the bot is not configured.");
                return false;
            }

            var payload = JsonSerializer.Serialize(new
            {
                chat_id = chatId,
                text,
                parse_mode = "HTML",
                disable_web_page_preview = true
            });

            return await PostAsync("sendMessage", payload, cancellationToken) is { Ok: true };
        }

        public async Task<(bool Ok, string Detail)> SetWebhookAsync(
            string url, string secretToken, CancellationToken cancellationToken = default)
        {
            if (!IsConfigured) return (false, "The Telegram bot is not configured.");

            var payload = JsonSerializer.Serialize(new
            {
                url,
                secret_token = secretToken,
                // Only messages are processed, so asking for anything else would just
                // be traffic this application discards.
                allowed_updates = new[] { "message" },
                drop_pending_updates = true
            });

            return await PostAsync("setWebhook", payload, cancellationToken);
        }

        public async Task<(bool Ok, string Detail)> GetWebhookInfoAsync(CancellationToken cancellationToken = default)
        {
            if (!IsConfigured) return (false, "The Telegram bot is not configured.");

            return await PostAsync("getWebhookInfo", "{}", cancellationToken);
        }

        /// <summary>
        /// One place where the token meets a URL. Failures are logged with the METHOD
        /// only — never the request URI, which contains the token.
        /// </summary>
        private async Task<(bool Ok, string Detail)> PostAsync(
            string method, string jsonPayload, CancellationToken cancellationToken)
        {
            try
            {
                var client = _httpFactory.CreateClient(nameof(TelegramClient));

                var uri = $"{_options.ApiBaseUrl.TrimEnd('/')}/bot{_options.BotToken}/{method}";

                using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                using var response = await client.PostAsync(uri, content, cancellationToken);

                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "Telegram {Method} failed with {StatusCode}: {Body}",
                        method, (int)response.StatusCode, Sanitise(body));

                    return (false, Sanitise(body));
                }

                return (true, Sanitise(body));
            }
            catch (Exception ex)
            {
                // Deliberately does not log the exception's full detail: an
                // HttpRequestException can carry the request URI, and the URI carries
                // the token.
                _logger.LogError("Telegram {Method} threw {Type}: {Message}",
                    method, ex.GetType().Name, Sanitise(ex.Message));

                return (false, "Could not reach Telegram.");
            }
        }

        /// <summary>
        /// Sorts a failed call into what actually went wrong. Three very different
        /// problems used to collapse into one message that blamed the chat id — being
        /// unable to REACH Telegram is not the operator mistyping a number, and
        /// neither is a revoked token.
        /// </summary>
        private static ChatLookupOutcome ClassifyFailure(string detail) =>
            detail.Contains("Could not reach Telegram", StringComparison.OrdinalIgnoreCase)
                ? ChatLookupOutcome.Unreachable
            : detail.Contains("\"error_code\":401", StringComparison.OrdinalIgnoreCase) ||
              detail.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase)
                ? ChatLookupOutcome.BadToken
                : ChatLookupOutcome.ChatNotFound;

        /// <summary>
        /// Telegram's own words when it gave any — "Bad Request: chat not found" says
        /// far more than a generic failure, and it is what the operator needs to see.
        /// </summary>
        private static string Describe(string detail)
        {
            if (string.IsNullOrWhiteSpace(detail)) return "No detail was returned.";

            try
            {
                using var doc = JsonDocument.Parse(detail);

                if (doc.RootElement.TryGetProperty("description", out var d))
                    return d.GetString() ?? detail;
            }
            catch (JsonException)
            {
                // Not JSON — the transport failed, and `detail` is already our sentence.
            }

            return detail;
        }

        /// <summary>
        /// Last-resort guard: strips the token if it ever appears in a response or
        /// message that is about to be logged or returned.
        /// </summary>
        private string Sanitise(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            var token = _options.BotToken;

            return string.IsNullOrWhiteSpace(token)
                ? value
                : value.Replace(token, "***", StringComparison.Ordinal);
        }
    }
}
