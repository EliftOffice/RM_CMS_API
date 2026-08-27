using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RM_CMS.Modules.Identity.Domain;
using RM_CMS.Modules.Volunteers.Domain;
using RM_CMS.Modules.Volunteers.Services;
using RM_CMS.Utilities;

namespace RM_CMS.Modules.Volunteers.Api
{
    /// <summary>
    /// Volunteers — the people who do the follow-up work.
    ///
    /// A volunteer is enrolled FROM an existing person, so contact details are never
    /// duplicated here. Safeguarding milestones are administrator-only: they decide
    /// who may be handed a crisis disclosure.
    /// </summary>
    [ApiController]
    [Route("api/volunteers")]
    [Produces("application/json")]
    public sealed class VolunteersController : ControllerBase
    {
        private readonly IVolunteerService _volunteers;

        public VolunteersController(IVolunteerService volunteers) => _volunteers = volunteers;

        /// <summary>Paged list, filtered by search text, status, team or spare capacity.</summary>
        [HttpGet]
        [Authorize(Policy = PolicyNames.VolunteerOrAbove)]
        [ProducesResponseType(typeof(ApiResponse<PagedResult<VolunteerSummaryDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Search(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 25,
            [FromQuery] string? search = null,
            [FromQuery] string? status = null,
            [FromQuery] string? teamId = null,
            [FromQuery] bool? hasCapacity = null)
        {
            return Ok(await _volunteers.SearchAsync(page, pageSize, search, status, teamId, hasCapacity));
        }

        [HttpGet("{id}")]
        [Authorize(Policy = PolicyNames.VolunteerOrAbove)]
        [ProducesResponseType(typeof(ApiResponse<VolunteerDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Get(string id)
        {
            if (!Ulid.IsValid(id))
                return BadRequest(new ApiResponse<VolunteerDto>(ResponseType.Warning, "Invalid volunteer id.", null!));

            return Ok(await _volunteers.GetAsync(id));
        }

        /// <summary>
        /// Enrols an existing person as a volunteer. Seeds the capacity history so the
        /// band trail starts at enrolment rather than at the first change.
        ///
        /// PASTOR OR ADMIN, not team leads. Enrolling somebody creates an account that
        /// can read other people's pastoral records, and a team lead choosing who joins
        /// their own team is the wrong person to make that call — it is the same reason
        /// they cannot promote anyone. The button is gone from their menu too; this is
        /// the half that cannot be bypassed by typing the URL.
        /// </summary>
        [HttpPost]
        [Authorize(Policy = PolicyNames.PastorOrAdmin)]
        [ProducesResponseType(typeof(ApiResponse<VolunteerDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<ApiResponse<VolunteerDto>>> Enrol([FromBody] EnrolVolunteerRequest request)
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            return HttpResponseHelper.CreateHttpResponse(await _volunteers.EnrolAsync(request));
        }

        /// <summary>
        /// Updates status, team placement and wellbeing flags.
        ///
        /// Standing a volunteer down while they still hold open cases is refused —
        /// the cases must be reassigned first, or they would be orphaned silently.
        /// </summary>
        [HttpPut("{id}")]
        [Authorize(Policy = PolicyNames.TeamLeadOrAbove)]
        [ProducesResponseType(typeof(ApiResponse<VolunteerDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<ApiResponse<VolunteerDto>>> Update(string id, [FromBody] UpdateVolunteerRequest request)
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            if (!Ulid.IsValid(id))
                return BadRequest(new ApiResponse<VolunteerDto>(ResponseType.Warning, "Invalid volunteer id.", null!));

            return HttpResponseHelper.CreateHttpResponse(await _volunteers.UpdateAsync(id, request));
        }

        /// <summary>Moves a volunteer to a different capacity band and records why.</summary>
        [HttpPut("{id}/capacity")]
        [Authorize(Policy = PolicyNames.TeamLeadOrAbove)]
        [ProducesResponseType(typeof(ApiResponse<VolunteerDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<ApiResponse<VolunteerDto>>> ChangeCapacity(string id, [FromBody] ChangeCapacityRequest request)
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            if (!Ulid.IsValid(id))
                return BadRequest(new ApiResponse<VolunteerDto>(ResponseType.Warning, "Invalid volunteer id.", null!));

            return HttpResponseHelper.CreateHttpResponse(await _volunteers.ChangeCapacityAsync(id, request));
        }

        /// <summary>The band history, newest first — the wellbeing trail.</summary>
        [HttpGet("{id}/capacity-history")]
        [Authorize(Policy = PolicyNames.TeamLeadOrAbove)]
        [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<CapacityChangeDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> CapacityHistory(string id)
        {
            if (!Ulid.IsValid(id))
                return BadRequest(new ApiResponse<IReadOnlyList<CapacityChangeDto>>(ResponseType.Warning, "Invalid volunteer id.", null!));

            return Ok(await _volunteers.GetCapacityHistoryAsync(id));
        }

        /// <summary>
        /// Records background check, confidentiality agreement and crisis training.
        ///
        /// Administrator-only: these three dates together decide whether a volunteer
        /// may be given a case involving abuse or self-harm disclosure.
        /// </summary>
        [HttpPut("{id}/safeguarding")]
        [Authorize(Policy = PolicyNames.AdminOnly)]
        [ProducesResponseType(typeof(ApiResponse<VolunteerDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<ApiResponse<VolunteerDto>>> Safeguarding(string id, [FromBody] SafeguardingRequest request)
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            if (!Ulid.IsValid(id))
                return BadRequest(new ApiResponse<VolunteerDto>(ResponseType.Warning, "Invalid volunteer id.", null!));

            return HttpResponseHelper.CreateHttpResponse(await _volunteers.UpdateSafeguardingAsync(id, request));
        }

        /// <summary>
        /// Volunteers who could take another case, least loaded first — the same
        /// shortlist the assignment job computes, so manual assignment and automatic
        /// assignment cannot disagree.
        /// </summary>
        [HttpGet("eligible")]
        [Authorize(Policy = PolicyNames.TeamLeadOrAbove)]
        [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<EligibleVolunteerDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Eligible(
            [FromQuery] string? campusId = null,
            [FromQuery] bool crisisCapable = false)
        {
            return Ok(await _volunteers.FindEligibleAsync(campusId, crisisCapable));
        }

        /// <summary>Reference data for volunteer forms.</summary>
        [HttpGet("reference")]
        [Authorize(Policy = PolicyNames.TeamLeadOrAbove)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Reference()
        {
            var bands = await _volunteers.GetCapacityBandsAsync();

            return Ok(new ApiResponse<object>(ResponseType.Success, "Reference data", new
            {
                statuses = VolunteerStatus.All,
                burnoutRisks = BurnoutRisk.All,
                capacityChangeReasons = CapacityChangeReasons.All,
                capacityBands = bands.Data
            }));
        }
    }

    /// <summary>
    /// Teams. A first-class entity so a team can be renamed, retired or handed to a
    /// new leader without touching a single volunteer row.
    /// </summary>
    [ApiController]
    [Route("api/teams")]
    [Produces("application/json")]
    public sealed class TeamsController : ControllerBase
    {
        private readonly IVolunteerService _volunteers;

        public TeamsController(IVolunteerService volunteers) => _volunteers = volunteers;

        [HttpGet]
        [Authorize(Policy = PolicyNames.VolunteerOrAbove)]
        [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<TeamDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> List(
            [FromQuery] string? campusId = null,
            [FromQuery] bool includeInactive = false)
        {
            return Ok(await _volunteers.ListTeamsAsync(campusId, includeInactive));
        }

        [HttpPost]
        [Authorize(Policy = PolicyNames.AdminOnly)]
        [ProducesResponseType(typeof(ApiResponse<TeamDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<ApiResponse<TeamDto>>> Create([FromBody] CreateTeamRequest request)
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            return HttpResponseHelper.CreateHttpResponse(await _volunteers.CreateTeamAsync(request));
        }

        [HttpPut("{id}")]
        [Authorize(Policy = PolicyNames.AdminOnly)]
        [ProducesResponseType(typeof(ApiResponse<TeamDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<ApiResponse<TeamDto>>> Update(string id, [FromBody] UpdateTeamRequest request)
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            if (!Ulid.IsValid(id))
                return BadRequest(new ApiResponse<TeamDto>(ResponseType.Warning, "Invalid team id.", null!));

            return HttpResponseHelper.CreateHttpResponse(await _volunteers.UpdateTeamAsync(id, request));
        }
    }
}
