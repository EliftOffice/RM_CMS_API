using System.ComponentModel.DataAnnotations;

namespace RM_CMS.Modules.Campuses.Api
{
    // -------------------------------------------------------------------------
    // Requests
    // -------------------------------------------------------------------------

    public sealed class CreateCampusRequest
    {
        /// <summary>
        /// Uppercased and trimmed before it is stored, so 'ongole' and 'Ongole' cannot
        /// become two campuses.
        /// </summary>
        [Required]
        [StringLength(20, MinimumLength = 2)]
        [RegularExpression("^[A-Za-z0-9_-]+$",
            ErrorMessage = "Code may use letters, numbers, hyphen and underscore only.")]
        public string Code { get; set; } = string.Empty;

        [Required][StringLength(100, MinimumLength = 2)]
        public string Name { get; set; } = string.Empty;

        /// <summary>IANA zone id. Defaults to the organisation's own zone.</summary>
        [StringLength(64)]
        public string? Timezone { get; set; }
    }

    /// <summary>
    /// The code is deliberately absent: it is referenced by operators and in exports,
    /// and renaming it silently would break both. A campus that needs a different code
    /// is a new campus.
    /// </summary>
    public sealed class UpdateCampusRequest
    {
        [Required][StringLength(100, MinimumLength = 2)]
        public string Name { get; set; } = string.Empty;

        [StringLength(64)]
        public string? Timezone { get; set; }

        public bool IsActive { get; set; } = true;

        [Required] public int RowVersion { get; set; }
    }

    // -------------------------------------------------------------------------
    // Responses
    // -------------------------------------------------------------------------

    public sealed class CampusDto
    {
        public string Id { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Timezone { get; set; } = string.Empty;
        public bool IsActive { get; set; }

        /// <summary>Visitors only — people with no sign-in and no volunteer record.</summary>
        public int PersonCount { get; set; }

        /// <summary>People with a sign-in at this campus.</summary>
        public int StaffCount { get; set; }
        public int VolunteerCount { get; set; }
        public int TeamCount { get; set; }
        public int OpenCaseCount { get; set; }

        /// <summary>False when people, volunteers, teams or open cases still belong to it.</summary>
        public bool CanRetire { get; set; }

        public DateTime CreatedAt { get; set; }
        public int RowVersion { get; set; }
    }

    /// <summary>
    /// The picker shape. Deliberately thin: the visitor-entry screen is used by
    /// data-entry operators, who have no business seeing how many volunteers or open
    /// cases another campus is carrying.
    /// </summary>
    public sealed class CampusOptionDto
    {
        public string Id { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }
}
