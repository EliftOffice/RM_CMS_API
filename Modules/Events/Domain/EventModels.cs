using System.Text.RegularExpressions;

namespace RM_CMS.Modules.Events.Domain
{
    /// <summary>
    /// A gathering the church invites people to, as listed on the public website.
    ///
    /// Not to be confused with <c>security_event</c>, which is an audit row about the
    /// application. This is the thing with a date, a venue and a poster.
    ///
    /// The website used to hold these in a hard-coded array in its own source, so
    /// publishing an event meant a code change and a redeploy. This is the source of
    /// truth now; the site fetches it.
    /// </summary>
    public sealed class ChurchEvent
    {
        public long Id { get; set; }
        public string PublicId { get; set; } = string.Empty;

        /// <summary>The URL segment: <c>/events/{slug}</c>.</summary>
        public string Slug { get; set; } = string.Empty;

        public long? CampusId { get; set; }
        public string? CampusPublicId { get; set; }
        public string? CampusName { get; set; }

        /// <summary>IANA zone of the campus, or the organisation default. Drives the offset the API emits.</summary>
        public string? CampusTimezone { get; set; }

        public string Title { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;

        /// <summary>Blank-line separated paragraphs. Split into an array on the way out.</summary>
        public string? Description { get; set; }

        public string Venue { get; set; } = string.Empty;

        /// <summary>UTC. Converted to the campus zone when the website reads it.</summary>
        public DateTime StartsAt { get; set; }
        public DateTime? EndsAt { get; set; }

        /// <summary>
        /// The poster's metadata. The BYTES are deliberately not on this object —
        /// they live next door in <c>church_event_poster</c> and are read only by the
        /// endpoint that serves them, so listing fifty events does not pull fifty
        /// images through the service layer to throw all but one of them away.
        ///
        /// The content type is the one the SERVER decided by reading the bytes, never
        /// the one the upload announced.
        /// </summary>
        public string? PosterContentType { get; set; }

        /// <summary>The uploaded file's name, kept only as a label for the staff screen.</summary>
        public string? PosterFileName { get; set; }

        public int? PosterByteSize { get; set; }

        /// <summary>
        /// When the poster was last replaced. Doubles as the cache-busting token in
        /// the URL, so a new picture appears immediately rather than when whatever
        /// the browser cached expires.
        /// </summary>
        public DateTime? PosterUpdatedAt { get; set; }

        public bool HasPoster => PosterByteSize is > 0;

        public string Status { get; set; } = EventStatus.Draft;
        public DateTime? PublishedAt { get; set; }

        public DateTime CreatedAt { get; set; }
        public string? CreatedByName { get; set; }
        public DateTime UpdatedAt { get; set; }
        public int RowVersion { get; set; }

        /// <summary>
        /// Whether this has already finished, measured against <c>EndsAt</c> when there
        /// is one. An event that runs 8am to noon is still "upcoming" at 10am — using
        /// the start would move it to the past while people are still in the room.
        /// </summary>
        public bool HasFinished(DateTime nowUtc) => (EndsAt ?? StartsAt) < nowUtc;
    }

    /// <summary>The poster's bytes, read only when one is actually being served.</summary>
    public sealed class EventPoster
    {
        public byte[] Bytes { get; set; } = Array.Empty<byte>();

        /// <summary>What the server decided these bytes are, not what was claimed.</summary>
        public string ContentType { get; set; } = string.Empty;

        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>
    /// What may be uploaded as a poster, decided by looking at the bytes.
    ///
    /// This replaced a fixed list of image slugs from the website's build-time
    /// manifest. That list could only ever offer stock plates the site already
    /// shipped, so the poster actually designed for the event was never one of the
    /// options and two events routinely showed the same photograph.
    ///
    /// The browser's Content-Type and the file's extension are both written by the
    /// caller and neither is evidence: a file named poster.jpg and announced as
    /// image/jpeg can hold anything at all. So the format is read from the first few
    /// bytes, and the content type SERVED later is the one this decided — which is
    /// what stops an upload being echoed back as text/html and running as a page on
    /// the API's own origin.
    /// </summary>
    public static class PosterImages
    {
        /// <summary>
        /// 6 MB. Comfortably above a phone photograph of a printed poster and well
        /// below the MEDIUMBLOB ceiling, so the limit that stops an upload is this
        /// one, with a sentence attached, rather than a truncated row.
        /// </summary>
        public const int MaxBytes = 6 * 1024 * 1024;

        /// <summary>Below this it is not a picture, it is a mistake.</summary>
        public const int MinBytes = 64;

        public const string Jpeg = "image/jpeg";
        public const string Png = "image/png";
        public const string Webp = "image/webp";

        /// <summary>For the file picker's accept attribute and the refusal message.</summary>
        public static readonly string[] Accepted = { Jpeg, Png, Webp };

        /// <summary>
        /// The real format of these bytes, or null when they are not one this server
        /// will serve.
        ///
        /// GIF and SVG are absent on purpose. An SVG is a document — it can carry
        /// script, and serving one from this origin would run it there. A poster is a
        /// photograph or a flyer, so nothing is lost by refusing both.
        /// </summary>
        public static string? Sniff(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length < 12) return null;

            // FF D8 FF — the JPEG start-of-image marker plus the first byte of the
            // marker that always follows it.
            if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
                return Jpeg;

            // 89 P N G CR LF SUB LF. The CR/LF pair is in the signature to catch a
            // transfer that mangled line endings, and checking it costs nothing.
            if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
                bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
                return Png;

            // RIFF....WEBP — a RIFF container whose form type is WEBP. The four bytes
            // between are the length, which is not checked here.
            if (bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
                bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
                return Webp;

            return null;
        }

        /// <summary>The extension for a format the server decided on, dot included.</summary>
        public static string Extension(string contentType) => contentType switch
        {
            Png  => ".png",
            Webp => ".webp",
            _    => ".jpg"
        };
    }

    public static class EventStatus
    {
        /// <summary>Written but invisible to the public.</summary>
        public const string Draft = "DRAFT";

        /// <summary>Live on the website.</summary>
        public const string Published = "PUBLISHED";

        /// <summary>
        /// Called off. Kept rather than deleted: somebody who saw it advertised will come
        /// looking, and a page saying "cancelled" serves them better than a 404.
        /// </summary>
        public const string Cancelled = "CANCELLED";

        public static readonly string[] All = { Draft, Published, Cancelled };

        public static bool IsKnown(string? code) =>
            !string.IsNullOrWhiteSpace(code) && All.Contains(code, StringComparer.Ordinal);
    }

    /// <summary>
    /// Turning a title into a URL segment.
    ///
    /// Generated once, when the event is created, and then left alone. The slug is what
    /// anyone who shared the link is holding, so a later retitle must not move the page
    /// out from under them — the admin screen can change it deliberately, but nothing
    /// changes it as a side effect.
    /// </summary>
    public static partial class EventSlugs
    {
        public const int MaxLength = 120;

        [GeneratedRegex("[^a-z0-9]+")]
        private static partial Regex NonSlug();

        [GeneratedRegex("^-+|-+$")]
        private static partial Regex EdgeDashes();

        public static string From(string? title)
        {
            var lowered = (title ?? string.Empty).ToLowerInvariant();

            // Accents and non-Latin characters collapse to nothing rather than being
            // transliterated. A Telugu title would otherwise produce an empty slug
            // silently; the caller checks for that and asks for one instead.
            var slug = EdgeDashes().Replace(NonSlug().Replace(lowered, "-"), string.Empty);

            return slug.Length > MaxLength ? slug[..MaxLength].TrimEnd('-') : slug;
        }

        /// <summary>Already-valid slugs pass through untouched; anything else is normalised.</summary>
        public static bool IsValid(string? slug) =>
            !string.IsNullOrWhiteSpace(slug) &&
            slug.Length <= MaxLength &&
            slug == From(slug);
    }

    /// <summary>What the admin list asked to see.</summary>
    public sealed class EventQuery
    {
        public string? Status { get; init; }
        public string? Search { get; init; }

        /// <summary>True to include events that have already finished.</summary>
        public bool IncludePast { get; init; } = true;

        public int Skip { get; init; }
        public int Take { get; init; } = 50;
    }

    /// <summary>Counts for the admin screen's header.</summary>
    public sealed class EventSummary
    {
        public int Draft { get; set; }
        public int Published { get; set; }
        public int Cancelled { get; set; }

        /// <summary>Published and still to come — what the public can actually see.</summary>
        public int Upcoming { get; set; }
        public int Total { get; set; }
    }
}
