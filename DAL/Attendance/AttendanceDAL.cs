using Dapper;
using MySqlConnector;
using RM_CMS.Data;
using RM_CMS.Data.DTO.Attendance;
using RM_CMS.Utilities;

namespace RM_CMS.DAL.Attendance
{
    public interface IAttendanceDAL
    {
        Task<ApiResponse<bool>> ExistsForSameDayAsync(long userId, long eventId, DateTime timestampUtc);
        Task<ApiResponse<long?>> GetExistingAttendanceIdAsync(long userId, long eventId, DateTime timestampUtc);
        Task<ApiResponse<long>> InsertAsync(AttendanceCheckInRequest request);
        Task<ApiResponse<List<AttendanceHistoryItemDto>>> GetHistoryAsync(long userId);
        bool IsDuplicateKeyException(Exception exception);
    }

    public class AttendanceDAL : IAttendanceDAL
    {
        private readonly IDbConnectionFactory _dbFactory;

        public AttendanceDAL(IDbConnectionFactory dbFactory)
        {
            _dbFactory = dbFactory;
        }

        public async Task<ApiResponse<bool>> ExistsForSameDayAsync(long userId, long eventId, DateTime timestampUtc)
        {
            try
            {
                const string sql = @"
                    SELECT COUNT(1)
                    FROM attendances
                    WHERE user_id = @UserId
                      AND event_id = @EventId
                      AND attendance_day = DATE(@TimestampUtc);";

                using var connection = _dbFactory.GetConnection();
                var count = await connection.ExecuteScalarAsync<int>(sql, new
                {
                    UserId = userId,
                    EventId = eventId,
                    TimestampUtc = timestampUtc
                });

                return new ApiResponse<bool>(ResponseType.Success, "Check completed", count > 0);
            }
            catch (Exception ex)
            {
                return new ApiResponse<bool>(ResponseType.Error, $"Error checking attendance: {ex.Message}", false);
            }
        }

        public async Task<ApiResponse<long?>> GetExistingAttendanceIdAsync(long userId, long eventId, DateTime timestampUtc)
        {
            try
            {
                const string sql = @"
                    SELECT id
                    FROM attendances
                    WHERE user_id = @UserId
                      AND event_id = @EventId
                      AND attendance_day = DATE(@TimestampUtc)
                    LIMIT 1;";

                using var connection = _dbFactory.GetConnection();
                var id = await connection.ExecuteScalarAsync<long?>(sql, new
                {
                    UserId = userId,
                    EventId = eventId,
                    TimestampUtc = timestampUtc
                });

                return new ApiResponse<long?>(ResponseType.Success, "Check completed", id);
            }
            catch (Exception ex)
            {
                return new ApiResponse<long?>(ResponseType.Error, $"Error retrieving attendance: {ex.Message}", null);
            }
        }

        public async Task<ApiResponse<long>> InsertAsync(AttendanceCheckInRequest request)
        {
            try
            {
                const string sql = @"
                    INSERT INTO attendances
                    (
                        user_id,
                        event_id,
                        event_title,
                        attendance_day,
                        checkin_time,
                        latitude,
                        longitude,
                        device_info
                    )
                    VALUES
                    (
                        @UserId,
                        @EventId,
                        @EventTitle,
                        DATE(@Timestamp),
                        @Timestamp,
                        @Latitude,
                        @Longitude,
                        @DeviceInfo
                    );
                    SELECT LAST_INSERT_ID();";

                using var connection = _dbFactory.GetConnection();
                var id = await connection.ExecuteScalarAsync<long>(sql, request);
                return new ApiResponse<long>(ResponseType.Success, "Attendance recorded", id);
            }
            catch (Exception ex)
            {
                return new ApiResponse<long>(ResponseType.Error, $"Error inserting attendance: {ex.Message}", 0);
            }
        }

        public async Task<ApiResponse<List<AttendanceHistoryItemDto>>> GetHistoryAsync(long userId)
        {
            try
            {
                const string sql = @"
                    SELECT
                        user_id AS UserId,
                        event_id AS EventId,
                        event_title AS EventTitle,
                        checkin_time AS Timestamp,
                        latitude AS Latitude,
                        longitude AS Longitude,
                        COALESCE(device_info, '') AS DeviceInfo,
                        1 AS IsSynced
                    FROM attendances
                    WHERE user_id = @UserId
                    ORDER BY checkin_time DESC;";

                using var connection = _dbFactory.GetConnection();
                var rows = await connection.QueryAsync<AttendanceHistoryItemDto>(sql, new { UserId = userId });
                return new ApiResponse<List<AttendanceHistoryItemDto>>(ResponseType.Success, "History retrieved", rows.ToList());
            }
            catch (Exception ex)
            {
                return new ApiResponse<List<AttendanceHistoryItemDto>>(ResponseType.Error, $"Error retrieving history: {ex.Message}", new List<AttendanceHistoryItemDto>());
            }
        }

        public bool IsDuplicateKeyException(Exception exception)
        {
            return exception is MySqlException mysqlException && mysqlException.Number == 1062;
        }
    }
}
