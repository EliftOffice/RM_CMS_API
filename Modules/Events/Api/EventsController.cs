using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RM_CMS.Modules.Events.Domain;
using RM_CMS.Modules.Events.Services;
using RM_CMS.Modules.Identity.Domain;
using RM_CMS.Security;
using RM_CMS.Utilities;

namespace RM_CMS.Modules.Events.Api
{
    /// <summary>
    /// Event management.
    ///
    /// Administrators, pastors and the website coordinator. Pastors because the church
    /// calendar is theirs; the coordinator because publishing to the public site is the
    /// job that role exists for.
    /// </summary>
    [ApiController]
    [Route("api/events")]
    [Produces("application/json")]
    [Authorize(Policy = PolicyNames.CanManageEvents)]
    public sealed class EventsController : ControllerBase
    {
        private readonly IEventService _events;

        public EventsController(IEventService events) => _events = events;

        /// <summary>Everything, drafts included, soonest first.</summary>
        [HttpGet]
        [ProducesResponseType(typeof(ApiResponse<EventListDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> List(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            [FromQuery] string? status = null,
            [FromQuery] string? search = null,
            [FromQuery] bool includePast = true) =>
            Ok(await _events.ListAsync(page, pageSize, status, search, includePast));

        [HttpGet("{id}")]
        [ProducesResponseType(typeof(ApiResponse<EventDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Get(string id)
        {
            if (!Ulid.IsValid(id))
                return BadRequest(new ApiResponse<EventDto>(
                    ResponseType.Warning, "Invalid event id.", default!));

            return Ok(await _events.GetAsync(id));
        }

        /// <summary>Creates a DRAFT. Publishing is a separate, deliberate action.</summary>
        [HttpPost]
        [EnableRateLimiting(RateLimitPolicies.Sensitive)]
        [ProducesResponseType(typeof(ApiResponse<EventDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Create([FromBody] CreateEventRequest request)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            return Ok(await _events.CreateAsync(request));
        }

        [HttpPut("{id}")]
        [EnableRateLimiting(RateLimitPolicies.Sensitive)]
        [ProducesResponseType(typeof(ApiResponse<EventDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Update(string id, [FromBody] UpdateEventRequest request)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            if (!Ulid.IsValid(id))
                return BadRequest(new ApiResponse<EventDto>(
                    ResponseType.Warning, "Invalid event id.", default!));

            return Ok(await _events.UpdateAsync(id, request));
        }

        /// <summary>Publish, take back to draft, or cancel.</summary>
        [HttpPut("{id}/status")]
        [EnableRateLimiting(RateLimitPolicies.Sensitive)]
        [ProducesResponseType(typeof(ApiResponse<EventDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> SetStatus(string id, [FromBody] EventStatusRequest request)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            if (!Ulid.IsValid(id))
                return BadRequest(new ApiResponse<EventDto>(
                    ResponseType.Warning, "Invalid event id.", default!));

            return Ok(await _events.SetStatusAsync(id, request));
        }

        /// <summary>
        /// Uploads the poster for an event, replacing whatever was there.
        ///
        /// A multipart file. This route replaced <c>GET /api/events/images</c> and the
        /// dropdown it fed: that list could only offer stock plates the website already
        /// shipped, so the poster actually designed for the event was never one of the
        /// options.
        ///
        /// One poster per event, so there is nothing to choose between and no list to
        /// manage. What the file may be is decided by reading its bytes, not by
        /// trusting its name or the Content-Type the browser attached.
        /// </summary>
        [HttpPost("{id}/poster")]
        [EnableRateLimiting(RateLimitPolicies.Sensitive)]
        [RequestSizeLimit(PosterImages.MaxBytes + 1048576)]
        [ProducesResponseType(typeof(ApiResponse<EventDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> UploadPoster(string id, IFormFile? file)
        {
            if (!Ulid.IsValid(id))
                return BadRequest(new ApiResponse<EventDto>(
                    ResponseType.Warning, "Invalid event id.", default!));

            if (file is null || file.Length == 0)
                return Ok(new ApiResponse<EventDto>(
                    ResponseType.Warning, "Choose an image to upload.", default!));

            await using var stream = file.OpenReadStream();

            return Ok(await _events.SavePosterAsync(id, stream, file.Length, file.FileName));
        }

        /// <summary>Takes the picture off an event, leaving the event itself alone.</summary>
        [HttpDelete("{id}/poster")]
        [EnableRateLimiting(RateLimitPolicies.Sensitive)]
        [ProducesResponseType(typeof(ApiResponse<EventDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> RemovePoster(string id)
        {
            if (!Ulid.IsValid(id))
                return BadRequest(new ApiResponse<EventDto>(
                    ResponseType.Warning, "Invalid event id.", default!));

            return Ok(await _events.RemovePosterAsync(id));
        }
    }

    /// <summary>
    /// The published event feed the public website reads.
    ///
    /// Anonymous, like the enquiry endpoint, and read-only. It returns nothing a visitor
    /// could not already see on the site, so there is nothing here to protect — but
    /// drafts never appear, which is the one rule that matters.
    /// </summary>
    [ApiController]
    [Route("api/public/events")]
    [AllowAnonymous]
    public sealed class PublicEventsController : ControllerBase
    {
        /// <summary>How long a browser may keep a poster. A day.</summary>
        private const int PosterCacheSeconds = 86400;

        private readonly IEventService _events;

        public PublicEventsController(IEventService events) => _events = events;

        /// <summary>
        /// Every published event, oldest first. The website splits them into upcoming and
        /// past itself, because it already does that against its own clock and a visitor
        /// in a different zone should see the same answer either way.
        /// </summary>
        // Declared per action rather than on the controller: the poster endpoint
        // below returns an image, so a class-level [Produces("application/json")]
        // would be a lie about half of this controller.
        [HttpGet]
        [Produces("application/json")]
        [EnableRateLimiting(RateLimitPolicies.PublicForm)]
        [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<PublicEventDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> List() => Ok(await _events.ListPublicAsync());

        /// <summary>
        /// One event's poster.
        ///
        /// Keyed by the event's ULID rather than its slug, so the admin screen can
        /// preview a DRAFT's poster without the draft itself becoming reachable. A
        /// 26-character random id is not guessable, and a poster is a picture the
        /// church intends to print on a flyer — there is nothing here to protect.
        /// </summary>
        [HttpGet("{id}/poster")]
        [EnableRateLimiting(RateLimitPolicies.PublicForm)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Poster(string id)
        {
            if (!Ulid.IsValid(id)) return NotFound();

            var poster = await _events.GetPosterAsync(id);

            if (poster is null) return NotFound();

            // The website embeds this from a different origin, and the API's default
            // Cross-Origin-Resource-Policy is same-origin — which makes the browser
            // drop the image with no console message explaining why. The security
            // headers middleware leaves a policy that is already set alone, so this
            // opt-out lives here, next to the reason for it.
            Response.Headers["Cross-Origin-Resource-Policy"] = "cross-origin";

            // Immutable is safe because the URL carries the upload's timestamp: a
            // replaced poster is a different URL, so nothing has to expire for the new
            // picture to appear.
            Response.Headers.CacheControl = $"public, max-age={PosterCacheSeconds}, immutable";

            Response.Headers.LastModified = poster.UpdatedAt
                .ToUniversalTime()
                .ToString("R", CultureInfo.InvariantCulture);

            // The content type is the one the server decided by reading the bytes,
            // never the one the upload announced. Echoing the caller's value back is
            // how an uploaded file becomes a page running on this origin.
            return File(poster.Bytes, poster.ContentType);
        }
    }
}
