using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RM_CMS.Modules.Identity.Domain;
using RM_CMS.Modules.Pipeline.Domain;
using RM_CMS.Modules.Pipeline.Services;
using RM_CMS.Utilities;

namespace RM_CMS.Modules.Pipeline.Api
{
    /// <summary>
    /// The people pipeline: every visitor and where their journey has reached.
    ///
    /// One endpoint, scoped by who is asking — a team lead sees their own team's
    /// people, a pastor sees their campus, an administrator sees everything. There is
    /// no scope parameter to pass, because a screen that shows more or less depending
    /// on a query string is a screen that can be made to show more.
    /// </summary>
    [ApiController]
    [Route("api/pipeline")]
    [Produces("application/json")]
    [Authorize(Policy = PolicyNames.TeamLeadOrAbove)]
    public sealed class PipelineController : ControllerBase
    {
        private readonly IPipelineService _pipeline;

        public PipelineController(IPipelineService pipeline) => _pipeline = pipeline;

        /// <summary>
        /// The pipeline, paged, with the funnel counts for the caller's scope.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(ApiResponse<PipelineResultDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Search(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            [FromQuery] string? search = null,
            [FromQuery] string? stage = null,
            [FromQuery] string? status = null,
            [FromQuery] bool includeUnstarted = true)
        {
            return Ok(await _pipeline.SearchAsync(page, pageSize, search, stage, status, includeUnstarted));
        }

        /// <summary>
        /// One visitor's whole history: who they are, every case they have had, and a
        /// single chronological timeline of contacts, escalations, reassignments and
        /// notes across all of them.
        ///
        /// Person-centric on purpose. <c>GET /api/cases/{id}</c> answers this one case;
        /// somebody who visited, went quiet and came back a year later has two, and
        /// reading them separately loses the shape of the whole relationship.
        ///
        /// Scoped by the caller's role in the service, the same as the list.
        /// </summary>
        [HttpGet("{personId}")]
        [ProducesResponseType(typeof(ApiResponse<VisitorJourney>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Journey(string personId)
        {
            if (!Ulid.IsValid(personId))
                return BadRequest(new ApiResponse<VisitorJourney>(
                    ResponseType.Warning, "Invalid person id.", default!));

            return Ok(await _pipeline.GetJourneyAsync(personId));
        }
    }

    public sealed class PipelineResultDto
    {
        /// <summary>Plain words for what the caller is looking at, shown on the page.</summary>
        public string Scope { get; set; } = string.Empty;

        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }

        public PipelineSummary Summary { get; set; } = new();
        public IReadOnlyList<PipelineRowDto> Items { get; set; } = Array.Empty<PipelineRowDto>();
    }

    public sealed class PipelineRowDto
    {
        public string PersonId { get; set; } = string.Empty;
        public string PersonName { get; set; } = string.Empty;
        public string? Phone { get; set; }

        public string? CaseId { get; set; }
        public string? CaseReference { get; set; }
        public string? Stage { get; set; }
        public string? Status { get; set; }

        public string? VolunteerName { get; set; }
        public string? TeamName { get; set; }
        public string? CampusName { get; set; }

        /// <summary>"3 of 7", or null outside the nurture stage.</summary>
        public string? NurtureProgress { get; set; }

        public int CurrentStepNumber { get; set; }
        public int? PlanStepCount { get; set; }
        public DateTime? NextStepDueOn { get; set; }

        public DateTime? FirstVisitOn { get; set; }
        public DateTime OpenedAt { get; set; }
        public DateTime? LastContactAt { get; set; }
        public DateTime? ClosedAt { get; set; }
        public string? CloseReason { get; set; }

        public bool HasOpenEscalation { get; set; }
        public int ContactAttemptCount { get; set; }
        public int? DaysSinceContact { get; set; }
    }
}
