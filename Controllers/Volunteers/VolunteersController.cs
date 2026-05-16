using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RM_CMS.BLL.Peoples;
using RM_CMS.BLL.Volunteers;
using RM_CMS.DAL.CommonDAL;
using RM_CMS.Data.DTO;
using RM_CMS.Data.DTO.Volunteers;
using RM_CMS.Data.Models;
using RM_CMS.Utilities;

namespace RM_CMS.Controllers.Volunteers
{
    [Route("api/[controller]")]
    [ApiController]
    public class VolunteersController : ControllerBase
    {
        private readonly IVolunteersBLL _VolunteersBLL;
        private readonly ILogger<VolunteersController> _logger;
        private readonly ITelegram _telegram;
        public VolunteersController(IVolunteersBLL volunteersBLL, ILogger<VolunteersController> logger,ITelegram tel)
        {
            _VolunteersBLL = volunteersBLL;
            _logger = logger;
            _telegram = tel;
        }

        [HttpPost("assign-volunteer")]
        public async Task<ActionResult<ApiResponse<AssignedVolunteerDTO>>> AssignToVolunteer([FromQuery] string personId)
        {
            try
            {
                _logger.LogInformation("Assigning volunteer for PersonId: {PersonId}", personId);

                var result = await _VolunteersBLL.AssignToVolunteerAsync(personId);

                return HttpResponseHelper.CreateHttpResponse(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error assigning volunteer");

                return HttpResponseHelper.CreateHttpResponse(new ApiResponse<AssignedVolunteerDTO>(
                    ResponseType.Error,
                    "An error occurred while assigning volunteer",
                    new AssignedVolunteerDTO()
                ));
            }
        }

        [HttpGet("/api/volunteers/{id}")]
        public async Task<ActionResult<ApiResponse<Volunteer>>> GetVolunteerByIdAsync(string id)
        {
            try
            {
                var result = await _VolunteersBLL.GetVolunteerByIdAsync(id);
                return HttpResponseHelper.CreateHttpResponse(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving volunteer by ID");

                return StatusCode(500, new ApiResponse<Volunteer>(
                    ResponseType.Error,
                    "An error occurred while retrieving volunteer",
                    new Volunteer()
                ));
            }
        }

        [HttpGet("/api/volunteers/GetVolunteersAsync")]
        public async Task<ActionResult<ApiResponse<List<Volunteer>>>> GetVolunteersAsync()
        {
            try
            {
                var result = await _VolunteersBLL.GetVolunteersAsync();
                return HttpResponseHelper.CreateHttpResponse(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving volunteers");

                return StatusCode(500, new ApiResponse<List<Volunteer>>(
                    ResponseType.Error,
                    "An error occurred while retrieving volunteers",
                    new List<Volunteer>()
                ));
            }
        }
        [HttpGet("/api/volunteers/{id}/assignments")]
        public async Task<ActionResult<ApiResponse<List<People>>>> GetVolunteerAssignmentsAsync(string id)
        {
            try
            {
                var result = await _VolunteersBLL.GetVolunteerAssignmentsAsync(id);
                return HttpResponseHelper.CreateHttpResponse(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving volunteer assignments");

                return StatusCode(500, new ApiResponse<List<People>>(
                    ResponseType.Error,
                    "An error occurred while retrieving assignments",
                    new List<People>()
                ));
            }
        }

        [HttpPost("/api/volunteers")]
        public async Task<ActionResult<ApiResponse<VolunteerResponseDto>>> CreateVolunteerAsync([FromBody] CreateVolunteerDto dto)
        {
            try
            {
                var result = await _VolunteersBLL.CreateVolunteerAsync(dto);

                return HttpResponseHelper.CreateHttpResponse(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating volunteer");

                return StatusCode(500, new ApiResponse<VolunteerResponseDto>(
                    ResponseType.Error,
                    "An error occurred while creating volunteer",
                    null
                ));
            }
        }

        [HttpGet("/api/volunteers/mobile/{mobile}")]
        public async Task<ActionResult<ApiResponse<List<VolunteerLookupDto>>>> GetVolunteersByMobileAsync(string mobile)
        {
            try
            {
                var result = await _VolunteersBLL.GetVolunteersByMobileAsync(mobile);
                return HttpResponseHelper.CreateHttpResponse(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving volunteer by mobile");

                return StatusCode(500, new ApiResponse<List<VolunteerLookupDto>>(
                    ResponseType.Error,
                    "An error occurred while retrieving volunteer by mobile",
                    new List<VolunteerLookupDto>()
                ));
            }
        }

        [HttpGet("/api/volunteers/GetVolunteersByMobileAsyncV1/{mobile}")]
        public async Task<ActionResult<ApiResponse<List<UserLookupDto>>>> GetVolunteersByMobileAsyncV1(string mobile)
        {
            try
            {
                var result = await _VolunteersBLL.GetVolunteersByMobileAsyncV1(mobile);
                return HttpResponseHelper.CreateHttpResponse(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving volunteer by mobile");

                return StatusCode(500, new ApiResponse<List<UserLookupDto>>(
                    ResponseType.Error,
                    "An error occurred while retrieving volunteer by mobile",
                    new List<UserLookupDto>()
                ));
            }
        }

        [HttpPut("update-mobile")]
        public async Task<ActionResult<ApiResponse<string>>> UpdateVolunteerMobileAsync([FromBody] UpdateVolunteerMobileDto dto)
        {
            try
            {
                // 🔹 Log correct identifiers
                _logger.LogInformation(
                    "Updating mobile for VolunteerId: {VolunteerId}",
                    dto?.VolunteerId
                );

                // 🔹 Call BLL
                var result = await _VolunteersBLL.UpdateVolunteerMobileAsync(dto);

                // 🔹 Standard response
                return HttpResponseHelper.CreateHttpResponse(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating volunteer mobile");

                return StatusCode(500, new ApiResponse<string>(
                    ResponseType.Error,
                    "An error occurred while updating mobile number",
                    null
                ));
            }
        }

        [HttpGet("get-latest-chat")]
        public async Task<ActionResult<ApiResponse<TelegramChatDto>>> GetLatestTelegramChat()
        {
            try
            {
                var result = await _VolunteersBLL.GetLatestTelegramChatAsync();
                return HttpResponseHelper.CreateHttpResponse(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting latest telegram chat");
                return HttpResponseHelper.CreateHttpResponse(new ApiResponse<TelegramChatDto>(ResponseType.Error, "Error retrieving latest chat", null));
            }
        }

        [HttpGet("get-telegram-bot-url")]
        public async Task<ActionResult<ApiResponse<string>>> GetTelegramBotUrl()
        {
            try
            {
                var result = await _telegram.GetTelegramBotUrl();
                return HttpResponseHelper.CreateHttpResponse(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting latest telegram chat");
                return HttpResponseHelper.CreateHttpResponse(new ApiResponse<string>(ResponseType.Error, "Error retrieving latest chat", null));
            }
        }

        [HttpPut("update-telegram")]
        public async Task<ActionResult<ApiResponse<bool>>> UpdateVolunteerTelegram([FromBody] UpdateVolunteerTelegramDto dto)
        {
            try
            {
                var result = await _VolunteersBLL.UpdateVolunteerTelegramAsync(dto);
                return HttpResponseHelper.CreateHttpResponse(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating telegram id");
                return HttpResponseHelper.CreateHttpResponse(new ApiResponse<bool>(ResponseType.Error, "Error updating telegram id", false));
            }
        }

        [HttpPost("manual-assign")]
        public async Task<ActionResult<ApiResponse<AssignedVolunteerDTO>>> ManualAssign([FromBody] ManualAssignDto dto)
        {
            try
            {
                if (dto == null || string.IsNullOrWhiteSpace(dto.PersonId) || string.IsNullOrWhiteSpace(dto.VolunteerId))
                    return HttpResponseHelper.CreateHttpResponse(new ApiResponse<AssignedVolunteerDTO>(ResponseType.Warning, "PersonId and VolunteerId are required", null));

                var result = await _VolunteersBLL.ManualAssignToVolunteerAsync(dto.PersonId, dto.VolunteerId);
                return HttpResponseHelper.CreateHttpResponse(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Manual assign failed");
                return HttpResponseHelper.CreateHttpResponse(new ApiResponse<AssignedVolunteerDTO>(ResponseType.Error, "Error during manual assign", null));
            }
        }

    }
}
