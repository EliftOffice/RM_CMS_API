using System.Data;
using Dapper;
using RM_CMS.Data;
using RM_CMS.Modules.Volunteers.Domain;

namespace RM_CMS.Modules.Volunteers.Data
{
    /// <summary>
    /// Data access for <c>volunteer</c>, <c>team</c>, <c>volunteer_capacity_change</c>
    /// and the <c>capacity_band</c> lookup.
    ///
    /// Mutating statements guard on <c>row_version</c> and return the affected row
    /// count, so a lost update is detected rather than silently applied.
    /// </summary>
    public interface IVolunteerRepository
    {
        Task<Volunteer?> GetByPublicIdAsync(string publicId);
        Task<Volunteer?> GetByIdAsync(long id);
        Task<Volunteer?> GetByPersonIdAsync(long personId);

        Task<IReadOnlyList<Volunteer>> SearchAsync(VolunteerQuery query);
        Task<int> CountAsync(VolunteerQuery query);

        /// <summary>
        /// Volunteers who could take another case, least loaded first.
        ///
        /// This is the shortlist the assignment job works from, exposed so a team
        /// lead assigning by hand sees exactly what the scheduler would have picked.
        /// </summary>
        Task<IReadOnlyList<Volunteer>> FindEligibleAsync(long campusId, bool crisisCapable, int limit);

        Task<long> EnrolAsync(Volunteer volunteer, long? actingUserId);

        Task<bool> UpdateAsync(long id, int rowVersion, string status, long? teamId,
                               string? serviceLevel, string? burnoutRisk, DateTime? endedOn, long? actingUserId);

        /// <summary>Changes the band and writes the history row in one transaction.</summary>
        Task<bool> ChangeCapacityAsync(long id, int rowVersion, string fromBand, string toBand,
                                       string? reason, string? notes, long? actingUserId);

        Task<bool> UpdateSafeguardingAsync(long id, int rowVersion, DateTime? backgroundCheckedOn,
                                           DateTime? confidentialitySignedOn, DateTime? crisisTrainedOn, long? actingUserId);

        Task<IReadOnlyList<CapacityChange>> GetCapacityHistoryAsync(long volunteerId);
        Task<IReadOnlyList<CapacityBand>> GetCapacityBandsAsync();
        Task<CapacityBand?> GetCapacityBandAsync(string code);

        Task<long?> ResolvePersonIdAsync(string personPublicId);
        Task<long?> ResolveCampusIdAsync(string? campusPublicId);
        Task<long?> ResolveTeamIdAsync(string? teamPublicId);
    }

    public sealed class VolunteerQuery
    {
        public string? Search { get; init; }
        public string? Status { get; init; }
        public long? CampusId { get; init; }
        public long? TeamId { get; init; }
        public bool? HasCapacity { get; init; }
        public int Skip { get; init; }
        public int Take { get; init; } = 25;
    }

    public sealed class VolunteerRepository : IVolunteerRepository
    {
        private readonly IDbConnectionFactory _dbFactory;

        public VolunteerRepository(IDbConnectionFactory dbFactory) => _dbFactory = dbFactory;

        /// <summary>
        /// Joins the person (identity and contact), the campus, the team and the
        /// capacity band, so a volunteer list needs one query rather than four.
        ///
        /// The primary phone and email are pulled with correlated subqueries because
        /// contacts are rows now; joining person_contact twice would multiply the
        /// result set.
        /// </summary>
        private const string SelectVolunteer = @"
            SELECT
                v.id                       AS Id,
                v.public_id                AS PublicId,
                v.reference_code           AS ReferenceCode,

                v.person_id                AS PersonId,
                p.public_id                AS PersonPublicId,
                p.full_name                AS FullName,
                (SELECT pc.value FROM person_contact pc
                  WHERE pc.person_id = p.id AND pc.contact_type = 'MOBILE'
                  ORDER BY pc.is_primary DESC, pc.id LIMIT 1)  AS PrimaryPhone,
                (SELECT pc.value FROM person_contact pc
                  WHERE pc.person_id = p.id AND pc.contact_type = 'EMAIL'
                  ORDER BY pc.is_primary DESC, pc.id LIMIT 1)  AS PrimaryEmail,

                v.campus_id                AS CampusId,
                c.public_id                AS CampusPublicId,
                c.name                     AS CampusName,

                v.team_id                  AS TeamId,
                t.public_id                AS TeamPublicId,
                t.name                     AS TeamName,

                v.status                   AS Status,
                v.service_level            AS ServiceLevel,

                v.capacity_band_code       AS CapacityBandCode,
                cb.label                   AS CapacityBandLabel,
                cb.min_per_week            AS CapacityMinPerWeek,
                cb.max_per_week            AS CapacityMaxPerWeek,

                v.current_case_load        AS CurrentCaseLoad,
                v.lifetime_cases_assigned  AS LifetimeCasesAssigned,
                v.lifetime_cases_closed    AS LifetimeCasesClosed,
                v.last_assigned_at         AS LastAssignedAt,

                v.started_on               AS StartedOn,
                v.ended_on                 AS EndedOn,

                v.burnout_risk             AS BurnoutRisk,
                v.last_check_in_on         AS LastCheckInOn,
                v.next_check_in_on         AS NextCheckInOn,

                v.background_checked_on    AS BackgroundCheckedOn,
                v.confidentiality_signed_on AS ConfidentialitySignedOn,
                v.crisis_trained_on        AS CrisisTrainedOn,
                v.boundary_violation_count AS BoundaryViolationCount,

                v.created_at               AS CreatedAt,
                v.updated_at               AS UpdatedAt,
                v.row_version              AS RowVersion
            FROM volunteer v
            JOIN person p         ON p.id = v.person_id
            JOIN campus c         ON c.id = v.campus_id
            JOIN capacity_band cb ON cb.code = v.capacity_band_code
            LEFT JOIN team t      ON t.id = v.team_id";

        // ------------------------------------------------------------------
        // Reads
        // ------------------------------------------------------------------

        public async Task<Volunteer?> GetByPublicIdAsync(string publicId)
        {
            const string sql = SelectVolunteer + @" WHERE v.public_id = @PublicId AND p.deleted_at IS NULL LIMIT 1;";

            using var connection = _dbFactory.GetConnection();
            return await connection.QueryFirstOrDefaultAsync<Volunteer>(sql, new { PublicId = publicId });
        }

        public async Task<Volunteer?> GetByIdAsync(long id)
        {
            const string sql = SelectVolunteer + @" WHERE v.id = @Id LIMIT 1;";

            using var connection = _dbFactory.GetConnection();
            return await connection.QueryFirstOrDefaultAsync<Volunteer>(sql, new { Id = id });
        }

        public async Task<Volunteer?> GetByPersonIdAsync(long personId)
        {
            const string sql = SelectVolunteer + @" WHERE v.person_id = @PersonId LIMIT 1;";

            using var connection = _dbFactory.GetConnection();
            return await connection.QueryFirstOrDefaultAsync<Volunteer>(sql, new { PersonId = personId });
        }

        public async Task<IReadOnlyList<Volunteer>> SearchAsync(VolunteerQuery query)
        {
            var sql = SelectVolunteer + WhereClause() + @"
            ORDER BY p.full_name
            LIMIT @Take OFFSET @Skip;";

            using var connection = _dbFactory.GetConnection();
            return (await connection.QueryAsync<Volunteer>(sql, Parameters(query))).ToList();
        }

        public async Task<int> CountAsync(VolunteerQuery query)
        {
            var sql = @"
                SELECT COUNT(1)
                FROM volunteer v
                JOIN person p         ON p.id = v.person_id
                JOIN campus c         ON c.id = v.campus_id
                JOIN capacity_band cb ON cb.code = v.capacity_band_code
                LEFT JOIN team t      ON t.id = v.team_id" + WhereClause() + ";";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteScalarAsync<int>(sql, Parameters(query));
        }

        private static string WhereClause() => @"
            WHERE p.deleted_at IS NULL
              AND (@Search IS NULL
                   OR p.full_name        LIKE CONCAT('%', @Search, '%')
                   OR v.reference_code   LIKE CONCAT('%', @Search, '%'))
              AND (@Status   IS NULL OR v.status    = @Status)
              AND (@CampusId IS NULL OR v.campus_id = @CampusId)
              AND (@TeamId   IS NULL OR v.team_id   = @TeamId)
              AND (@HasCapacity IS NULL
                   OR (@HasCapacity = 1 AND v.current_case_load < cb.max_per_week)
                   OR (@HasCapacity = 0 AND v.current_case_load >= cb.max_per_week))";

        private static object Parameters(VolunteerQuery query) => new
        {
            Search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(),
            query.Status,
            query.CampusId,
            query.TeamId,
            query.HasCapacity,
            query.Skip,
            query.Take
        };

        public async Task<IReadOnlyList<Volunteer>> FindEligibleAsync(long campusId, bool crisisCapable, int limit)
        {
            // Least loaded first, then longest since last assigned. The MVP used
            // RAND() as the tie-break, which made assignment non-reproducible and
            // impossible to explain to a volunteer asking why they got a case.
            // Ordering by last_assigned_at spreads work fairly AND deterministically.
            const string sql = SelectVolunteer + @"
            WHERE p.deleted_at IS NULL
              AND v.campus_id = @CampusId
              AND v.status = 'ACTIVE'
              AND v.current_case_load < cb.max_per_week
              AND (@CrisisCapable = 0 OR (
                       v.crisis_trained_on         IS NOT NULL
                   AND v.background_checked_on     IS NOT NULL
                   AND v.confidentiality_signed_on IS NOT NULL))
            ORDER BY v.current_case_load ASC,
                     v.last_assigned_at ASC,
                     v.id ASC
            LIMIT @Limit;";

            using var connection = _dbFactory.GetConnection();
            return (await connection.QueryAsync<Volunteer>(sql, new
            {
                CampusId = campusId,
                CrisisCapable = crisisCapable,
                Limit = limit
            })).ToList();
        }

        // ------------------------------------------------------------------
        // Writes
        // ------------------------------------------------------------------

        public async Task<long> EnrolAsync(Volunteer volunteer, long? actingUserId)
        {
            const string insert = @"
                INSERT INTO volunteer
                    (public_id, reference_code, person_id, campus_id, team_id,
                     status, service_level, capacity_band_code, started_on,
                     created_by, updated_by)
                VALUES
                    (@PublicId, @ReferenceCode, @PersonId, @CampusId, @TeamId,
                     @Status, @ServiceLevel, @CapacityBandCode, @StartedOn,
                     @ActingUserId, @ActingUserId);
                SELECT LAST_INSERT_ID();";

            const string history = @"
                INSERT INTO volunteer_capacity_change
                    (volunteer_id, from_band_code, to_band_code, reason, changed_by)
                VALUES (@VolunteerId, NULL, @ToBand, 'ONBOARDING', @ActingUserId);";

            using var connection = _dbFactory.GetConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                volunteer.ReferenceCode = await NextReferenceCodeAsync(connection, transaction);

                var id = await connection.ExecuteScalarAsync<long>(insert, new
                {
                    volunteer.PublicId,
                    volunteer.ReferenceCode,
                    volunteer.PersonId,
                    volunteer.CampusId,
                    volunteer.TeamId,
                    volunteer.Status,
                    volunteer.ServiceLevel,
                    volunteer.CapacityBandCode,
                    volunteer.StartedOn,
                    ActingUserId = actingUserId
                }, transaction);

                // Seed the history so the band trail starts at enrolment rather than
                // at the first change.
                await connection.ExecuteAsync(history, new
                {
                    VolunteerId = id,
                    ToBand = volunteer.CapacityBandCode,
                    ActingUserId = actingUserId
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

        public async Task<bool> UpdateAsync(
            long id, int rowVersion, string status, long? teamId,
            string? serviceLevel, string? burnoutRisk, DateTime? endedOn, long? actingUserId)
        {
            const string sql = @"
                UPDATE volunteer
                SET status        = @Status,
                    team_id       = @TeamId,
                    service_level = COALESCE(@ServiceLevel, service_level),
                    burnout_risk  = @BurnoutRisk,
                    ended_on      = @EndedOn,
                    updated_by    = @ActingUserId,
                    row_version   = row_version + 1
                WHERE id = @Id AND row_version = @RowVersion;";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteAsync(sql, new
            {
                Id = id,
                RowVersion = rowVersion,
                Status = status,
                TeamId = teamId,
                ServiceLevel = serviceLevel,
                BurnoutRisk = burnoutRisk,
                EndedOn = endedOn,
                ActingUserId = actingUserId
            }) == 1;
        }

        public async Task<bool> ChangeCapacityAsync(
            long id, int rowVersion, string fromBand, string toBand,
            string? reason, string? notes, long? actingUserId)
        {
            const string update = @"
                UPDATE volunteer
                SET capacity_band_code = @ToBand,
                    updated_by         = @ActingUserId,
                    row_version        = row_version + 1
                WHERE id = @Id AND row_version = @RowVersion;";

            const string history = @"
                INSERT INTO volunteer_capacity_change
                    (volunteer_id, from_band_code, to_band_code, reason, notes, changed_by)
                VALUES (@Id, @FromBand, @ToBand, @Reason, @Notes, @ActingUserId);";

            using var connection = _dbFactory.GetConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                var updated = await connection.ExecuteAsync(update, new
                {
                    Id = id,
                    RowVersion = rowVersion,
                    ToBand = toBand,
                    ActingUserId = actingUserId
                }, transaction);

                if (updated != 1)
                {
                    transaction.Rollback();
                    return false;
                }

                // Same transaction: a band change without its history row would leave
                // the wellbeing trail lying about what happened.
                await connection.ExecuteAsync(history, new
                {
                    Id = id,
                    FromBand = fromBand,
                    ToBand = toBand,
                    Reason = reason,
                    Notes = notes,
                    ActingUserId = actingUserId
                }, transaction);

                transaction.Commit();
                return true;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public async Task<bool> UpdateSafeguardingAsync(
            long id, int rowVersion, DateTime? backgroundCheckedOn,
            DateTime? confidentialitySignedOn, DateTime? crisisTrainedOn, long? actingUserId)
        {
            const string sql = @"
                UPDATE volunteer
                SET background_checked_on     = @BackgroundCheckedOn,
                    confidentiality_signed_on = @ConfidentialitySignedOn,
                    crisis_trained_on         = @CrisisTrainedOn,
                    updated_by                = @ActingUserId,
                    row_version               = row_version + 1
                WHERE id = @Id AND row_version = @RowVersion;";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteAsync(sql, new
            {
                Id = id,
                RowVersion = rowVersion,
                BackgroundCheckedOn = backgroundCheckedOn,
                ConfidentialitySignedOn = confidentialitySignedOn,
                CrisisTrainedOn = crisisTrainedOn,
                ActingUserId = actingUserId
            }) == 1;
        }

        // ------------------------------------------------------------------
        // Lookups
        // ------------------------------------------------------------------

        public async Task<IReadOnlyList<CapacityChange>> GetCapacityHistoryAsync(long volunteerId)
        {
            const string sql = @"
                SELECT  vc.id             AS Id,
                        vc.volunteer_id   AS VolunteerId,
                        vc.from_band_code AS FromBandCode,
                        vc.to_band_code   AS ToBandCode,
                        vc.reason         AS Reason,
                        vc.notes          AS Notes,
                        vc.changed_at     AS ChangedAt,
                        p.full_name       AS ChangedByName
                FROM volunteer_capacity_change vc
                LEFT JOIN user_account ua ON ua.id = vc.changed_by
                LEFT JOIN person p        ON p.id  = ua.person_id
                WHERE vc.volunteer_id = @VolunteerId
                ORDER BY vc.changed_at DESC;";

            using var connection = _dbFactory.GetConnection();
            return (await connection.QueryAsync<CapacityChange>(sql, new { VolunteerId = volunteerId })).ToList();
        }

        public async Task<IReadOnlyList<CapacityBand>> GetCapacityBandsAsync()
        {
            const string sql = @"
                SELECT code AS Code, label AS Label, min_per_week AS MinPerWeek,
                       max_per_week AS MaxPerWeek, description AS Description
                FROM capacity_band
                WHERE is_active = 1
                ORDER BY sort_order;";

            using var connection = _dbFactory.GetConnection();
            return (await connection.QueryAsync<CapacityBand>(sql)).ToList();
        }

        public async Task<CapacityBand?> GetCapacityBandAsync(string code)
        {
            const string sql = @"
                SELECT code AS Code, label AS Label, min_per_week AS MinPerWeek,
                       max_per_week AS MaxPerWeek, description AS Description
                FROM capacity_band
                WHERE code = @Code AND is_active = 1
                LIMIT 1;";

            using var connection = _dbFactory.GetConnection();
            return await connection.QueryFirstOrDefaultAsync<CapacityBand>(sql, new { Code = code });
        }

        public async Task<long?> ResolvePersonIdAsync(string personPublicId)
        {
            const string sql = @"SELECT id FROM person WHERE public_id = @PublicId AND deleted_at IS NULL LIMIT 1;";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteScalarAsync<long?>(sql, new { PublicId = personPublicId });
        }

        public async Task<long?> ResolveCampusIdAsync(string? campusPublicId)
        {
            if (string.IsNullOrWhiteSpace(campusPublicId)) return null;

            const string sql = @"SELECT id FROM campus WHERE public_id = @PublicId AND is_active = 1 LIMIT 1;";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteScalarAsync<long?>(sql, new { PublicId = campusPublicId });
        }

        public async Task<long?> ResolveTeamIdAsync(string? teamPublicId)
        {
            if (string.IsNullOrWhiteSpace(teamPublicId)) return null;

            const string sql = @"SELECT id FROM team WHERE public_id = @PublicId LIMIT 1;";

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteScalarAsync<long?>(sql, new { PublicId = teamPublicId });
        }

        private static async Task<string> NextReferenceCodeAsync(IDbConnection connection, IDbTransaction transaction)
        {
            const string sql = @"
                SELECT CONCAT('V', LPAD(
                    IFNULL(MAX(CAST(SUBSTRING(reference_code, 2) AS UNSIGNED)), 0) + 1, 3, '0'))
                FROM volunteer
                WHERE reference_code REGEXP '^V[0-9]+$'
                FOR UPDATE;";

            return await connection.ExecuteScalarAsync<string>(sql, transaction: transaction) ?? "V001";
        }
    }
}
