using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RM_CMS.Modules.Identity.Domain;
using RM_CMS.Modules.People.Services;
using RM_CMS.Utilities;

namespace RM_CMS.Modules.People.Api
{
    /// <summary>
    /// People — the canonical record of a human being.
    ///
    /// Authorization is split deliberately rather than applied uniformly:
    ///   • intake (create + duplicate lookup) is open to DATA_ENTRY, whose whole job
    ///     it is, and who cannot do anything else here
    ///   • browsing the full register needs VOLUNTEER or above, because a list of
    ///     everyone's contact details is a different privilege from recording one
    ///     person at the desk
    ///   • consent and deletion need TEAM_LEAD / ADMIN
    /// </summary>
    [ApiController]
    [Route("api/people")]
    [Produces("application/json")]
    public sealed class PeopleController : ControllerBase
    {
        private readonly IPeopleService _people;

        public PeopleController(IPeopleService people) => _people = people;

        /// <summary>Paged register, filtered by search text, lifecycle status or campus.</summary>
        [HttpGet]
        [Authorize(Policy = PolicyNames.VolunteerOrAbove)]
        [ProducesResponseType(typeof(ApiResponse<PagedResult<PersonSummaryDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Search(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 25,
            [FromQuery] string? search = null,
            [FromQuery] string? status = null,
            [FromQuery] string? campusId = null,
            [FromQuery] bool? doNotContact = null)
        {
            return Ok(await _people.SearchAsync(page, pageSize, search, status, campusId, doNotContact));
        }

        /// <summary>Full detail for one person.</summary>
        [HttpGet("{id}")]
        [Authorize(Policy = PolicyNames.VolunteerOrAbove)]
        [ProducesResponseType(typeof(ApiResponse<PersonDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Get(string id)
        {
            if (!Ulid.IsValid(id))
                return BadRequest(new ApiResponse<PersonDto>(ResponseType.Warning, "Invalid person id.", null!));

            return Ok(await _people.GetAsync(id));
        }

        /// <summary>
        /// Duplicate check for the intake screen. Contact values are masked — an
        /// operator needs to know a number is already on file, not to read the
        /// contact details of everyone recorded.
        /// </summary>
        [HttpGet("lookup")]
        [Authorize(Policy = PolicyNames.CanRecordVisitors)]
        [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<PersonMatchDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Lookup([FromQuery] string q)
        {
            return Ok(await _people.LookupAsync(q));
        }

        /// <summary>
        /// Name search for the person picker. Used by the admin "grant access" flow,
        /// which must find a person before it can attach an account to them.
        /// </summary>
        [HttpGet("picker")]
        [Authorize(Policy = PolicyNames.TeamLeadOrAbove)]
        [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<PersonMatchDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Picker([FromQuery] string q)
        {
            return Ok(await _people.PickerAsync(q));
        }

        /// <summary>
        /// Records a visitor at intake.
        ///
        /// Refuses when an existing person shares a contact number and returns the
        /// matches, so a duplicate is a deliberate choice rather than an accident.
        /// Resubmit with <c>allowDuplicate: true</c> to override.
        /// </summary>
        [HttpPost]
        [Authorize(Policy = PolicyNames.CanRecordVisitors)]
        [ProducesResponseType(typeof(ApiResponse<PersonDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<ApiResponse<PersonDto>>> Create([FromBody] CreatePersonRequest request)
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            // Somebody local must have an area. They are followed up in person, and
            // the area is what decides which volunteer is near enough to take them;
            // a blank one leaves the case matchable to nobody in particular.
            //
            // The rule is HERE and not in PeopleService because it belongs to intake,
            // not to the person model. Creating a user goes through the service
            // directly with no address at all — an administrator adding a pastor is
            // not recording where they live, and enforcing this there would block
            // that for no reason.
            if (request.IsLocal &&
                string.IsNullOrWhiteSpace(request.AreaId) &&
                string.IsNullOrWhiteSpace(request.AreaName))
            {
                return HttpResponseHelper.CreateHttpResponse(new ApiResponse<PersonDto>(
                    ResponseType.Warning,
                    "Enter the area they live in, or uncheck 'lives locally' and record " +
                    "their address instead.",
                    default!));
            }

            return HttpResponseHelper.CreateHttpResponse(await _people.CreateAsync(request));
        }

        /// <summary>Updates demographic and address detail. Requires the record's rowVersion.</summary>
        [HttpPut("{id}")]
        [Authorize(Policy = PolicyNames.VolunteerOrAbove)]
        [ProducesResponseType(typeof(ApiResponse<PersonDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<ApiResponse<PersonDto>>> Update(string id, [FromBody] UpdatePersonRequest request)
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            if (!Ulid.IsValid(id))
                return BadRequest(new ApiResponse<PersonDto>(ResponseType.Warning, "Invalid person id.", null!));

            return HttpResponseHelper.CreateHttpResponse(await _people.UpdateAsync(id, request));
        }

        /// <summary>
        /// Records or lifts a do-not-contact request.
        ///
        /// Team leads and above only: this is a consent decision that permanently
        /// stops outreach, not an ordinary edit.
        /// </summary>
        [HttpPut("{id}/do-not-contact")]
        [Authorize(Policy = PolicyNames.TeamLeadOrAbove)]
        [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
        public async Task<ActionResult<ApiResponse<bool>>> SetDoNotContact(string id, [FromBody] DoNotContactRequest request)
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            if (!Ulid.IsValid(id))
                return BadRequest(new ApiResponse<bool>(ResponseType.Warning, "Invalid person id.", false));

            return HttpResponseHelper.CreateHttpResponse(await _people.SetDoNotContactAsync(id, request));
        }

        /// <summary>Moves a person along the lifecycle, e.g. VISITOR to MEMBER.</summary>
        [HttpPut("{id}/lifecycle")]
        [Authorize(Policy = PolicyNames.TeamLeadOrAbove)]
        [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
        public async Task<ActionResult<ApiResponse<bool>>> SetLifecycle(string id, [FromBody] LifecycleRequest request)
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            if (!Ulid.IsValid(id))
                return BadRequest(new ApiResponse<bool>(ResponseType.Warning, "Invalid person id.", false));

            return HttpResponseHelper.CreateHttpResponse(await _people.SetLifecycleAsync(id, request));
        }

        /// <summary>Removes a person. Soft delete — care history stays readable.</summary>
        [HttpDelete("{id}")]
        [Authorize(Policy = PolicyNames.AdminOnly)]
        [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
        public async Task<ActionResult<ApiResponse<bool>>> Delete(string id)
        {
            if (!Ulid.IsValid(id))
                return BadRequest(new ApiResponse<bool>(ResponseType.Warning, "Invalid person id.", false));

            return HttpResponseHelper.CreateHttpResponse(await _people.DeleteAsync(id));
        }

        // ------------------------------------------------------------------
        // Contacts
        // ------------------------------------------------------------------

        [HttpPost("{id}/contacts")]
        [Authorize(Policy = PolicyNames.VolunteerOrAbove)]
        [ProducesResponseType(typeof(ApiResponse<PersonDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<ApiResponse<PersonDto>>> AddContact(string id, [FromBody] ContactRequest request)
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            if (!Ulid.IsValid(id))
                return BadRequest(new ApiResponse<PersonDto>(ResponseType.Warning, "Invalid person id.", null!));

            return HttpResponseHelper.CreateHttpResponse(await _people.AddContactAsync(id, request));
        }

        [HttpDelete("{id}/contacts/{contactId:long}")]
        [Authorize(Policy = PolicyNames.VolunteerOrAbove)]
        [ProducesResponseType(typeof(ApiResponse<PersonDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<ApiResponse<PersonDto>>> RemoveContact(string id, long contactId)
        {
            if (!Ulid.IsValid(id))
                return BadRequest(new ApiResponse<PersonDto>(ResponseType.Warning, "Invalid person id.", null!));

            return HttpResponseHelper.CreateHttpResponse(await _people.RemoveContactAsync(id, contactId));
        }

        [HttpPut("{id}/contacts/{contactId:long}/primary")]
        [Authorize(Policy = PolicyNames.VolunteerOrAbove)]
        [ProducesResponseType(typeof(ApiResponse<PersonDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<ApiResponse<PersonDto>>> SetPrimaryContact(string id, long contactId)
        {
            if (!Ulid.IsValid(id))
                return BadRequest(new ApiResponse<PersonDto>(ResponseType.Warning, "Invalid person id.", null!));

            return HttpResponseHelper.CreateHttpResponse(await _people.SetPrimaryContactAsync(id, contactId));
        }

        /// <summary>Reference data for the intake form.</summary>
        [HttpGet("/api/people-reference")]
        [Authorize(Policy = PolicyNames.CanRecordVisitors)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
        public IActionResult Reference() =>
            Ok(new ApiResponse<object>(ResponseType.Success, "Reference data", new
            {
                lifecycleStatuses = Domain.PersonLifecycle.All,
                contactTypes = Domain.ContactTypes.All,
                ageBands = Domain.AgeBands.All
            }));
    }
}
