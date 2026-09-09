using System.ComponentModel.DataAnnotations;

namespace RM_CMS.Modules.WebEnquiries.Api
{
    // -------------------------------------------------------------------------
    // Public submission
    //
    // This is the only request shape in the system that arrives from an
    // unauthenticated stranger, so every field is length-capped and nothing here
    // maps onto a domain model. A caller cannot set a status, a reviewer, or a
    // linked person by adding a field to their JSON, because those properties do
    // not exist on this class.
    // -------------------------------------------------------------------------

    public sealed class SubmitEnquiryRequest
    {
        /// <summary>
        /// Which form this came from. Must be one of <c>WebFormTypes.All</c> —
        /// an unrecognised value is refused rather than stored.
        /// </summary>
        [Required(ErrorMessage = "A form type is required.")]
        [StringLength(40)]
        public string FormType { get; set; } = string.Empty;

        [StringLength(160)] public string? FullName { get; set; }
        [StringLength(20)]  public string? Mobile { get; set; }
        [StringLength(255)] public string? Email { get; set; }

        [StringLength(100)] public string? City { get; set; }
        [StringLength(200)] public string? Street { get; set; }
        [StringLength(200)] public string? Landmark { get; set; }

        [StringLength(160)] public string? ReferredByName { get; set; }
        [StringLength(20)]  public string? ReferredByMobile { get; set; }

        /// <summary>Prayer request text, or any free-text message.</summary>
        [StringLength(4000)] public string? Message { get; set; }

        /// <summary>Public id of the campus, when the website knows which site.</summary>
        [StringLength(26)] public string? CampusId { get; set; }

        /// <summary>The page it was submitted from, for telling two copies of a form apart.</summary>
        [StringLength(255)] public string? SourcePage { get; set; }

        /// <summary>
        /// Honeypot. The website renders this hidden and a person never fills it in,
        /// so anything here means a bot. The submission is still accepted — replying
        /// with an error just tells the bot what to change — but it is flagged and
        /// kept out of the coordinator's default view.
        /// </summary>
        [StringLength(200)] public string? Website { get; set; }

        /// <summary>
        /// Anything the form collects that has no field here. Capped, and stored as
        /// opaque JSON — nothing reads it as configuration.
        /// </summary>
        public Dictionary<string, string>? Extra { get; set; }
    }

    /// <summary>
    /// What the website is told back.
    ///
    /// Deliberately thin: a reference code so the visitor can be told "your request
    /// was received", and nothing else. It must not leak whether the number matched
    /// somebody already on file, which would turn the form into a way of testing
    /// whether a given person attends this church.
    /// </summary>
    public sealed class EnquiryReceiptDto
    {
        public string ReferenceCode { get; set; } = string.Empty;
        public DateTime ReceivedAt { get; set; }
    }

    // -------------------------------------------------------------------------
    // Coordinator views
    // -------------------------------------------------------------------------

    public sealed class WebEnquiryDto
    {
        public string Id { get; set; } = string.Empty;
        public string? ReferenceCode { get; set; }

        public string FormType { get; set; } = string.Empty;
        public string FormLabel { get; set; } = string.Empty;

        public string? CampusName { get; set; }

        public string? FullName { get; set; }
        public string? Mobile { get; set; }
        public string? Email { get; set; }

        public string? City { get; set; }
        public string? Street { get; set; }
        public string? Landmark { get; set; }

        public string? ReferredByName { get; set; }
        public string? ReferredByMobile { get; set; }

        public string? Message { get; set; }

        /// <summary>Extra fields the website sent, flattened for display.</summary>
        public Dictionary<string, string> Extra { get; set; } = new();

        public string? SourcePage { get; set; }
        public bool IsSuspectedSpam { get; set; }

        public string Status { get; set; } = string.Empty;
        public string? ReviewedByName { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public string? ReviewNote { get; set; }

        public string? LinkedPersonId { get; set; }
        public string? LinkedPersonName { get; set; }

        /// <summary>
        /// How many people already on file share this mobile number. Lets the
        /// coordinator see "we know this person" before deciding what to do.
        /// </summary>
        public int MatchingPeople { get; set; }

        public DateTime SubmittedAt { get; set; }
        public int RowVersion { get; set; }
    }

    public sealed class WebEnquiryListDto
    {
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }

        public Domain.WebEnquirySummary Summary { get; set; } = new();
        public IReadOnlyList<WebEnquiryDto> Items { get; set; } = Array.Empty<WebEnquiryDto>();
    }

    /// <summary>
    /// Moves an enquiry along. The note is what the next person reads to understand
    /// why, so it is required for anything other than simply picking the item up.
    /// </summary>
    public sealed class UpdateEnquiryStatusRequest
    {
        [Required][StringLength(20)]
        public string Status { get; set; } = string.Empty;

        [StringLength(1000)]
        public string? Note { get; set; }

        [Required] public int RowVersion { get; set; }
    }
}
