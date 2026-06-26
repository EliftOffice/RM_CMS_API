using RM_CMS.DAL.Events;
using RM_CMS.DAL.Attendance;
using RM_CMS.Data.DTO.Attendance;
using RM_CMS.Utilities;

namespace RM_CMS.BLL.Attendance
{
    public interface IAttendanceBLL
    {
        Task<ApiResponse<AttendanceCheckInResultDto>> CheckInAsync(AttendanceCheckInRequest request);
        Task<ApiResponse<List<AttendanceHistoryItemDto>>> GetHistoryAsync(long userId);
    }

    public class AttendanceBLL : IAttendanceBLL
    {
        private readonly IEventsDAL _eventsDAL;
        private readonly IAttendanceDAL _attendanceDAL;

        public AttendanceBLL(IEventsDAL eventsDAL, IAttendanceDAL attendanceDAL)
        {
            _eventsDAL = eventsDAL;
            _attendanceDAL = attendanceDAL;
        }

        public async Task<ApiResponse<AttendanceCheckInResultDto>> CheckInAsync(AttendanceCheckInRequest request)
        {
            try
            {
                if (request == null)
                {
                    return new ApiResponse<AttendanceCheckInResultDto>(ResponseType.Warning, "Invalid request", null);
                }

                if (request.UserId <= 0 || request.EventId <= 0)
                {
                    return new ApiResponse<AttendanceCheckInResultDto>(ResponseType.Warning, "Invalid user or event", null);
                }

                var timestampUtc = request.Timestamp.ToUniversalTime();
                request.EventTitle = request.EventTitle?.Trim() ?? string.Empty;
                request.DeviceInfo = request.DeviceInfo?.Trim() ?? string.Empty;

                var eventIsActiveResult = await _eventsDAL.IsEventActiveAsync(request.EventId);
                if (!eventIsActiveResult.Data)
                {
                    return new ApiResponse<AttendanceCheckInResultDto>(ResponseType.Warning, "Event is not active", null);
                }

                var existingAttendanceResult = await _attendanceDAL.GetExistingAttendanceIdAsync(
                    request.UserId,
                    request.EventId,
                    timestampUtc);

                if (existingAttendanceResult.Data.HasValue)
                {
                    return new ApiResponse<AttendanceCheckInResultDto>(
                        ResponseType.Success,
                        "Attendance already marked",
                        new AttendanceCheckInResultDto
                        {
                            AttendanceId = existingAttendanceResult.Data.Value,
                            Status = "duplicate"
                        });
                }

                try
                {
                    var newRequest = new AttendanceCheckInRequest
                    {
                        UserId = request.UserId,
                        EventId = request.EventId,
                        EventTitle = request.EventTitle,
                        Timestamp = timestampUtc,
                        Latitude = request.Latitude,
                        Longitude = request.Longitude,
                        DeviceInfo = request.DeviceInfo,
                        IsSynced = true
                    };

                    var insertResult = await _attendanceDAL.InsertAsync(newRequest);

                    if (insertResult.ResponseType == ResponseType.Success)
                    {
                        return new ApiResponse<AttendanceCheckInResultDto>(
                            ResponseType.Success,
                            "Attendance marked successfully",
                            new AttendanceCheckInResultDto
                            {
                                AttendanceId = insertResult.Data,
                                Status = "present"
                            });
                    }

                    return new ApiResponse<AttendanceCheckInResultDto>(ResponseType.Error, "Failed to mark attendance", null);
                }
                catch (Exception ex) when (_attendanceDAL.IsDuplicateKeyException(ex))
                {
                    var existingResult = await _attendanceDAL.GetExistingAttendanceIdAsync(
                        request.UserId,
                        request.EventId,
                        timestampUtc);

                    return new ApiResponse<AttendanceCheckInResultDto>(
                        ResponseType.Success,
                        "Attendance already marked",
                        new AttendanceCheckInResultDto
                        {
                            AttendanceId = existingResult.Data ?? 0,
                            Status = "duplicate"
                        });
                }
            }
            catch (Exception ex)
            {
                return new ApiResponse<AttendanceCheckInResultDto>(ResponseType.Error, $"Error during check-in: {ex.Message}", null);
            }
        }

        public async Task<ApiResponse<List<AttendanceHistoryItemDto>>> GetHistoryAsync(long userId)
        {
            try
            {
                if (userId <= 0)
                {
                    return new ApiResponse<List<AttendanceHistoryItemDto>>(ResponseType.Warning, "Invalid user ID", new List<AttendanceHistoryItemDto>());
                }

                return await _attendanceDAL.GetHistoryAsync(userId);
            }
            catch (Exception ex)
            {
                return new ApiResponse<List<AttendanceHistoryItemDto>>(ResponseType.Error, $"Error retrieving history: {ex.Message}", new List<AttendanceHistoryItemDto>());
            }
        }
    }
}
