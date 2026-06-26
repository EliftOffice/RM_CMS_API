using Microsoft.AspNetCore.Mvc;
using RM_CMS.BLL.Attendance;
using RM_CMS.Data.DTO.Attendance;
using RM_CMS.Utilities;

namespace RM_CMS.Controllers.Attendance
{
    [ApiController]
    [Route("api/attendance")]
    public class AttendanceController : ControllerBase
    {
        private readonly IAttendanceBLL _attendanceBLL;
        private readonly ILogger<AttendanceController> _logger;

        public AttendanceController(IAttendanceBLL attendanceBLL, ILogger<AttendanceController> logger)
        {
            _attendanceBLL = attendanceBLL;
            _logger = logger;
        }

        [HttpPost("checkin")]
        public async Task<ActionResult<ApiResponse<AttendanceCheckInResultDto>>> CheckIn([FromBody] AttendanceCheckInRequest request)
        {
            try
            {
                _logger.LogInformation("Check-in for userId: {UserId}, eventId: {EventId}", request?.UserId, request?.EventId);

                if (request == null)
                {
                    return BadRequest(new ApiResponse<AttendanceCheckInResultDto>(ResponseType.Warning, "Invalid request", null));
                }

                var result = await _attendanceBLL.CheckInAsync(request);

                if (result.ResponseType == ResponseType.Success && result.Data?.Status == "duplicate")
                {
                    return new ObjectResult(result)
                    {
                        StatusCode = StatusCodes.Status409Conflict
                    };
                }

                return HttpResponseHelper.CreateHttpResponse(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during check-in");
                return StatusCode(500, new ApiResponse<AttendanceCheckInResultDto>(ResponseType.Error, "An error occurred during check-in", null));
            }
        }

        [HttpGet("history")]
        public async Task<IActionResult> History([FromQuery] long userId)
        {
            try
            {
                _logger.LogInformation("Fetching history for userId: {UserId}", userId);

                if (userId <= 0)
                {
                    return BadRequest(new { success = false, message = "Invalid userId" });
                }

                var result = await _attendanceBLL.GetHistoryAsync(userId);
                return Ok(new { success = true, data = result.Data });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching attendance history");
                return StatusCode(500, new { success = false, message = "An error occurred while fetching history" });
            }
        }
    }
}
