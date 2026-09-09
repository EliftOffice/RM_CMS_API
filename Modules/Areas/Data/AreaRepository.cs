using Dapper;
using RM_CMS.Data;
using RM_CMS.Modules.Areas.Domain;

namespace RM_CMS.Modules.Areas.Data
{
    /// <summary>
    /// Data access for <c>area</c>.
    ///
    /// There is no delete. People point at an area and their records outlive it, so
    /// retiring sets <c>is_active = 0</c>: the rows stay readable and the area stops
    /// being offered in the picker.
    /// </summary>
    public interface IAreaRepository
    {
        /// <summary>
        /// The management list, with how many people are filed against each. When
        /// <paramref name="campusId"/> is null every campus is returned — only an
        /// administrator is ever allowed to ask for that.
        /// </summary>
        Task<IReadOnlyList<Area>> ListAsync(long? campusId, bool includeInactive);

        Task<Area?> GetByPublicIdAsync(string publicId);
        Task<Area?> GetByIdAsync(long id);

        /// <summary>
        /// Active areas at one campus whose name contains <paramref name="term"/>.
        /// Backs the type-ahead. An empty term returns the first page of the list,
        /// which is what an operator sees before typing anything.
        /// </summary>
        Task<IReadOnlyList<Area>> SuggestAsync(long campusId, string? term, int limit);

        /// <summary>
        /// The exact match a typed name resolves to, active or not.
        ///
        /// Retired areas are included deliberately: re-typing a retired name must
        /// reuse that row rather than hit the unique index and fail, or silently
        /// mint a duplicate the index would reject anyway.
        /// </summary>
        Task<Area?> FindByNameAsync(long campusId, string normalizedName);

        Task<long> CreateAsync(Area area, long? actingUserId);

        Task<bool> UpdateAsync(long id, int rowVersion, string name, string normalizedName,
                               bool isActive, long? actingUserId);

        /// <summary>Resolves a campus public id to its internal key. Null when unknown.</summary>
        Task<long?> ResolveCampusIdAsync(string? campusPublicId);
    }

    public sealed class AreaRepository : IAreaRepository
    {
        private readonly IDbConnectionFactory _dbFactory;

        public AreaRepository(IDbConnectionFactory dbFactory) => _dbFactory = dbFactory;

        private const string SelectArea = @"
            SELECT
                a.id              AS Id,
                a.public_id       AS PublicId,
                a.campus_id       AS CampusId,
                c.public_id       AS CampusPublicId,
                c.name            AS CampusName,
                a.name            AS Name,
                a.normalized_name AS NormalizedName,
                a.is_active       AS IsActive,
                a.created_at      AS CreatedAt,
                a.row_version     AS RowVersion
            FROM area a
            JOIN campus c ON c.id = a.campus_id";

        /// <summary>
        /// The counts are correlated subqueries rather than maintained columns, the
        /// same choice campus and team make: a stored counter is one more thing to
        /// drift, and this list is read occasionally by one person, not on a hot path.
        ///
        /// Soft-deleted people are excluded, so an area emptied by deletions reads as
        /// unused rather than permanently occupied by records nobody can see.
        /// </summary>
        private const string SelectAreaWithCounts = @"
            SELECT
                a.id              AS Id,
                a.public_id       AS PublicId,
                a.campus_id       AS CampusId,
                c.public_id       AS CampusPublicId,
                c.name            AS CampusName,
                a.name            AS Name,
                a.normalized_name AS NormalizedName,
                a.is_active       AS IsActive,
                a.created_at      AS CreatedAt,
                a.row_version     AS RowVersion,
                (SELECT COUNT(*) FROM person p
                  WHERE p.area_id = a.id AND p.deleted_at IS NULL)          AS PersonCount,
                (SELECT COUNT(*) FROM person p
                  JOIN volunteer v ON v.person_id = p.id AND v.status = 'ACTIVE'
                  WHERE p.area_id = a.id AND p.deleted_at IS NULL)          AS VolunteerCount
            FROM area a
            JOIN campus c ON c.id = a.campus_id";

        // ------------------------------------------------------------------
        // Reads
        // ------------------------------------------------------------------

        public async Task<IReadOnlyList<Area>> ListAsync(long? campusId, bool includeInactive)
        {
            const string sql = SelectAreaWithCounts + @"
            WHERE (@CampusId IS NULL OR a.campus_id = @CampusId)
              AND (@IncludeInactive = 1 OR a.is_active = 1)
            ORDER BY a.is_active DESC, c.name, a.name;";

            using var connection = _dbFactory.GetConnection();

            return (await connection.QueryAsync<Area>(
                sql, new { CampusId = campusId, IncludeInactive = includeInactive })).ToList();
        }

        public async Task<Area?> GetByPublicIdAsync(string publicId)
        {
            const string sql = SelectAreaWithCounts + " WHERE a.public_id = @PublicId LIMIT 1;";

            using var connection = _dbFactory.GetConnection();
            return await connection.QueryFirstOrDefaultAsync<Area>(sql, new { PublicId = publicId });
        }

        public async Task<Area?> GetByIdAsync(long id)
        {
            const string sql = SelectAreaWithCounts + " WHERE a.id = @Id LIMIT 1;";

            using var connection = _dbFactory.GetConnection();
            return await connection.QueryFirstOrDefaultAsync<Area>(sql, new { Id = id });
        }

        public async Task<IReadOnlyList<Area>> SuggestAsync(long campusId, string? term, int limit)
        {
            // Matched on the normalized name so 'kurnool road' finds 'Kurnool Road',
            // and ordered so a prefix hit outranks one buried mid-string: typing
            // 'raj' should put 'Rajiv Nagar' above 'Old Rajiv Colony'.
            const string sql = SelectArea + @"
            WHERE a.campus_id = @CampusId
              AND a.is_active = 1
              AND (@Term = '' OR a.normalized_name LIKE @Contains)
            ORDER BY
                CASE WHEN @Term <> '' AND a.normalized_name LIKE @StartsWith THEN 0 ELSE 1 END,
                a.name
            LIMIT @Limit;";

            var normalized = AreaNames.Normalize(term);

            // LIKE wildcards typed by the operator would otherwise widen the search
            // silently — a lone '%' matching every area is a confusing result, not a
            // dangerous one, but it is still not what they asked for.
            var escaped = normalized
                .Replace("\\", "\\\\")
                .Replace("%", "\\%")
                .Replace("_", "\\_");

            using var connection = _dbFactory.GetConnection();

            return (await connection.QueryAsync<Area>(sql, new
            {
                CampusId = campusId,
                Term = normalized,
                Contains = "%" + escaped + "%",
                StartsWith = escaped + "%",
                Limit = limit
            })).ToList();
        }

        public async Task<Area?> FindByNameAsync(long campusId, string normalizedName)
        {
            const string sql = SelectArea + @"
            WHERE a.campus_id = @CampusId AND a.normalized_name = @NormalizedName
            LIMIT 1;";

            using var connection = _dbFactory.GetConnection();

            return await connection.QueryFirstOrDefaultAsync<Area>(
                sql, new { CampusId = campusId, NormalizedName = normalizedName });
        }

        // ------------------------------------------------------------------
        // Writes
        // ------------------------------------------------------------------

        public async Task<long> CreateAsync(Area area, long? actingUserId)
        {
            const string sql = @"
                INSERT INTO area
                    (public_id, campus_id, name, normalized_name, is_active, created_by, updated_by)
                VALUES
                    (@PublicId, @CampusId, @Name, @NormalizedName, 1, @ActingUserId, @ActingUserId);
                SELECT LAST_INSERT_ID();";

            using var connection = _dbFactory.GetConnection();

            return await connection.ExecuteScalarAsync<long>(sql, new
            {
                area.PublicId,
                area.CampusId,
                area.Name,
                area.NormalizedName,
                ActingUserId = actingUserId
            });
        }

        public async Task<bool> UpdateAsync(
            long id, int rowVersion, string name, string normalizedName, bool isActive, long? actingUserId)
        {
            const string sql = @"
                UPDATE area
                SET name            = @Name,
                    normalized_name = @NormalizedName,
                    is_active       = @IsActive,
                    updated_by      = @ActingUserId,
                    row_version     = row_version + 1
                WHERE id = @Id AND row_version = @RowVersion;";

            using var connection = _dbFactory.GetConnection();

            return await connection.ExecuteAsync(sql, new
            {
                Id = id,
                RowVersion = rowVersion,
                Name = name,
                NormalizedName = normalizedName,
                IsActive = isActive,
                ActingUserId = actingUserId
            }) == 1;
        }

        public async Task<long?> ResolveCampusIdAsync(string? campusPublicId)
        {
            if (string.IsNullOrWhiteSpace(campusPublicId)) return null;

            using var connection = _dbFactory.GetConnection();

            return await connection.ExecuteScalarAsync<long?>(
                "SELECT id FROM campus WHERE public_id = @PublicId LIMIT 1;",
                new { PublicId = campusPublicId });
        }
    }
}
