using Dapper;
using RM_CMS.Data;
using RM_CMS.Modules.MessageTemplates.Domain;

namespace RM_CMS.Modules.MessageTemplates.Data
{
    /// <summary>
    /// A stored override for one scenario's wording.
    /// </summary>
    public sealed class StoredTemplate
    {
        public long Id { get; set; }
        public string PublicId { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public DateTime UpdatedAt { get; set; }
        public string? UpdatedByName { get; set; }
        public int RowVersion { get; set; }
    }

    /// <summary>
    /// Data access for <c>telegram_template</c>.
    /// </summary>
    /// <remarks>
    /// Only edited templates have rows. A scenario nobody has touched is absent, and
    /// the service falls back to the default in <see cref="TelegramTemplates"/> — so
    /// "reset to default" here really is <see cref="DeleteAsync"/>, not a second copy
    /// of the wording to keep in step with the code.
    /// </remarks>
    public interface ITemplateRepository
    {
        Task<IReadOnlyList<StoredTemplate>> ListAsync();
        Task<StoredTemplate?> GetAsync(string code);

        /// <summary>
        /// Writes the wording for a scenario, inserting or replacing as needed.
        /// </summary>
        /// <remarks>
        /// An upsert rather than a read-then-branch: the unique index on `code` is what
        /// guarantees one wording per scenario, and two administrators saving the same
        /// template at once would otherwise race between the check and the insert.
        /// </remarks>
        Task SaveAsync(string code, string body, long? actingUserId, string newPublicId);

        /// <summary>Removes the override, so the scenario goes back to its default.</summary>
        Task<bool> DeleteAsync(string code);

        /// <summary>
        /// The name and campus of a person an alert is addressed to, for the
        /// recipient placeholders. Null when the person is gone.
        /// </summary>
        Task<string?> GetRecipientNameAsync(long personId);
    }

    public sealed class TemplateRepository : ITemplateRepository
    {
        private readonly IDbConnectionFactory _dbFactory;

        public TemplateRepository(IDbConnectionFactory dbFactory) => _dbFactory = dbFactory;

        private const string SelectTemplate = @"
            SELECT
                t.public_id   AS PublicId,
                t.id          AS Id,
                t.code        AS Code,
                t.body        AS Body,
                t.updated_at  AS UpdatedAt,
                p.full_name   AS UpdatedByName,
                t.row_version AS RowVersion
            FROM telegram_template t
            LEFT JOIN user_account ua ON ua.id = t.updated_by
            LEFT JOIN person p        ON p.id = ua.person_id";

        public async Task<IReadOnlyList<StoredTemplate>> ListAsync()
        {
            const string sql = SelectTemplate + " ORDER BY t.code;";

            using var connection = _dbFactory.GetConnection();

            return (await connection.QueryAsync<StoredTemplate>(sql)).ToList();
        }

        public async Task<StoredTemplate?> GetAsync(string code)
        {
            const string sql = SelectTemplate + " WHERE t.code = @Code LIMIT 1;";

            using var connection = _dbFactory.GetConnection();

            return await connection.QueryFirstOrDefaultAsync<StoredTemplate>(sql, new { Code = code });
        }

        public async Task SaveAsync(string code, string body, long? actingUserId, string newPublicId)
        {
            // public_id is only consumed on an insert; VALUES(public_id) on the update
            // branch would replace a stable identifier every time somebody fixed a typo.
            const string sql = @"
                INSERT INTO telegram_template (public_id, code, body, updated_by)
                VALUES (@PublicId, @Code, @Body, @ActingUserId)
                ON DUPLICATE KEY UPDATE
                    body        = VALUES(body),
                    updated_by  = VALUES(updated_by),
                    row_version = row_version + 1;";

            using var connection = _dbFactory.GetConnection();

            await connection.ExecuteAsync(sql, new
            {
                PublicId = newPublicId,
                Code = code,
                Body = body,
                ActingUserId = actingUserId
            });
        }

        public async Task<bool> DeleteAsync(string code)
        {
            const string sql = "DELETE FROM telegram_template WHERE code = @Code;";

            using var connection = _dbFactory.GetConnection();

            return await connection.ExecuteAsync(sql, new { Code = code }) == 1;
        }

        public async Task<string?> GetRecipientNameAsync(long personId)
        {
            const string sql = @"
                SELECT full_name FROM person
                WHERE id = @PersonId AND deleted_at IS NULL
                LIMIT 1;";

            using var connection = _dbFactory.GetConnection();

            return await connection.ExecuteScalarAsync<string?>(sql, new { PersonId = personId });
        }
    }
}
