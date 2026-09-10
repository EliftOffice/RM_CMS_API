using System.ComponentModel.DataAnnotations;

namespace RM_CMS.Modules.Events.Api
{
    // -------------------------------------------------------------------------
    // Requests
    // -------------------------------------------------------------------------

    /// <summary>
    /// Not sealed: <see cref="UpdateEventRequest"/> is exactly this plus a row version,
    /// and duplicating nine properties to keep the seal would guarantee they drift.
    /// </summary>
    public class CreateEventRequest
    {
        [Required(ErrorMessage = "Give the event a title.")]
        [StringLength(160, MinimumLength = 2,
            ErrorMessage = "The title needs to be at least 2 characters, and at most 160.")]
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Optional. Generated from the title when omitted. Supplying one matters for an
        /// event that recurs — "easter-2027" reads better in a link than "easter-2".
        /// </summary>
        [StringLength(120)]
        public string? Slug { get; set; }

        [Required(ErrorMessage = "Write a one-line summary — it is what shows on the card.")]
        [StringLength(400, MinimumLength = 2,
            ErrorMessage = "The summary needs to be at least 2 characters, and at most 400.")]
        public string Summary { get; set; } = string.Empty;

        /// <summary>Blank-line separated paragraphs for the event's own page.</summary>
        [StringLength(8000)]
        public string? Description { get; set; }

        [Required(ErrorMessage = "Say where it is.")]
        [StringLength(200, MinimumLength = 2,
            ErrorMessage = "The venue needs to be at least 2 characters, and at most 200.")]
        public string Venue { get; set; } = string.Empty;

        /// <summary>
        /// Local date and time at the campus, e.g. <c>2027-04-03T08:00</c>. Sent without
        /// an offset on purpose: whoever types it is thinking in local time, and the
        /// server applies the campus zone. An offset here would be the browser's, which
        /// is not necessarily the church's.
        /// </summary>
        [Required(ErrorMessage = "Say when it starts.")]
        public string StartsAt { get; set; } = string.Empty;

        public string? EndsAt { get; set; }

        /// <summary>Public id of the campus. Omit for an event at every campus.</summary>
        [StringLength(26)]
        public string? CampusId { get; set; }

        // The poster is NOT a field here. It is a file, uploaded to
        // POST /api/events/{id}/poster once the event exists — there is no id to
        // attach an upload to before that. It used to be an ImageSlug naming one of
        // the website's built-in stock pictures, which meant the poster actually
        // designed for the event was never one of the choices.
    }

    public sealed class UpdateEventRequest : CreateEventRequest
    {
        /// <summary>From the record being edited, so a concurrent edit is refused.</summary>
        [Required] public int RowVersion { get; set; }
    }

    /// <summary>
    /// Publishes, unpublishes or cancels. Separate from an edit because it changes who
    /// can see the event, which is a different decision from fixing a typo in it.
    /// </summary>
    public sealed class EventStatusRequest
    {
        [Required][StringLength(20)]
        public string Status { get; set; } = string.Empty;

        [Required] public int RowVersion { get; set; }
    }

    // -------------------------------------------------------------------------
    // Admin responses
    // -------------------------------------------------------------------------

    public sealed class EventDto
    {
        public string Id { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string Venue { get; set; } = string.Empty;

        /// <summary>Local time at the campus, ready for a <c>datetime-local</c> input.</summary>
        public string StartsAtLocal { get; set; } = string.Empty;
        public string? EndsAtLocal { get; set; }

        /// <summary>The same instants in UTC, for anything that needs to compare them.</summary>
        public DateTime StartsAtUtc { get; set; }
        public DateTime? EndsAtUtc { get; set; }

        public string? CampusId { get; set; }
        public string? CampusName { get; set; }
        public string Timezone { get; set; } = string.Empty;

        public bool HasPoster { get; set; }

        /// <summary>
        /// Where the poster is served from, or null when there is none. Carries the
        /// upload's timestamp as a query parameter, so replacing a picture shows the
        /// new one at once rather than whatever the browser had cached.
        /// </summary>
        public string? PosterUrl { get; set; }

        /// <summary>The uploaded name and size, shown beside the preview in the editor.</summary>
        public string? PosterFileName { get; set; }
        public int? PosterByteSize { get; set; }

        public string Status { get; set; } = string.Empty;
        public DateTime? PublishedAt { get; set; }

        /// <summary>True once it has finished, so the screen can grey it out.</summary>
        public bool HasFinished { get; set; }

        public DateTime CreatedAt { get; set; }
        public string? CreatedByName { get; set; }
        public int RowVersion { get; set; }
    }

    public sealed class EventListDto
    {
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }

        public Domain.EventSummary Summary { get; set; } = new();
        public IReadOnlyList<EventDto> Items { get; set; } = Array.Empty<EventDto>();
    }

    // -------------------------------------------------------------------------
    // Public response
    //
    // Shaped to the website's own ChurchEvent type so the site can use it without
    // a translation layer. Field names and casing match what React already
    // expects, which is why this is not simply EventDto.
    // -------------------------------------------------------------------------

    public sealed class PublicEventDto
    {
        public string Slug { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;

        /// <summary>ISO 8601 carrying the campus's real offset, e.g. <c>2027-04-03T08:00:00+05:30</c>.</summary>
        public string Date { get; set; } = string.Empty;
        public string? EndDate { get; set; }

        public string Venue { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;

        /// <summary>Paragraphs. Absent when the event has no long description.</summary>
        public IReadOnlyList<string>? Description { get; set; }

        /// <summary>
        /// Absolute URL of the poster the church uploaded, or null when the event has
        /// none. Absolute because the website runs on a different origin — a relative
        /// path would resolve against the site and 404.
        ///
        /// This used to be a slug naming a picture in the website's own build-time
        /// manifest, which is why the site still calls the field `image`.
        /// </summary>
        public string? Image { get; set; }

        /// <summary>True for an event that was advertised and then called off.</summary>
        public bool Cancelled { get; set; }
    }
}
