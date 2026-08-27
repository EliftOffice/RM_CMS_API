using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RM_CMS.Modules.Identity.Domain;
using RM_CMS.Modules.Settings.Services;
using RM_CMS.Security;
using RM_CMS.Utilities;

namespace RM_CMS.Modules.Settings.Api
{
    /// <summary>
    /// The business rules an administrator may change without a deployment: how many
    /// contact attempts before someone is unreachable, how long a team lead has to
    /// acknowledge an escalation, how far ahead nurture steps are created.
    ///
    /// Replaces the MVP's <c>/api/systemconfig</c>, which read a <c>system_config</c>
    /// table that no longer exists — and which also served the live Telegram bot token
    /// in plaintext to anyone who could reach it.
    /// </summary>
    [ApiController]
    [Route("api/admin/settings")]
    [Produces("application/json")]
    [Authorize(Policy = PolicyNames.AdminOnly)]
    public sealed class SettingsController : ControllerBase
    {
        private readonly ISettingService _settings;

        public SettingsController(ISettingService settings) => _settings = settings;

        /// <summary>Every setting, grouped by category, with its type and bounds.</summary>
        [HttpGet]
        [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<SettingGroupDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> List() => Ok(await _settings.ListAsync());

        /// <summary>
        /// Saves a batch. Validated as a whole first — if any value is out of bounds
        /// the entire batch is rejected, so the rules never end up half-applied.
        /// </summary>
        [HttpPut]
        [EnableRateLimiting(RateLimitPolicies.Sensitive)]
        [ProducesResponseType(typeof(ApiResponse<SettingUpdateResultDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Update([FromBody] UpdateSettingsRequest request)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            return Ok(await _settings.UpdateAsync(request));
        }
    }

    // -------------------------------------------------------------------------
    // Contracts
    // -------------------------------------------------------------------------

    public sealed class UpdateSettingsRequest
    {
        [Required][MinLength(1, ErrorMessage = "At least one setting is required.")]
        public List<SettingChange> Settings { get; set; } = new();
    }

    public sealed class SettingChange
    {
        [Required][StringLength(80)]
        public string Key { get; set; } = string.Empty;

        [Required][StringLength(500)]
        public string Value { get; set; } = string.Empty;
    }

    public sealed class SettingGroupDto
    {
        public string Category { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public List<SettingDto> Settings { get; set; } = new();
    }

    public sealed class SettingDto
    {
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string ValueType { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int? MinValue { get; set; }
        public int? MaxValue { get; set; }
        public bool IsSecret { get; set; }
        public bool IsEditable { get; set; }
        public DateTime UpdatedAt { get; set; }
        public int RowVersion { get; set; }
    }

    public sealed class SettingUpdateResultDto
    {
        public List<string> Updated { get; set; } = new();
        public List<string> Unchanged { get; set; } = new();
        public List<SettingRejectionDto> Rejected { get; set; } = new();
    }

    public sealed class SettingRejectionDto
    {
        public string Key { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
    }
}
