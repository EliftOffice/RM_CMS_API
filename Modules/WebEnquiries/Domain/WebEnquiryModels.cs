using System.Text.RegularExpressions;

namespace RM_CMS.Modules.WebEnquiries.Domain
{
    /// <summary>
    /// One thing somebody submitted through the public website.
    ///
    /// This is UNTRUSTED INPUT, held apart from the pastoral records on purpose. The
    /// submit endpoint is open to the internet; writing straight into <c>person</c>
    /// would let anyone create people, and the first spam run would sit beside real
    /// prayer requests with no way to tell them apart afterwards.
    ///
    /// So a submission lands here, a human with WEB_COORDINATOR reads it, and
    /// <see cref="LinkedPersonId"/> records what they decided it becomes.
    /// </summary>
    public sealed class WebEnquiry
    {
        public long Id { get; set; }
        public string PublicId { get; set; } = string.Empty;
        public string? ReferenceCode { get; set; }

        public string FormType { get; set; } = string.Empty;

        public long? CampusId { get; set; }
        public string? CampusName { get; set; }

        public string? FullName { get; set; }
        public string? Mobile { get; set; }
        public string? MobileNormalized { get; set; }
        public string? Email { get; set; }

        public string? City { get; set; }
        public string? Street { get; set; }
        public string? Landmark { get; set; }

        public string? ReferredByName { get; set; }
        public string? ReferredByMobile { get; set; }

        public string? Message { get; set; }

        /// <summary>Raw JSON for fields the website added that have no column yet.</summary>
        public string? Payload { get; set; }

        public string? SourcePage { get; set; }
        public string? UserAgent { get; set; }
        public string? SubmitterHash { get; set; }
        public bool IsSuspectedSpam { get; set; }

        public string Status { get; set; } = WebEnquiryStatus.New;
        public string? ReviewedByName { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public string? ReviewNote { get; set; }

        public long? LinkedPersonId { get; set; }
        public string? LinkedPersonPublicId { get; set; }
        public string? LinkedPersonName { get; set; }

        public DateTime SubmittedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public int RowVersion { get; set; }

        /// <summary>
        /// Somebody already on file shares this mobile number. Counted at read time so
        /// the coordinator sees "we already know this person" before deciding what the
        /// enquiry becomes, rather than creating a second copy of them.
        /// </summary>
        public int MatchingPeople { get; set; }
    }

    /// <summary>
    /// The forms the website may submit.
    ///
    /// Validated against this list at the edge. An unknown form type is refused rather
    /// than stored, because a value nobody recognises is either a mistake in the
    /// website or somebody probing the endpoint, and neither should end up in a
    /// coordinator's queue looking like a real enquiry.
    /// </summary>
    public static class WebFormTypes
    {
        /// <summary>The Bible Study Groups registration on a ministry page.</summary>
        public const string MinistryRegistration = "MINISTRY_REGISTRATION";

        /// <summary>The prayer request box on the home and visit pages.</summary>
        public const string PrayerRequest = "PRAYER_REQUEST";

        /// <summary>A general "get in touch" message.</summary>
        public const string ContactMessage = "CONTACT_MESSAGE";

        /// <summary>Someone saying they intend to visit a service.</summary>
        public const string PlanVisit = "PLAN_VISIT";

        /// <summary>
        /// Volunteering for a serving team — worship, reception, sound, and so on.
        ///
        /// Kept apart from <see cref="MinistryRegistration"/> even though both name a
        /// ministry, because the intentions are opposite: a Bible Study Group
        /// registration is somebody asking to be CARED FOR, and this is somebody
        /// offering to SERVE. They reach different people and are answered differently.
        /// </summary>
        public const string TeamRegistration = "TEAM_REGISTRATION";

        public static readonly string[] All =
            { MinistryRegistration, PrayerRequest, ContactMessage, PlanVisit, TeamRegistration };

        public static bool IsKnown(string? code) =>
            !string.IsNullOrWhiteSpace(code) && All.Contains(code, StringComparer.Ordinal);

        /// <summary>
        /// Whether this form must carry a way to reach the person back.
        ///
        /// A registration without a number is useless — nobody can follow it up, which
        /// is the entire point of it. A prayer request is different: someone may want
        /// prayer without leaving their name, and demanding contact details would stop
        /// them asking at all.
        /// </summary>
        public static bool RequiresContact(string formType) =>
            formType is MinistryRegistration or PlanVisit or TeamRegistration;

        public static string Label(string formType) => formType switch
        {
            MinistryRegistration => "Ministry registration",
            PrayerRequest        => "Prayer request",
            ContactMessage       => "Contact message",
            PlanVisit            => "Planning a visit",
            TeamRegistration     => "Joining a serving team",
            _                    => formType
        };
    }

    /// <summary>
    /// Where an enquiry has got to.
    ///
    /// Deliberately small. What happens after a coordinator reads one is a workflow
    /// still being decided, and inventing states for it now would mean guessing at
    /// transitions nobody has asked for yet.
    /// </summary>
    public static class WebEnquiryStatus
    {
        /// <summary>Nobody has looked at it. The queue is exactly this.</summary>
        public const string New = "NEW";

        /// <summary>Somebody has picked it up.</summary>
        public const string InReview = "IN_REVIEW";

        /// <summary>Dealt with — whatever that turned out to mean.</summary>
        public const string Actioned = "ACTIONED";

        /// <summary>Junk. Kept rather than deleted, so the volume stays visible.</summary>
        public const string Spam = "SPAM";

        /// <summary>Read and needs nothing further.</summary>
        public const string Closed = "CLOSED";

        public static readonly string[] All = { New, InReview, Actioned, Spam, Closed };

        public static bool IsKnown(string? code) =>
            !string.IsNullOrWhiteSpace(code) && All.Contains(code, StringComparer.Ordinal);
    }

    /// <summary>How many enquiries sit in each state, for the queue header.</summary>
    public sealed class WebEnquirySummary
    {
        public int New { get; set; }
        public int InReview { get; set; }
        public int Actioned { get; set; }
        public int Spam { get; set; }
        public int Closed { get; set; }
        public int Total { get; set; }

        /// <summary>Arrived in the last 24 hours, whatever their state.</summary>
        public int Today { get; set; }
    }

    /// <summary>
    /// Normalising a submitted phone number.
    ///
    /// The website collects a 10-digit Indian mobile, the same shape the intake screen
    /// enforces. Stored twice: as typed, and digits-only so it can be matched against
    /// <c>person_contact.normalized_value</c> without re-parsing.
    /// </summary>
    public static partial class WebContact
    {
        [GeneratedRegex(@"\D")]
        private static partial Regex NonDigits();

        public static string? Digits(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            var digits = NonDigits().Replace(raw, string.Empty);

            // A leading country code is common when somebody pastes a number.
            if (digits.Length == 12 && digits.StartsWith("91", StringComparison.Ordinal))
                digits = digits[2..];

            return digits.Length == 0 ? null : digits;
        }

        /// <summary>Ten digits starting 6-9 — the rule the rest of the system uses.</summary>
        public static bool LooksLikeMobile(string? raw)
        {
            var digits = Digits(raw);

            return digits is { Length: 10 } && digits[0] is >= '6' and <= '9';
        }

        public static bool LooksLikeEmail(string? raw) =>
            !string.IsNullOrWhiteSpace(raw) &&
            raw.Contains('@', StringComparison.Ordinal) &&
            raw.IndexOf('@', StringComparison.Ordinal) < raw.LastIndexOf('.');
    }
}
