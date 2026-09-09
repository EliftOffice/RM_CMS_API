using System.Data;
using Dapper;
using RM_CMS.Data;
using RM_CMS.Modules.WebEnquiries.Domain;

namespace RM_CMS.Modules.WebEnquiries.Data
{
    /// <summary>
    /// Data access for <c>web_enquiry</c>.
    ///
    /// There is no delete. Junk is marked SPAM rather than removed, so the volume of
    /// attempts stays visible — a queue that quietly discards things cannot tell you
    /// it is under attack.
    /// </summary>
    public interface IWebEnquiryRepository
    {
        Task<long> CreateAsync(WebEnquiry enquiry);

        Task<IReadOnlyList<WebEnquiry>> SearchAsync(WebEnquiryQuery query);
        Task<int> CountAsync(WebEnquiryQuery query);
        Task<WebEnquiry?> GetByPublicIdAsync(string publicId);
        Task<WebEnquirySummary> GetSummaryAsync(DateTime since);

        Task<bool> UpdateStatusAsync(
            long id, int rowVersion, string status, string? reviewNote,
            long reviewerAccountId, DateTime nowUtc);

        /// <summary>
        /// How many submissions this fingerprint has made since a cut-off. The public
        /// endpoint's own back-stop against a flood that slips past the rate limiter —
        /// the limiter partitions on the proxy's IP behind Coolify, so it cannot tell
        /// two visitors apart on its own.
        /// </summary>
        Task<int> CountRecentBySubmitterAsync(string submitterHash, DateTime since);

        Task<long?> ResolveCampusIdAsync(string? campusPublicId);
    }

    public sealed class WebEnquiryQuery
    {
        public string? Status { get; init; }
        public string? FormType { get; init; }
        public string? Search { get; init; }

        /// <summary>False hides anything flagged by the honeypot.</summary>
        public bool IncludeSpam { get; init; }

        public int Skip { get; init; }
        public int Take { get; init; } = 50;
    }

    public sealed class WebEnquiryRepository : IWebEnquiryRepository
    {
        private readonly IDbConnectionFactory _dbFactory;

        public WebEnquiryRepository(IDbConnectionFactory dbFactory) => _dbFactory = dbFactory;

        private const string SelectEnquiry = @"
            SELECT
                w.id                 AS Id,
                w.public_id          AS PublicId,
                w.reference_code     AS ReferenceCode,
                w.form_type          AS FormType,
                w.campus_id          AS CampusId,
                cam.name             AS CampusName,
                w.full_name          AS FullName,
                w.mobile             AS Mobile,
                w.mobile_normalized  AS MobileNormalized,
                w.email              AS Email,
                w.city               AS City,
                w.street             AS Street,
                w.landmark           AS Landmark,
                w.referred_by_name   AS ReferredByName,
                w.referred_by_mobile AS ReferredByMobile,
                w.message            AS Message,
                w.payload            AS Payload,
                w.source_page        AS SourcePage,
                w.user_agent         AS UserAgent,
                w.submitter_hash     AS SubmitterHash,
                w.is_suspected_spam  AS IsSuspectedSpam,
                w.status             AS Status,
                rp.full_name         AS ReviewedByName,
                w.reviewed_at        AS ReviewedAt,
                w.review_note        AS ReviewNote,
                w.linked_person_id   AS LinkedPersonId,
                lp.public_id         AS LinkedPersonPublicId,
                lp.full_name         AS LinkedPersonName,
                w.submitted_at       AS SubmittedAt,
                w.created_at         AS CreatedAt,
                w.row_version        AS RowVersion,
                -- Somebody on file already has this number. Shown so the coordinator
                -- knows before deciding, rather than creating a second copy of them.
                (SELECT COUNT(*) FROM person_contact pc
                  JOIN person pp ON pp.id = pc.person_id AND pp.deleted_at IS NULL
                  WHERE w.mobile_normalized IS NOT NULL
                    AND pc.normalized_value = w.mobile_normalized) AS MatchingPeople
            FROM web_enquiry w
            LEFT JOIN campus cam        ON cam.id = w.campus_id
            LEFT JOIN user_account rua  ON rua.id = w.reviewed_by
            LEFT JOIN person rp         ON rp.id = rua.person_id
            LEFT JOIN person lp         ON lp.id = w.linked_person_id";

        public async Task<long> CreateAsync(WebEnquiry e)
        {
            const string sql = @"
                INSERT INTO web_enquiry
                    (public_id, reference_code, form_type, campus_id,
                     full_name, mobile, mobile_normalized, email,
                     city, street, landmark,
                     referred_by_name, referred_by_mobile, message, payload,
                     source_page, user_agent, submitter_hash, is_suspected_spam,
                     status, submitted_at)
                VALUES
                    (@PublicId, @ReferenceCode, @FormType, @CampusId,
                     @FullName, @Mobile, @MobileNormalized, @Email,
                     @City, @Street, @Landmark,
                     @ReferredByName, @ReferredByMobile, @Message, @Payload,
                     @SourcePage, @UserAgent, @SubmitterHash, @IsSuspectedSpam,
                     @Status, @SubmittedAt);
                SELECT LAST_INSERT_ID();";

            using var connection = _dbFactory.GetConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                // Generated inside the transaction so two simultaneous submissions
                // cannot be handed the same code.
                e.ReferenceCode = await NextReferenceCodeAsync(connection, transaction);

                var id = await connection.ExecuteScalarAsync<long>(sql, new
                {
                    e.PublicId, e.ReferenceCode, e.FormType, e.CampusId,
                    e.FullName, e.Mobile, e.MobileNormalized, e.Email,
                    e.City, e.Street, e.Landmark,
                    e.ReferredByName, e.ReferredByMobile, e.Message, e.Payload,
                    e.SourcePage, e.UserAgent, e.SubmitterHash, e.IsSuspectedSpam,
                    e.Status, e.SubmittedAt
                }, transaction);

                transaction.Commit();
                return id;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        private static async Task<string> NextReferenceCodeAsync(
            IDbConnection connection, IDbTransaction transaction)
        {
            const string sql = @"
                SELECT CONCAT('W', LPAD(
                    COALESCE(MAX(CAST(SUBSTRING(reference_code, 2) AS UNSIGNED)), 0) + 1, 4, '0'))
                FROM web_enquiry
                WHERE reference_code REGEXP '^W[0-9]+$';";

            return await connection.ExecuteScalarAsync<string>(sql, transaction: transaction)
                   ?? "W0001";
        }

        private static string FilterClause(WebEnquiryQuery q)
        {
            var sql = string.Empty;

            if (!string.IsNullOrWhiteSpace(q.Status)) sql += " AND w.status = @Status ";
            if (!string.IsNullOrWhiteSpace(q.FormType)) sql += " AND w.form_type = @FormType ";

            // Spam is hidden by default but never deleted. A coordinator can ask for it.
            if (!q.IncludeSpam) sql += " AND w.is_suspected_spam = 0 AND w.status <> 'SPAM' ";

            if (!string.IsNullOrWhiteSpace(q.Search))
            {
                sql += @" AND (w.full_name LIKE @Search
                            OR w.mobile_normalized LIKE @Search
                            OR w.email LIKE @Search
                            OR w.reference_code LIKE @Search
                            OR w.city LIKE @Search
                            OR w.message LIKE @Search) ";
            }

            return sql;
        }

        public async Task<IReadOnlyList<WebEnquiry>> SearchAsync(WebEnquiryQuery query)
        {
            var sql = SelectEnquiry + $@"
                WHERE 1 = 1
                {FilterClause(query)}
                -- Unhandled first, then newest. The top of this list should be what
                -- nobody has looked at yet.
                ORDER BY (w.status <> 'NEW'), w.submitted_at DESC
                LIMIT @Take OFFSET @Skip;";

            using var connection = _dbFactory.GetConnection();

            return (await connection.QueryAsync<WebEnquiry>(sql, new
            {
                query.Status,
                query.FormType,
                Search = $"%{query.Search}%",
                query.Take,
                query.Skip
            })).ToList();
        }

        public async Task<int> CountAsync(WebEnquiryQuery query)
        {
            var sql = $@"
                SELECT COUNT(*) FROM web_enquiry w
                WHERE 1 = 1
                {FilterClause(query)};";

            using var connection = _dbFactory.GetConnection();

            return await connection.ExecuteScalarAsync<int>(sql, new
            {
                query.Status,
                query.FormType,
                Search = $"%{query.Search}%"
            });
        }

        public async Task<WebEnquiry?> GetByPublicIdAsync(string publicId)
        {
            var sql = SelectEnquiry + " WHERE w.public_id = @PublicId LIMIT 1;";

            using var connection = _dbFactory.GetConnection();
            return await connection.QueryFirstOrDefaultAsync<WebEnquiry>(sql, new { PublicId = publicId });
        }

        public async Task<WebEnquirySummary> GetSummaryAsync(DateTime since)
        {
            const string sql = @"
                SELECT
                    SUM(status = 'NEW')       AS `New`,
                    SUM(status = 'IN_REVIEW') AS InReview,
                    SUM(status = 'ACTIONED')  AS Actioned,
                    SUM(status = 'SPAM' OR is_suspected_spam = 1) AS Spam,
                    SUM(status = 'CLOSED')    AS Closed,
                    COUNT(*)                  AS Total,
                    SUM(submitted_at >= @Since) AS Today
                FROM web_enquiry;";

            using var connection = _dbFactory.GetConnection();

            return await connection.QueryFirstOrDefaultAsync<WebEnquirySummary>(sql, new { Since = since })
                   ?? new WebEnquirySummary();
        }

        public async Task<bool> UpdateStatusAsync(
            long id, int rowVersion, string status, string? reviewNote,
            long reviewerAccountId, DateTime nowUtc)
        {
            // reviewed_at is passed explicitly rather than left to the column default:
            // DEFAULT CURRENT_TIMESTAMP stamps the MySQL server's LOCAL time, which is
            // 5.5 hours off every other instant this application stores.
            const string sql = @"
                UPDATE web_enquiry
                SET status      = @Status,
                    review_note = @ReviewNote,
                    reviewed_by = @ReviewerId,
                    reviewed_at = @Now,
                    updated_by  = @ReviewerId,
                    row_version = row_version + 1
                WHERE id = @Id AND row_version = @RowVersion;";

            using var connection = _dbFactory.GetConnection();

            return await connection.ExecuteAsync(sql, new
            {
                Id = id,
                RowVersion = rowVersion,
                Status = status,
                ReviewNote = reviewNote,
                ReviewerId = reviewerAccountId,
                Now = nowUtc
            }) == 1;
        }

        public async Task<int> CountRecentBySubmitterAsync(string submitterHash, DateTime since)
        {
            const string sql = @"
                SELECT COUNT(*) FROM web_enquiry
                WHERE submitter_hash = @Hash AND submitted_at >= @Since;";

            using var connection = _dbFactory.GetConnection();

            return await connection.ExecuteScalarAsync<int>(
                sql, new { Hash = submitterHash, Since = since });
        }

        public async Task<long?> ResolveCampusIdAsync(string? campusPublicId)
        {
            if (string.IsNullOrWhiteSpace(campusPublicId)) return null;

            using var connection = _dbFactory.GetConnection();

            return await connection.ExecuteScalarAsync<long?>(
                "SELECT id FROM campus WHERE public_id = @PublicId AND is_active = 1 LIMIT 1;",
                new { PublicId = campusPublicId });
        }
    }
}
