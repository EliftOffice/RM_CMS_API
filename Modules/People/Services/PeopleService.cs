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
        /// Records a visitor. Refuses on a contact-number match unless the caller
        /// explicitly opts in, and returns the matches so intake can offer them.
        /// </summary>
        Task<ApiResponse<PersonDto>> CreateAsync(CreatePersonRequest request);

        Task<ApiResponse<PersonDto>> UpdateAsync(string publicId, UpdatePersonRequest request);

        Task<ApiResponse<IReadOnlyList<PersonMatchDto>>> LookupAsync(string term);
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
        private readonly ICurrentIdentity _current;
        private readonly IUserAccountRepository _accounts;
        private readonly TimeProvider _clock;
        private readonly ILogger<PeopleService> _logger;

        public PeopleService(
            IPersonRepository people,
            ICurrentIdentity current,
            IUserAccountRepository accounts,
            TimeProvider clock,
            ILogger<PeopleService> logger)
        {
            _people = people;
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

                // ---- duplicate detection ----
                if (!request.AllowDuplicate)
                {
                    var existing = await _people.FindByContactAsync(contacts.Select(c => c.NormalizedValue));
                    var visible = existing.Where(CanAccess).ToList();

                    if (visible.Count > 0)
                    {
                        _logger.LogInformation(
                            "Intake blocked: {Count} existing people share a contact number", visible.Count);

                        return new ApiResponse<PersonDto>(
                            ResponseType.Warning,
                            $"Someone with this contact number is already recorded ({string.Join(", ", visible.Select(p => p.FullName))}). " +
                            "Open their record, or resubmit with allowDuplicate to record a separate person.",
                            null!);
                    }
                }

                // ---- campus ----
                var campusPublicId = request.CampusId ?? _current.DefaultCampusId;
                var campusKey = await _people.ResolveCampusIdAsync(campusPublicId);

                if (campusPublicId is not null && campusKey is null)
                    return Warn<PersonDto>("Unknown campus.");

                if (!_current.CanAccessCampus(campusPublicId))
                    return Warn<PersonDto>("You cannot record a person for that campus.");

                var person = new Person
                {
                    PublicId = Ulid.NewUlid(),
                    CampusId = campusKey,
                    GivenName = request.GivenName.Trim(),
                    FamilyName = Clean(request.FamilyName),
                    AgeBand = Clean(request.AgeBand),
                    Gender = Clean(request.Gender),
                    HouseholdType = Clean(request.HouseholdType),
                    AddressLine = Clean(request.AddressLine),
                    Locality = Clean(request.Locality),
                    PostalCode = Clean(request.PostalCode),
                    IsLocal = request.IsLocal,
                    LifecycleStatus = PersonLifecycle.Visitor,
                    Notes = Clean(request.Notes)
                };

                var actingUserId = await ActingUserIdAsync();
                var id = await _people.CreateAsync(person, contacts, actingUserId);

                _logger.LogInformation("Person {PublicId} recorded by {Account}", person.PublicId, _current.AccountId);

                var created = await _people.GetByIdAsync(id);

                return created is null
                    ? Fail<PersonDto>("The person was created but could not be read back.")
                    : Ok(ToDto(created), "Person recorded");
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

            person.GivenName = request.GivenName.Trim();
            person.FamilyName = Clean(request.FamilyName);
            person.CampusId = campusKey;
            person.AgeBand = Clean(request.AgeBand);
            person.Gender = Clean(request.Gender);
            person.HouseholdType = Clean(request.HouseholdType);
            person.AddressLine = Clean(request.AddressLine);
            person.Locality = Clean(request.Locality);
            person.PostalCode = Clean(request.PostalCode);
            person.IsLocal = request.IsLocal;
            person.Notes = Clean(request.Notes);
            person.RowVersion = request.RowVersion;

            var updated = await _people.UpdateAsync(person, await ActingUserIdAsync());

            if (!updated)
                return Warn<PersonDto>("This record was changed by someone else. Reload and try again.");

            var fresh = await _people.GetByIdAsync(person.Id);
            return Ok(ToDto(fresh!), "Person updated");
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
            AddressLine = p.AddressLine,
            Locality = p.Locality,
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
