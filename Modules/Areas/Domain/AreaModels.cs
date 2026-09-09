using System.Text.RegularExpressions;

namespace RM_CMS.Modules.Areas.Domain
{
    /// <summary>
    /// A locality inside a campus's catchment — the neighbourhood a person lives in.
    ///
    /// The MVP typed this fresh into <c>person.locality</c> every time, so one road
    /// arrived as three spellings and "who else lives near this visitor" could not be
    /// answered at all. An area is picked from what already exists and only becomes a
    /// new row when nothing matches.
    ///
    /// Scoped to a campus, like <c>team</c>: an area is a place near one site, and
    /// offering another campus's neighbourhoods would be a picker whose entries can
    /// only ever be wrong.
    /// </summary>
    public sealed class Area
    {
        public long Id { get; set; }
        public string PublicId { get; set; } = string.Empty;

        public long CampusId { get; set; }
        public string? CampusPublicId { get; set; }
        public string? CampusName { get; set; }

        /// <summary>As typed by whoever first recorded it, trimmed. This is displayed.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Lower-cased, whitespace collapsed. Matching and uniqueness use this.</summary>
        public string NormalizedName { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        // ---- derived, counted at read time ----

        /// <summary>Visitors and members filed against this area.</summary>
        public int PersonCount { get; set; }

        /// <summary>Of those, how many hold a volunteer record.</summary>
        public int VolunteerCount { get; set; }

        public DateTime CreatedAt { get; set; }
        public int RowVersion { get; set; }

        /// <summary>
        /// Nobody is filed here, so retiring it costs nothing. Retiring an area that
        /// IS in use is still allowed — unlike a campus, an area is only a label, and
        /// the people keep pointing at it. It just stops being offered.
        /// </summary>
        public bool IsUnused => PersonCount == 0;
    }

    /// <summary>
    /// The one place an area name is turned into its matching key.
    ///
    /// Both the uniqueness index and the "does this already exist?" lookup key off
    /// the same string, so this must be the only implementation. The database
    /// collation already ignores case, but it does not ignore a doubled space —
    /// 'Kurnool  Road' would otherwise become a second area nobody can tell apart
    /// from the first.
    /// </summary>
    public static partial class AreaNames
    {
        public const int MaxLength = 100;

        [GeneratedRegex(@"\s+")]
        private static partial Regex Whitespace();

        /// <summary>Trimmed, with runs of whitespace collapsed to one space. What is stored and shown.</summary>
        public static string Display(string? raw) =>
            Whitespace().Replace((raw ?? string.Empty).Trim(), " ");

        /// <summary>The matching key. Never shown.</summary>
        public static string Normalize(string? raw) =>
            Display(raw).ToLowerInvariant();
    }

    /// <summary>
    /// What the caller may do on the area management screen.
    ///
    /// An administrator always may. A data-entry operator may only when an
    /// administrator has switched <c>area.manage_by_data_entry</c> on — they are the
    /// people typing area names all day, and so the first to see a misspelling, but
    /// renaming a row the whole organisation reads is still a granted power.
    ///
    /// Note what is NOT gated: creating an area by typing a new one at intake. That
    /// is part of recording a visitor and belongs to anyone who may record one.
    /// </summary>
    public enum AreaAccess
    {
        None,

        /// <summary>Granted data-entry operator: rename and retire at their own campus.</summary>
        OwnCampusOnly,

        /// <summary>Administrator: every campus.</summary>
        Administrator
    }
}
