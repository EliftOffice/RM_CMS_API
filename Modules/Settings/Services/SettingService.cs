using System.Globalization;
using RM_CMS.Modules.Identity.Data;
using RM_CMS.Modules.Identity.Services;
using RM_CMS.Modules.Settings.Api;
using RM_CMS.Modules.Settings.Data;
using RM_CMS.Utilities;

namespace RM_CMS.Modules.Settings.Services
{
    /// <summary>
    /// Reads and writes the tunable business rules.
    ///
    /// This service is where <c>min_value</c> / <c>max_value</c> actually bite. The
    /// database records the bounds but cannot enforce them per-row against a VARCHAR
    /// column, so an unchecked write here would let an administrator set
    /// <c>escalation.pastor_alert_hours</c> to 0 and turn the chase-up ladder into a
    /// pager, or set it to 10000 and silence it entirely.
    ///
    /// Every write is validated against the row's own declared type and bounds — never
    /// against a list kept in this file, which would drift from the seed data.
    /// </summary>
    public interface ISettingService
    {
        Task<ApiResponse<IReadOnlyList<SettingGroupDto>>> ListAsync();
        Task<ApiResponse<SettingUpdateResultDto>> UpdateAsync(UpdateSettingsRequest request);
    }

    public sealed class SettingService : ISettingService
    {
        private readonly ISettingRepository _settings;
        private readonly IUserAccountRepository _accounts;
        private readonly ICurrentIdentity _current;
        private readonly ILogger<SettingService> _logger;

        public SettingService(
            ISettingRepository settings,
            IUserAccountRepository accounts,
            ICurrentIdentity current,
            ILogger<SettingService> logger)
        {
            _settings = settings;
            _accounts = accounts;
            _current = current;
            _logger = logger;
        }

        public async Task<ApiResponse<IReadOnlyList<SettingGroupDto>>> ListAsync()
        {
            var rows = await _settings.ListAsync();

            var groups = rows
                .GroupBy(r => r.Category)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => new SettingGroupDto
                {
                    Category = g.Key,
                    Label = Humanise(g.Key),
                    Settings = g.Select(ToDto).ToList()
                })
                .ToList();

            return Ok<IReadOnlyList<SettingGroupDto>>(groups, $"{rows.Count} setting(s).");
        }

        public async Task<ApiResponse<SettingUpdateResultDto>> UpdateAsync(UpdateSettingsRequest request)
        {
            var result = new SettingUpdateResultDto();

            if (request.Settings.Count == 0)
                return Warn<SettingUpdateResultDto>("Nothing to save.");

            var actingUserId = await ResolveActingUserIdAsync();

            // Validate everything BEFORE writing anything. A half-applied batch would
            // leave the rules in a state nobody chose — worse than rejecting the lot.
            var validated = new List<(SettingRow Row, string Value)>();

            foreach (var change in request.Settings)
            {
                var row = await _settings.GetAsync(change.Key);

                if (row is null)
                {
                    result.Rejected.Add(new SettingRejectionDto
                    {
                        Key = change.Key,
                        Reason = "No such setting."
                    });
                    continue;
                }

                if (!row.IsEditable)
                {
                    result.Rejected.Add(new SettingRejectionDto
                    {
                        Key = change.Key,
                        Reason = "This setting is not editable."
                    });
                    continue;
                }

                var problem = Validate(row, change.Value);

                if (problem is not null)
                {
                    result.Rejected.Add(new SettingRejectionDto { Key = change.Key, Reason = problem });
                    continue;
                }

                validated.Add((row, change.Value.Trim()));
            }

            if (result.Rejected.Count > 0)
            {
                return new ApiResponse<SettingUpdateResultDto>(
                    ResponseType.Warning,
                    $"Nothing was saved. {result.Rejected.Count} value(s) are not valid.",
                    result);
            }

            foreach (var (row, value) in validated)
            {
                // Unchanged values are skipped so a "save all" does not bump every
                // row_version and invalidate everyone else's open form.
                if (string.Equals(row.SettingValue, value, StringComparison.Ordinal))
                {
                    result.Unchanged.Add(row.SettingKey);
                    continue;
                }

                var saved = await _settings.UpdateAsync(row.SettingKey, value, row.RowVersion, actingUserId);

                if (saved)
                {
                    result.Updated.Add(row.SettingKey);

                    _logger.LogInformation(
                        "Setting {Key} changed from {Old} to {New}", row.SettingKey, row.SettingValue, value);
                }
                else
                {
                    // Zero rows affected is a conflict, never a no-op.
                    result.Rejected.Add(new SettingRejectionDto
                    {
                        Key = row.SettingKey,
                        Reason = "Somebody else changed this setting first. Reload and try again."
                    });
                }
            }

            var message = result.Rejected.Count > 0
                ? $"{result.Updated.Count} saved, {result.Rejected.Count} could not be."
                : $"{result.Updated.Count} setting(s) saved.";

            return result.Rejected.Count > 0
                ? new ApiResponse<SettingUpdateResultDto>(ResponseType.Warning, message, result)
                : Ok(result, message);
        }

        /// <summary>
        /// Checks a candidate against the row's own declared type and bounds. Returns
        /// null when acceptable, otherwise the reason to show the administrator.
        /// </summary>
        private static string? Validate(SettingRow row, string? candidate)
        {
            var value = (candidate ?? string.Empty).Trim();

            if (value.Length == 0) return "A value is required.";
            if (value.Length > 500) return "That value is too long.";

            switch (row.ValueType)
            {
                case "INTEGER":
                {
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
                        return "Must be a whole number.";

                    if (row.MinValue.HasValue && number < row.MinValue.Value)
                        return $"Must be at least {row.MinValue.Value}.";

                    if (row.MaxValue.HasValue && number > row.MaxValue.Value)
                        return $"Must be at most {row.MaxValue.Value}.";

                    return null;
                }

                case "DECIMAL":
                {
                    if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
                        return "Must be a number.";

                    if (row.MinValue.HasValue && number < row.MinValue.Value)
                        return $"Must be at least {row.MinValue.Value}.";

                    if (row.MaxValue.HasValue && number > row.MaxValue.Value)
                        return $"Must be at most {row.MaxValue.Value}.";

                    return null;
                }

                case "BOOLEAN":
                    return value is "true" or "false"
                        ? null
                        : "Must be true or false.";

                case "JSON":
                {
                    try
                    {
                        System.Text.Json.JsonDocument.Parse(value);
                        return null;
                    }
                    catch (System.Text.Json.JsonException)
                    {
                        return "Must be valid JSON.";
                    }
                }

                default:
                    return null;   // STRING
            }
        }

        private static SettingDto ToDto(SettingRow row) => new()
        {
            Key = row.SettingKey,

            // A secret must never reach a browser. Nothing in this table should be one
            // — see the repository note — but if something is flagged, honour it.
            Value = row.IsSecret ? "********" : row.SettingValue,

            ValueType = row.ValueType,
            Category = row.Category,
            Label = Humanise(row.SettingKey.Contains('.') ? row.SettingKey[(row.SettingKey.IndexOf('.') + 1)..] : row.SettingKey),
            Description = row.Description,
            MinValue = row.MinValue,
            MaxValue = row.MaxValue,
            IsSecret = row.IsSecret,
            IsEditable = row.IsEditable && !row.IsSecret,
            UpdatedAt = row.UpdatedAt,
            RowVersion = row.RowVersion
        };

        /// <summary>Turns ASSIGNMENT / max_retry_attempts into readable text.</summary>
        private static string Humanise(string raw)
        {
            var spaced = raw.Replace('_', ' ').Replace('.', ' ').Trim().ToLowerInvariant();

            return spaced.Length == 0
                ? raw
                : char.ToUpperInvariant(spaced[0]) + spaced[1..];
        }

        private async Task<long?> ResolveActingUserIdAsync()
        {
            var publicId = _current.AccountId;

            if (string.IsNullOrWhiteSpace(publicId)) return null;

            return (await _accounts.GetByPublicIdAsync(publicId))?.Id;
        }

        private static ApiResponse<T> Ok<T>(T data, string message) => new(ResponseType.Success, message, data);
        private static ApiResponse<T> Warn<T>(string message) => new(ResponseType.Warning, message, default!);
    }
}
