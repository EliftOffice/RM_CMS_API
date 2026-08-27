using Dapper;
using RM_CMS.Data;

namespace RM_CMS.Modules.Settings.Data
{
    /// <summary>
    /// Data access for <c>app_setting</c> — the business rules an administrator may
    /// change without a deployment.
    ///
    /// Note what is deliberately NOT here: bot tokens, signing keys and connection
    /// strings. The MVP kept the live Telegram bot token in this table in plaintext,
    /// reachable through a generic settings API. Third-party secrets are environment
    /// variables now, and <c>is_secret</c> exists so anything that slips in is at least
    /// never returned to a browser.
    /// </summary>
    public interface ISettingRepository
    {
        Task<IReadOnlyList<SettingRow>> ListAsync();
        Task<SettingRow?> GetAsync(string key);

        /// <summary>
        /// Writes a value, guarding on <c>row_version</c>. Returns false on a version
        /// mismatch — two administrators editing the same rule must not silently
        /// overwrite each other.
        /// </summary>
        Task<bool> UpdateAsync(string key, string value, int rowVersion, long? actingUserId);

        /// <summary>
        /// Reads a BOOLEAN setting. Values are stored as the strings 'true'/'false',
        /// so an integer reader would silently fall back to its default on every
        /// boolean — a switch that appears to work and never turns on.
        /// </summary>
        Task<bool> GetBoolAsync(string key, bool fallback);

        Task<int> GetIntAsync(string key, int fallback);
    }

    public sealed class SettingRow
    {
        public string SettingKey { get; set; } = string.Empty;
        public string SettingValue { get; set; } = string.Empty;
        public string ValueType { get; set; } = "STRING";
        public string Category { get; set; } = "GENERAL";
        public string? Description { get; set; }
        public int? MinValue { get; set; }
        public int? MaxValue { get; set; }
        public bool IsSecret { get; set; }
        public bool IsEditable { get; set; }
        public DateTime UpdatedAt { get; set; }
        public int RowVersion { get; set; }
    }

    public sealed class SettingRepository : ISettingRepository
    {
        private readonly IDbConnectionFactory _dbFactory;

        public SettingRepository(IDbConnectionFactory dbFactory) => _dbFactory = dbFactory;

        private const string SelectSetting = @"
            SELECT
                setting_key   AS SettingKey,
                setting_value AS SettingValue,
                value_type    AS ValueType,
                category      AS Category,
                description   AS Description,
                -- Backticked: MAXVALUE is a reserved word in MySQL (partitioning), so
                -- an unquoted alias of that name is a syntax error. MinValue is quoted
                -- alongside it purely for symmetry.
                min_value     AS `MinValue`,
                max_value     AS `MaxValue`,
                is_secret     AS IsSecret,
                is_editable   AS IsEditable,
                updated_at    AS UpdatedAt,
                row_version   AS RowVersion
            FROM app_setting";

        public async Task<IReadOnlyList<SettingRow>> ListAsync()
        {
            var sql = SelectSetting + " ORDER BY category, setting_key;";

            using var connection = _dbFactory.GetConnection();
            return (await connection.QueryAsync<SettingRow>(sql)).ToList();
        }

        public async Task<SettingRow?> GetAsync(string key)
        {
            var sql = SelectSetting + " WHERE setting_key = @Key LIMIT 1;";

            using var connection = _dbFactory.GetConnection();
            return await connection.QueryFirstOrDefaultAsync<SettingRow>(sql, new { Key = key });
        }

        private async Task<string?> RawValueAsync(string key)
        {
            using var connection = _dbFactory.GetConnection();

            return await connection.ExecuteScalarAsync<string?>(
                "SELECT setting_value FROM app_setting WHERE setting_key = @Key LIMIT 1;",
                new { Key = key });
        }

        public async Task<bool> GetBoolAsync(string key, bool fallback)
        {
            var raw = (await RawValueAsync(key))?.Trim();

            if (string.IsNullOrEmpty(raw)) return fallback;

            // Accepts the stored form plus the shapes an administrator might type.
            return raw.Equals("true", StringComparison.OrdinalIgnoreCase)
                || raw.Equals("1", StringComparison.Ordinal)
                || raw.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<int> GetIntAsync(string key, int fallback) =>
            int.TryParse(await RawValueAsync(key), out var value) ? value : fallback;

        public async Task<bool> UpdateAsync(string key, string value, int rowVersion, long? actingUserId)
        {
            const string sql = @"
                UPDATE app_setting
                SET setting_value = @Value,
                    updated_by    = @ActingUserId,
                    row_version   = row_version + 1
                WHERE setting_key = @Key
                  AND row_version = @RowVersion
                  AND is_editable = 1;";

            using var connection = _dbFactory.GetConnection();

            return await connection.ExecuteAsync(sql, new
            {
                Key = key,
                Value = value,
                RowVersion = rowVersion,
                ActingUserId = actingUserId
            }) == 1;
        }
    }
}
