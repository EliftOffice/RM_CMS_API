using Microsoft.AspNetCore.Mvc;
using RM_CMS.BLL.Users;
using RM_CMS.Data.DTO.Users;
using RM_CMS.Utilities;

namespace RM_CMS.Controllers.Users
{
    [ApiController]
    [Route("api/user")]
    public class UserController : ControllerBase
    {
        private readonly IUsersBLL _usersBLL;
        private readonly ILogger<UserController> _logger;

        public UserController(IUsersBLL usersBLL, ILogger<UserController> logger)
        {
            _usersBLL = usersBLL;
            _logger = logger;
        }

        [HttpPost("check")]
        public async Task<IActionResult> Check([FromBody] UserCheckRequest request)
        {
            try
            {
                _logger.LogInformation("Checking user with mobile: {Mobile}", request?.MobileNumber);

                if (request == null || string.IsNullOrWhiteSpace(request.MobileNumber))
                {
                    return BadRequest(new { success = false, message = "Mobile number is required" });
                }

                var result = await _usersBLL.CheckUserAsync(request.MobileNumber);

                if (result.ResponseType == ResponseType.Success && result.Data != null)
                {
                    return Ok(new { success = true, exists = true, data = result.Data });
                }

                return Ok(new { success = true, exists = false, data = (object)null });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking user");
                return StatusCode(500, new { success = false, message = "An error occurred while checking user" });
            }
        }

        [HttpPost("register")]
        public async Task<ActionResult<ApiResponse<UserDto>>> Register([FromForm] UserRegisterRequest request)
        {
            try
            {
                _logger.LogInformation("Registering user: {Name}", request?.Name);

                if (request == null || string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.MobileNumber))
                {
                    return BadRequest(new ApiResponse<UserDto>(ResponseType.Warning, "Name and mobile number are required", null));
                }

                var result = await _usersBLL.RegisterUserAsync(request.Name, request.MobileNumber);

                return HttpResponseHelper.CreateHttpResponse(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error registering user");
                return StatusCode(500, new ApiResponse<UserDto>(ResponseType.Error, "An error occurred while registering user", null));
            }
        }
    }
}
