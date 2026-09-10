using System.ComponentModel.DataAnnotations;

namespace RM_CMS.Modules.MessageTemplates.Api
{
    // -------------------------------------------------------------------------
    // Requests
    // -------------------------------------------------------------------------

    public sealed class SaveTemplateRequest
    {
        [Required(ErrorMessage = "The message cannot be empty.")]
        [StringLength(4096, MinimumLength = 2,
            ErrorMessage = "A Telegram message can be at most 4096 characters.")]
        public string Body { get; set; } = string.Empty;
    }

    // -------------------------------------------------------------------------
    // Responses
    // -------------------------------------------------------------------------

    public sealed class PlaceholderDto
    {
        public string Token { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        /// <summary>What this token looks like when filled in. Drives the preview.</summary>
        public string Sample { get; set; } = string.Empty;
    }

    /// <summary>One scenario as the admin screen shows it.</summary>
    public sealed class TemplateDto
    {
        public string Code { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Group { get; set; } = string.Empty;

        /// <summary>The wording in force — the edited one, or the default.</summary>
        public string Body { get; set; } = string.Empty;

        /// <summary>The built-in wording, so the editor can show what Reset would restore.</summary>
        public string DefaultBody { get; set; } = string.Empty;

        /// <summary>False when nobody has changed this scenario, so it is running on the default.</summary>
        public bool IsCustomised { get; set; }

        public IReadOnlyList<PlaceholderDto> Placeholders { get; set; } = Array.Empty<PlaceholderDto>();

        /// <summary>The body with sample values filled in, exactly as the renderer would.</summary>
        public string Preview { get; set; } = string.Empty;

        public DateTime? UpdatedAt { get; set; }
        public string? UpdatedByName { get; set; }
    }
}
