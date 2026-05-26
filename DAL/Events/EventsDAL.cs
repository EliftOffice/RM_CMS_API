using Dapper;
using RM_CMS.Data;
using RM_CMS.Data.DTO.Events;
using RM_CMS.Utilities;

namespace RM_CMS.DAL.Events
{
    public interface IEventsDAL
    {
        Task<ApiResponse<List<EventDto>>> GetAdminEventsAsync();
        Task<ApiResponse<EventDto>> GetByIdAsync(long eventId);
        Task<ApiResponse<long>> CreateAsync(AdminEventRequest request);
        Task<ApiResponse<bool>> UpdateAsync(long id, AdminEventRequest request);
        Task<ApiResponse<bool>> UpdateStatusAsync(long id, bool isActive);
        Task<ApiResponse<List<EventDto>>> GetActiveEventsAsync(long? userId);
        Task<ApiResponse<bool>> IsEventActiveAsync(long eventId);
    }

    public class EventsDAL : IEventsDAL
    {
        private readonly IDbConnectionFactory _dbFactory;

        public EventsDAL(IDbConnectionFactory dbFactory)
        {
            _dbFactory = dbFactory;
        }

        public async Task<ApiResponse<List<EventDto>>> GetAdminEventsAsync()
        {
            try
            {
                const string sql = @"
                    SELECT
                        id AS Id,
                        title AS Title,
                        venue_name AS VenueName,
                        address AS Address,
                        latitude AS Latitude,
                        longitude AS Longitude,
                        radius AS Radius,
                        start_time AS StartTime,
                        end_time AS EndTime,
                        is_active AS IsActive,
                        recurrence_type AS RecurrenceType,
                        COALESCE(recurrence_day, '') AS RecurrenceDay,
                        repeat_until AS RepeatUntil,
                        reuse_same_location AS ReuseSameLocation,
                        auto_activate_recurring AS AutoActivateRecurring,
                        0 AS IsAttended
                    FROM events
                    ORDER BY start_time DESC;";

                using var connection = _dbFactory.GetConnection();
                var rows = await connection.QueryAsync<EventDto>(sql);
                return new ApiResponse<List<EventDto>>(ResponseType.Success, "Events retrieved", rows.ToList());
            }
            catch (Exception ex)
            {
                return new ApiResponse<List<EventDto>>(ResponseType.Error, $"Error retrieving events: {ex.Message}", new List<EventDto>());
            }
        }

        public async Task<ApiResponse<EventDto>> GetByIdAsync(long eventId)
        {
            try
            {
                const string sql = @"
                    SELECT
                        id AS Id,
                        title AS Title,
                        venue_name AS VenueName,
                        address AS Address,
                        latitude AS Latitude,
                        longitude AS Longitude,
                        radius AS Radius,
                        start_time AS StartTime,
                        end_time AS EndTime,
                        is_active AS IsActive,
                        recurrence_type AS RecurrenceType,
                        COALESCE(recurrence_day, '') AS RecurrenceDay,
                        repeat_until AS RepeatUntil,
                        reuse_same_location AS ReuseSameLocation,
                        auto_activate_recurring AS AutoActivateRecurring,
                        0 AS IsAttended
                    FROM events
                    WHERE id = @EventId
                    LIMIT 1;";

                using var connection = _dbFactory.GetConnection();
                var evt = await connection.QueryFirstOrDefaultAsync<EventDto>(sql, new { EventId = eventId });

                if (evt == null)
                {
                    return new ApiResponse<EventDto>(ResponseType.Warning, "Event not found", null);
                }

                return new ApiResponse<EventDto>(ResponseType.Success, "Event retrieved", evt);
            }
            catch (Exception ex)
            {
                return new ApiResponse<EventDto>(ResponseType.Error, $"Error retrieving event: {ex.Message}", null);
            }
        }

        public async Task<ApiResponse<long>> CreateAsync(AdminEventRequest request)
        {
            try
            {
                const string sql = @"
                    INSERT INTO events
                    (
                        title, venue_name, address, latitude, longitude, radius,
                        start_time, end_time, is_active, recurrence_type, recurrence_day,
                        repeat_until, reuse_same_location, auto_activate_recurring
                    )
                    VALUES
                    (
                        @Title, @VenueName, @Address, @Latitude, @Longitude, @Radius,
                        @StartTime, @EndTime, @IsActive, @RecurrenceType, @RecurrenceDay,
                        @RepeatUntil, @ReuseSameLocation, @AutoActivateRecurring
                    );
                    SELECT LAST_INSERT_ID();";

                using var connection = _dbFactory.GetConnection();
                var id = await connection.ExecuteScalarAsync<long>(sql, request);

                return new ApiResponse<long>(ResponseType.Success, "Event created successfully", id);
            }
            catch (Exception ex)
            {
                return new ApiResponse<long>(ResponseType.Error, $"Error creating event: {ex.Message}", 0);
            }
        }

        public async Task<ApiResponse<bool>> UpdateAsync(long id, AdminEventRequest request)
        {
            try
            {
                const string sql = @"
                    UPDATE events
                    SET
                        title = @Title,
                        venue_name = @VenueName,
                        address = @Address,
                        latitude = @Latitude,
                        longitude = @Longitude,
                        radius = @Radius,
                        start_time = @StartTime,
                        end_time = @EndTime,
                        is_active = @IsActive,
                        recurrence_type = @RecurrenceType,
                        recurrence_day = @RecurrenceDay,
                        repeat_until = @RepeatUntil,
                        reuse_same_location = @ReuseSameLocation,
                        auto_activate_recurring = @AutoActivateRecurring
                    WHERE id = @Id;";

                using var connection = _dbFactory.GetConnection();
                await connection.ExecuteAsync(sql, new
                {
                    Id = id,
                    request.Title,
                    request.VenueName,
                    request.Address,
                    request.Latitude,
                    request.Longitude,
                    request.Radius,
                    request.StartTime,
                    request.EndTime,
                    request.IsActive,
                    request.RecurrenceType,
                    request.RecurrenceDay,
                    request.RepeatUntil,
                    request.ReuseSameLocation,
                    request.AutoActivateRecurring
                });

                return new ApiResponse<bool>(ResponseType.Success, "Event updated successfully", true);
            }
            catch (Exception ex)
            {
                return new ApiResponse<bool>(ResponseType.Error, $"Error updating event: {ex.Message}", false);
            }
        }

        public async Task<ApiResponse<bool>> UpdateStatusAsync(long id, bool isActive)
        {
            try
            {
                const string sql = "UPDATE events SET is_active = @IsActive WHERE id = @Id;";
                using var connection = _dbFactory.GetConnection();
                await connection.ExecuteAsync(sql, new { Id = id, IsActive = isActive });

                return new ApiResponse<bool>(ResponseType.Success, "Event status updated", true);
            }
            catch (Exception ex)
            {
                return new ApiResponse<bool>(ResponseType.Error, $"Error updating status: {ex.Message}", false);
            }
        }

        public async Task<ApiResponse<List<EventDto>>> GetActiveEventsAsync(long? userId)
        {
            try
            {
                const string sql = @"
                    SELECT
                        e.id AS Id,
                        e.title AS Title,
                        e.venue_name AS VenueName,
                        e.address AS Address,
                        e.latitude AS Latitude,
                        e.longitude AS Longitude,
                        e.radius AS Radius,
                        CASE
                            WHEN e.recurrence_type IN ('weekly', 'every_sunday') THEN
                                TIMESTAMP(UTC_DATE(), TIME(e.start_time))
                            ELSE e.start_time
                        END AS StartTime,
                        CASE
                            WHEN e.recurrence_type IN ('weekly', 'every_sunday') THEN
                                TIMESTAMP(UTC_DATE(), TIME(e.end_time))
                            ELSE e.end_time
                        END AS EndTime,
                        CASE
                            WHEN @UserId IS NULL THEN 0
                            WHEN EXISTS (
                                SELECT 1
                                FROM attendances a
                                WHERE a.user_id = @UserId
                                  AND a.event_id = e.id
                                  AND a.attendance_day = UTC_DATE()
                            ) THEN 1
                            ELSE 0
                        END AS IsAttended,
                        e.is_active AS IsActive,
                        e.recurrence_type AS RecurrenceType,
                        COALESCE(e.recurrence_day, '') AS RecurrenceDay,
                        e.repeat_until AS RepeatUntil,
                        e.reuse_same_location AS ReuseSameLocation,
                        e.auto_activate_recurring AS AutoActivateRecurring
                    FROM events e
                    WHERE
                        (
                            e.recurrence_type = 'once'
                            AND e.is_active = 1
                            AND UTC_TIMESTAMP() BETWEEN e.start_time AND e.end_time
                        )
                        OR
                        (
                            e.recurrence_type = 'weekly'
                            AND e.auto_activate_recurring = 1
                            AND (e.repeat_until IS NULL OR UTC_DATE() <= e.repeat_until)
                            AND DAYNAME(UTC_DATE()) = e.recurrence_day
                            AND TIME(UTC_TIME()) BETWEEN TIME(e.start_time) AND TIME(e.end_time)
                        )
                        OR
                        (
                            e.recurrence_type = 'every_sunday'
                            AND e.auto_activate_recurring = 1
                            AND (e.repeat_until IS NULL OR UTC_DATE() <= e.repeat_until)
                            AND DAYNAME(UTC_DATE()) = 'Sunday'
                            AND TIME(UTC_TIME()) BETWEEN TIME(e.start_time) AND TIME(e.end_time)
                        )
                    ORDER BY e.start_time ASC;";

                using var connection = _dbFactory.GetConnection();
                var rows = await connection.QueryAsync<EventDto>(sql, new { UserId = userId });
                return new ApiResponse<List<EventDto>>(ResponseType.Success, "Active events retrieved", rows.ToList());
            }
            catch (Exception ex)
            {
                return new ApiResponse<List<EventDto>>(ResponseType.Error, $"Error retrieving active events: {ex.Message}", new List<EventDto>());
            }
        }

        public async Task<ApiResponse<bool>> IsEventActiveAsync(long eventId)
        {
            try
            {
                const string sql = @"
                    SELECT COUNT(1)
                    FROM events e
                    WHERE e.id = @EventId
                      AND
                      (
                          (
                              e.recurrence_type = 'once'
                              AND e.is_active = 1
                              AND UTC_TIMESTAMP() BETWEEN e.start_time AND e.end_time
                          )
                          OR
                          (
                              e.recurrence_type = 'weekly'
                              AND e.auto_activate_recurring = 1
                              AND (e.repeat_until IS NULL OR UTC_DATE() <= e.repeat_until)
                              AND DAYNAME(UTC_DATE()) = e.recurrence_day
                              AND TIME(UTC_TIME()) BETWEEN TIME(e.start_time) AND TIME(e.end_time)
                          )
                          OR
                          (
                              e.recurrence_type = 'every_sunday'
                              AND e.auto_activate_recurring = 1
                              AND (e.repeat_until IS NULL OR UTC_DATE() <= e.repeat_until)
                              AND DAYNAME(UTC_DATE()) = 'Sunday'
                              AND TIME(UTC_TIME()) BETWEEN TIME(e.start_time) AND TIME(e.end_time)
                          )
                      );";

                using var connection = _dbFactory.GetConnection();
                var count = await connection.ExecuteScalarAsync<int>(sql, new { EventId = eventId });
                return new ApiResponse<bool>(ResponseType.Success, "Check completed", count > 0);
            }
            catch (Exception ex)
            {
                return new ApiResponse<bool>(ResponseType.Error, $"Error checking event status: {ex.Message}", false);
            }
        }
    }
}
