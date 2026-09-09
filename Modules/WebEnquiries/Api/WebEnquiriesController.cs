using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RM_CMS.Modules.Identity.Domain;
using RM_CMS.Modules.WebEnquiries.Domain;
using RM_CMS.Modules.WebEnquiries.Services;
using RM_CMS.Security;
using RM_CMS.Utilities;

namespace RM_CMS.Modules.WebEnquiries.Api
{
    /// <summary>
    /// The public website's only way in.
    ///
    /// This is the single anonymous WRITE endpoint in the application, so it is
    /// deliberately narrow: one route, one shape, no ids to guess and nothing readable.
    /// A caller can add to the queue and learn nothing else — there is no GET here, so
    /// the form cannot be turned into a way of asking whether a given person attends
    /// this church.
    ///
    /// CORS decides which sites may call it: <c>Auth:AllowedOrigins</c> must list the
    /// website's origin, or the browser refuses the request before it arrives.
    /// </summary>
    [ApiController]
    [Route("api/public/enquiries")]
    [Produces("application/json")]
    [AllowAnonymous]
    public sealed class PublicEnquiriesController : ControllerBase
    {
        private readonly IWebEnquiryService _enquiries;

        public PublicEnquiriesController(IWebEnquiryService enquiries) => _enquiries = enquiries;

        /// <summary>
        /// Records a website form submission. Always returns 200 with an
        /// <c>ApiResponse</c> — a refusal is <c>responseType 1</c> and a message the
        /// site can show beside the form.
        /// </summary>
        [HttpPost]
        [EnableRateLimiting(RateLimitPolicies.PublicForm)]
        [ProducesResponseType(typeof(ApiResponse<EnquiryReceiptDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Submit([FromBody] SubmitEnquiryRequest request)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            return Ok(await _enquiries.SubmitAsync(request, ClientIp(), UserAgent()));
        }

        /// <summary>
        /// The forms this server will accept, so the website can be checked against the
        /// API rather than the two drifting apart silently. Anonymous, and it discloses
        /// nothing but a list of constants.
        /// </summary>
        [HttpGet("form-types")]
        [EnableRateLimiting(RateLimitPolicies.PublicForm)]
        [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<object>>), StatusCodes.Status200OK)]
        public IActionResult FormTypes()
        {
            var types = WebFormTypes.All
                .Select(code => new
                {
                    code,
                    label = WebFormTypes.Label(code),
                    requiresContact = WebFormTypes.RequiresContact(code)
                })
                .ToList();

            return Ok(new ApiResponse<IReadOnlyList<object>>(
                ResponseType.Success, $"{types.Count} form types.", types));
        }

        /// <summary>
        /// The caller's address, preferring the proxy's forwarded header.
        ///
        /// Behind Coolify every request arrives from the proxy, so
        /// <c>RemoteIpAddress</c> alone would give every visitor the same value and the
        /// per-submitter throttle would treat the whole internet as one person.
        ///
        /// X-Forwarded-For is client-controlled and trivially spoofed, so this is NOT
        /// used for authorization — only to spread the throttle across callers and to
        /// group a flood. Taking the FIRST entry is the convention; a spoofed value
        /// only ever splits an attacker into more buckets, never merges honest callers.
        /// </summary>
        private string? ClientIp()
        {
            var forwarded = Request.Headers["X-Forwarded-For"].FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(forwarded))
            {
                var first = forwarded.Split(',')[0].Trim();
                if (first.Length is > 0 and <= 64) return first;
            }

            return HttpContext.Connection.RemoteIpAddress?.ToString();
        }

        private string? UserAgent() => Request.Headers.UserAgent.FirstOrDefault();
    }

    /// <summary>
    /// The website coordinator's queue.
    ///
    /// Split from the public controller because the audiences could not be further
    /// apart: one is anonymous and may only write, the other is a named account reading
    /// whatever the internet typed. Keeping them in one controller would put a single
    /// attribute between a stranger and the whole list.
    /// </summary>
    [ApiController]
    [Route("api/web-enquiries")]
    [Produces("application/json")]
    [Authorize(Policy = PolicyNames.CanReviewWebEnquiries)]
    public sealed class WebEnquiriesController : ControllerBase
    {
        private readonly IWebEnquiryService _enquiries;

        public WebEnquiriesController(IWebEnquiryService enquiries) => _enquiries = enquiries;

        /// <summary>The queue: unhandled first, then newest.</summary>
        [HttpGet]
        [ProducesResponseType(typeof(ApiResponse<WebEnquiryListDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> List(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            [FromQuery] string? status = null,
            [FromQuery] string? formType = null,
            [FromQuery] string? search = null,
            [FromQuery] bool includeSpam = false) =>
            Ok(await _enquiries.ListAsync(page, pageSize, status, formType, search, includeSpam));

        [HttpGet("{id}")]
        [ProducesResponseType(typeof(ApiResponse<WebEnquiryDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Get(string id)
        {
            if (!Ulid.IsValid(id))
                return BadRequest(new ApiResponse<WebEnquiryDto>(
                    ResponseType.Warning, "Invalid enquiry id.", default!));

            return Ok(await _enquiries.GetAsync(id));
        }

        /// <summary>Picks one up, marks it done, or files it as junk.</summary>
        [HttpPut("{id}/status")]
        [EnableRateLimiting(RateLimitPolicies.Sensitive)]
        [ProducesResponseType(typeof(ApiResponse<WebEnquiryDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> UpdateStatus(
            string id, [FromBody] UpdateEnquiryStatusRequest request)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            if (!Ulid.IsValid(id))
                return BadRequest(new ApiResponse<WebEnquiryDto>(
                    ResponseType.Warning, "Invalid enquiry id.", default!));

            return Ok(await _enquiries.UpdateStatusAsync(id, request));
        }
    }
}
