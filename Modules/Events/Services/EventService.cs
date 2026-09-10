using System.Globalization;
using RM_CMS.Modules.Events.Api;
using RM_CMS.Modules.Events.Data;
using RM_CMS.Modules.Events.Domain;
using RM_CMS.Modules.Identity.Data;
using RM_CMS.Modules.Identity.Domain;
using RM_CMS.Modules.Identity.Services;
using RM_CMS.Utilities;

namespace RM_CMS.Modules.Events.Services
{
    /// <summary>
    /// Church events: the admin screens that manage them, and the feed the public
    /// website reads.
    ///
    /// The interesting problem here is TIME. An event is "8am on Good Friday at the
    /// Ongole campus" — a wall-clock time in a place. The database stores UTC like
    /// everything else, the editor types local time, and the website needs an ISO
    /// string carrying a real offset so a browser anywhere renders 8am as 8am. Every
    /// conversion between those three goes through this class, and nowhere else.
    /// </summary>
    public interface IEventService
    {
        Task<ApiResponse<EventListDto>> ListAsync(
            int page, int pageSize, string? status, string? search, bool includePast);

        Task<ApiResponse<EventDto>> GetAsync(string publicId);
        Task<ApiResponse<EventDto>> CreateAsync(CreateEventRequest request);
        Task<ApiResponse<EventDto>> UpdateAsync(string publicId, UpdateEventRequest request);
        Task<ApiResponse<EventDto>> SetStatusAsync(string publicId, EventStatusRequest request);

        /// <summary>
        /// Accepts a poster for an event, replacing whatever was there.
        ///
        /// Separate from the create and update requests because a file cannot travel
        /// in a JSON body, and because on create there is no id to attach an upload
        /// to until the event exists.
        /// </summary>
        Task<ApiResponse<EventDto>> SavePosterAsync(
            string publicId, Stream content, long declaredLength, string? fileName);

        Task<ApiResponse<EventDto>> RemovePosterAsync(string publicId);

        /// <summary>The bytes, for the endpoint that serves them. Null when there is none.</summary>
        Task<EventPoster?> GetPosterAsync(string publicId);

        /// <summary>Published events for the public website. Anonymous.</summary>
        Task<ApiResponse<IReadOnlyList<PublicEventDto>>> ListPublicAsync();
    }

    public sealed class EventService : IEventService
    {
        private const string FallbackTimezone = "Asia/Kolkata";

        private readonly IEventRepository _events;
        private readonly IUserAccountRepository _accounts;
        private readonly ICurrentIdentity _current;
        private readonly IHttpContextAccessor _http;
        private readonly TimeProvider _clock;
        private readonly ILogger<EventService> _logger;

        public EventService(
            IEventRepository events,
            IUserAccountRepository accounts,
            ICurrentIdentity current,
            IHttpContextAccessor http,
            TimeProvider clock,
            ILogger<EventService> logger)
        {
            _events = events;
            _accounts = accounts;
            _current = current;
            _http = http;
            _clock = clock;
            _logger = logger;
        }

        // ==================================================================
        // Admin
        // ==================================================================

        public async Task<ApiResponse<EventListDto>> ListAsync(
            int page, int pageSize, string? status, string? search, bool includePast)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);

            var normalised = string.IsNullOrWhiteSpace(status) ? null : status.Trim().ToUpperInvariant();

            if (normalised is not null && !EventStatus.IsKnown(normalised))
                return Warn<EventListDto>($"Unknown status '{status}'.");

            var now = _clock.GetUtcNow().UtcDateTime;

            var query = new EventQuery
            {
                Status = normalised,
                Search = string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
                IncludePast = includePast,
                Skip = (page - 1) * pageSize,
                Take = pageSize
            };

            var rows = await _events.SearchAsync(query, now);
            var total = await _events.CountAsync(query, now);
            var summary = await _events.GetSummaryAsync(now);

            return Ok(new EventListDto
            {
                Page = page,
                PageSize = pageSize,
                TotalCount = total,
                Summary = summary,
                Items = rows.Select(r => ToDto(r, now)).ToList()
            }, $"{total} event(s).");
        }

        public async Task<ApiResponse<EventDto>> GetAsync(string publicId)
        {
            var row = await _events.GetByPublicIdAsync(publicId);

            return row is null
                ? Warn<EventDto>("That event was not found.")
                : Ok(ToDto(row, _clock.GetUtcNow().UtcDateTime), "Event.");
        }

        public async Task<ApiResponse<EventDto>> CreateAsync(CreateEventRequest request)
        {
            var validated = await ValidateAsync(request, existingId: null);

            if (validated.Problem is not null) return Warn<EventDto>(validated.Problem);

            var id = await _events.CreateAsync(new ChurchEvent
            {
                PublicId = Ulid.NewUlid(),
                Slug = validated.Slug!,
                CampusId = validated.CampusId,
                Title = request.Title.Trim(),
                Summary = request.Summary.Trim(),
                Description = Clean(request.Description),
                Venue = request.Venue.Trim(),
                StartsAt = validated.StartsAtUtc,
                EndsAt = validated.EndsAtUtc,
                // Always a draft. Creating and publishing in one step means a half-typed
                // event can reach the public website on a mis-click.
                Status = EventStatus.Draft,
                PublishedAt = null
            }, await ActingUserIdAsync());

            var created = await _events.GetByIdAsync(id);

            _logger.LogInformation(
                "Event {Slug} created by {AccountId}", validated.Slug, _current.AccountId);

            return created is null
                ? Fail<EventDto>("The event was created but could not be read back.")
                : Ok(ToDto(created, _clock.GetUtcNow().UtcDateTime), $"{request.Title.Trim()} saved as a draft.");
        }

        public async Task<ApiResponse<EventDto>> UpdateAsync(string publicId, UpdateEventRequest request)
        {
            var existing = await _events.GetByPublicIdAsync(publicId);

            if (existing is null) return Warn<EventDto>("That event was not found.");

            var validated = await ValidateAsync(request, existing.Id);

            if (validated.Problem is not null) return Warn<EventDto>(validated.Problem);

            existing.Slug = validated.Slug!;
            existing.CampusId = validated.CampusId;
            existing.Title = request.Title.Trim();
            existing.Summary = request.Summary.Trim();
            existing.Description = Clean(request.Description);
            existing.Venue = request.Venue.Trim();
            existing.StartsAt = validated.StartsAtUtc;
            existing.EndsAt = validated.EndsAtUtc;
            existing.RowVersion = request.RowVersion;

            if (!await _events.UpdateAsync(existing, await ActingUserIdAsync()))
                return Warn<EventDto>("This event was changed by someone else. Reload and try again.");

            var fresh = await _events.GetByPublicIdAsync(publicId);

            return Ok(ToDto(fresh!, _clock.GetUtcNow().UtcDateTime), "Saved.");
        }

        public async Task<ApiResponse<EventDto>> SetStatusAsync(string publicId, EventStatusRequest request)
        {
            var existing = await _events.GetByPublicIdAsync(publicId);

            if (existing is null) return Warn<EventDto>("That event was not found.");

            var status = (request.Status ?? string.Empty).Trim().ToUpperInvariant();

            if (!EventStatus.IsKnown(status))
                return Warn<EventDto>($"Unknown status '{request.Status}'.");

            var now = _clock.GetUtcNow().UtcDateTime;

            // published_at records the FIRST time it went live and is not reset by an
            // unpublish-republish cycle — "this has been public since March" stays true.
            var publishedAt = status == EventStatus.Published
                ? existing.PublishedAt ?? now
                : existing.PublishedAt;

            // Back to draft means it is no longer public, so the stamp goes with it.
            if (status == EventStatus.Draft) publishedAt = null;

            if (!await _events.SetStatusAsync(
                    existing.Id, request.RowVersion, status, publishedAt, await ActingUserIdAsync()))
            {
                return Warn<EventDto>("This event was changed by someone else. Reload and try again.");
            }

            _logger.LogInformation(
                "Event {Slug} moved to {Status} by {AccountId}", existing.Slug, status, _current.AccountId);

            var fresh = await _events.GetByPublicIdAsync(publicId);
            return Ok(ToDto(fresh!, now), Describe(status, fresh!.Title));
        }

        // ==================================================================
        // The poster
        //
        // This replaced a dropdown of image slugs from the website's build-time
        // manifest. The manifest holds about twenty stock plates, so the poster
        // actually designed for the event — the one on the flyer and the WhatsApp
        // forward — was never one of the options, and two unrelated events routinely
        // showed the same photograph.
        // ==================================================================

        public async Task<ApiResponse<EventDto>> SavePosterAsync(
            string publicId, Stream content, long declaredLength, string? fileName)
        {
            var existing = await _events.GetByPublicIdAsync(publicId);

            if (existing is null) return Warn<EventDto>("That event was not found.");

            // Checked before reading a byte. The declared length is the caller's claim
            // and is not trusted as the real size — but a claim above the ceiling means
            // there is no point spending the memory to find out.
            if (declaredLength > PosterImages.MaxBytes) return Warn<EventDto>(TooBig());

            byte[] bytes;

            using (var buffer = new MemoryStream())
            {
                // Capped one byte above the ceiling: a stream that keeps going past it
                // is stopped there rather than buffered whole, so a caller who lied
                // about the length cannot spend the server's memory.
                await CopyCappedAsync(content, buffer, PosterImages.MaxBytes + 1);

                if (buffer.Length > PosterImages.MaxBytes) return Warn<EventDto>(TooBig());

                bytes = buffer.ToArray();
            }

            if (bytes.Length < PosterImages.MinBytes)
                return Warn<EventDto>("That file is empty. Choose the poster image again.");

            // The upload's own Content-Type and file extension are both ignored. Both
            // are written by the caller, so a script renamed to poster.jpg would pass
            // either check; the bytes cannot lie about what they are.
            var contentType = PosterImages.Sniff(bytes);

            if (contentType is null)
                return Warn<EventDto>(
                    "That is not an image this system can show. Upload a JPEG, PNG or WebP.");

            var stored = await _events.SavePosterAsync(
                existing.Id, bytes, contentType, SafeFileName(fileName, contentType),
                _clock.GetUtcNow().UtcDateTime, await ActingUserIdAsync());

            if (!stored) return Warn<EventDto>("The poster could not be saved.");

            // No file name in the log line. It is caller-supplied text, and a log is
            // read by more people and in more places than the event screen.
            _logger.LogInformation(
                "Poster for event {Slug} replaced ({Bytes} bytes, {ContentType}) by {AccountId}",
                existing.Slug, bytes.Length, contentType, _current.AccountId);

            var fresh = await _events.GetByPublicIdAsync(publicId);

            return Ok(ToDto(fresh!, _clock.GetUtcNow().UtcDateTime), "Poster uploaded.");
        }

        public async Task<ApiResponse<EventDto>> RemovePosterAsync(string publicId)
        {
            var existing = await _events.GetByPublicIdAsync(publicId);

            if (existing is null) return Warn<EventDto>("That event was not found.");
            if (!existing.HasPoster) return Warn<EventDto>("This event has no poster.");

            if (!await _events.RemovePosterAsync(existing.Id, await ActingUserIdAsync()))
                return Warn<EventDto>("The poster could not be removed.");

            _logger.LogInformation(
                "Poster for event {Slug} removed by {AccountId}", existing.Slug, _current.AccountId);

            var fresh = await _events.GetByPublicIdAsync(publicId);

            return Ok(ToDto(fresh!, _clock.GetUtcNow().UtcDateTime), "Poster removed.");
        }

        public Task<EventPoster?> GetPosterAsync(string publicId) => _events.GetPosterAsync(publicId);

        /// <summary>
        /// Copies at most <paramref name="limit"/> bytes, so an oversized or endless
        /// upload cannot be buffered whole before anyone notices its size.
        /// </summary>
        private static async Task CopyCappedAsync(Stream source, Stream destination, long limit)
        {
            var buffer = new byte[81920];
            long total = 0;

            while (total < limit)
            {
                var wanted = (int)Math.Min(buffer.Length, limit - total);
                var read = await source.ReadAsync(buffer.AsMemory(0, wanted));

                if (read == 0) break;

                await destination.WriteAsync(buffer.AsMemory(0, read));
                total += read;
            }
        }

        /// <summary>
        /// The uploaded name, kept only as a label beside the preview.
        ///
        /// Stripped to its own last segment and re-extended from the format the bytes
        /// turned out to be. It is never used to build a path or a header, so this is
        /// belt and braces — but a stored name like ../../web.config is one careless
        /// later use away from being a problem.
        /// </summary>
        private static string? SafeFileName(string? fileName, string contentType)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return null;

            var leaf = Path.GetFileNameWithoutExtension(fileName.Replace('\\', '/'));

            leaf = new string(leaf
                .Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or ' ')
                .ToArray()).Trim();

            if (leaf.Length == 0) return null;
            if (leaf.Length > 150) leaf = leaf[..150];

            return leaf + PosterImages.Extension(contentType);
        }

        private static string TooBig() =>
            $"That image is too large. The limit is {PosterImages.MaxBytes / (1024 * 1024)} MB.";

        /// <summary>
        /// Where this server serves the event's poster from, absolute.
        ///
        /// ABSOLUTE because the website runs on another origin entirely — a path would
        /// resolve against the site and 404 — and built from the REQUEST rather than
        /// configuration so it is right in development, in a preview deployment and in
        /// production without three settings to keep in step.
        ///
        /// The timestamp on the end is a cache buster. The URL is otherwise stable for
        /// the life of the event, so a replaced poster would keep showing the old
        /// picture until every visitor's cache expired.
        /// </summary>
        private string? PosterUrl(ChurchEvent e)
        {
            if (!e.HasPoster) return null;

            var request = _http.HttpContext?.Request;

            var origin = request is null ? string.Empty : $"{request.Scheme}://{request.Host}";

            return $"{origin}/api/public/events/{e.PublicId}/poster?v={e.PosterUpdatedAt?.Ticks ?? 0}";
        }

        // ==================================================================
        // Public feed
        // ==================================================================

        public async Task<ApiResponse<IReadOnlyList<PublicEventDto>>> ListPublicAsync()
        {
            var rows = await _events.ListPublishedAsync();

            var items = rows.Select(ToPublicDto).ToList();

            return new ApiResponse<IReadOnlyList<PublicEventDto>>(
                ResponseType.Success, $"{items.Count} event(s).", items);
        }

        // ==================================================================
        // Validation
        // ==================================================================

        private sealed record Validated(
            string? Problem, string? Slug, long? CampusId, DateTime StartsAtUtc, DateTime? EndsAtUtc);

        private async Task<Validated> ValidateAsync(CreateEventRequest request, long? existingId)
        {
            // ---- campus, and therefore the zone the typed times mean ----
            long? campusId = null;

            if (!string.IsNullOrWhiteSpace(request.CampusId))
            {
                campusId = await _events.ResolveCampusIdAsync(request.CampusId);

                if (campusId is null) return Problem("That campus was not found.");
            }

            var timezone = await ResolveTimezoneAsync(request.CampusId);

            // ---- times ----
            if (!TryParseLocal(request.StartsAt, timezone, out var startsUtc))
                return Problem("The start date and time could not be read.");

            DateTime? endsUtc = null;

            if (!string.IsNullOrWhiteSpace(request.EndsAt))
            {
                if (!TryParseLocal(request.EndsAt!, timezone, out var parsedEnd))
                    return Problem("The end date and time could not be read.");

                // Checked here as well as by the CHECK constraint, so the editor gets a
                // sentence rather than a database error.
                if (parsedEnd < startsUtc)
                    return Problem("The event cannot end before it starts.");

                endsUtc = parsedEnd;
            }

            // The poster is not validated here. It is not part of this request at
            // all — it is a file, uploaded separately, and what it may be is decided
            // by reading its bytes in SavePosterAsync.

            // ---- slug ----
            var slug = string.IsNullOrWhiteSpace(request.Slug)
                ? EventSlugs.From(request.Title)
                : EventSlugs.From(request.Slug);

            if (string.IsNullOrEmpty(slug))
            {
                // A title with no Latin letters or digits — a Telugu title, for instance —
                // produces an empty slug. Silently storing "" would collide with the next
                // one and break /events/.
                return Problem(
                    "A web address could not be made from that title. Enter one in the address field, using letters and numbers.");
            }

            if (await _events.SlugExistsAsync(slug, existingId))
                return Problem($"Another event already uses the web address '{slug}'. Choose a different one.");

            return new Validated(null, slug, campusId, startsUtc, endsUtc);

            static Validated Problem(string message) =>
                new(message, null, null, default, null);
        }

        /// <summary>
        /// Reads a local wall-clock string and converts it to UTC using the campus zone.
        ///
        /// Accepts what a <c>datetime-local</c> input produces ("2027-04-03T08:00"), and
        /// tolerates an offset if one is supplied. <see cref="DateTimeKind.Unspecified"/>
        /// is what makes this correct: the value means 8am AT THE CAMPUS, so it must not
        /// be treated as UTC or as the server's own zone.
        /// </summary>
        private static bool TryParseLocal(string value, TimeZoneInfo zone, out DateTime utc)
        {
            utc = default;

            if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var withOffset) &&
                (value.Contains('+') || value.EndsWith('Z') || value.LastIndexOf('-') > 8))
            {
                utc = withOffset.UtcDateTime;
                return true;
            }

            if (!DateTime.TryParse(value, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var local))
            {
                return false;
            }

            local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);

            // A time that does not exist locally (the hour a clock skips forward) would
            // throw. India has no daylight saving, but the church may add a campus that
            // does, and a 500 on a date field is a poor way to find out.
            if (zone.IsInvalidTime(local)) return false;

            utc = TimeZoneInfo.ConvertTimeToUtc(local, zone);
            return true;
        }

        /// <summary>
        /// The zone a typed local time is expressed in — the campus's, or the
        /// organisation default for an event that belongs to every campus.
        /// </summary>
        private async Task<TimeZoneInfo> ResolveTimezoneAsync(string? campusPublicId) =>
            Zone(await _events.GetTimezoneAsync(campusPublicId));

        private static TimeZoneInfo Zone(string? id)
        {
            if (!string.IsNullOrWhiteSpace(id) &&
                TimeZoneInfo.TryFindSystemTimeZoneById(id, out var found))
            {
                return found;
            }

            return TimeZoneInfo.TryFindSystemTimeZoneById(FallbackTimezone, out var fallback)
                ? fallback
                : TimeZoneInfo.Utc;
        }

        // ==================================================================
        // Mapping
        // ==================================================================

        private EventDto ToDto(ChurchEvent e, DateTime nowUtc)
        {
            var zone = Zone(e.CampusTimezone);
            var posterUrl = PosterUrl(e);

            return new EventDto
            {
                Id = e.PublicId,
                Slug = e.Slug,
                Title = e.Title,
                Summary = e.Summary,
                Description = e.Description,
                Venue = e.Venue,
                StartsAtLocal = ToLocalInput(e.StartsAt, zone),
                EndsAtLocal = e.EndsAt is null ? null : ToLocalInput(e.EndsAt.Value, zone),
                StartsAtUtc = e.StartsAt,
                EndsAtUtc = e.EndsAt,
                CampusId = e.CampusPublicId,
                CampusName = e.CampusName,
                Timezone = zone.Id,
                HasPoster = e.HasPoster,
                PosterUrl = posterUrl,
                PosterFileName = e.PosterFileName,
                PosterByteSize = e.PosterByteSize,
                Status = e.Status,
                PublishedAt = e.PublishedAt,
                HasFinished = e.HasFinished(nowUtc),
                CreatedAt = e.CreatedAt,
                CreatedByName = e.CreatedByName,
                RowVersion = e.RowVersion
            };
        }

        /// <summary>"2027-04-03T08:00" — exactly what a datetime-local input wants back.</summary>
        private static string ToLocalInput(DateTime utc, TimeZoneInfo zone) =>
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone)
                .ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture);

        private PublicEventDto ToPublicDto(ChurchEvent e)
        {
            var zone = Zone(e.CampusTimezone);

            return new PublicEventDto
            {
                Slug = e.Slug,
                Title = e.Title,
                Date = ToOffsetIso(e.StartsAt, zone),
                EndDate = e.EndsAt is null ? null : ToOffsetIso(e.EndsAt.Value, zone),
                Venue = e.Venue,
                Summary = e.Summary,
                Description = SplitParagraphs(e.Description),
                Image = PosterUrl(e),
                Cancelled = e.Status == EventStatus.Cancelled
            };
        }

        /// <summary>
        /// "2027-04-03T08:00:00+05:30".
        ///
        /// The offset is what makes this right. Sending a bare UTC stamp would leave the
        /// browser to apply the VISITOR's zone, so an 8am service in Ongole would read as
        /// 2:30am to someone opening the page in London — and as the wrong DAY either
        /// side of midnight.
        /// </summary>
        private static string ToOffsetIso(DateTime utc, TimeZoneInfo zone)
        {
            var asUtc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            var offset = zone.GetUtcOffset(asUtc);
            var local = new DateTimeOffset(asUtc).ToOffset(offset);

            return local.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Blank-line separated text into paragraphs, which is the shape the website's
        /// ChurchEvent.description expects. Null rather than an empty array when there is
        /// nothing, so the site's `description &amp;&amp; ...` check behaves.
        /// </summary>
        private static IReadOnlyList<string>? SplitParagraphs(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            var parts = text
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(p => p.Length > 0)
                .ToList();

            return parts.Count == 0 ? null : parts;
        }

        private static string Describe(string status, string title) => status switch
        {
            EventStatus.Published => $"{title} is now live on the website.",
            EventStatus.Draft     => $"{title} has been taken off the website.",
            EventStatus.Cancelled => $"{title} is marked as cancelled.",
            _                     => "Saved."
        };

        private static string? Clean(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private async Task<long?> ActingUserIdAsync()
        {
            if (string.IsNullOrWhiteSpace(_current.AccountId)) return null;

            return (await _accounts.GetByPublicIdAsync(_current.AccountId))?.Id;
        }

        private static ApiResponse<T> Ok<T>(T data, string message) => new(ResponseType.Success, message, data);
        private static ApiResponse<T> Warn<T>(string message) => new(ResponseType.Warning, message, default!);
        private static ApiResponse<T> Fail<T>(string message) => new(ResponseType.Error, message, default!);
    }
}
