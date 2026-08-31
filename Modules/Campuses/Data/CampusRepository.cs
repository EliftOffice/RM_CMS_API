using Dapper;
using RM_CMS.Data;
using RM_CMS.Modules.Campuses.Domain;

namespace RM_CMS.Modules.Campuses.Data
{
    /// <summary>
    /// Data access for <c>campus</c>.
    ///
    /// There is no delete. A campus is referenced by people, cases and escalations
    /// that must survive it — a closed site still has a pastoral history somebody may
    /// need to read. Retiring sets <c>is_active = 0</c> and leaves the rows intact.
    /// </summary>
    public interface ICampusRepository
    {
        Task<IReadOnlyList<Campus>> ListAsync(bool includeInactive);
        Task<Campus?> GetByPublicIdAsync(string publicId);
        Task<Campus?> GetByIdAsync(long id);

        Task<long> CreateAsync(Campus campus, long? actingUserId);
        Task<bool> UpdateAsync(long id, int rowVersion, string name, string timezone,
                               bool isActive, long? actingUserId);

        Task<bool> CodeExistsAsync(string code, long? excludingId = null);
    }

    public sealed class CampusRepository : ICampusRepository
    {
        private readonly IDbConnectionFactory _dbFactory;

        public CampusRepository(IDbConnectionFactory dbFactory) => _dbFactory = dbFactory;

        /// <summary>
        /// The counts are correlated subqueries rather than maintained columns, for the
        /// same reason team membership is: a stored counter is one more thing to drift,
        /// and this list is read by one administrator occasionally, not on a hot path.
        ///
        /// Open cases only — a campus whose cases are all closed can be retired, and
        /// counting the closed ones would block that forever.
        /// </summary>
        private const string SelectCampus = @"
            SELECT
                c.id         AS Id,
                c.public_id  AS PublicId,
                c.code       AS Code,
                c.name       AS Name,
                c.timezone   AS Timezone,
                c.is_active  AS IsActive,
                c.created_at AS CreatedAt,
                c.row_version AS RowVersion,
                (SELECT COUNT(*) FROM person p
                  WHERE p.campus_id = c.id AND p.deleted_at IS NULL)        AS PersonCount,
                (SELECT COUNT(*) FROM volunteer v
                  WHERE v.campus_id = c.id AND v.status = 'ACTIVE')         AS VolunteerCount,
                (SELECT COUNT(*) FROM team t
                  WHERE t.campus_id = c.id AND t.is_active = 1)             AS TeamCount,
                (SELECT COUNT(*) FROM care_case cc
                  WHERE cc.campus_id = c.id AND cc.status <> 'CLOSED')      AS OpenCaseCount
            FROM campus c";

        public async Task<IReadOnlyList<Campus>> ListAsync(bool includeInactive)
        {
            const string sql = SelectCampus + @"
            WHERE (@IncludeInactive = 1 OR c.is_active = 1)
            ORDER BY c.is_active DESC, c.name;";

            using var connection = _dbFactory.GetConnection();

            return (await connection.QueryAsync<Campus>(
                sql, new { IncludeInactive = includeInactive })).ToList();
        }

        public async Task<Campus?> GetByPublicIdAsync(string publicId)
        {
            const string sql = SelectCampus + " WHERE c.public_id = @PublicId LIMIT 1;";

            using var connection = _dbFactory.GetConnection();
            return await connection.QueryFirstOrDefaultAsync<Campus>(sql, new { PublicId = publicId });
        }

        public async Task<Campus?> GetByIdAsync(long id)
        {
            const string sql = SelectCampus + " WHERE c.id = @Id LIMIT 1;";

            using var connection = _dbFactory.GetConnection();
            return await connection.QueryFirstOrDefaultAsync<Campus>(sql, new { Id = id });
        }

        public async Task<long> CreateAsync(Campus campus, long? actingUserId)
        {
            const string sql = @"
                INSERT INTO campus (public_id, code, name, timezone, is_active, created_by, updated_by)
                VALUES (@PublicId, @Code, @Name, @Timezone, 1, @ActingUserId, @ActingUserId);
                SELECT LAST_INSERT_ID();";

            using var connection = _dbFactory.GetConnection();

            return await connection.ExecuteScalarAsync<long>(sql, new
            {
                campus.PublicId,
                campus.Code,
                campus.Name,
                campus.Timezone,
                ActingUserId = actingUserId
            });
        }

        public async Task<bool> UpdateAsync(
            long id, int rowVersion, string name, string timezone, bool isActive, long? actingUserId)
        {
            const string sql = @"
                UPDATE campus
                SET name        = @Name,
                    timezone    = @Timezone,
                    is_active   = @IsActive,
                    updated_by  = @ActingUserId,
                    row_version = row_version + 1
                WHERE id = @Id AND row_version = @RowVersion;";

            using var connection = _dbFactory.GetConnection();

            return await connection.ExecuteAsync(sql, new
            {
                Id = id,
                RowVersion = rowVersion,
                Name = name,
                Timezone = timezone,
                IsActive = isActive,
                ActingUserId = actingUserId
            }) == 1;
        }

        public async Task<bool> CodeExistsAsync(string code, long? excludingId = null)
        {
            // Matches ux_campus_code, so the caller gets a sentence rather than a
            // duplicate-key exception.
            const string sql = @"
                SELECT COUNT(1) FROM campus
                WHERE code = @Code AND (@Excluding IS NULL OR id <> @Excluding);";

            using var connection = _dbFactory.GetConnection();

            return await connection.ExecuteScalarAsync<int>(
                sql, new { Code = code, Excluding = excludingId }) > 0;
        }
    }
}
