using Dapper;
using RM_CMS.Data;

namespace RM_CMS.Modules.People.Data
{
    /// <summary>
    /// The vocabulary for "who is this person to the base visitor?" — Wife, Son,
    /// Brother and the rest.
    ///
    /// A table rather than an enum in code, for the same reason <c>care_outcome</c>
    /// and <c>capacity_band</c> are tables: a church that needs "Grandmother" or
    /// "Guardian" adds a row and the intake screen offers it, with no deployment.
    /// The column is foreign-keyed to it, so a code this repository does not know
    /// is one the database will refuse anyway — validating here simply turns that
    /// into a sentence the operator can read.
    /// </summary>
    public interface IRelationshipTypeRepository
    {
        /// <summary>Everything still on offer, in the order the picker shows it.</summary>
        Task<IReadOnlyList<RelationshipTypeRow>> ListActiveAsync();

        /// <summary>
        /// Whether a code may be recorded. Checks <c>is_active</c> as well as
        /// existence — a retired relationship stays readable on everybody already
        /// filed under it, but must not be chosen again.
        /// </summary>
        Task<bool> IsSelectableAsync(string code);
    }

    public sealed class RelationshipTypeRow
    {
        public string Code { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public int SortOrder { get; set; }
    }

    public sealed class RelationshipTypeRepository : IRelationshipTypeRepository
    {
        private readonly IDbConnectionFactory _dbFactory;

        public RelationshipTypeRepository(IDbConnectionFactory dbFactory) => _dbFactory = dbFactory;

        public async Task<IReadOnlyList<RelationshipTypeRow>> ListActiveAsync()
        {
            const string sql = @"
                SELECT code AS Code, label AS Label, sort_order AS SortOrder
                FROM relationship_type
                WHERE is_active = 1
                ORDER BY sort_order, label;";

            using var connection = _dbFactory.GetConnection();
            return (await connection.QueryAsync<RelationshipTypeRow>(sql)).ToList();
        }

        public async Task<bool> IsSelectableAsync(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return false;

            const string sql = @"
                SELECT COUNT(*) FROM relationship_type
                WHERE code = @Code AND is_active = 1;";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteScalarAsync<int>(sql, new { Code = code.Trim().ToUpperInvariant() }) > 0;
        }
    }
}
