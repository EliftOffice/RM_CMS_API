using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RM_CMS.Modules.Identity.Domain;
using RM_CMS.Modules.MessageTemplates.Domain;
using RM_CMS.Modules.MessageTemplates.Services;
using RM_CMS.Security;
using RM_CMS.Utilities;

namespace RM_CMS.Modules.MessageTemplates.Api
{
    /// <summary>
    /// The wording of every Telegram message the church sends.
    /// </summary>
    /// <remarks>
    /// Administrators only. Editing these changes what arrives on a volunteer's phone
    /// in the middle of a crisis escalation, and the safeguarding line in an escalation
    /// template is not a sentence to leave editable by everyone who can open the care
    /// screens.
    ///
    /// There is no delete of a scenario, only <c>reset</c> — the list of messages is
    /// decided by the code that sends them, and removing one from this screen would
    /// mean a message with no wording at all.
    /// </remarks>
    [ApiController]
    [Route("api/admin/telegram-templates")]
    [Produces("application/json")]
    [Authorize(Policy = PolicyNames.AdminOnly)]
    public sealed class TelegramTemplatesController : ControllerBase
    {
        private readonly ITemplateService _templates;

        public TelegramTemplatesController(ITemplateService templates) => _templates = templates;

        /// <summary>
        /// Every scenario, its current wording, its placeholders and a filled-in preview.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<TemplateDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> List() => Ok(await _templates.ListAsync());

        [HttpGet("{code}")]
        [ProducesResponseType(typeof(ApiResponse<TemplateDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Get(string code)
        {
            if (!TelegramTemplates.IsKnown(code)) return BadRequest(Invalid());

            return Ok(await _templates.GetAsync(code));
        }

        /// <summary>
        /// Replaces the wording for one scenario.
        /// </summary>
        /// <remarks>
        /// Refused when it uses a placeholder this scenario does not have, so a typed
        /// <c>{{Name}}</c> is caught here rather than arriving as a gap in somebody's
        /// message.
        /// </remarks>
        [HttpPut("{code}")]
        [EnableRateLimiting(RateLimitPolicies.Sensitive)]
        [ProducesResponseType(typeof(ApiResponse<TemplateDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<ApiResponse<TemplateDto>>> Save(
            string code, [FromBody] SaveTemplateRequest request)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            if (!TelegramTemplates.IsKnown(code)) return BadRequest(Invalid());

            return HttpResponseHelper.CreateHttpResponse(await _templates.SaveAsync(code, request));
        }

        /// <summary>Puts a scenario back to the wording the application ships with.</summary>
        [HttpDelete("{code}")]
        [EnableRateLimiting(RateLimitPolicies.Sensitive)]
        [ProducesResponseType(typeof(ApiResponse<TemplateDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<ApiResponse<TemplateDto>>> Reset(string code)
        {
            if (!TelegramTemplates.IsKnown(code)) return BadRequest(Invalid());

            return HttpResponseHelper.CreateHttpResponse(await _templates.ResetAsync(code));
        }

        private static ApiResponse<TemplateDto> Invalid() =>
            new(ResponseType.Warning, "There is no message with that name.", default!);
    }
}
