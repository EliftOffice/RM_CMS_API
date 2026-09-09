using System.ComponentModel.DataAnnotations;

namespace RM_CMS.Modules.Areas.Api
{
    // -------------------------------------------------------------------------
    // Requests
    // -------------------------------------------------------------------------

    public sealed class CreateAreaRequest
    {
        [Required(ErrorMessage = "Give the area a name.")]
        [StringLength(100, MinimumLength = 2)]
        public string Name { get; set; } = string.Empty;

        /// <summary>Public id of the campus. Defaults to the caller's own when omitted.</summary>
        [StringLength(26)]
        public string? CampusId { get; set; }
    }

    /// <summary>
    /// Rename or retire. The campus is deliberately absent: an area is a place,
    /// and moving one between sites would silently re-file every person who lives
    /// there. An area at the wrong campus is retired and re-created at the right one.
    /// </summary>
    public sealed class UpdateAreaRequest
    {
        [Required(ErrorMessage = "Give the area a name.")]
        [StringLength(100, MinimumLength = 2)]
        public string Name { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        [Required] public int RowVersion { get; set; }
    }

    // -------------------------------------------------------------------------
    // Responses
    // -------------------------------------------------------------------------

    /// <summary>The management-screen row.</summary>
    public sealed class AreaDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;

        public string? CampusId { get; set; }
        public string? CampusName { get; set; }

        public bool IsActive { get; set; }

        /// <summary>People filed against this area. Soft-deleted ones excluded.</summary>
        public int PersonCount { get; set; }

        /// <summary>Of those, how many are active volunteers.</summary>
        public int VolunteerCount { get; set; }

        public DateTime CreatedAt { get; set; }
        public int RowVersion { get; set; }
    }

    /// <summary>
    /// The picker shape. Deliberately thin: the type-ahead is used at intake by
    /// data-entry operators, who have no business learning how many people live in
    /// each neighbourhood from a dropdown.
    /// </summary>
    public sealed class AreaOptionDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>
    /// What THIS caller may do with areas.
    ///
    /// The screen renders from this rather than from the role claim, because two
    /// roles can reach it and which of them actually may is a setting an
    /// administrator controls. A client that ignores it still meets the same checks
    /// in the service.
    /// </summary>
    public sealed class AreaAccessDto
    {
        public bool CanOpen { get; set; }
        public bool CanCreate { get; set; }
        public bool CanEdit { get; set; }
        public bool CanRetire { get; set; }

        /// <summary>True for an administrator, who sees every campus's areas.</summary>
        public bool CanSeeAllCampuses { get; set; }

        /// <summary>NONE | OWNCAMPUSONLY | ADMINISTRATOR.</summary>
        public string Scope { get; set; } = "NONE";
    }
}
