using Dapper;
using RM_CMS.Data;
using RM_CMS.Modules.CheckIns.Domain;

namespace RM_CMS.Modules.CheckIns.Data
{
    /// <summary>
    /// Data access for <c>volunteer_check_in</c>.
    ///
    /// Recording a check-in also moves the volunteer's own <c>last_check_in_on</c> and
    /// <c>next_check_in_on</c>, and those two writes go together in one transaction.
    /// Split apart, a failure between them leaves a volunteer with a check-in on file
    /// that the "who is due?" list cannot see — which is precisely the volunteer who
    /// would then be chased again a day later.
    /// </summary>
    public interface ICheckInRepository
    {
        Task<long> RecordAsync(VolunteerCheckIn checkIn, long? actingUserId);

        /// <summary>Most recent first. The history a team lead reads before the next one.</summary>
        Task<IReadOnlyList<VolunteerCheckIn>> GetForVolunteerAsync(long volunteerId, int limit);

        Task<VolunteerCheckIn?> GetByPublicIdAsync(string publicId);

        /// <summary>
        /// Active volunteers on these teams whose check-in has fallen due, and those
        /// who have never had one at all. Ordered by who has waited longest.
        /// </summary>
        Task<IReadOnlyList<CheckInDue>> FindDueAsync(IReadOnlyList<long> teamIds, DateTime today, int overdueDays);

        /// <summary>The team a volunteer belongs to, for the object-level check.</summary>
        Task<(long VolunteerId, long? TeamId)?> ResolveVolunteerAsync(string volunteerPublicId);
    }

    public sealed class CheckInRepository : ICheckInRepository
    {
        private readonly IDbConnectionFactory _dbFactory;

        public CheckInRepository(IDbConnectionFactory dbFactory) => _dbFactory = dbFactory;

        private const string SelectCheckIn = @"
            SELECT
                ci.id                     AS Id,
                ci.public_id              AS PublicId,
                ci.volunteer_id           AS VolunteerId,
                v.public_id               AS VolunteerPublicId,
                vp.full_name              AS VolunteerName,
                ci.conducted_by           AS ConductedBy,
                cp.full_name              AS ConductedByName,
                ci.held_on                AS HeldOn,
                ci.duration_minutes       AS DurationMinutes,
                ci.meeting_type           AS MeetingType,
                ci.emotional_tone         AS EmotionalTone,
                ci.concerns               AS Concerns,
                ci.training_needs         AS TrainingNeeds,
                ci.action_items           AS ActionItems,
                ci.capacity_reviewed      AS CapacityReviewed,
                ci.boundary_issues_raised AS BoundaryIssuesRaised,
                ci.follow_up_required     AS FollowUpRequired,
                ci.next_check_in_on       AS NextCheckInOn,
                ci.created_at             AS CreatedAt
            FROM volunteer_check_in ci
            JOIN volunteer v        ON v.id = ci.volunteer_id
            JOIN person vp          ON vp.id = v.person_id
            LEFT JOIN user_account ua ON ua.id = ci.conducted_by
            LEFT JOIN person cp     ON cp.id = ua.person_id";

        public async Task<long> RecordAsync(VolunteerCheckIn checkIn, long? actingUserId)
        {
            const string insertSql = @"
                INSERT INTO volunteer_check_in
                    (public_id, volunteer_id, conducted_by, held_on, duration_minutes,
                     meeting_type, emotional_tone, concerns, training_needs, action_items,
                     capacity_reviewed, boundary_issues_raised, follow_up_required,
                     next_check_in_on, created_at, created_by, updated_at, updated_by)
                VALUES
                    (@PublicId, @VolunteerId, @ConductedBy, @HeldOn, @DurationMinutes,
                     @MeetingType, @EmotionalTone, @Concerns, @TrainingNeeds, @ActionItems,
                     @CapacityReviewed, @BoundaryIssuesRaised, @FollowUpRequired,
                     @NextCheckInOn, @Now, @ActingUserId, @Now, @ActingUserId);

                SELECT LAST_INSERT_ID();";

            // The volunteer's own dates move with it. next_check_in_on is only advanced
            // when this check-in actually names one — clearing it because a lead left
            // the field blank would quietly drop that volunteer off the due list.
            const string updateVolunteerSql = @"
                UPDATE volunteer
                SET last_check_in_on = @HeldOn,
                    next_check_in_on = COALESCE(@NextCheckInOn, next_check_in_on),
                    updated_by       = @ActingUserId
                WHERE id = @VolunteerId;";

            using var connection = _dbFactory.GetConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                var parameters = new
                {
                    checkIn.PublicId,
                    checkIn.VolunteerId,
                    checkIn.ConductedBy,
                    checkIn.HeldOn,
                    checkIn.DurationMinutes,
                    checkIn.MeetingType,
                    checkIn.EmotionalTone,
                    checkIn.Concerns,
                    checkIn.TrainingNeeds,
                    checkIn.ActionItems,
                    checkIn.CapacityReviewed,
                    checkIn.BoundaryIssuesRaised,
                    checkIn.FollowUpRequired,
                    checkIn.NextCheckInOn,
                    Now = checkIn.CreatedAt,
                    ActingUserId = actingUserId
                };

                var id = await connection.ExecuteScalarAsync<long>(insertSql, parameters, transaction);

                await connection.ExecuteAsync(updateVolunteerSql, parameters, transaction);

                transaction.Commit();
                return id;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public async Task<IReadOnlyList<VolunteerCheckIn>> GetForVolunteerAsync(long volunteerId, int limit)
        {
            var sql = SelectCheckIn + @"
            WHERE ci.volunteer_id = @VolunteerId
            ORDER BY ci.held_on DESC, ci.id DESC
            LIMIT @Limit;";

            using var connection = _dbFactory.GetConnection();

            return (await connection.QueryAsync<VolunteerCheckIn>(
                sql, new { VolunteerId = volunteerId, Limit = limit })).ToList();
        }

        public async Task<VolunteerCheckIn?> GetByPublicIdAsync(string publicId)
        {
            var sql = SelectCheckIn + @" WHERE ci.public_id = @PublicId LIMIT 1;";

            using var connection = _dbFactory.GetConnection();

            return await connection.QueryFirstOrDefaultAsync<VolunteerCheckIn>(sql, new { PublicId = publicId });
        }

        public async Task<IReadOnlyList<CheckInDue>> FindDueAsync(
            IReadOnlyList<long> teamIds, DateTime today, int overdueDays)
        {
            if (teamIds.Count == 0) return Array.Empty<CheckInDue>();

            // A volunteer with no check-in at all is due by definition, and sorts
            // first: never having had one is worse than being a fortnight late.
            const string sql = @"
                SELECT
                    v.public_id       AS VolunteerId,
                    v.reference_code  AS ReferenceCode,
                    p.full_name       AS VolunteerName,
                    t.name            AS TeamName,
                    v.last_check_in_on AS LastCheckInOn,
                    v.next_check_in_on AS NextCheckInOn,
                    CASE WHEN v.last_check_in_on IS NULL THEN NULL
                         ELSE DATEDIFF(@Today, v.last_check_in_on) END AS DaysSinceLastCheckIn,
                    (SELECT ci.emotional_tone FROM volunteer_check_in ci
                      WHERE ci.volunteer_id = v.id
                      ORDER BY ci.held_on DESC, ci.id DESC LIMIT 1) AS LastEmotionalTone,
                    1 AS IsOverdue
                FROM volunteer v
                JOIN person p    ON p.id = v.person_id
                LEFT JOIN team t ON t.id = v.team_id
                WHERE v.team_id IN @TeamIds
                  AND v.status = 'ACTIVE'
                  AND p.deleted_at IS NULL
                  AND (
                        v.last_check_in_on IS NULL
                     OR (v.next_check_in_on IS NOT NULL AND v.next_check_in_on <= @Today)
                     OR (v.next_check_in_on IS NULL
                         AND DATEDIFF(@Today, v.last_check_in_on) >= @OverdueDays)
                      )
                ORDER BY (v.last_check_in_on IS NOT NULL), v.last_check_in_on, p.full_name;";

            using var connection = _dbFactory.GetConnection();

            return (await connection.QueryAsync<CheckInDue>(sql, new
            {
                TeamIds = teamIds,
                Today = today.Date,
                OverdueDays = overdueDays
            })).ToList();
        }

        public async Task<(long VolunteerId, long? TeamId)?> ResolveVolunteerAsync(string volunteerPublicId)
        {
            const string sql = @"
                SELECT v.id AS VolunteerId, v.team_id AS TeamId
                FROM volunteer v
                JOIN person p ON p.id = v.person_id
                WHERE v.public_id = @PublicId AND p.deleted_at IS NULL
                LIMIT 1;";

            using var connection = _dbFactory.GetConnection();

            var row = await connection.QueryFirstOrDefaultAsync<(long VolunteerId, long? TeamId)?>(
                sql, new { PublicId = volunteerPublicId });

            return row;
        }
    }
}
