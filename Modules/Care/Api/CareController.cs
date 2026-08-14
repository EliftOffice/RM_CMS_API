using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RM_CMS.Modules.Care.Domain;
using RM_CMS.Modules.Care.Data;
using RM_CMS.Modules.Care.Services;
using RM_CMS.Modules.Identity.Domain;
using RM_CMS.Utilities;

namespace RM_CMS.Modules.Care.Api
{
    /// <summary>
    /// Care cases — the follow-up journey with a person.
    ///
    /// Volunteers see and act on their own cases; team leads and above see the whole
    /// campus. That scoping is enforced in the service, not just here, so it holds
    /// however the endpoint is reached.
    /// </summary>
    [ApiController]
    [Route("api/cases")]
    [Produces("application/json")]
    public sealed class CasesController : ControllerBase
    {
        private readonly ICareService _care;

        public CasesController(ICareService care) => _care = care;

        [HttpGet]
        [Authorize(Policy = PolicyNames.VolunteerOrAbove)]
        [ProducesResponseType(typeof(ApiResponse<PagedResult<CaseSummaryDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Search(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 25,
            [FromQuery] string? search = null,
            [FromQuery] string? stage = null,
            [FromQuery] string? status = null,
            [FromQuery] string? volunteerId = null,
            [FromQuery] bool? unassigned = null)
        {
            return Ok(await _care.SearchAsync(page, pageSize, search, stage, status, volunteerId, unassigned));
        }

        /// <summary>Full detail: contacts, escalations, notes and assignment history.</summary>
        [HttpGet("{id}")]
        [Authorize(Policy = PolicyNames.VolunteerOrAbove)]
        [ProducesResponseType(typeof(ApiResponse<CaseDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Get(string id)
        {
            if (!Ulid.IsValid(id))
                return BadRequest(new ApiResponse<CaseDto>(ResponseType.Warning, "Invalid case id.", null!));

            return Ok(await _care.GetAsync(id));
        }

        /// <summary>
        /// Opens a journey for a person, optionally assigning it immediately to the
        /// least-loaded eligible volunteer.
        /// </summary>
        [HttpPost]
        [Authorize(Policy = PolicyNames.CanRecordVisitors)]
        [ProducesResponseType(typeof(ApiResponse<CaseDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<ApiResponse<CaseDto>>> Open([FromBody] OpenCaseRequest request)
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            return HttpResponseHelper.CreateHttpResponse(await _care.OpenAsync(request));
        }

        /// <summary>
        /// Assigns or reassigns the case. A handover note is strongly encouraged —
        /// the incoming volunteer is inheriting a relationship, not a ticket.
        /// </summary>
        [HttpPut("{id}/assign")]
        [Authorize(Policy = PolicyNames.TeamLeadOrAbove)]
        [ProducesResponseType(typeof(ApiResponse<CaseDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<ApiResponse<CaseDto>>> Assign(string id, [FromBody] AssignCaseRequest request)
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            if (!Ulid.IsValid(id))
                return BadRequest(new ApiResponse<CaseDto>(ResponseType.Warning, "Invalid case id.", null!));

            return HttpResponseHelper.CreateHttpResponse(await _care.AssignAsync(id, request));
        }

        /// <summary>Raises a concern by hand. Pauses the case until it is resolved.</summary>
        [HttpPost("{id}/escalations")]
        [Authorize(Policy = PolicyNames.VolunteerOrAbove)]
        [ProducesResponseType(typeof(ApiResponse<EscalationDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<ApiResponse<EscalationDto>>> Escalate(string id, [FromBody] RaiseEscalationRequest request)
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            if (!Ulid.IsValid(id))
                return BadRequest(new ApiResponse<EscalationDto>(ResponseType.Warning, "Invalid case id.", null!));

            return HttpResponseHelper.CreateHttpResponse(await _care.RaiseEscalationAsync(id, request));
        }

        /// <summary>
        /// The team lead's decision when a case reaches review after the nurture plan
        /// is exhausted — the Permanent/Failed call the system deliberately does not
        /// make on its own.
        /// </summary>
        [HttpPost("{id}/review")]
        [Authorize(Policy = PolicyNames.TeamLeadOrAbove)]
        [ProducesResponseType(typeof(ApiResponse<CaseDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<ApiResponse<CaseDto>>> CompleteReview(string id, [FromBody] ReviewDecisionRequest request)
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            if (!Ulid.IsValid(id))
                return BadRequest(new ApiResponse<CaseDto>(ResponseType.Warning, "Invalid case id.", null!));

            return HttpResponseHelper.CreateHttpResponse(await _care.CompleteReviewAsync(id, request));
        }

        [HttpPost("{id}/notes")]
        [Authorize(Policy = PolicyNames.VolunteerOrAbove)]
        [ProducesResponseType(typeof(ApiResponse<NoteDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<ApiResponse<NoteDto>>> AddNote(string id, [FromBody] AddNoteRequest request)
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            if (!Ulid.IsValid(id))
                return BadRequest(new ApiResponse<NoteDto>(ResponseType.Warning, "Invalid case id.", null!));

            return HttpResponseHelper.CreateHttpResponse(
                await _care.AddNoteAsync(NoteEntityTypes.CareCase, id, request));
        }
    }

    /// <summary>Contacts — the volunteer's day-to-day work.</summary>
    [ApiController]
    [Route("api/contacts")]
    [Produces("application/json")]
    public sealed class ContactsController : ControllerBase
    {
        private readonly ICareService _care;
        private readonly ICareLookupRepository _lookups;

        public ContactsController(ICareService care, ICareLookupRepository lookups)
        {
            _care = care;
            _lookups = lookups;
        }

        /// <summary>What the signed-in volunteer owes today, oldest first.</summary>
        [HttpGet("mine")]
        [Authorize(Policy = PolicyNames.VolunteerOrAbove)]
        [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<InteractionDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> MyWorkList()
        {
            return Ok(await _care.GetMyWorkListAsync());
        }

        /// <summary>
        /// Logs a contact. The outcome and the visitor's intent together decide, via
        /// the admin-configured rules, what happens to the case next — and the
        /// response says which rule matched and what it did.
        /// </summary>
        [HttpPost("{id}/log")]
        [Authorize(Policy = PolicyNames.VolunteerOrAbove)]
        [ProducesResponseType(typeof(ApiResponse<InteractionResultDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<ApiResponse<InteractionResultDto>>> Log(string id, [FromBody] LogInteractionRequest request)
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            if (!Ulid.IsValid(id))
                return BadRequest(new ApiResponse<InteractionResultDto>(ResponseType.Warning, "Invalid contact id.", null!));

            return HttpResponseHelper.CreateHttpResponse(await _care.LogInteractionAsync(id, request));
        }

        /// <summary>Vocabulary for the log-contact form.</summary>
        [HttpGet("reference")]
        [Authorize(Policy = PolicyNames.VolunteerOrAbove)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Reference()
        {
            var outcomes = await _lookups.GetOutcomesAsync();
            var intents = await _lookups.GetIntentsAsync();
            var methods = await _lookups.GetContactMethodsAsync();
            var reasons = await _lookups.GetEscalationReasonsAsync();
            var escalationOutcomes = await _lookups.GetEscalationOutcomesAsync();

            return Ok(new ApiResponse<object>(ResponseType.Success, "Reference data", new
            {
                outcomes = outcomes.Select(o => new { code = o.Code, label = o.Label }),
                intents = intents.Select(i => new { code = i.Code, label = i.Label }),
                methods = methods.Select(m => new { code = m.Code, label = m.Label }),
                escalationReasons = reasons.Select(r => new { code = r.Code, label = r.Label, requiresProtocol = r.RequiresProtocol }),
                escalationOutcomes = escalationOutcomes.Select(o => new { code = o.Code, label = o.Label }),
                closeReasons = CaseCloseReason.All
            }));
        }
    }

    /// <summary>Escalations — the team lead's queue.</summary>
    [ApiController]
    [Route("api/escalations")]
    [Authorize(Policy = PolicyNames.TeamLeadOrAbove)]
    [Produces("application/json")]
    public sealed class EscalationsController : ControllerBase
    {
        private readonly ICareService _care;

        public EscalationsController(ICareService care) => _care = care;

        /// <summary>Most urgent first, then longest waiting.</summary>
        [HttpGet]
        [ProducesResponseType(typeof(ApiResponse<PagedResult<EscalationDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Search(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 25,
            [FromQuery] string? status = null,
            [FromQuery] bool mine = false)
        {
            return Ok(await _care.SearchEscalationsAsync(page, pageSize, status, mine));
        }

        /// <summary>Takes ownership. The case stays paused until it is resolved.</summary>
        [HttpPost("{id}/acknowledge")]
        [ProducesResponseType(typeof(ApiResponse<EscalationDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<ApiResponse<EscalationDto>>> Acknowledge(string id)
        {
            if (!Ulid.IsValid(id))
                return BadRequest(new ApiResponse<EscalationDto>(ResponseType.Warning, "Invalid escalation id.", null!));

            return HttpResponseHelper.CreateHttpResponse(await _care.AcknowledgeAsync(id));
        }

        /// <summary>
        /// Resolves the concern and resumes the case — unless another escalation is
        /// still open on it. Safeguarding reasons require the protocol question to be
        /// answered before this will succeed.
        /// </summary>
        [HttpPost("{id}/resolve")]
        [ProducesResponseType(typeof(ApiResponse<EscalationDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<ApiResponse<EscalationDto>>> Resolve(string id, [FromBody] ResolveEscalationRequest request)
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            if (!Ulid.IsValid(id))
                return BadRequest(new ApiResponse<EscalationDto>(ResponseType.Warning, "Invalid escalation id.", null!));

            return HttpResponseHelper.CreateHttpResponse(await _care.ResolveAsync(id, request));
        }
    }
}
