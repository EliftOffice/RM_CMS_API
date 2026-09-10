using Dapper;
using RM_CMS.Data;
using RM_CMS.Modules.Events.Domain;

namespace RM_CMS.Modules.Events.Data
{
    /// <summary>
    /// Data access for <c>church_event</c>.
    ///
    /// There is no delete. An event that was advertised and then called off becomes
    /// CANCELLED — somebody who saw the poster will come looking for it, and a page
    /// saying "cancelled" serves them better than a 404.
    /// </summary>
    public interface IEventRepository
    {
        Task<IReadOnlyList<ChurchEvent>> SearchAsync(EventQuery query, DateTime nowUtc);
        Task<int> CountAsync(EventQuery query, DateTime nowUtc);
        Task<EventSummary> GetSummaryAsync(DateTime nowUtc);

        Task<ChurchEvent?> GetByPublicIdAsync(string publicId);
        Task<ChurchEvent?> GetByIdAsync(long id);
        Task<ChurchEvent?> GetBySlugAsync(string slug, bool publishedOnly);

        /// <summary>
        /// The IANA zone the given campus keeps, or the organisation default when no
        /// campus was named. This is what a typed local time actually means.
        /// </summary>
        Task<string> GetTimezoneAsync(string? campusPublicId);

        /// <summary>Published events only, ordered by start. What the public website reads.</summary>
        Task<IReadOnlyList<ChurchEvent>> ListPublishedAsync();

        Task<long> CreateAsync(ChurchEvent churchEvent, long? actingUserId);
        Task<bool> UpdateAsync(ChurchEvent churchEvent, long? actingUserId);
        Task<bool> SetStatusAsync(long id, int rowVersion, string status, DateTime? publishedAt, long? actingUserId);

        Task<bool> SlugExistsAsync(string slug, long? excludingId = null);
        Task<long?> ResolveCampusIdAsync(string? campusPublicId);

        /// <summary>
        /// The poster's bytes, with the content type the server decided on when it
        /// accepted them. Null when the event has no poster.
        ///
        /// The only read in this repository that touches <c>church_event_poster</c> —
        /// everything else works from the metadata on the event row, so the admin list
        /// never pulls an image it is not going to show.
        /// </summary>
        Task<EventPoster?> GetPosterAsync(string publicId);

        /// <summary>
        /// Stores a poster, replacing whatever was there.
        ///
        /// The bytes and the metadata that describes them go in one transaction. A row
        /// claiming a poster it does not have would serve a 404 to a page that had
        /// already decided to show a picture, and the reverse would be megabytes
        /// nothing could reach.
        /// </summary>
        Task<bool> SavePosterAsync(
            long id, byte[] bytes, string contentType, string? fileName,
            DateTime nowUtc, long? actingUserId);

        Task<bool> RemovePosterAsync(long id, long? actingUserId);
    }

    public sealed class EventRepository : IEventRepository
    {
        private readonly IDbConnectionFactory _dbFactory;

        public EventRepository(IDbConnectionFactory dbFactory) => _dbFactory = dbFactory;

        /// <summary>
        /// The campus timezone falls back to the organisation default rather than being
        /// left null: an event with no campus still has to be rendered at a real local
        /// time, and 'unknown zone' would leave the website formatting it as UTC.
        /// </summary>
        private const string SelectEvent = @"
            SELECT
                e.id            AS Id,
                e.public_id     AS PublicId,
                e.slug          AS Slug,
                e.campus_id     AS CampusId,
                cam.public_id   AS CampusPublicId,
                cam.name        AS CampusName,
                COALESCE(cam.timezone, 'Asia/Kolkata') AS CampusTimezone,
                e.title         AS Title,
                e.summary       AS Summary,
                e.description   AS Description,
                e.venue         AS Venue,
                e.starts_at     AS StartsAt,
                e.ends_at       AS EndsAt,
                e.poster_content_type AS PosterContentType,
                e.poster_file_name    AS PosterFileName,
                e.poster_byte_size    AS PosterByteSize,
                e.poster_updated_at   AS PosterUpdatedAt,
                e.status        AS Status,
                e.published_at  AS PublishedAt,
                e.created_at    AS CreatedAt,
                cb.full_name    AS CreatedByName,
                e.updated_at    AS UpdatedAt,
                e.row_version   AS RowVersion
            FROM church_event e
            LEFT JOIN campus cam       ON cam.id = e.campus_id
            LEFT JOIN user_account cua ON cua.id = e.created_by
            LEFT JOIN person cb        ON cb.id = cua.person_id";

        private static string FilterClause(EventQuery q)
        {
            var sql = string.Empty;

            if (!string.IsNullOrWhiteSpace(q.Status)) sql += " AND e.status = @Status ";

            // Past is measured on the END, so a day-long event stays "upcoming" while it
            // is still running.
            if (!q.IncludePast) sql += " AND COALESCE(e.ends_at, e.starts_at) >= @Now ";

            if (!string.IsNullOrWhiteSpace(q.Search))
            {
                sql += @" AND (e.title LIKE @Search
                            OR e.summary LIKE @Search
                            OR e.venue LIKE @Search
                            OR e.slug LIKE @Search) ";
            }

            return sql;
        }

        public async Task<IReadOnlyList<ChurchEvent>> SearchAsync(EventQuery query, DateTime nowUtc)
        {
            var sql = SelectEvent + $@"
                WHERE 1 = 1
                {FilterClause(query)}
                -- Soonest first among what is still to come, then the most recent past
                -- event. An editor opens this screen to work on what is next.
                ORDER BY (COALESCE(e.ends_at, e.starts_at) < @Now),
                         CASE WHEN COALESCE(e.ends_at, e.starts_at) >= @Now
                              THEN e.starts_at END ASC,
                         e.starts_at DESC
                LIMIT @Take OFFSET @Skip;";

            using var connection = _dbFactory.GetConnection();

            return (await connection.QueryAsync<ChurchEvent>(sql, new
            {
                query.Status,
                Search = $"%{query.Search}%",
                query.Take,
                query.Skip,
                Now = nowUtc
            })).ToList();
        }

        public async Task<int> CountAsync(EventQuery query, DateTime nowUtc)
        {
            var sql = $@"
                SELECT COUNT(*) FROM church_event e
                WHERE 1 = 1
                {FilterClause(query)};";

            using var connection = _dbFactory.GetConnection();

            return await connection.ExecuteScalarAsync<int>(sql, new
            {
                query.Status,
                Search = $"%{query.Search}%",
                Now = nowUtc
            });
        }

        public async Task<EventSummary> GetSummaryAsync(DateTime nowUtc)
        {
            const string sql = @"
                SELECT
                    SUM(status = 'DRAFT')     AS Draft,
                    SUM(status = 'PUBLISHED') AS Published,
                    SUM(status = 'CANCELLED') AS Cancelled,
                    SUM(status = 'PUBLISHED'
                        AND COALESCE(ends_at, starts_at) >= @Now) AS Upcoming,
                    COUNT(*)                  AS Total
                FROM church_event;";

            using var connection = _dbFactory.GetConnection();

            return await connection.QueryFirstOrDefaultAsync<EventSummary>(sql, new { Now = nowUtc })
                   ?? new EventSummary();
        }

        public async Task<ChurchEvent?> GetByPublicIdAsync(string publicId)
        {
            var sql = SelectEvent + " WHERE e.public_id = @PublicId LIMIT 1;";

            using var connection = _dbFactory.GetConnection();
            return await connection.QueryFirstOrDefaultAsync<ChurchEvent>(sql, new { PublicId = publicId });
        }

        public async Task<ChurchEvent?> GetByIdAsync(long id)
        {
            var sql = SelectEvent + " WHERE e.id = @Id LIMIT 1;";

            using var connection = _dbFactory.GetConnection();
            return await connection.QueryFirstOrDefaultAsync<ChurchEvent>(sql, new { Id = id });
        }

        public async Task<string> GetTimezoneAsync(string? campusPublicId)
        {
            const string fallback = "Asia/Kolkata";

            if (string.IsNullOrWhiteSpace(campusPublicId)) return fallback;

            using var connection = _dbFactory.GetConnection();

            var zone = await connection.ExecuteScalarAsync<string?>(
                "SELECT timezone FROM campus WHERE public_id = @PublicId LIMIT 1;",
                new { PublicId = campusPublicId });

            return string.IsNullOrWhiteSpace(zone) ? fallback : zone;
        }

        public async Task<ChurchEvent?> GetBySlugAsync(string slug, bool publishedOnly)
        {
            var sql = SelectEvent + @"
                WHERE e.slug = @Slug
                  AND (@PublishedOnly = 0 OR e.status IN ('PUBLISHED','CANCELLED'))
                LIMIT 1;";

            using var connection = _dbFactory.GetConnection();

            return await connection.QueryFirstOrDefaultAsync<ChurchEvent>(
                sql, new { Slug = slug, PublishedOnly = publishedOnly });
        }

        /// <summary>
        /// What the website gets. CANCELLED is included deliberately — the site shows it
        /// struck through rather than pretending it never existed, and a DRAFT is the
        /// only thing the public must never see.
        /// </summary>
        public async Task<IReadOnlyList<ChurchEvent>> ListPublishedAsync()
        {
            var sql = SelectEvent + @"
                WHERE e.status IN ('PUBLISHED','CANCELLED')
                ORDER BY e.starts_at;";

            using var connection = _dbFactory.GetConnection();
            return (await connection.QueryAsync<ChurchEvent>(sql)).ToList();
        }

        public async Task<long> CreateAsync(ChurchEvent e, long? actingUserId)
        {
            const string sql = @"
                INSERT INTO church_event
                    (public_id, slug, campus_id, title, summary, description, venue,
                     starts_at, ends_at, status, published_at,
                     created_by, updated_by)
                VALUES
                    (@PublicId, @Slug, @CampusId, @Title, @Summary, @Description, @Venue,
                     @StartsAt, @EndsAt, @Status, @PublishedAt,
                     @ActingUserId, @ActingUserId);
                SELECT LAST_INSERT_ID();";

            using var connection = _dbFactory.GetConnection();

            return await connection.ExecuteScalarAsync<long>(sql, new
            {
                e.PublicId, e.Slug, e.CampusId, e.Title, e.Summary, e.Description, e.Venue,
                e.StartsAt, e.EndsAt, e.Status, e.PublishedAt,
                ActingUserId = actingUserId
            });
        }

        public async Task<bool> UpdateAsync(ChurchEvent e, long? actingUserId)
        {
            // Status is deliberately absent: publishing is its own action with its own
            // audit stamp, so an edit cannot quietly push a draft live.
            const string sql = @"
                UPDATE church_event
                SET slug        = @Slug,
                    campus_id   = @CampusId,
                    title       = @Title,
                    summary     = @Summary,
                    description = @Description,
                    venue       = @Venue,
                    starts_at   = @StartsAt,
                    ends_at     = @EndsAt,
                    updated_by  = @ActingUserId,
                    row_version = row_version + 1
                WHERE id = @Id AND row_version = @RowVersion;";

            using var connection = _dbFactory.GetConnection();

            return await connection.ExecuteAsync(sql, new
            {
                e.Id, e.RowVersion, e.Slug, e.CampusId, e.Title, e.Summary, e.Description,
                e.Venue, e.StartsAt, e.EndsAt,
                ActingUserId = actingUserId
            }) == 1;
        }

        public async Task<bool> SetStatusAsync(
            long id, int rowVersion, string status, DateTime? publishedAt, long? actingUserId)
        {
            // published_at is passed in rather than left to a column default:
            // CURRENT_TIMESTAMP stamps the MySQL server's LOCAL time, which is hours out
            // from every other instant this application writes.
            const string sql = @"
                UPDATE church_event
                SET status       = @Status,
                    published_at = @PublishedAt,
                    updated_by   = @ActingUserId,
                    row_version  = row_version + 1
                WHERE id = @Id AND row_version = @RowVersion;";

            using var connection = _dbFactory.GetConnection();

            return await connection.ExecuteAsync(sql, new
            {
                Id = id,
                RowVersion = rowVersion,
                Status = status,
                PublishedAt = publishedAt,
                ActingUserId = actingUserId
            }) == 1;
        }

        public async Task<bool> SlugExistsAsync(string slug, long? excludingId = null)
        {
            // Matches ux_church_event_slug, so the caller gets a sentence rather than a
            // duplicate-key exception.
            const string sql = @"
                SELECT COUNT(1) FROM church_event
                WHERE slug = @Slug AND (@Excluding IS NULL OR id <> @Excluding);";

            using var connection = _dbFactory.GetConnection();

            return await connection.ExecuteScalarAsync<int>(
                sql, new { Slug = slug, Excluding = excludingId }) > 0;
        }

        public async Task<long?> ResolveCampusIdAsync(string? campusPublicId)
        {
            if (string.IsNullOrWhiteSpace(campusPublicId)) return null;

            using var connection = _dbFactory.GetConnection();

            return await connection.ExecuteScalarAsync<long?>(
                "SELECT id FROM campus WHERE public_id = @PublicId AND is_active = 1 LIMIT 1;",
                new { PublicId = campusPublicId });
        }

        // ------------------------------------------------------------------
        // The poster
        // ------------------------------------------------------------------

        public async Task<EventPoster?> GetPosterAsync(string publicId)
        {
            const string sql = @"
                SELECT
                    p.bytes               AS Bytes,
                    e.poster_content_type AS ContentType,
                    e.poster_updated_at   AS UpdatedAt
                FROM church_event e
                JOIN church_event_poster p ON p.church_event_id = e.id
                WHERE e.public_id = @PublicId
                LIMIT 1;";

            using var connection = _dbFactory.GetConnection();

            return await connection.QueryFirstOrDefaultAsync<EventPoster>(
                sql, new { PublicId = publicId });
        }

        public async Task<bool> SavePosterAsync(
            long id, byte[] bytes, string contentType, string? fileName,
            DateTime nowUtc, long? actingUserId)
        {
            const string upsertBytes = @"
                INSERT INTO church_event_poster (church_event_id, bytes)
                VALUES (@Id, @Bytes)
                ON DUPLICATE KEY UPDATE bytes = VALUES(bytes);";

            // row_version moves, so an editor holding the old one is told to reload
            // rather than saving over a poster somebody else has just replaced.
            const string stampEvent = @"
                UPDATE church_event
                SET poster_content_type = @ContentType,
                    poster_file_name    = @FileName,
                    poster_byte_size    = @ByteSize,
                    poster_updated_at   = @NowUtc,
                    updated_by          = @ActingUserId,
                    row_version         = row_version + 1
                WHERE id = @Id;";

            using var connection = _dbFactory.GetConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                await connection.ExecuteAsync(upsertBytes, new { Id = id, Bytes = bytes }, transaction);

                var stamped = await connection.ExecuteAsync(stampEvent, new
                {
                    Id = id,
                    ContentType = contentType,
                    FileName = fileName,
                    ByteSize = bytes.Length,
                    NowUtc = nowUtc,
                    ActingUserId = actingUserId
                }, transaction);

                transaction.Commit();
                return stamped == 1;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public async Task<bool> RemovePosterAsync(long id, long? actingUserId)
        {
            const string deleteBytes =
                "DELETE FROM church_event_poster WHERE church_event_id = @Id;";

            const string clearEvent = @"
                UPDATE church_event
                SET poster_content_type = NULL,
                    poster_file_name    = NULL,
                    poster_byte_size    = NULL,
                    poster_updated_at   = NULL,
                    updated_by          = @ActingUserId,
                    row_version         = row_version + 1
                WHERE id = @Id;";

            using var connection = _dbFactory.GetConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                await connection.ExecuteAsync(deleteBytes, new { Id = id }, transaction);

                var cleared = await connection.ExecuteAsync(
                    clearEvent, new { Id = id, ActingUserId = actingUserId }, transaction);

                transaction.Commit();
                return cleared == 1;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }
}
