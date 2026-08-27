using System.Security.Cryptography;
using RM_CMS.Modules.Settings.Data;
using RM_CMS.Modules.Identity.Data;
using RM_CMS.Modules.Identity.Services;
using RM_CMS.Modules.Telegram.Data;
using RM_CMS.Modules.Telegram.Domain;
using RM_CMS.Utilities;

namespace RM_CMS.Modules.Telegram.Services
{
    /// <summary>
    /// Connects a Telegram account to a person.
    ///
    /// The MVP did this by asking Telegram for the most recent chat and assuming it
    /// belonged to whoever was on screen. Two people connecting at once silently
    /// crossed the wires, and from then on one person's crisis alerts went to a
    /// stranger. Here a token names exactly one person before Telegram is ever opened,
    /// so there is nothing to guess.
    ///
    /// Scope note: this deliberately does NOT create people from unknown chats. An
    /// unauthenticated endpoint that writes rows into a pastoral database is a
    /// different feature with its own abuse questions.
    /// </summary>
    public interface ITelegramLinkService
    {
        Task<ApiResponse<TelegramLinkStatus>> GetStatusAsync(string? personPublicId = null);

        /// <summary>Issues a fresh one-time link, retiring any earlier unused one.</summary>
        Task<ApiResponse<TelegramLinkInvitation>> CreateInvitationAsync(string? personPublicId = null);

        /// <summary>Handles an inbound <c>/start</c>. Returns what to reply.</summary>
        Task<(LinkOutcome Outcome, string Reply)> HandleStartAsync(TelegramMessage message);

        Task<ApiResponse<bool>> DisconnectAsync(string? personPublicId = null);

        /// <summary>How many people are linked, for the setup screen's adoption figure.</summary>
        Task<int> CountLinkedAsync();
    }

    public sealed class TelegramLinkService : ITelegramLinkService
    {
        private const string RequireLinkingSetting = "telegram.require_linking";
        private const string TokenMinutesSetting = "telegram.link_token_minutes";

        private readonly ITelegramRepository _telegram;
        private readonly ITelegramClient _client;
        private readonly ISettingRepository _settings;
        private readonly IUserAccountRepository _accounts;
        private readonly ICurrentIdentity _current;
        private readonly TimeProvider _clock;
        private readonly ILogger<TelegramLinkService> _logger;

        public TelegramLinkService(
            ITelegramRepository telegram,
            ITelegramClient client,
            ISettingRepository settings,
            IUserAccountRepository accounts,
            ICurrentIdentity current,
            TimeProvider clock,
            ILogger<TelegramLinkService> logger)
        {
            _telegram = telegram;
            _client = client;
            _settings = settings;
            _accounts = accounts;
            _current = current;
            _clock = clock;
            _logger = logger;
        }

        // ==================================================================
        // Status
        // ==================================================================
        public async Task<ApiResponse<TelegramLinkStatus>> GetStatusAsync(string? personPublicId = null)
        {
            var personId = await ResolveTargetAsync(personPublicId);

            if (personId is null) return Warn<TelegramLinkStatus>("That person was not found.");

            var contact = await _telegram.GetContactAsync(personId.Value);
            var linked = contact is not null && contact.OptedOutAt is null && contact.IsVerified;

            return Ok(new TelegramLinkStatus
            {
                IsLinked = linked,
                IsConfigured = _client.IsConfigured,
                IsRequired = await IsRequiredAsync(),
                Username = linked ? contact!.Value : null,
                LinkedAt = linked ? contact!.VerifiedAt : null
            }, linked ? "Connected" : "Not connected");
        }

        // ==================================================================
        // Invitation
        // ==================================================================
        public async Task<ApiResponse<TelegramLinkInvitation>> CreateInvitationAsync(string? personPublicId = null)
        {
            if (!_client.IsConfigured)
                return Warn<TelegramLinkInvitation>(
                    "Telegram is not set up yet. Ask an administrator to configure the bot.");

            var personId = await ResolveTargetAsync(personPublicId);

            if (personId is null) return Warn<TelegramLinkInvitation>("That person was not found.");

            var now = _clock.GetUtcNow().UtcDateTime;
            var minutes = await _settings.GetIntAsync(TokenMinutesSetting, 30);
            var expiresAt = now.AddMinutes(minutes);

            // Only one live invitation per person: an old link left working would be a
            // second way into the same account.
            await _telegram.ExpireOutstandingTokensAsync(personId.Value, now);

            // 32 bytes of CSPRNG output. Telegram's start payload allows only
            // A-Z a-z 0-9 _ - and caps it at 64 characters, so base64url is converted
            // to that alphabet rather than sent raw — a '+' or '=' would be dropped by
            // Telegram and the token would never match.
            var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');

            await _telegram.CreateTokenAsync(personId.Value, Hash(token), now, expiresAt, await ActingUserIdAsync());

            _logger.LogInformation("Telegram invitation issued for person {PersonId}", personId.Value);

            return Ok(new TelegramLinkInvitation
            {
                DeepLink = _client.BuildDeepLink(token),
                ExpiresAt = expiresAt
            }, "Open this link in Telegram and press Start.");
        }

        // ==================================================================
        // Inbound /start
        // ==================================================================
        public async Task<(LinkOutcome Outcome, string Reply)> HandleStartAsync(TelegramMessage message)
        {
            var chatId = message.ChatId.ToString();
            var now = _clock.GetUtcNow().UtcDateTime;

            var existingOwner = await _telegram.FindPersonByChatIdAsync(chatId);
            var payload = message.StartPayload;

            // ---- no token: only useful if we already know this chat ----
            if (string.IsNullOrWhiteSpace(payload))
            {
                if (existingOwner is not null)
                {
                    // Re-verify, which also undoes an earlier disconnect.
                    await _telegram.LinkAsync(existingOwner.Value, chatId, message.Username, now, null);

                    return (LinkOutcome.AlreadyLinked,
                        $"You are already connected to RMChurch, {Escape(message.DisplayName)}.");
                }

                return (LinkOutcome.Unknown,
                    "Hello. To connect this Telegram account, open the personal link from your " +
                    "RMChurch profile — the Connect Telegram button on your account page.");
            }

            // ---- token present ----
            var personId = await _telegram.RedeemTokenAsync(Hash(payload), now);

            if (personId is null)
            {
                _logger.LogInformation("Telegram link rejected for chat {ChatId}: token invalid or spent", chatId);

                return (LinkOutcome.InvalidToken,
                    "That link has expired or has already been used. " +
                    "Please generate a new one from your RMChurch profile.");
            }

            // ---- the chat may already belong to somebody ----
            if (existingOwner is not null && existingOwner.Value != personId.Value)
            {
                // Never reassigned silently. Being wrong here means one person's
                // pastoral alerts arrive on another person's phone.
                _logger.LogWarning(
                    "Telegram link conflict: chat {ChatId} is held by person {Owner}, token named person {Target}",
                    chatId, existingOwner.Value, personId.Value);

                return (LinkOutcome.ClaimedByAnotherPerson,
                    "This Telegram account is already connected to a different RMChurch profile. " +
                    "Please contact an administrator.");
            }

            await _telegram.LinkAsync(personId.Value, chatId, message.Username, now, null);

            _logger.LogInformation("Telegram linked for person {PersonId}", personId.Value);

            return (LinkOutcome.Linked,
                $"✅ Connected. Thank you, {Escape(message.DisplayName)} — " +
                "RMChurch will now reach you here.");
        }

        // ==================================================================
        // Disconnect
        // ==================================================================
        public async Task<ApiResponse<bool>> DisconnectAsync(string? personPublicId = null)
        {
            var personId = await ResolveTargetAsync(personPublicId);

            if (personId is null) return Warn<bool>("That person was not found.");

            var now = _clock.GetUtcNow().UtcDateTime;
            var removed = await _telegram.DisconnectAsync(personId.Value, now, await ActingUserIdAsync());

            if (!removed) return Warn<bool>("There was no connected Telegram account.");

            _logger.LogInformation("Telegram disconnected for person {PersonId}", personId.Value);

            return Ok(true, "Telegram disconnected. They will no longer receive messages here.");
        }

        public Task<int> CountLinkedAsync() => _telegram.CountLinkedAsync();

        // ==================================================================
        // Helpers
        // ==================================================================

        /// <summary>
        /// Whose link is being acted on. Null means the caller's own person, which is
        /// the only target a non-administrator may use — the controller enforces that.
        /// </summary>
        private async Task<long?> ResolveTargetAsync(string? personPublicId)
        {
            var target = personPublicId ?? _current.PersonId;

            return string.IsNullOrWhiteSpace(target)
                ? null
                : await _telegram.ResolvePersonIdAsync(target);
        }

        private async Task<bool> IsRequiredAsync() =>
            await _settings.GetBoolAsync(RequireLinkingSetting, false);

        private async Task<long?> ActingUserIdAsync()
        {
            var publicId = _current.AccountId;

            return string.IsNullOrWhiteSpace(publicId)
                ? null
                : (await _accounts.GetByPublicIdAsync(publicId))?.Id;
        }

        private static string Hash(string value) =>
            Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

        /// <summary>Messages are sent as HTML, so a name with a bracket must not break it.</summary>
        private static string Escape(string value) =>
            value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

        private static ApiResponse<T> Ok<T>(T data, string message) => new(ResponseType.Success, message, data);
        private static ApiResponse<T> Warn<T>(string message) => new(ResponseType.Warning, message, default!);
    }
}
