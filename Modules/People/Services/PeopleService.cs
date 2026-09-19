using RM_CMS.Modules.Areas.Services;
using RM_CMS.Modules.Identity.Data;
using RM_CMS.Modules.Identity.Domain;
using RM_CMS.Modules.Identity.Services;
using RM_CMS.Modules.People.Api;
using RM_CMS.Modules.People.Data;
using RM_CMS.Modules.People.Domain;
using RM_CMS.Utilities;

namespace RM_CMS.Modules.People.Services
{
    public interface IPeopleService
    {
        Task<ApiResponse<PagedResult<PersonSummaryDto>>> SearchAsync(
            int page, int pageSize, string? search, string? lifecycleStatus, string? campusId, bool? doNotContact);

        Task<ApiResponse<PersonDto>> GetAsync(string publicId);

        /// <summary>
        /// Records a visitor.
        ///
        /// A mobile number somebody already holds is NOT a refusal — families share a
        /// phone. It requires a relationship to the base visitor on that number, and
        /// says so with <see cref="ResponseCodes.RelationshipRequired"/>. Any other
        /// contact detail still refuses on a match unless the caller opts in.
        /// </summary>
        Task<ApiResponse<PersonDto>> CreateAsync(CreatePersonRequest request);

        Task<ApiResponse<PersonDto>> UpdateAsync(string publicId, UpdatePersonRequest request);

        /// <summary>
        /// Files an EXISTING person against an area, creating it when the typed name
        /// matches nothing. Used when an administrator gives somebody already on file
        /// a volunteer role and records their area at the same time — every other
        /// field of theirs stays exactly as it was.
        /// </summary>
        Task<ApiResponse<PersonDto>> SetAreaAsync(string publicId, string? areaId, string? areaName);

        Task<ApiResponse<IReadOnlyList<PersonMatchDto>>> LookupAsync(string term);

        /// <summary>
        /// Who already holds this mobile number, and therefore whether the next person
        /// recorded on it needs a relationship.
        ///
        /// The intake screen asks BEFORE the operator saves, so the question — "what
        /// is Mary's relationship with John?" — is put while they are still looking at
        /// the form rather than after a rejected save. It is a courtesy, not the
        /// enforcement: <see cref="CreateAsync"/> resolves the base visitor again from
        /// the contact rows and never trusts what a screen sends.
        /// </summary>
        Task<ApiResponse<BaseVisitorDto>> GetBaseVisitorAsync(string mobile);

        /// <summary>
        /// One person, reduced to the fields the intake form collects, so a data-entry
        /// operator can correct a record they typed.
        /// </summary>
        Task<ApiResponse<IntakePersonDto>> GetForIntakeAsync(string publicId);
        Task<ApiResponse<IReadOnlyList<PersonMatchDto>>> PickerAsync(string term);

        Task<ApiResponse<bool>> SetDoNotContactAsync(string publicId, DoNotContactRequest request);
        Task<ApiResponse<bool>> SetLifecycleAsync(string publicId, LifecycleRequest request);
        Task<ApiResponse<bool>> DeleteAsync(string publicId);

        Task<ApiResponse<PersonDto>> AddContactAsync(string publicId, ContactRequest request);
        Task<ApiResponse<PersonDto>> RemoveContactAsync(string publicId, long contactId);
        Task<ApiResponse<PersonDto>> SetPrimaryContactAsync(string publicId, long contactId);
    }

    public sealed class PeopleService : IPeopleService
    {
        /// <summary>
        /// Returned when a caller asks for a person outside their campus. Deliberately
        /// identical to "not found": confirming that a record exists but is off-limits
        /// still leaks that the person is known to the system.
        /// </summary>
        private const string NotFound = "Person not found.";

        private readonly IPersonRepository _people;

        /// <summary>The Wife / Son / Brother vocabulary. See <see cref="IRelationshipTypeRepository"/>.</summary>
        private readonly IRelationshipTypeRepository _relationships;

        private readonly IAreaService _areas;
        private readonly ICurrentIdentity _current;
        private readonly IUserAccountRepository _accounts;
        private readonly TimeProvider _clock;
        private readonly ILogger<PeopleService> _logger;

        public PeopleService(
            IPersonRepository people,
            IRelationshipTypeRepository relationships,
            IAreaService areas,
            ICurrentIdentity current,
            IUserAccountRepository accounts,
            TimeProvider clock,
            ILogger<PeopleService> logger)
        {
            _people = people;
            _relationships = relationships;
            _areas = areas;
            _current = current;
            _accounts = accounts;
            _clock = clock;
            _logger = logger;
        }

        // ==================================================================
        // Reads
        // ==================================================================

        public async Task<ApiResponse<PagedResult<PersonSummaryDto>>> SearchAsync(
            int page, int pageSize, string? search, string? lifecycleStatus, string? campusId, bool? doNotContact)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);

            if (!string.IsNullOrWhiteSpace(lifecycleStatus) && !PersonLifecycle.IsKnown(lifecycleStatus))
                return Warn<PagedResult<PersonSummaryDto>>($"Unknown lifecycle status '{lifecycleStatus}'.");

            // A campus-scoped account may only ever see its own campus, whatever it asks for.
            var effectiveCampus = ScopeCampus(campusId);
            var campusKey = await _people.ResolveCampusIdAsync(effectiveCampus);

            if (effectiveCampus is not null && campusKey is null)
                return Warn<PagedResult<PersonSummaryDto>>("Unknown campus.");

            var query = new PersonQuery
            {
                Search = search,
                LifecycleStatus = lifecycleStatus,
                CampusId = campusKey,
                DoNotContact = doNotContact,
                Skip = (page - 1) * pageSize,
                Take = pageSize
            };

            var total = await _people.CountAsync(query);
            var people = await _people.SearchAsync(query);

            return Ok(new PagedResult<PersonSummaryDto>
            {
                Items = people.Select(ToSummary).ToList(),
                Page = page,
                PageSize = pageSize,
                TotalCount = total
            }, "People retrieved");
        }

        public async Task<ApiResponse<PersonDto>> GetAsync(string publicId)
        {
            var person = await _people.GetByPublicIdAsync(publicId);

            if (person is null || !CanAccess(person))
                return Warn<PersonDto>(NotFound);

            return Ok(ToDto(person), "Person retrieved");
        }

        /// <summary>
        /// Duplicate check for intake. Contact values come back masked — an operator
        /// needs to know a number is already on file, not to read everyone's details.
        /// </summary>
        public async Task<ApiResponse<IReadOnlyList<PersonMatchDto>>> LookupAsync(string term)
        {
            if (string.IsNullOrWhiteSpace(term) || term.Trim().Length < 3)
                return Ok<IReadOnlyList<PersonMatchDto>>(Array.Empty<PersonMatchDto>(), "Enter at least 3 characters");

            var normalized = ContactNormalizer.Normalize(
                ContactNormalizer.LooksLikeEmail(term) ? ContactTypes.Email : ContactTypes.Mobile, term);

            var byContact = await _people.FindByContactAsync(new[] { normalized });
            var byName = await _people.FindByNameAsync(term, 10);

            var matches = byContact.Concat(byName)
                .DistinctBy(p => p.Id)
                .Where(CanAccess)
                .Take(10)
                .Select(ToMatch)
                .ToList();

            return Ok<IReadOnlyList<PersonMatchDto>>(matches, matches.Count > 0 ? "Possible matches" : "No matches");
        }

        public async Task<ApiResponse<BaseVisitorDto>> GetBaseVisitorAsync(string mobile)
        {
            var result = new BaseVisitorDto();

            if (string.IsNullOrWhiteSpace(mobile))
                return Ok(result, "No number given.");

            var normalized = ContactNormalizer.Normalize(ContactTypes.Mobile, mobile);
            var baseVisitor = await _people.FindBaseByContactAsync(normalized);

            // Off-limits is answered as "nobody", exactly as CreateAsync treats it.
            // Naming somebody at another campus would disclose that they exist, and
            // the two must agree or the screen asks a question the save then refuses.
            if (baseVisitor is null || !CanAccess(baseVisitor))
                return Ok(result, "Nobody holds this number yet.");

            result.RelationshipRequired = true;
            result.BaseVisitorId = baseVisitor.PublicId;
            result.BaseVisitorName = baseVisitor.FullName;

            // Everyone already on the number, so the operator can see the household
            // they are adding to rather than just the one name.
            var household = await _people.FindByBasePersonAsync(baseVisitor.Id);

            result.HouseholdNames = household
                .Where(CanAccess)
                .Select(p => p.RelationshipLabel is null
                    ? p.FullName
                    : $"{p.FullName} ({p.RelationshipLabel.ToLowerInvariant()})")
                .ToList();

            return Ok(result, $"{baseVisitor.FullName} already holds this number.");
        }

        /// <summary>
        /// One person as the intake screen sees them.
        /// </summary>
        /// <remarks>
        /// Goes through exactly the same campus check as every other read here — an
        /// operator cannot correct somebody at a site they have no access to, and an
        /// off-limits record answers "not found" rather than confirming it exists.
        /// </remarks>
        public async Task<ApiResponse<IntakePersonDto>> GetForIntakeAsync(string publicId)
        {
            var person = await _people.GetByPublicIdAsync(publicId);

            if (person is null || !CanAccess(person))
                return Warn<IntakePersonDto>(NotFound);

            return Ok(new IntakePersonDto
            {
                Id = person.PublicId,
                GivenName = person.GivenName,
                FamilyName = person.FamilyName,
                CampusId = person.CampusPublicId,
                AgeBand = person.AgeBand,
                Gender = person.Gender,
                HouseholdType = person.HouseholdType,
                AddressLine = person.AddressLine,
                Locality = person.Locality,
                AreaId = person.AreaPublicId,
                AreaName = person.AreaName,
                PostalCode = person.PostalCode,
                IsLocal = person.IsLocal,
                Notes = person.Notes,
                Mobile = person.PrimaryContact(ContactTypes.Mobile)?.Value,
                Email = person.PrimaryContact(ContactTypes.Email)?.Value,
                RowVersion = person.RowVersion
            }, "Person.");
        }

        /// <summary>
        /// Name search for the person picker — the admin "grant access" flow needs to
        /// find a person before it can attach an account.
        /// </summary>
        public async Task<ApiResponse<IReadOnlyList<PersonMatchDto>>> PickerAsync(string term)
        {
            if (string.IsNullOrWhiteSpace(term) || term.Trim().Length < 2)
                return Ok<IReadOnlyList<PersonMatchDto>>(Array.Empty<PersonMatchDto>(), "Enter at least 2 characters");

            var people = await _people.FindByNameAsync(term, 20);

            var matches = people.Where(CanAccess).Select(ToMatch).ToList();

            return Ok<IReadOnlyList<PersonMatchDto>>(matches, "Matches");
        }

        // ==================================================================
        // Create
        // ==================================================================

        public async Task<ApiResponse<PersonDto>> CreateAsync(CreatePersonRequest request)
        {
            try
            {
                var contacts = new List<PersonContact>();
                var errors = new List<string>();

                foreach (var item in request.Contacts)
                {
                    var type = (item.ContactType ?? string.Empty).Trim().ToUpperInvariant();

                    if (!ContactTypes.IsKnown(type))
                    {
                        errors.Add($"Unknown contact type '{item.ContactType}'.");
                        continue;
                    }

                    var value = (item.Value ?? string.Empty).Trim();
                    var normalized = ContactNormalizer.Normalize(type, value);

                    if (string.IsNullOrWhiteSpace(normalized))
                    {
                        errors.Add($"'{value}' is not a usable {type.ToLowerInvariant()}.");
                        continue;
                    }

                    if (type == ContactTypes.Email && !ContactNormalizer.LooksLikeEmail(value))
                    {
                        errors.Add($"'{value}' is not a valid email address.");
                        continue;
                    }

                    contacts.Add(new PersonContact
                    {
                        ContactType = type,
                        Value = value,
                        NormalizedValue = normalized,
                        IsPrimary = item.IsPrimary
                    });
                }

                if (errors.Count > 0)
                    return Warn<PersonDto>(string.Join(" ", errors));

                if (contacts.Count == 0)
                    return Warn<PersonDto>("At least one contact number or email is required.");

                // Exactly one primary per type. If the caller flagged none, the first of
                // each type wins — otherwise nothing would be reachable by default.
                foreach (var group in contacts.GroupBy(c => c.ContactType))
                {
                    if (!group.Any(c => c.IsPrimary))
                        group.First().IsPrimary = true;
                    else
                        foreach (var extra in group.Where(c => c.IsPrimary).Skip(1))
                            extra.IsPrimary = false;
                }

                if (!string.IsNullOrWhiteSpace(request.AgeBand) && !AgeBands.IsKnown(request.AgeBand))
                    return Warn<PersonDto>($"Unknown age band '{request.AgeBand}'.");

                // ---- shared phone: find the base visitor ----
                //
                // A FAMILY SHARES A PHONE, so a number already on file is not an error
                // to be argued past. It is a question: this number is already John's,
                // so who is this person to John?
                //
                // The base visitor is resolved from the CONTACT ROWS, never from what
                // the caller sent. A screen can name the wrong base — it read the
                // number a moment ago, and somebody may have been recorded on it
                // since — and the whole point of the rule is that the base does not
                // move once it is set.
                var mobile = contacts.FirstOrDefault(c => c.ContactType == ContactTypes.Mobile);

                Person? baseVisitor = null;

                if (mobile is not null)
                {
                    var candidate = await _people.FindBaseByContactAsync(mobile.NormalizedValue);

                    // A base visitor at a campus this operator cannot reach is one they
                    // must not be told about, so they are treated as absent and this
                    // person becomes a base visitor on their own. Recording them is
                    // better than refusing, and the alternative leaks that the number
                    // is known.
                    if (candidate is not null && CanAccess(candidate)) baseVisitor = candidate;
                }

                string? relationshipCode = null;

                if (baseVisitor is not null)
                {
                    relationshipCode = Clean(request.RelationshipCode)?.ToUpperInvariant();

                    if (relationshipCode is null)
                    {
                        // Not a rejection of the person — a request for the one fact
                        // that is missing. The code lets the screen open the
                        // relationship prompt rather than showing this as an error.
                        return new ApiResponse<PersonDto>(
                            ResponseType.Warning,
                            $"{mobile!.Value} is already recorded for {baseVisitor.FullName}. " +
                            $"Say how {request.GivenName.Trim()} is related to them and save again.",
                            null!,
                            ResponseCodes.RelationshipRequired);
                    }

                    if (!await _relationships.IsSelectableAsync(relationshipCode))
                        return Warn<PersonDto>($"Unknown relationship '{request.RelationshipCode}'.");
                }
                else if (relationshipCode is null && !string.IsNullOrWhiteSpace(request.RelationshipCode))
                {
                    // Nobody to be related TO. Silently dropping it would file the
                    // first person on a number as somebody's wife with no husband.
                    return Warn<PersonDto>(
                        "A relationship can only be recorded against somebody who already " +
                        "holds this mobile number. Nobody does, so this person is the first.");
                }

                // ---- other contact details still collide the old way ----
                //
                // Sharing a MOBILE is a family. Sharing an email or a landline is not
                // the case this rule is about, so those keep the existing "records
                // exist, save anyway if you meant to" behaviour.
                if (!request.AllowDuplicate && baseVisitor is null)
                {
                    var existing = await _people.FindByContactAsync(contacts.Select(c => c.NormalizedValue));
                    var visible = existing.Where(CanAccess).ToList();

                    if (visible.Count > 0)
                    {
                        _logger.LogInformation(
                            "Intake blocked: {Count} existing people share a contact detail", visible.Count);

                        return new ApiResponse<PersonDto>(
                            ResponseType.Warning,
                            DescribeDuplicate(visible, contacts),
                            null!,
                            ResponseCodes.DuplicateContact);
                    }
                }

                // ---- campus ----
                var campusPublicId = request.CampusId ?? _current.DefaultCampusId;
                var campusKey = await _people.ResolveCampusIdAsync(campusPublicId);

                if (campusPublicId is not null && campusKey is null)
                    return Warn<PersonDto>("Unknown campus.");

                if (!_current.CanAccessCampus(campusPublicId))
                    return Warn<PersonDto>("You cannot record a person for that campus.");

                var actingUserId = await ActingUserIdAsync();

                // ---- area ----
                // Resolved BEFORE the person is written, so a bad area name refuses
                // the whole intake rather than saving somebody with the locality
                // silently dropped. A brand-new area is created here; that is the
                // "no match found, so save the area first" step.
                var area = await _areas.ResolveAsync(
                    request.AreaId, request.AreaName, campusKey, campusPublicId, actingUserId);

                if (area.Failed) return Warn<PersonDto>(area.Problem!);

                var person = new Person
                {
                    PublicId = Ulid.NewUlid(),
                    CampusId = campusKey,
                    GivenName = request.GivenName.Trim(),
                    FamilyName = Clean(request.FamilyName),
                    AgeBand = Clean(request.AgeBand),
                    Gender = Clean(request.Gender),
                    HouseholdType = Clean(request.HouseholdType),

                    // Both, or neither. The database enforces the pair as well — a
                    // relationship to nobody, or a base visitor nobody stated a
                    // relationship to, is the state this whole flow exists to end.
                    BasePersonId = baseVisitor?.Id,
                    RelationshipCode = baseVisitor is null ? null : relationshipCode,

                    AddressLine = Clean(request.AddressLine),
                    Locality = Clean(request.Locality),
                    AreaId = area.AreaId,
                    PostalCode = Clean(request.PostalCode),
                    IsLocal = request.IsLocal,
                    LifecycleStatus = PersonLifecycle.Visitor,
                    Notes = Clean(request.Notes)
                };

                var id = await _people.CreateAsync(person, contacts, actingUserId);

                _logger.LogInformation("Person {PublicId} recorded by {Account}", person.PublicId, _current.AccountId);

                var created = await _people.GetByIdAsync(id);

                if (created is null)
                    return Fail<PersonDto>("The person was created but could not be read back.");

                var message = baseVisitor is null
                    ? "Person recorded"
                    : $"Person recorded as {created.RelationshipLabel?.ToLowerInvariant() ?? "a relative"} " +
                      $"of {baseVisitor.FullName}.";

                return Ok(ToDto(created), message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error recording a person");
                return Fail<PersonDto>("Unable to record the person.");
            }
        }

        // ==================================================================
        // Update
        // ==================================================================

        public async Task<ApiResponse<PersonDto>> UpdateAsync(string publicId, UpdatePersonRequest request)
        {
            var person = await _people.GetByPublicIdAsync(publicId);

            if (person is null || !CanAccess(person))
                return Warn<PersonDto>(NotFound);

            if (!string.IsNullOrWhiteSpace(request.AgeBand) && !AgeBands.IsKnown(request.AgeBand))
                return Warn<PersonDto>($"Unknown age band '{request.AgeBand}'.");

            var campusPublicId = request.CampusId ?? person.CampusPublicId;

            if (!_current.CanAccessCampus(campusPublicId))
                return Warn<PersonDto>("You cannot move a person to that campus.");

            var campusKey = request.CampusId is null
                ? person.CampusId
                : await _people.ResolveCampusIdAsync(request.CampusId);

            if (request.CampusId is not null && campusKey is null)
                return Warn<PersonDto>("Unknown campus.");

            // Contact corrections are worked out BEFORE anything is written, so an
            // unusable number refuses the whole save instead of storing the name
            // change and dropping the number the operator actually came to fix.
            var contactPlan = PlanContactCorrections(person, request);

            if (contactPlan.Problem is not null)
                return Warn<PersonDto>(contactPlan.Problem);

            if (contactPlan.Mobile is not null)
            {
                var clash = (await _people.FindByContactAsync(new[] { contactPlan.Mobile.Normalized }))
                    .FirstOrDefault(p => p.Id != person.Id && CanAccess(p));

                // No "save anyway" here, deliberately. At intake a shared number is a
                // real case — a family phone — and recording a second person is the
                // answer. On a correction it means the digits now point at somebody
                // else's record, and the operator has either mistyped again or is
                // editing the wrong person.
                if (clash is not null)
                    return Warn<PersonDto>(
                        $"That mobile number already belongs to {clash.FullName}. " +
                        "Check the number, or correct that record instead.");
            }

            person.GivenName = request.GivenName.Trim();
            person.FamilyName = Clean(request.FamilyName);
            person.CampusId = campusKey;
            person.AgeBand = Clean(request.AgeBand);
            person.Gender = Clean(request.Gender);
            person.HouseholdType = Clean(request.HouseholdType);
            var actingUserId = await ActingUserIdAsync();

            // An area the caller did not name leaves the existing one alone, the same
            // way an omitted campus does. Clearing it is a separate act: send an empty
            // areaName with no areaId.
            var area = await _areas.ResolveAsync(
                request.AreaId, request.AreaName, campusKey, campusPublicId, actingUserId);

            if (area.Failed) return Warn<PersonDto>(area.Problem!);

            person.AddressLine = Clean(request.AddressLine);
            person.Locality = Clean(request.Locality);
            person.AreaId = area.AreaId;
            person.PostalCode = Clean(request.PostalCode);
            person.IsLocal = request.IsLocal;
            person.Notes = Clean(request.Notes);
            person.RowVersion = request.RowVersion;

            var updated = await _people.UpdateAsync(person, actingUserId);

            if (!updated)
                return Warn<PersonDto>("This record was changed by someone else. Reload and try again.");

            // After the row-version guard, so a lost update refuses the contact
            // changes too rather than applying them to a record somebody else moved on.
            await ApplyContactCorrectionsAsync(person, contactPlan, actingUserId);

            var fresh = await _people.GetByIdAsync(person.Id);
            return Ok(ToDto(fresh!), "Person updated");
        }

        /// <summary>One contact value to write, already validated and normalized.</summary>
        private sealed record ContactWrite(string Value, string Normalized);

        /// <summary>
        /// What an update should do to a person's mobile and email: correct the value
        /// in place, add one that was missing, remove one that was cleared, or nothing.
        /// </summary>
        private sealed record ContactPlan(
            ContactWrite? Mobile,
            ContactWrite? Email,
            bool RemoveEmail,
            string? Problem);

        /// <summary>
        /// Works out the contact changes without making any. Pure, so the caller can
        /// refuse the whole save on a bad value before touching the record.
        /// </summary>
        /// <remarks>
        /// Omitted (null) means "leave it alone", which is what every caller that does
        /// not collect the field sends. Only email can be cleared, by sending an empty
        /// string: a person with no contact at all cannot be followed up, and emptying
        /// the mobile box is far more likely to be an accident than an instruction.
        /// </remarks>
        private static ContactPlan PlanContactCorrections(Person person, UpdatePersonRequest request)
        {
            ContactWrite? mobile = null;
            ContactWrite? email = null;
            var removeEmail = false;

            if (!string.IsNullOrWhiteSpace(request.Mobile))
            {
                var value = request.Mobile.Trim();
                var normalized = ContactNormalizer.Normalize(ContactTypes.Mobile, value);

                if (string.IsNullOrWhiteSpace(normalized))
                    return new ContactPlan(null, null, false, $"'{value}' is not a usable mobile number.");

                // Unchanged numbers are not rewritten: the write clears is_verified,
                // and re-saving the form without touching the number should not
                // un-verify it.
                var current = person.PrimaryContact(ContactTypes.Mobile);

                if (current is null || !string.Equals(current.NormalizedValue, normalized, StringComparison.Ordinal))
                    mobile = new ContactWrite(value, normalized);
            }

            if (request.Email is not null)
            {
                var value = request.Email.Trim();
                var current = person.PrimaryContact(ContactTypes.Email);

                if (value.Length == 0)
                {
                    removeEmail = current is not null;
                }
                else if (!ContactNormalizer.LooksLikeEmail(value))
                {
                    return new ContactPlan(null, null, false, $"'{value}' is not a valid email address.");
                }
                else
                {
                    var normalized = ContactNormalizer.Normalize(ContactTypes.Email, value);

                    if (current is null || !string.Equals(current.NormalizedValue, normalized, StringComparison.Ordinal))
                        email = new ContactWrite(value, normalized);
                }
            }

            return new ContactPlan(mobile, email, removeEmail, null);
        }

        /// <summary>
        /// Carries out a <see cref="ContactPlan"/>. A value already on file is
        /// corrected in place so its id, primary flag and verification history stay
        /// with it; a missing one is inserted and promoted to primary.
        /// </summary>
        private async Task ApplyContactCorrectionsAsync(Person person, ContactPlan plan, long? actingUserId)
        {
            if (plan.Mobile is not null)
                await WriteContactAsync(person, ContactTypes.Mobile, plan.Mobile, actingUserId);

            if (plan.Email is not null)
                await WriteContactAsync(person, ContactTypes.Email, plan.Email, actingUserId);

            if (plan.RemoveEmail)
            {
                var current = person.PrimaryContact(ContactTypes.Email);

                if (current is not null)
                    await _people.RemoveContactAsync(person.Id, current.Id);
            }
        }

        private async Task WriteContactAsync(
            Person person, string contactType, ContactWrite write, long? actingUserId)
        {
            var current = person.PrimaryContact(contactType);

            if (current is not null)
            {
                await _people.UpdateContactValueAsync(
                    person.Id, current.Id, write.Value, write.Normalized, actingUserId);

                if (!current.IsPrimary)
                    await _people.SetPrimaryContactAsync(person.Id, current.Id, contactType);

                return;
            }

            var contactId = await _people.AddContactAsync(person.Id, new PersonContact
            {
                ContactType = contactType,
                Value = write.Value,
                NormalizedValue = write.Normalized,
                IsPrimary = true
            }, actingUserId);

            await _people.SetPrimaryContactAsync(person.Id, contactId, contactType);
        }

        /// <summary>
        /// Files an existing person against an area and changes nothing else.
        ///
        /// Separate from <see cref="UpdateAsync"/> because the caller — the add-user
        /// screen, attaching a role to somebody already on file — holds none of that
        /// person's other fields and no row version for them. Reading the record here
        /// and writing it straight back keeps the concurrency guard honest: the write
        /// still carries a version, and a save that lost a race to a real edit is
        /// reported rather than silently overwriting it.
        /// </summary>
        public async Task<ApiResponse<PersonDto>> SetAreaAsync(string publicId, string? areaId, string? areaName)
        {
            var person = await _people.GetByPublicIdAsync(publicId);

            if (person is null || !CanAccess(person))
                return Warn<PersonDto>(NotFound);

            var actingUserId = await ActingUserIdAsync();

            var area = await _areas.ResolveAsync(
                areaId, areaName, person.CampusId, person.CampusPublicId, actingUserId);

            if (area.Failed) return Warn<PersonDto>(area.Problem!);

            // Nothing was asked for. Not an error, and not a reason to clear what
            // they already have — the screen simply left the field empty.
            if (area.AreaId is null) return Ok(ToDto(person), "No area recorded.");

            if (area.AreaId == person.AreaId) return Ok(ToDto(person), "Area unchanged.");

            person.AreaId = area.AreaId;

            var updated = await _people.UpdateAsync(person, actingUserId);

            if (!updated)
                return Warn<PersonDto>("This record was changed by someone else. Reload and try again.");

            var fresh = await _people.GetByIdAsync(person.Id);
            return Ok(ToDto(fresh!), "Area recorded.");
        }

        public async Task<ApiResponse<bool>> SetDoNotContactAsync(string publicId, DoNotContactRequest request)
        {
            var person = await _people.GetByPublicIdAsync(publicId);

            if (person is null || !CanAccess(person))
                return new ApiResponse<bool>(ResponseType.Warning, NotFound, false);

            var now = _clock.GetUtcNow().UtcDateTime;

            var updated = await _people.SetDoNotContactAsync(
                person.Id, person.RowVersion, request.DoNotContact,
                Clean(request.Note), now, await ActingUserIdAsync());

            if (!updated)
                return new ApiResponse<bool>(ResponseType.Warning, "This record was changed by someone else. Reload and try again.", false);

            // Consent decisions are logged loudly: this stops all future contact and
            // must be explainable afterwards.
            _logger.LogWarning(
                "Do-not-contact {State} for person {PublicId} by {Account}",
                request.DoNotContact ? "SET" : "CLEARED", publicId, _current.AccountId);

            return new ApiResponse<bool>(ResponseType.Success,
                request.DoNotContact
                    ? "Marked do-not-contact. No new care case can be opened for this person."
                    : "Do-not-contact removed.",
                true);
        }

        public async Task<ApiResponse<bool>> SetLifecycleAsync(string publicId, LifecycleRequest request)
        {
            if (!PersonLifecycle.IsKnown(request.LifecycleStatus))
                return new ApiResponse<bool>(ResponseType.Warning, $"Unknown lifecycle status '{request.LifecycleStatus}'.", false);

            var person = await _people.GetByPublicIdAsync(publicId);

            if (person is null || !CanAccess(person))
                return new ApiResponse<bool>(ResponseType.Warning, NotFound, false);

            // Becoming a member is dated; every other transition is not.
            DateTime? becameMemberOn = request.LifecycleStatus == PersonLifecycle.Member
                ? request.BecameMemberOn ?? _clock.GetUtcNow().UtcDateTime.Date
                : null;

            var updated = await _people.SetLifecycleAsync(
                person.Id, person.RowVersion, request.LifecycleStatus, becameMemberOn, await ActingUserIdAsync());

            return updated
                ? new ApiResponse<bool>(ResponseType.Success, "Status updated", true)
                : new ApiResponse<bool>(ResponseType.Warning, "This record was changed by someone else. Reload and try again.", false);
        }

        public async Task<ApiResponse<bool>> DeleteAsync(string publicId)
        {
            var person = await _people.GetByPublicIdAsync(publicId);

            if (person is null || !CanAccess(person))
                return new ApiResponse<bool>(ResponseType.Warning, NotFound, false);

            var deleted = await _people.SoftDeleteAsync(
                person.Id, person.RowVersion, _clock.GetUtcNow().UtcDateTime, await ActingUserIdAsync());

            if (!deleted)
                return new ApiResponse<bool>(ResponseType.Warning, "This record was changed by someone else. Reload and try again.", false);

            _logger.LogWarning("Person {PublicId} deleted by {Account}", publicId, _current.AccountId);

            return new ApiResponse<bool>(ResponseType.Success, "Person removed", true);
        }

        // ==================================================================
        // Contacts
        // ==================================================================

        public async Task<ApiResponse<PersonDto>> AddContactAsync(string publicId, ContactRequest request)
        {
            var person = await _people.GetByPublicIdAsync(publicId);

            if (person is null || !CanAccess(person))
                return Warn<PersonDto>(NotFound);

            var type = (request.ContactType ?? string.Empty).Trim().ToUpperInvariant();

            if (!ContactTypes.IsKnown(type))
                return Warn<PersonDto>($"Unknown contact type '{request.ContactType}'.");

            var value = (request.Value ?? string.Empty).Trim();
            var normalized = ContactNormalizer.Normalize(type, value);

            if (string.IsNullOrWhiteSpace(normalized))
                return Warn<PersonDto>("That contact value is not usable.");

            if (type == ContactTypes.Email && !ContactNormalizer.LooksLikeEmail(value))
                return Warn<PersonDto>("That is not a valid email address.");

            if (person.Contacts.Any(c => c.ContactType == type && c.NormalizedValue == normalized))
                return Warn<PersonDto>("That contact is already recorded for this person.");

            var contactId = await _people.AddContactAsync(person.Id, new PersonContact
            {
                ContactType = type,
                Value = value,
                NormalizedValue = normalized,
                IsPrimary = request.IsPrimary
            }, await ActingUserIdAsync());

            // Promoting after insert keeps "one primary per type" in one place.
            if (request.IsPrimary)
                await _people.SetPrimaryContactAsync(person.Id, contactId, type);

            var fresh = await _people.GetByIdAsync(person.Id);
            return Ok(ToDto(fresh!), "Contact added");
        }

        public async Task<ApiResponse<PersonDto>> RemoveContactAsync(string publicId, long contactId)
        {
            var person = await _people.GetByPublicIdAsync(publicId);

            if (person is null || !CanAccess(person))
                return Warn<PersonDto>(NotFound);

            if (person.Contacts.Count <= 1)
                return Warn<PersonDto>("A person must keep at least one contact point.");

            var removed = await _people.RemoveContactAsync(person.Id, contactId);

            if (!removed)
                return Warn<PersonDto>("Contact not found.");

            var fresh = await _people.GetByIdAsync(person.Id);
            return Ok(ToDto(fresh!), "Contact removed");
        }

        public async Task<ApiResponse<PersonDto>> SetPrimaryContactAsync(string publicId, long contactId)
        {
            var person = await _people.GetByPublicIdAsync(publicId);

            if (person is null || !CanAccess(person))
                return Warn<PersonDto>(NotFound);

            var contact = person.Contacts.FirstOrDefault(c => c.Id == contactId);

            if (contact is null)
                return Warn<PersonDto>("Contact not found.");

            await _people.SetPrimaryContactAsync(person.Id, contactId, contact.ContactType);

            var fresh = await _people.GetByIdAsync(person.Id);
            return Ok(ToDto(fresh!), "Primary contact updated");
        }

        // ==================================================================
        // Helpers
        // ==================================================================

        /// <summary>
        /// A campus-scoped account may only reach its own campus. People with no
        /// campus are visible to everyone authenticated.
        /// </summary>
        private bool CanAccess(Person person) => _current.CanAccessCampus(person.CampusPublicId);

        /// <summary>
        /// Says which detail clashed, with whom, and what kind it was.
        ///
        /// This used to read "Someone with this contact number is already recorded"
        /// regardless — so a duplicate EMAIL was reported as a duplicate phone number,
        /// and the operator had no idea which of the two fields to change. It also
        /// told them to "resubmit with allowDuplicate", which is a request-body flag,
        /// not something anybody can do from a screen.
        /// </summary>
        private static string DescribeDuplicate(
            IReadOnlyList<Person> matches, IReadOnlyList<PersonContact> submitted)
        {
            var submittedByValue = submitted
                .GroupBy(c => c.NormalizedValue, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            // Which of the values actually collided, and on whom. A person can match on
            // more than one, so each clash is reported once with the name beside it.
            var clashes = new List<string>();

            foreach (var person in matches)
            {
                foreach (var contact in person.Contacts ?? Enumerable.Empty<PersonContact>())
                {
                    if (!submittedByValue.TryGetValue(contact.NormalizedValue, out var mine)) continue;

                    var label = mine.ContactType switch
                    {
                        "EMAIL" => "email",
                        "MOBILE" => "mobile number",
                        "WHATSAPP" => "WhatsApp number",
                        "LANDLINE" => "landline",
                        _ => "contact detail"
                    };

                    var line = $"The {label} {mine.Value} is already recorded for {person.FullName}";

                    if (!clashes.Contains(line, StringComparer.Ordinal)) clashes.Add(line);
                }
            }

            // The lookup matched on something, so an empty list means the matching
            // contact is one this caller cannot see. Say that rather than nothing.
            if (clashes.Count == 0)
            {
                return $"These details are already recorded for {string.Join(", ", matches.Select(p => p.FullName))}. " +
                       "Open that record, or record this as a separate person if they are genuinely different.";
            }

            return string.Join(". ", clashes) +
                   ". Open that record, or record this as a separate person if they are genuinely different.";
        }

        /// <summary>Forces a campus-scoped caller onto their own campus regardless of what they asked for.</summary>
        private string? ScopeCampus(string? requested) =>
            string.IsNullOrWhiteSpace(_current.CampusId) ? requested : _current.CampusId;

        /// <summary>
        /// Internal key of the signed-in account, for the audit columns. Null for the
        /// scheduler, which has no identity.
        /// </summary>
        private async Task<long?> ActingUserIdAsync()
        {
            if (string.IsNullOrWhiteSpace(_current.AccountId)) return null;

            var account = await _accounts.GetByPublicIdAsync(_current.AccountId);
            return account?.Id;
        }

        private static string? Clean(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static ApiResponse<T> Ok<T>(T data, string message) => new(ResponseType.Success, message, data);
        private static ApiResponse<T> Warn<T>(string message) => new(ResponseType.Warning, message, default!);
        private static ApiResponse<T> Fail<T>(string message) => new(ResponseType.Error, message, default!);

        private static PersonDto ToDto(Person p) => new()
        {
            Id = p.PublicId,
            ReferenceCode = p.ReferenceCode,
            GivenName = p.GivenName,
            FamilyName = p.FamilyName,
            FullName = p.FullName,
            CampusId = p.CampusPublicId,
            CampusName = p.CampusName,
            AgeBand = p.AgeBand,
            Gender = p.Gender,
            HouseholdType = p.HouseholdType,
            BaseVisitorId = p.BasePersonPublicId,
            BaseVisitorName = p.BasePersonName,
            RelationshipCode = p.RelationshipCode,
            RelationshipLabel = p.RelationshipLabel,
            AddressLine = p.AddressLine,
            Locality = p.Locality,
            AreaId = p.AreaPublicId,
            AreaName = p.AreaName,
            PostalCode = p.PostalCode,
            IsLocal = p.IsLocal,
            LifecycleStatus = p.LifecycleStatus,
            BecameMemberOn = p.BecameMemberOn,
            DoNotContact = p.DoNotContact,
            DoNotContactAt = p.DoNotContactAt,
            DoNotContactNote = p.DoNotContactNote,
            Notes = p.Notes,
            RowVersion = p.RowVersion,
            CreatedAt = p.CreatedAt,
            UpdatedAt = p.UpdatedAt,
            Contacts = p.Contacts.Select(c => new ContactDto
            {
                Id = c.Id,
                ContactType = c.ContactType,
                Value = c.Value,
                IsPrimary = c.IsPrimary,
                IsVerified = c.IsVerified,
                OptedOut = c.OptedOutAt.HasValue
            }).ToList()
        };

        private static PersonSummaryDto ToSummary(Person p) => new()
        {
            Id = p.PublicId,
            ReferenceCode = p.ReferenceCode,
            FullName = p.FullName,
            CampusName = p.CampusName,
            LifecycleStatus = p.LifecycleStatus,
            DoNotContact = p.DoNotContact,
            PrimaryPhone = p.PrimaryContact(ContactTypes.Mobile)?.Value,
            PrimaryEmail = p.PrimaryContact(ContactTypes.Email)?.Value,
            CreatedAt = p.CreatedAt
        };

        private static PersonMatchDto ToMatch(Person p) => new()
        {
            Id = p.PublicId,
            FullName = p.FullName,
            // Masked on purpose — see PersonMatchDto.
            MaskedContact = ContactNormalizer.Mask(p.PrimaryContact(ContactTypes.Mobile)?.Value),
            LifecycleStatus = p.LifecycleStatus,
            CreatedAt = p.CreatedAt
        };
    }
}
