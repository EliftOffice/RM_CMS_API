using System.ComponentModel.DataAnnotations;

namespace RM_CMS.Modules.People.Api
{
    // -------------------------------------------------------------------------
    // Requests
    //
    // Narrow by design: no domain model is ever model-bound, so a caller cannot
    // set lifecycleStatus, doNotContact, rowVersion or deletedAt by adding a field
    // to their JSON. Those move only through their own endpoints.
    // -------------------------------------------------------------------------

    public sealed class CreatePersonRequest
    {
        [Required(ErrorMessage = "First name is required.")]
        [StringLength(80, MinimumLength = 1)]
        public string GivenName { get; set; } = string.Empty;

        [StringLength(80)]
        public string? FamilyName { get; set; }

        /// <summary>Public id of the campus. Defaults to the caller's campus when omitted.</summary>
        [StringLength(26)]
        public string? CampusId { get; set; }

        [StringLength(20)] public string? AgeBand { get; set; }
        [StringLength(20)] public string? Gender { get; set; }
        [StringLength(40)] public string? HouseholdType { get; set; }

        [StringLength(200)] public string? AddressLine { get; set; }

        /// <summary>
        /// Free text, for somebody from out of town. Anyone local gets an
        /// <see cref="AreaId"/> or <see cref="AreaName"/> instead.
        /// </summary>
        [StringLength(100)] public string? Locality { get; set; }

        /// <summary>
        /// The area they live in, as a public id already chosen from the picker.
        /// Wins over <see cref="AreaName"/> when both arrive.
        /// </summary>
        [StringLength(26)] public string? AreaId { get; set; }

        /// <summary>
        /// The area they live in, as typed. When nothing on file matches, the area
        /// is created and then this person is filed against it — that find-or-create
        /// happens on the server so two operators typing the same new neighbourhood
        /// at once end up with one area, not two.
        /// </summary>
        [StringLength(100)] public string? AreaName { get; set; }

        [StringLength(20)]  public string? PostalCode { get; set; }

        public bool IsLocal { get; set; } = true;

        [StringLength(2000)] public string? Notes { get; set; }

        /// <summary>
        /// At least one contact point is required — a person nobody can reach cannot
        /// be followed up, which is the entire purpose of recording them.
        /// </summary>
        [Required]
        [MinLength(1, ErrorMessage = "At least one contact number or email is required.")]
        public List<ContactRequest> Contacts { get; set; } = new();

        /// <summary>
        /// Set true to record the person even when an existing one matches a contact
        /// number. The default refuses and returns the match, so intake does not
        /// silently create duplicates.
        /// </summary>
        public bool AllowDuplicate { get; set; }
    }

    public sealed class UpdatePersonRequest
    {
        [Required][StringLength(80, MinimumLength = 1)]
        public string GivenName { get; set; } = string.Empty;

        [StringLength(80)] public string? FamilyName { get; set; }
        [StringLength(26)] public string? CampusId { get; set; }
        [StringLength(20)] public string? AgeBand { get; set; }
        [StringLength(20)] public string? Gender { get; set; }
        [StringLength(40)] public string? HouseholdType { get; set; }
        [StringLength(200)] public string? AddressLine { get; set; }
        [StringLength(100)] public string? Locality { get; set; }

        /// <summary>
        /// The area they live in, as a public id already chosen from the picker.
        /// Wins over <see cref="AreaName"/> when both arrive.
        /// </summary>
        [StringLength(26)] public string? AreaId { get; set; }

        /// <summary>
        /// The area they live in, as typed. When nothing on file matches, the area
        /// is created and then this person is filed against it — that find-or-create
        /// happens on the server so two operators typing the same new neighbourhood
        /// at once end up with one area, not two.
        /// </summary>
        [StringLength(100)] public string? AreaName { get; set; }

        [StringLength(20)]  public string? PostalCode { get; set; }

        public bool IsLocal { get; set; } = true;

        [StringLength(2000)] public string? Notes { get; set; }

        /// <summary>
        /// From the record being edited. A mismatch means someone else saved first,
        /// and the update is refused rather than silently overwriting them.
        /// </summary>
        [Required] public int RowVersion { get; set; }
    }

    public sealed class ContactRequest
    {
        [Required][StringLength(20)]
        public string ContactType { get; set; } = "MOBILE";

        [Required][StringLength(255, MinimumLength = 3)]
        public string Value { get; set; } = string.Empty;

        public bool IsPrimary { get; set; }
    }

    /// <summary>
    /// Records or clears the do-not-contact flag. Separate from the general update
    /// because it is a consent decision, not an edit, and is restricted to team
    /// leads and above.
    /// </summary>
    public sealed class DoNotContactRequest
    {
        [Required] public bool DoNotContact { get; set; }

        [StringLength(255)]
        public string? Note { get; set; }
    }

    /// <summary>Moves a person along the lifecycle, e.g. VISITOR to MEMBER.</summary>
    public sealed class LifecycleRequest
    {
        [Required][StringLength(20)]
        public string LifecycleStatus { get; set; } = string.Empty;

        public DateTime? BecameMemberOn { get; set; }
    }

    // -------------------------------------------------------------------------
    // Responses
    // -------------------------------------------------------------------------

    /// <summary>Full detail. Public ids only — no internal keys.</summary>
    public sealed class PersonDto
    {
        public string Id { get; set; } = string.Empty;
        public string? ReferenceCode { get; set; }

        public string GivenName { get; set; } = string.Empty;
        public string? FamilyName { get; set; }
        public string FullName { get; set; } = string.Empty;

        public string? CampusId { get; set; }
        public string? CampusName { get; set; }

        public string? AgeBand { get; set; }
        public string? Gender { get; set; }
        public string? HouseholdType { get; set; }

        public string? AddressLine { get; set; }
        public string? Locality { get; set; }

        /// <summary>Public id of the area they live in. Null when none was recorded.</summary>
        public string? AreaId { get; set; }
        public string? AreaName { get; set; }

        public string? PostalCode { get; set; }
        public bool IsLocal { get; set; }

        public string LifecycleStatus { get; set; } = string.Empty;
        public DateTime? BecameMemberOn { get; set; }

        public bool DoNotContact { get; set; }
        public DateTime? DoNotContactAt { get; set; }
        public string? DoNotContactNote { get; set; }

        public string? Notes { get; set; }

        public List<ContactDto> Contacts { get; set; } = new();

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        /// <summary>Send back on update to detect a concurrent edit.</summary>
        public int RowVersion { get; set; }
    }

    /// <summary>Row shape for lists. Deliberately lighter than <see cref="PersonDto"/>.</summary>
    public sealed class PersonSummaryDto
    {
        public string Id { get; set; } = string.Empty;
        public string? ReferenceCode { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string? CampusName { get; set; }
        public string LifecycleStatus { get; set; } = string.Empty;
        public bool DoNotContact { get; set; }

        /// <summary>Primary mobile, in full. Lists are for staff who may contact them.</summary>
        public string? PrimaryPhone { get; set; }
        public string? PrimaryEmail { get; set; }

        public DateTime CreatedAt { get; set; }
    }

    public sealed class ContactDto
    {
        public long Id { get; set; }
        public string ContactType { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public bool IsPrimary { get; set; }
        public bool IsVerified { get; set; }
        public bool OptedOut { get; set; }
    }

    /// <summary>
    /// Duplicate-check result for the intake screen.
    ///
    /// Contact values are MASKED here. A data-entry operator needs to know "this
    /// number already belongs to Ravi K." to avoid creating a duplicate; they do not
    /// need the full contact details of everyone already recorded.
    /// </summary>
    public sealed class PersonMatchDto
    {
        public string Id { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string? MaskedContact { get; set; }
        public string LifecycleStatus { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// A person as the INTAKE screen sees them, for correcting a record.
    /// </summary>
    /// <remarks>
    /// Deliberately narrower than <c>PersonDto</c>, and that narrowness is the point.
    /// A data-entry operator needs to fix what they typed — a misheard name, a
    /// transposed digit, the wrong area — so this carries exactly the fields the
    /// intake form itself collects, plus the row version needed to save them.
    ///
    /// What it does NOT carry: lifecycle, do-not-contact and its note, reference code,
    /// or anything about cases. Those are pastoral decisions taken on other screens by
    /// other roles, and an operator who can see the intake form has no business
    /// reading them. `lookup` already masks contact values for the same reason, and
    /// widening the general GET would have quietly undone that.
    /// </remarks>
    public sealed class IntakePersonDto
    {
        public string Id { get; set; } = string.Empty;

        public string GivenName { get; set; } = string.Empty;
        public string? FamilyName { get; set; }

        public string? CampusId { get; set; }
        public string? AgeBand { get; set; }
        public string? Gender { get; set; }
        public string? HouseholdType { get; set; }

        public string? AddressLine { get; set; }
        public string? Locality { get; set; }
        public string? AreaId { get; set; }
        public string? AreaName { get; set; }
        public string? PostalCode { get; set; }

        public bool IsLocal { get; set; }
        public string? Notes { get; set; }

        /// <summary>
        /// Their mobile number in full, not masked.
        /// </summary>
        /// <remarks>
        /// The one place this screen shows a whole number. Correcting a transposed
        /// digit is impossible against a masked value, and the operator has already
        /// identified this specific person rather than browsing a list.
        /// </remarks>
        public string? Mobile { get; set; }

        public int RowVersion { get; set; }
    }

    public sealed class PagedResult<T>
    {
        public IReadOnlyList<T> Items { get; set; } = Array.Empty<T>();
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
        public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    }

    /// <summary>
    /// Returned when intake finds an existing person with the same contact number.
    /// The caller either picks the match or resubmits with allowDuplicate.
    /// </summary>
    public sealed class DuplicateWarningDto
    {
        public IReadOnlyList<PersonMatchDto> Matches { get; set; } = Array.Empty<PersonMatchDto>();
    }
}
