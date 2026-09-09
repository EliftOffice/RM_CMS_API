using System.Text.RegularExpressions;

namespace RM_CMS.Modules.People.Domain
{
    /// <summary>
    /// A human being. The single canonical record — a visitor, a volunteer and a
    /// signed-in administrator are all rows in <c>person</c>, distinguished by what
    /// else references them.
    ///
    /// Never returned from a controller; map to a DTO first.
    /// </summary>
    public sealed class Person
    {
        public long Id { get; set; }
        public string PublicId { get; set; } = string.Empty;
        public string? ReferenceCode { get; set; }

        public long? CampusId { get; set; }
        public string? CampusPublicId { get; set; }
        public string? CampusName { get; set; }

        public string GivenName { get; set; } = string.Empty;
        public string? FamilyName { get; set; }

        /// <summary>Generated column — do not write to it.</summary>
        public string FullName { get; set; } = string.Empty;

        public DateTime? DateOfBirth { get; set; }
        public string? AgeBand { get; set; }
        public string? Gender { get; set; }
        public string? HouseholdType { get; set; }

        public string? AddressLine { get; set; }

        /// <summary>
        /// Free text, as typed. Written for somebody from OUT OF TOWN, where the
        /// locality is a one-off and not worth adding to the shared list. For anyone
        /// local it is <see cref="AreaId"/> that carries the locality.
        /// </summary>
        public string? Locality { get; set; }

        /// <summary>The controlled locality, from <c>area</c>. Null when none was chosen.</summary>
        public long? AreaId { get; set; }
        public string? AreaPublicId { get; set; }
        public string? AreaName { get; set; }

        public string? PostalCode { get; set; }

        /// <summary>
        /// Whether they live near enough for an in-person visit. Decides whether a
        /// nurture step can be a Visit or must be a Call.
        /// </summary>
        public bool IsLocal { get; set; } = true;

        public string LifecycleStatus { get; set; } = PersonLifecycle.Visitor;
        public DateTime? BecameMemberOn { get; set; }

        /// <summary>
        /// Consent. Set when the person asks not to be contacted. This is a property
        /// of the PERSON, not of one case: if they are recorded again later as a new
        /// visitor, the block still applies.
        /// </summary>
        public bool DoNotContact { get; set; }
        public DateTime? DoNotContactAt { get; set; }
        public string? DoNotContactNote { get; set; }

        public string? Notes { get; set; }

        public DateTime? DeletedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public int RowVersion { get; set; }

        /// <summary>Loaded separately from <c>person_contact</c>.</summary>
        public List<PersonContact> Contacts { get; set; } = new();

        public bool IsDeleted => DeletedAt.HasValue;

        /// <summary>The primary contact of a given type, if one is recorded.</summary>
        public PersonContact? PrimaryContact(string contactType) =>
            Contacts.FirstOrDefault(c =>
                string.Equals(c.ContactType, contactType, StringComparison.Ordinal) && c.IsPrimary)
            ?? Contacts.FirstOrDefault(c =>
                string.Equals(c.ContactType, contactType, StringComparison.Ordinal));
    }

    /// <summary>
    /// One contact point. Normalised out of <c>person</c> because the MVP's single
    /// phone/email columns had nowhere to put a second number, so extra numbers
    /// ended up buried in free-text notes.
    /// </summary>
    public sealed class PersonContact
    {
        public long Id { get; set; }
        public long PersonId { get; set; }

        public string ContactType { get; set; } = ContactTypes.Mobile;

        /// <summary>As entered and as displayed.</summary>
        public string Value { get; set; } = string.Empty;

        /// <summary>
        /// Canonical form used for matching and duplicate detection: digits-only for
        /// phones, lower-cased for email. Never shown to a user.
        /// </summary>
        public string NormalizedValue { get; set; } = string.Empty;

        public bool IsPrimary { get; set; }
        public bool IsVerified { get; set; }
        public DateTime? VerifiedAt { get; set; }

        /// <summary>Set when the person asks not to be reached this particular way.</summary>
        public DateTime? OptedOutAt { get; set; }

        public DateTime CreatedAt { get; set; }

        public bool IsContactable => OptedOutAt is null;
    }

    /// <summary>
    /// Where a person stands with the church. Mirrors the CHECK constraint on
    /// <c>person.lifecycle_status</c>; code branches on these, so they are a fixed
    /// vocabulary rather than an admin-editable lookup.
    /// </summary>
    public static class PersonLifecycle
    {
        public const string Visitor = "VISITOR";
        public const string Member = "MEMBER";
        public const string Lapsed = "LAPSED";
        public const string MovedAway = "MOVED_AWAY";
        public const string Deceased = "DECEASED";

        public static readonly string[] All = { Visitor, Member, Lapsed, MovedAway, Deceased };

        public static bool IsKnown(string? value) =>
            !string.IsNullOrWhiteSpace(value) && All.Contains(value, StringComparer.Ordinal);
    }

    /// <summary>Mirrors the CHECK on <c>person_contact.contact_type</c>.</summary>
    public static class ContactTypes
    {
        public const string Mobile = "MOBILE";
        public const string Email = "EMAIL";
        public const string Telegram = "TELEGRAM";
        public const string WhatsApp = "WHATSAPP";
        public const string Landline = "LANDLINE";

        public static readonly string[] All = { Mobile, Email, Telegram, WhatsApp, Landline };

        public static bool IsKnown(string? value) =>
            !string.IsNullOrWhiteSpace(value) && All.Contains(value, StringComparer.Ordinal);
    }

    /// <summary>Mirrors the CHECK on <c>person.age_band</c>.</summary>
    public static class AgeBands
    {
        public static readonly string[] All =
        {
            "UNDER_18", "18_25", "26_35", "36_45", "46_60", "OVER_60"
        };

        public static bool IsKnown(string? value) =>
            !string.IsNullOrWhiteSpace(value) && All.Contains(value, StringComparer.Ordinal);
    }

    /// <summary>
    /// Turns a contact value into its canonical form for matching.
    ///
    /// This is what makes duplicate detection work: "+91 98765 43210",
    /// "098765 43210" and "9876543210" are the same person's number typed three
    /// ways, and at intake a data-entry operator will type whichever they are given.
    /// </summary>
    public static partial class ContactNormalizer
    {
        /// <summary>
        /// Local subscriber-number length. Indian mobile numbers are 10 digits, so a
        /// longer string is treated as carrying a country/trunk prefix and reduced to
        /// its last 10 digits for comparison.
        /// </summary>
        private const int LocalNumberLength = 10;

        public static string Normalize(string contactType, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;

            return contactType switch
            {
                ContactTypes.Email => value.Trim().ToLowerInvariant(),

                // Telegram chat ids are opaque numeric ids, not phone numbers, so they
                // are compared verbatim.
                ContactTypes.Telegram => value.Trim(),

                _ => NormalizePhone(value)
            };
        }

        /// <summary>
        /// Reduces a phone number to comparable digits. Keeps the last
        /// <see cref="LocalNumberLength"/> digits so a country code or a leading zero
        /// does not make the same number look like two different people.
        /// </summary>
        public static string NormalizePhone(string value)
        {
            var digits = NonDigits().Replace(value ?? string.Empty, string.Empty);

            if (digits.Length == 0) return string.Empty;

            return digits.Length > LocalNumberLength
                ? digits[^LocalNumberLength..]
                : digits;
        }

        /// <summary>
        /// Masks a phone number for display to callers who may search but should not
        /// harvest contact details — the duplicate-check at intake, for instance.
        /// </summary>
        public static string Mask(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;

            var digits = NonDigits().Replace(value, string.Empty);

            return digits.Length <= 4
                ? new string('•', digits.Length)
                : new string('•', digits.Length - 4) + digits[^4..];
        }

        public static bool LooksLikeEmail(string? value) =>
            !string.IsNullOrWhiteSpace(value) && EmailShape().IsMatch(value.Trim());

        [GeneratedRegex(@"\D")]
        private static partial Regex NonDigits();

        [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
        private static partial Regex EmailShape();
    }
}
