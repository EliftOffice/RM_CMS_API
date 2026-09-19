using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RM_CMS.Modules.Care.Services;
using RM_CMS.Modules.Identity.Domain;
using RM_CMS.Modules.Settings.Data;
using RM_CMS.Utilities;

namespace RM_CMS.Modules.Settings.Api
{
    /// <summary>
    /// The handful of administrator rules the INTAKE screen has to know about, for
    /// the people who work that screen.
    ///
    /// Separate from <see cref="SettingsController"/> on purpose. That controller is
    /// AdminOnly and returns every rule with its bounds, type and row version — the
    /// right shape for the settings editor and entirely the wrong thing to hand a
    /// data-entry operator so their form can decide whether to show one checkbox.
    /// This returns the answer, nothing else.
    ///
    /// The screen uses this to avoid offering a choice that will not be honoured.
    /// It is NOT the enforcement: <see cref="ICareService"/> checks the same rules
    /// when the case is opened, because a browser can always send whatever it likes.
    /// </summary>
    [ApiController]
    [Route("api/intake-rules")]
    [Produces("application/json")]
    [Authorize(Policy = PolicyNames.CanRecordVisitors)]
    public sealed class IntakeRulesController : ControllerBase
    {
        private readonly ISettingRepository _settings;

        public IntakeRulesController(ISettingRepository settings) => _settings = settings;

        [HttpGet]
        [ProducesResponseType(typeof(ApiResponse<IntakeRulesDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Get()
        {
            var rules = new IntakeRulesDto
            {
                AutoAssignOnIntake = await _settings.GetBoolAsync(CareService.AutoAssignOnIntakeKey, true)
            };

            return Ok(new ApiResponse<IntakeRulesDto>(ResponseType.Success, "Intake rules.", rules));
        }
    }

    public sealed class IntakeRulesDto
    {
        /// <summary>
        /// Whether a case opened at intake is assigned there and then. When false the
        /// case is still opened — it waits for the assignment job instead.
        /// </summary>
        public bool AutoAssignOnIntake { get; set; }
    }
}
