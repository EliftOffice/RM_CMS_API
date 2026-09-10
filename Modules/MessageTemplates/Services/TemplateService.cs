using Microsoft.Extensions.Options;
using RM_CMS.Modules.Identity.Data;
using RM_CMS.Modules.Identity.Domain;
using RM_CMS.Modules.Identity.Services;
using RM_CMS.Modules.MessageTemplates.Api;
using RM_CMS.Modules.MessageTemplates.Data;
using RM_CMS.Modules.MessageTemplates.Domain;
using RM_CMS.Modules.Notifications.Domain;
using RM_CMS.Utilities;

namespace RM_CMS.Modules.MessageTemplates.Services
{
    /// <summary>
    /// The wording of every Telegram message, and the screen that edits it.
    /// </summary>
    /// <remarks>
    /// Two audiences, as usual, but here they are the same text read two ways: an
    /// administrator EDITING a scenario's wording, and the sending code RENDERING it
    /// for one person. <see cref="RenderAsync"/> is the second, and it is the only
    /// thing on the sending path — everything else on this interface belongs to the
    /// screen.
    /// </remarks>
    public interface ITemplateService
    {
        Task<ApiResponse<IReadOnlyList<TemplateDto>>> ListAsync();
        Task<ApiResponse<TemplateDto>> GetAsync(string code);
        Task<ApiResponse<TemplateDto>> SaveAsync(string code, SaveTemplateRequest request);

        /// <summary>Drops the override so the scenario runs on its built-in wording again.</summary>
        Task<ApiResponse<TemplateDto>> ResetAsync(string code);

        /// <summary>
        /// Renders one scenario for one recipient. Returns null only when the scenario
        /// is not in the catalogue at all, which is a coding gap rather than a
        /// transient fault.
        /// </summary>
        Task<string?> RenderAsync(
            string code,
            long? recipientPersonId,
            IReadOnlyDictionary<string, string?>? values = null,
            IReadOnlyDictionary<string, string?>? rawValues = null);

        /// <summary>
        /// The same, for a recipient whose name the caller already holds — the Telegram
        /// webhook knows the name off the message and has no person id yet.
        /// </summary>
        Task<string?> RenderForNameAsync(
            string code,
            string? recipientName,
            IReadOnlyDictionary<string, string?>? values = null,
            IReadOnlyDictionary<string, string?>? rawValues = null);
    }

    public sealed class TemplateService : ITemplateService
    {
        private readonly ITemplateRepository _templates;
        private readonly IUserAccountRepository _accounts;
        private readonly ICurrentIdentity _current;
        private readonly ILogger<TemplateService> _logger;
        private readonly string _baseUrl;
        private readonly string _churchName;

        public TemplateService(
            ITemplateRepository templates,
            IUserAccountRepository accounts,
            ICurrentIdentity current,
            IOptions<NotificationOptions> options,
            ILogger<TemplateService> logger)
        {
            _templates = templates;
            _accounts = accounts;
            _current = current;
            _logger = logger;
            _baseUrl = options.Value.PublicBaseUrl.TrimEnd('/');

            // Not configurable yet, and named here rather than being typed into a dozen
            // default templates — a rename should be one edit, not twelve.
            _churchName = "Resurrection Ministries";
        }

        // ==================================================================
        // The screen
        // ==================================================================

        public async Task<ApiResponse<IReadOnlyList<TemplateDto>>> ListAsync()
        {
            var stored = (await _templates.ListAsync())
                .ToDictionary(t => t.Code, StringComparer.Ordinal);

            // Driven by the catalogue, not by the table. A scenario added in code shows
            // up here immediately; a row for a code the application no longer sends is
            // simply not listed, rather than offering an editor for a dead message.
            var items = TelegramTemplates.All
                .Select(definition => ToDto(definition, stored.GetValueOrDefault(definition.Code)))
                .ToList();

            var customised = items.Count(i => i.IsCustomised);

            return new ApiResponse<IReadOnlyList<TemplateDto>>(
                ResponseType.Success,
                $"{items.Count} message{(items.Count == 1 ? string.Empty : "s")}, {customised} customised.",
                items);
        }

        public async Task<ApiResponse<TemplateDto>> GetAsync(string code)
        {
            var definition = TelegramTemplates.Find(code);

            if (definition is null) return Warn($"There is no message called '{code}'.");

            return new ApiResponse<TemplateDto>(
                ResponseType.Success, "Message.",
                ToDto(definition, await _templates.GetAsync(definition.Code)));
        }

        public async Task<ApiResponse<TemplateDto>> SaveAsync(string code, SaveTemplateRequest request)
        {
            var definition = TelegramTemplates.Find(code);

            if (definition is null) return Warn($"There is no message called '{code}'.");

            var body = (request.Body ?? string.Empty).Trim();

            if (body.Length == 0) return Warn("The message cannot be empty.");

            // Refused rather than silently blanked at send time. A pastor who typed
            // {{Name}} instead of {{PersonName}} should be told now, not discover it
            // when a volunteer receives a message with a hole in it.
            var known = TelegramTemplates.PlaceholdersFor(definition)
                .Select(p => p.Token.Trim('{', '}').Trim())
                .ToHashSet(StringComparer.Ordinal);

            var unknown = TemplateRenderer.TokensIn(body)
                .Where(token => !known.Contains(token))
                .ToList();

            if (unknown.Count > 0)
            {
                return Warn(
                    $"This message does not have {string.Join(", ", unknown.Select(u => "{{" + u + "}}"))}. " +
                    "Use one of the placeholders listed beside the editor.");
            }

            // Checked against the rendered length, not the template's: a short template
            // full of placeholders can still produce a message Telegram refuses.
            var previewLength = RenderSample(definition, body).Length;

            if (previewLength >= TemplateRenderer.MaxMessageLength)
            {
                return Warn(
                    $"Once the details are filled in this message reaches Telegram's {TemplateRenderer.MaxMessageLength}-character limit. " +
                    "Shorten it.");
            }

            await _templates.SaveAsync(definition.Code, body, await ActingUserIdAsync(), Ulid.NewUlid());

            _logger.LogInformation(
                "Telegram template {Code} edited by {AccountId}", definition.Code, _current.AccountId);

            return new ApiResponse<TemplateDto>(
                ResponseType.Success, "Saved. New messages will use this wording.",
                ToDto(definition, await _templates.GetAsync(definition.Code)));
        }

        public async Task<ApiResponse<TemplateDto>> ResetAsync(string code)
        {
            var definition = TelegramTemplates.Find(code);

            if (definition is null) return Warn($"There is no message called '{code}'.");

            var existing = await _templates.GetAsync(definition.Code);

            if (existing is null)
                return new ApiResponse<TemplateDto>(
                    ResponseType.Success, "This message is already using the standard wording.",
                    ToDto(definition, null));

            await _templates.DeleteAsync(definition.Code);

            _logger.LogInformation(
                "Telegram template {Code} reset to default by {AccountId}", definition.Code, _current.AccountId);

            return new ApiResponse<TemplateDto>(
                ResponseType.Success, "Reset to the standard wording.", ToDto(definition, null));
        }

        // ==================================================================
        // The sending path
        // ==================================================================

        public async Task<string?> RenderAsync(
            string code,
            long? recipientPersonId,
            IReadOnlyDictionary<string, string?>? values = null,
            IReadOnlyDictionary<string, string?>? rawValues = null)
        {
            var recipientName = recipientPersonId is null
                ? null
                : await _templates.GetRecipientNameAsync(recipientPersonId.Value);

            return await RenderForNameAsync(code, recipientName, values, rawValues);
        }

        public async Task<string?> RenderForNameAsync(
            string code,
            string? recipientName,
            IReadOnlyDictionary<string, string?>? values = null,
            IReadOnlyDictionary<string, string?>? rawValues = null)
        {
            var definition = TelegramTemplates.Find(code);

            if (definition is null)
            {
                _logger.LogError("No Telegram template is defined for '{Code}'.", code);
                return null;
            }

            string body;

            try
            {
                body = (await _templates.GetAsync(definition.Code))?.Body ?? definition.DefaultBody;
            }
            catch (Exception ex)
            {
                // The database being unreachable must not stop an escalation alert.
                // Falling back to the wording compiled into the application sends the
                // right message with the wrong customisation, which beats silence.
                _logger.LogError(ex, "Could not read the template for {Code}; using the default.", definition.Code);
                body = definition.DefaultBody;
            }

            var merged = new Dictionary<string, string?>(StringComparer.Ordinal);

            foreach (var pair in Common(recipientName)) merged[pair.Key] = pair.Value;

            if (values is not null)
                foreach (var pair in values) merged[pair.Key] = pair.Value;

            return TemplateRenderer.Render(body, merged, rawValues);
        }

        // ==================================================================
        // Helpers
        // ==================================================================

        /// <summary>
        /// The recipient placeholders, available to every scenario. This is what makes
        /// a message specific to the person receiving it rather than a broadcast.
        /// </summary>
        private Dictionary<string, string?> Common(string? recipientName)
        {
            var full = string.IsNullOrWhiteSpace(recipientName) ? null : recipientName.Trim();

            return new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["RecipientName"] = full,
                ["RecipientFirstName"] = FirstNameOf(full),
                ["ChurchName"] = _churchName,
                ["SiteUrl"] = _baseUrl
            };
        }

        /// <summary>
        /// The first word of a name. Good enough for a greeting, and the alternative —
        /// asking the database for given_name on every send — buys a query per message
        /// to be right about a case that reads fine either way.
        /// </summary>
        private static string? FirstNameOf(string? fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName)) return null;

            var space = fullName.IndexOf(' ');

            return space <= 0 ? fullName : fullName[..space];
        }

        /// <summary>
        /// The body filled in with the sample values from the catalogue, which is what
        /// the screen previews and what the length check measures against.
        /// </summary>
        private string RenderSample(TelegramTemplateDefinition definition, string body)
        {
            var values = new Dictionary<string, string?>(StringComparer.Ordinal);

            foreach (var placeholder in TelegramTemplates.PlaceholdersFor(definition))
                values[placeholder.Token.Trim('{', '}').Trim()] = placeholder.Sample;

            // The two live ones are shown as they really are, so the preview does not
            // promise a link that differs from the one that actually goes out.
            values["ChurchName"] = _churchName;
            values["SiteUrl"] = _baseUrl;

            return TemplateRenderer.Render(body, values);
        }

        private TemplateDto ToDto(TelegramTemplateDefinition definition, StoredTemplate? stored)
        {
            var body = stored?.Body ?? definition.DefaultBody;

            return new TemplateDto
            {
                Code = definition.Code,
                Label = definition.Label,
                Description = definition.Description,
                Group = definition.Group,
                Body = body,
                DefaultBody = definition.DefaultBody,
                IsCustomised = stored is not null,
                Placeholders = TelegramTemplates.PlaceholdersFor(definition)
                    .Select(p => new PlaceholderDto
                    {
                        Token = p.Token,
                        Description = p.Description,
                        Sample = p.Sample
                    })
                    .ToList(),
                Preview = RenderSample(definition, body),
                UpdatedAt = stored?.UpdatedAt,
                UpdatedByName = stored?.UpdatedByName
            };
        }

        private async Task<long?> ActingUserIdAsync()
        {
            if (string.IsNullOrWhiteSpace(_current.AccountId)) return null;

            return (await _accounts.GetByPublicIdAsync(_current.AccountId))?.Id;
        }

        private static ApiResponse<TemplateDto> Warn(string message) =>
            new(ResponseType.Warning, message, default!);
    }
}
