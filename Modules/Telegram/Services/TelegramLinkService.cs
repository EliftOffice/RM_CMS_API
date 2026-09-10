using System.Security.Cryptography;
using RM_CMS.Modules.Settings.Data;
using RM_CMS.Modules.Identity.Data;
using RM_CMS.Modules.Identity.Services;
using RM_CMS.Modules.MessageTemplates.Domain;
using RM_CMS.Modules.MessageTemplates.Services;
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

        /// <summary>
        /// Links a person to a chat id an administrator already holds, without waiting
        /// for that person to press Start.
        ///
        /// The id is checked against Telegram first. Ordinary linking proves ownership
        /// — the person opens the deep link and Telegram tells us who they are — and
        /// this path has no such proof, so the one guarantee it CAN offer is that the
        /// chat exists and the bot can reach it. Without that a mistyped digit is a
        /// valid id belonging to a stranger, and the first sign of the mistake is
        /// somebody else receiving another person's pastoral alerts.
        /// </summary>
        Task<ApiResponse<TelegramLinkStatus>> LinkManuallyAsync(AdminLinkTelegramRequest request);

        /// <summary>
        /// Sends a short message to a person's linked chat, so an administrator can
        /// confirm it arrives at the right person before a real alert does.
        /// </summary>
        Task<ApiResponse<bool>> SendTestMessageAsync(string personPublicId);

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

        /// <summary>
        /// The wording of every reply below. Editable by an administrator on the
        /// Telegram messages screen; falls back to the text compiled in.
        /// </summary>
        private readonly ITemplateService _templates;
        private readonly TimeProvider _clock;
        private readonly ILogger<TelegramLinkService> _logger;

        public TelegramLinkService(
            ITelegramRepository telegram,
            ITelegramClient client,
            ISettingRepository settings,
            IUserAccountRepository accounts,
            ICurrentIdentity current,
            ITemplateService templates,
            TimeProvider clock,
            ILogger<TelegramLinkService> logger)
        {
            _telegram = telegram;
            _client = client;
            _settings = settings;
            _accounts = accounts;
            _current = current;
            _templates = templates;
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

            // Only worth a query when they are actually linked — the answer is
            // meaningless otherwise.
            var sharedWith = linked
                ? await _telegram.CountOthersOnChatIdAsync(contact!.ChatId, personId.Value)
                : 0;

            return Ok(new TelegramLinkStatus
            {
                IsLinked = linked,
                IsConfigured = _client.IsConfigured,
                IsRequired = await IsRequiredAsync(),
                Username = linked ? contact!.Value : null,
                LinkedAt = linked ? contact!.VerifiedAt : null,
                SharedWithCount = sharedWith
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
                        await ReplyAsync(TelegramTemplates.LinkAlreadyConnected, message));
                }

                return (LinkOutcome.Unknown,
                    await ReplyAsync(TelegramTemplates.LinkUnknownChat, message));
            }

            // ---- token present ----
            var personId = await _telegram.RedeemTokenAsync(Hash(payload), now);

            if (personId is null)
            {
                _logger.LogInformation("Telegram link rejected for chat {ChatId}: token invalid or spent", chatId);

                return (LinkOutcome.InvalidToken,
                    await ReplyAsync(TelegramTemplates.LinkExpired, message));
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
                    await ReplyAsync(TelegramTemplates.LinkClaimedByAnother, message));
            }

            await _telegram.LinkAsync(personId.Value, chatId, message.Username, now, null);

            _logger.LogInformation("Telegram linked for person {PersonId}", personId.Value);

            return (LinkOutcome.Linked,
                await ReplyAsync(TelegramTemplates.LinkWelcome, message, personId.Value));
        }

        /// <summary>
        /// Renders one of the /start replies.
        /// </summary>
        /// <remarks>
        /// {{TelegramName}} comes off the Telegram message rather than the database,
        /// because for most of these outcomes there is no person on file yet — an
        /// unrecognised chat and an expired token both reach a stranger. It is passed
        /// as an ordinary value so the renderer escapes it: a Telegram display name is
        /// whatever its owner typed, angle brackets included.
        ///
        /// {{RecipientName}} is only meaningful once we know who they are, which is why
        /// the successful link is the one call that passes a person id.
        /// </remarks>
        private async Task<string> ReplyAsync(string code, TelegramMessage message, long? personId = null)
        {
            var values = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["TelegramName"] = message.DisplayName
            };

            var rendered = await _templates.RenderAsync(code, personId, values);

            // Never silence. A /start that gets no answer at all reads as a broken bot,
            // and the person is left with no idea whether they are connected.
            return string.IsNullOrWhiteSpace(rendered)
                ? "Thank you. Please contact the church office if you need help connecting."
                : rendered;
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

        public async Task<ApiResponse<TelegramLinkStatus>> LinkManuallyAsync(AdminLinkTelegramRequest request)
        {
            var personId = await _telegram.ResolvePersonIdAsync(request.PersonId);

            if (personId is null) return Warn<TelegramLinkStatus>("That person was not found.");

            if (!long.TryParse(request.ChatId?.Trim(), out var chatId) || chatId == 0)
            {
                return Warn<TelegramLinkStatus>(
                    "A Telegram chat id is a whole number, like 123456789. " +
                    "It is not the @username.");
            }

            // Sharing a chat id is ALLOWED, by decision.
            //
            // This used to refuse outright, on the grounds that two people on one chat
            // id means each receives the other's alerts. That is still true and is why
            // the count below is reported rather than left silent — but it is a
            // legitimate arrangement when one phone is genuinely shared, or when an
            // office relays messages, and the guard made those cases impossible.
            var sharedWith = await _telegram.CountOthersOnChatIdAsync(
                chatId.ToString(), personId.Value);

            if (!_client.IsConfigured)
            {
                return Warn<TelegramLinkStatus>(
                    "The Telegram bot is not configured, so the chat id cannot be checked. " +
                    "Set it up under Telegram first.");
            }

            var lookup = await _client.GetChatAsync(chatId);
            var verified = lookup.Ok;

            if (!lookup.Ok)
            {
                _logger.LogWarning(
                    "Telegram chat lookup for {ChatId} by {AccountId} returned {Outcome}: {Detail}",
                    chatId, _current.AccountId, lookup.Outcome, lookup.Detail);

                // BadToken, NotConfigured and ChatNotFound are real problems with a
                // real fix, and none of them get better by linking anyway: a bad token
                // cannot send the test message either, and "chat not found" is
                // Telegram actively saying this id does not exist for this bot.
                if (lookup.Outcome != ChatLookupOutcome.Unreachable)
                {
                    return Warn<TelegramLinkStatus>(lookup.Outcome switch
                    {
                        ChatLookupOutcome.BadToken =>
                            "Telegram rejected the bot token. It is wrong, or it was revoked in " +
                            "BotFather. Check it under Telegram setup.",

                        ChatLookupOutcome.NotConfigured =>
                            "The Telegram bot is not configured, so the chat id cannot be checked.",

                        // Telegram answered and does not know this chat for this bot.
                        _ => $"Telegram does not recognise that chat id for this bot ({lookup.Detail}). " +
                             "The person must open the bot and press Start at least once before " +
                             "their chat id exists."
                    });
                }

                // Unreachable falls through instead of refusing. GetChatAsync already
                // retried once, so this is a second failure, not a single slow
                // round-trip — and refusing here would trade a real, working link for
                // a network problem on our own side. Nothing Telegram said is being
                // overridden, because Telegram was never reached at all; the link is
                // recorded on the administrator's word, which is the same trust level
                // this whole path already runs on. A test message gives the missing
                // proof a moment later instead of blocking the link entirely.
            }

            var username = lookup.Username;
            var displayName = lookup.DisplayName;

            var now = _clock.GetUtcNow().UtcDateTime;
            var actingUserId = await ActingUserIdAsync();

            await _telegram.LinkAsync(
                personId.Value, chatId.ToString(),
                username ?? request.Username?.TrimStart('@'),
                now, actingUserId);

            // Worth a warning rather than an information line: this is the one linking
            // path with no proof of ownership, so it should be easy to find later.
            _logger.LogWarning(
                "Telegram linked MANUALLY by admin {AccountId} for person {PersonId} " +
                "to chat {ChatId} ({DisplayName}). Verified={Verified}.",
                _current.AccountId, personId.Value, chatId, displayName ?? "unnamed", verified);

            var status = await GetStatusAsync(request.PersonId);
            string message;
            string? code = null;

            if (verified)
            {
                var who = string.IsNullOrWhiteSpace(displayName) ? "that chat" : displayName;
                message = $"Linked to {who}.";
            }
            else
            {
                // No name to show, because Telegram was never asked. The message says
                // that plainly rather than a "Linked to ..." line implying a
                // confidence the server does not have.
                code = ResponseCodes.TelegramLinkedUnverified;
                message = $"Linked to chat {chatId}, but Telegram could not be reached to confirm " +
                          "it exists. Send a test message now to check — if it does not arrive, " +
                          "the chat id is wrong.";
            }

            // Said plainly rather than buried: everyone on a shared chat id receives
            // each other's alerts, and that is not visible from anywhere else.
            if (sharedWith > 0)
            {
                message += sharedWith == 1
                    ? " 1 other person already uses this chat id — both of them will receive each other's messages."
                    : $" {sharedWith} other people already use this chat id — all of them will receive each other's messages.";
            }

            if (verified) message += " Send a test message to confirm it arrives.";

            return Ok(status.Data, message, code);
        }

        public async Task<ApiResponse<bool>> SendTestMessageAsync(string personPublicId)
        {
            var personId = await _telegram.ResolvePersonIdAsync(personPublicId);

            if (personId is null) return Warn<bool>("That person was not found.");

            var contact = await _telegram.GetContactAsync(personId.Value);

            if (contact is null || contact.OptedOutAt is not null)
                return Warn<bool>("They have no connected Telegram account.");

            // ChatId, not Value: Value is the display form ('@handle' or 'chat:123')
            // and does not parse.
            if (!long.TryParse(contact.ChatId, out var chatId))
                return Warn<bool>("Their stored chat id is not a number, so nothing can be sent.");

            var body = await _templates.RenderAsync(TelegramTemplates.TestMessage, personId.Value);

            var sent = await _client.SendMessageAsync(
                chatId,
                body ?? "This is a test message. If you did not expect it, please tell the church office.");

            if (!sent)
            {
                return Warn<bool>(
                    "Telegram would not deliver the message. The link is stored but not working — " +
                    "disconnect it and have them link it themselves.");
            }

            _logger.LogInformation(
                "Telegram test message sent to person {PersonId} by {AccountId}",
                personId.Value, _current.AccountId);

            // The wording matters: a delivered message proves the chat is reachable,
            // not that it belongs to the right person. Only they can confirm that.
            return Ok(true, "Test message sent. Ask them to confirm they received it.");
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

        private static ApiResponse<T> Ok<T>(T data, string message, string? code = null) =>
            new(ResponseType.Success, message, data, code);
        private static ApiResponse<T> Warn<T>(string message) => new(ResponseType.Warning, message, default!);
    }
}
