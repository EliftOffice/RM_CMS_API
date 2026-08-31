using System.ComponentModel.DataAnnotations;

namespace RM_CMS.Modules.Telegram.Domain
{
    /// <summary>
    /// Configuration for the bot.
    ///
    /// The token is a third-party secret and comes from the environment
    /// (<c>Telegram__BotToken</c>), never the database. The MVP kept the live token in
    /// a <c>system_config</c> row, readable through a generic settings API by anyone
    /// who could reach it — whoever held it could impersonate the church to every
    /// volunteer who had ever messaged the bot.
    /// </summary>
    public sealed class TelegramOptions
    {
        public const string SectionName = "Telegram";

        /// <summary>From BotFather. Absent means Telegram features are simply off.</summary>
        public string? BotToken { get; set; }

        /// <summary>The bot's @name, used to build the deep link people click.</summary>
        public string? BotUsername { get; set; }

        /// <summary>Base address, overridable only so tests can point elsewhere.</summary>
        public string ApiBaseUrl { get; set; } = "https://api.telegram.org";

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(BotToken) && !string.IsNullOrWhiteSpace(BotUsername);
    }

    /// <summary>A one-time token that names exactly one person.</summary>
    public sealed class TelegramLinkToken
    {
        public long Id { get; set; }
        public long PersonId { get; set; }
        public string TokenHash { get; set; } = string.Empty;
        public DateTime IssuedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public DateTime? UsedAt { get; set; }
        public long? IssuedBy { get; set; }
    }

    /// <summary>
    /// The slice of a Telegram update this application acts on.
    ///
    /// Telegram sends a large, loosely-typed envelope. Binding only these fields means
    /// an unexpected update shape is ignored rather than throwing, which matters because
    /// a webhook that errors makes Telegram retry the same update indefinitely.
    /// </summary>
    public sealed class TelegramUpdate
    {
        public long UpdateId { get; set; }
        public TelegramMessage? Message { get; set; }
    }

    public sealed class TelegramMessage
    {
        public long MessageId { get; set; }
        public long ChatId { get; set; }
        public long? FromId { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Username { get; set; }
        public string? Text { get; set; }

        /// <summary>The payload after <c>/start</c>, when the message is a start command.</summary>
        public string? StartPayload
        {
            get
            {
                var text = (Text ?? string.Empty).Trim();

                if (!text.StartsWith("/start", StringComparison.OrdinalIgnoreCase))
                    return null;

                var payload = text[6..].Trim();
                return payload.Length == 0 ? null : payload;
            }
        }

        public bool IsStartCommand =>
            (Text ?? string.Empty).TrimStart().StartsWith("/start", StringComparison.OrdinalIgnoreCase);

        /// <summary>Best available display name, for the confirmation message only.</summary>
        public string DisplayName =>
            string.Join(' ', new[] { FirstName, LastName }.Where(p => !string.IsNullOrWhiteSpace(p)))
                is { Length: > 0 } name
                ? name
                : Username ?? "there";
    }

    /// <summary>What linking a chat concluded, so the caller can reply appropriately.</summary>
    public enum LinkOutcome
    {
        /// <summary>Linked for the first time.</summary>
        Linked,

        /// <summary>This chat was already linked to this same person.</summary>
        AlreadyLinked,

        /// <summary>The token was missing, expired or already used.</summary>
        InvalidToken,

        /// <summary>
        /// This Telegram account belongs to a different person. Never silently
        /// reassigned — one chat id delivering another person's pastoral alerts is
        /// the worst failure this feature can have.
        /// </summary>
        ClaimedByAnotherPerson,

        /// <summary>No token, and this chat is not linked to anyone.</summary>
        Unknown
    }

    public sealed class TelegramLinkStatus
    {
        public bool IsLinked { get; set; }
        public bool IsConfigured { get; set; }

        /// <summary>Whether an administrator has made linking mandatory.</summary>
        public bool IsRequired { get; set; }

        public string? Username { get; set; }
        public DateTime? LinkedAt { get; set; }

        /// <summary>
        /// How many OTHER people are linked to the same chat id. Zero is the normal
        /// case; anything above it means alerts are shared both ways.
        /// </summary>
        public int SharedWithCount { get; set; }
    }

    public sealed class TelegramLinkInvitation
    {
        /// <summary>The t.me link to open. Contains the token, never the person id.</summary>
        public string DeepLink { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
    }

    /// <summary>
    /// What an administrator needs to get Telegram working, and to see whether it is.
    ///
    /// Secrets appear only as booleans. The bot token is never returned, so this can be
    /// rendered in a browser, logged, or pasted into a support ticket without leaking
    /// the credential that lets someone impersonate the church.
    /// </summary>
    public sealed class TelegramSetupDto
    {
        public bool BotTokenConfigured { get; set; }
        public string? BotUsername { get; set; }
        public bool WebhookSecretConfigured { get; set; }

        /// <summary>Everything present that the application itself controls.</summary>
        public bool IsConfigured => BotTokenConfigured && !string.IsNullOrWhiteSpace(BotUsername);

        /// <summary>Whether the token actually works, proved by calling getMe.</summary>
        public bool ConnectionOk { get; set; }
        public string? ConnectionDetail { get; set; }

        public bool WebhookRegistered { get; set; }
        public string? WebhookUrl { get; set; }
        public int PendingUpdates { get; set; }
        public string? LastError { get; set; }
        public string? WebhookDetail { get; set; }

        /// <summary>Where the webhook would be registered if asked right now.</summary>
        public string? SuggestedWebhookUrl { get; set; }

        /// <summary>Adoption, so an administrator can see whether anyone is linked.</summary>
        public int LinkedPeople { get; set; }
    }

    public sealed class DisconnectTelegramRequest
    {
        /// <summary>Whose link to remove. Omitted means the caller's own.</summary>
        [StringLength(26)]
        public string? PersonId { get; set; }
    }

    /// <summary>
    /// An administrator linking somebody using details they already hold.
    ///
    /// <see cref="PersonId"/> is required and has no "my own" default: this is not a
    /// self-service action, and an omitted target that silently meant "me" would be a
    /// way to link the administrator's own account by accident.
    /// </summary>
    public sealed class AdminLinkTelegramRequest
    {
        [Required][StringLength(26, MinimumLength = 26)]
        public string PersonId { get; set; } = string.Empty;

        /// <summary>
        /// The numeric Telegram chat id. Not the @username — Telegram's API cannot
        /// start a conversation from a handle, only from an id of a chat that has
        /// already messaged the bot.
        /// </summary>
        [Required][StringLength(32)]
        public string ChatId { get; set; } = string.Empty;

        /// <summary>
        /// Optional display handle. Only used when Telegram does not report one
        /// itself, which it usually does.
        /// </summary>
        [StringLength(64)]
        public string? Username { get; set; }

    }
}
