using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RM_CMS.Modules.Identity.Data;
using RM_CMS.Modules.Identity.Domain;
using RM_CMS.Modules.Identity.Services;
using RM_CMS.Modules.WebEnquiries.Api;
using RM_CMS.Modules.WebEnquiries.Data;
using RM_CMS.Modules.WebEnquiries.Domain;
using RM_CMS.Utilities;

namespace RM_CMS.Modules.WebEnquiries.Services
{
    /// <summary>
    /// Everything the public website collects, and the queue a coordinator works.
    ///
    /// Two audiences with opposite trust levels in one service:
    ///
    ///   SUBMIT  — anonymous, open to the internet. Validates hard, stores raw, and
    ///             tells the caller almost nothing back.
    ///   REVIEW  — WEB_COORDINATOR or an administrator, reading untrusted input.
    ///
    /// The gap between them is the point. Nothing a stranger submits becomes a
    /// pastoral record until a person decides it should.
    /// </summary>
    public interface IWebEnquiryService
    {
        Task<ApiResponse<EnquiryReceiptDto>> SubmitAsync(
            SubmitEnquiryRequest request, string? clientIp, string? userAgent);

        Task<ApiResponse<WebEnquiryListDto>> ListAsync(
            int page, int pageSize, string? status, string? formType, string? search, bool includeSpam);

        Task<ApiResponse<WebEnquiryDto>> GetAsync(string publicId);

        Task<ApiResponse<WebEnquiryDto>> UpdateStatusAsync(string publicId, UpdateEnquiryStatusRequest request);
    }

    public sealed class WebEnquiryService : IWebEnquiryService
    {
        /// <summary>
        /// Submissions allowed from one fingerprint per hour.
        ///
        /// This is a back-stop, not the main defence — the rate limiter runs first.
        /// But behind Coolify's proxy the limiter partitions on the proxy's own
        /// address, so every anonymous caller shares one bucket and it cannot tell two
        /// visitors apart. This can, because the fingerprint includes the user agent.
        /// </summary>
        private const int MaxPerSubmitterPerHour = 10;

        private readonly IWebEnquiryRepository _enquiries;
        private readonly IUserAccountRepository _accounts;
        private readonly ICurrentIdentity _current;
        private readonly TimeProvider _clock;
        private readonly ILogger<WebEnquiryService> _logger;

        public WebEnquiryService(
            IWebEnquiryRepository enquiries,
            IUserAccountRepository accounts,
            ICurrentIdentity current,
            TimeProvider clock,
            ILogger<WebEnquiryService> logger)
        {
            _enquiries = enquiries;
            _accounts = accounts;
            _current = current;
            _clock = clock;
            _logger = logger;
        }

        // ==================================================================
        // Public submission
        // ==================================================================

        public async Task<ApiResponse<EnquiryReceiptDto>> SubmitAsync(
            SubmitEnquiryRequest request, string? clientIp, string? userAgent)
        {
            var formType = (request.FormType ?? string.Empty).Trim().ToUpperInvariant();

            if (!WebFormTypes.IsKnown(formType))
            {
                // Not echoed back to the caller. An unknown form type is either a bug in
                // the website or somebody probing, and listing the valid values would
                // help the second more than the first.
                _logger.LogWarning("Website enquiry refused: unknown form type {FormType}", formType);
                return Warn("That form could not be submitted.");
            }

            var now = _clock.GetUtcNow().UtcDateTime;

            var fullName = Clean(request.FullName, 160);
            var mobile = Clean(request.Mobile, 20);
            var email = Clean(request.Email, 255);
            var message = Clean(request.Message, 4000);

            // ---- what each form actually needs -------------------------------
            if (WebFormTypes.RequiresContact(formType))
            {
                if (string.IsNullOrWhiteSpace(fullName))
                    return Warn("Please enter your name.");

                if (!WebContact.LooksLikeMobile(mobile))
                    return Warn("Please enter a 10-digit mobile number starting 6, 7, 8 or 9.");
            }
            else if (string.IsNullOrWhiteSpace(message) &&
                     string.IsNullOrWhiteSpace(fullName) &&
                     string.IsNullOrWhiteSpace(mobile))
            {
                // A prayer request may be anonymous, but it cannot be empty.
                return Warn("Please write your request before sending.");
            }

            if (!string.IsNullOrWhiteSpace(email) && !WebContact.LooksLikeEmail(email))
                return Warn("That does not look like a valid email address.");

            // A supplied mobile is validated even on forms that do not require one:
            // a half-typed number is worse than none, because somebody will try it.
            if (!string.IsNullOrWhiteSpace(mobile) && !WebContact.LooksLikeMobile(mobile))
                return Warn("Please enter a 10-digit mobile number starting 6, 7, 8 or 9.");

            // ---- abuse ---------------------------------------------------------
            var submitterHash = Fingerprint(clientIp, userAgent, now);

            var recent = await _enquiries.CountRecentBySubmitterAsync(submitterHash, now.AddHours(-1));

            if (recent >= MaxPerSubmitterPerHour)
            {
                _logger.LogWarning(
                    "Website enquiry throttled: {Count} submissions from one fingerprint in an hour", recent);

                // Vague on purpose. "You have submitted 10 times" tells a script exactly
                // where the ceiling is.
                return Warn("Too many submissions from this device. Please try again later.");
            }

            // The honeypot is a hidden field a person never sees. Anything in it means a
            // bot — but the submission is ACCEPTED anyway and flagged instead. Rejecting
            // it outright tells the bot which field to leave blank next time.
            var suspectedSpam = !string.IsNullOrWhiteSpace(request.Website);

            var campusId = await _enquiries.ResolveCampusIdAsync(request.CampusId);

            var enquiry = new WebEnquiry
            {
                PublicId = Ulid.NewUlid(),
                FormType = formType,
                CampusId = campusId,
                FullName = fullName,
                Mobile = mobile,
                MobileNormalized = WebContact.Digits(mobile),
                Email = email?.ToLowerInvariant(),
                City = Clean(request.City, 100),
                Street = Clean(request.Street, 200),
                Landmark = Clean(request.Landmark, 200),
                ReferredByName = Clean(request.ReferredByName, 160),
                ReferredByMobile = Clean(request.ReferredByMobile, 20),
                Message = message,
                Payload = SerialiseExtra(request.Extra),
                SourcePage = Clean(request.SourcePage, 255),
                UserAgent = Clean(userAgent, 255),
                SubmitterHash = submitterHash,
                IsSuspectedSpam = suspectedSpam,
                Status = WebEnquiryStatus.New,
                SubmittedAt = now
            };

            await _enquiries.CreateAsync(enquiry);

            // No personal detail in the log line. This is the one endpoint whose input
            // is written by strangers, and a log is read by more people than the queue.
            _logger.LogInformation(
                "Website enquiry {Reference} received ({FormType}){Spam}",
                enquiry.ReferenceCode, formType, suspectedSpam ? " [flagged]" : string.Empty);

            return new ApiResponse<EnquiryReceiptDto>(
                ResponseType.Success,
                "Thank you — your request has been received.",
                new EnquiryReceiptDto
                {
                    ReferenceCode = enquiry.ReferenceCode ?? string.Empty,
                    ReceivedAt = now
                });
        }

        /// <summary>
        /// Groups submissions that plausibly came from one place today.
        ///
        /// Salted with the DATE, so it rotates every midnight. That is deliberate: the
        /// only question worth answering is "did these forty arrive from one source
        /// today", and a hash that never changes would be a permanent identifier for
        /// somebody who did nothing but ask for prayer.
        /// </summary>
        private static string Fingerprint(string? ip, string? userAgent, DateTime nowUtc)
        {
            var material = $"{ip ?? "unknown"}|{userAgent ?? "unknown"}|{nowUtc:yyyy-MM-dd}";

            return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
        }

        /// <summary>
        /// Extra fields, capped hard. An unbounded dictionary from an anonymous caller
        /// is a way to write a megabyte into the database per request.
        /// </summary>
        private static string? SerialiseExtra(Dictionary<string, string>? extra)
        {
            if (extra is null || extra.Count == 0) return null;

            var trimmed = extra
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Key))
                .Take(20)
                .ToDictionary(
                    kv => kv.Key.Length > 60 ? kv.Key[..60] : kv.Key,
                    kv => (kv.Value ?? string.Empty).Length > 500
                        ? kv.Value![..500]
                        : kv.Value ?? string.Empty,
                    StringComparer.Ordinal);

            return trimmed.Count == 0 ? null : JsonSerializer.Serialize(trimmed);
        }

        // ==================================================================
        // Coordinator queue
        // ==================================================================

        public async Task<ApiResponse<WebEnquiryListDto>> ListAsync(
            int page, int pageSize, string? status, string? formType, string? search, bool includeSpam)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);

            var normalisedStatus = string.IsNullOrWhiteSpace(status)
                ? null
                : status.Trim().ToUpperInvariant();

            if (normalisedStatus is not null && !WebEnquiryStatus.IsKnown(normalisedStatus))
                return WarnList($"Unknown status '{status}'.");

            var normalisedForm = string.IsNullOrWhiteSpace(formType)
                ? null
                : formType.Trim().ToUpperInvariant();

            if (normalisedForm is not null && !WebFormTypes.IsKnown(normalisedForm))
                return WarnList($"Unknown form type '{formType}'.");

            var query = new WebEnquiryQuery
            {
                Status = normalisedStatus,
                FormType = normalisedForm,
                Search = string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
                IncludeSpam = includeSpam,
                Skip = (page - 1) * pageSize,
                Take = pageSize
            };

            var rows = await _enquiries.SearchAsync(query);
            var total = await _enquiries.CountAsync(query);
            var summary = await _enquiries.GetSummaryAsync(_clock.GetUtcNow().UtcDateTime.AddDays(-1));

            return new ApiResponse<WebEnquiryListDto>(ResponseType.Success,
                $"{total} enquir{(total == 1 ? "y" : "ies")}.",
                new WebEnquiryListDto
                {
                    Page = page,
                    PageSize = pageSize,
                    TotalCount = total,
                    Summary = summary,
                    Items = rows.Select(ToDto).ToList()
                });
        }

        public async Task<ApiResponse<WebEnquiryDto>> GetAsync(string publicId)
        {
            var enquiry = await _enquiries.GetByPublicIdAsync(publicId);

            return enquiry is null
                ? Warn<WebEnquiryDto>("That enquiry was not found.")
                : new ApiResponse<WebEnquiryDto>(ResponseType.Success, "Enquiry.", ToDto(enquiry));
        }

        public async Task<ApiResponse<WebEnquiryDto>> UpdateStatusAsync(
            string publicId, UpdateEnquiryStatusRequest request)
        {
            var enquiry = await _enquiries.GetByPublicIdAsync(publicId);

            if (enquiry is null) return Warn<WebEnquiryDto>("That enquiry was not found.");

            var status = (request.Status ?? string.Empty).Trim().ToUpperInvariant();

            if (!WebEnquiryStatus.IsKnown(status))
                return Warn<WebEnquiryDto>($"Unknown status '{request.Status}'.");

            if (status == WebEnquiryStatus.New)
                return Warn<WebEnquiryDto>("An enquiry cannot be put back to new once it has been read.");

            var accountId = await ActingAccountIdAsync();

            if (accountId is null) return Warn<WebEnquiryDto>("You are not signed in.");

            var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();

            // Closing or actioning without saying why leaves the next person with a
            // status and no reason for it. Picking an item up needs no explanation.
            if (note is null && status is WebEnquiryStatus.Actioned or WebEnquiryStatus.Closed)
                return Warn<WebEnquiryDto>("Add a short note saying what was done.");

            var updated = await _enquiries.UpdateStatusAsync(
                enquiry.Id, request.RowVersion, status, note, accountId.Value,
                _clock.GetUtcNow().UtcDateTime);

            if (!updated)
                return Warn<WebEnquiryDto>("This was changed by someone else. Reload and try again.");

            _logger.LogInformation(
                "Website enquiry {Reference} moved to {Status} by {AccountId}",
                enquiry.ReferenceCode, status, _current.AccountId);

            var fresh = await _enquiries.GetByPublicIdAsync(publicId);
            return new ApiResponse<WebEnquiryDto>(ResponseType.Success, "Saved.", ToDto(fresh!));
        }

        // ==================================================================
        // Helpers
        // ==================================================================

        private async Task<long?> ActingAccountIdAsync()
        {
            if (string.IsNullOrWhiteSpace(_current.AccountId)) return null;

            return (await _accounts.GetByPublicIdAsync(_current.AccountId))?.Id;
        }

        private static string? Clean(string? value, int max)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;

            var trimmed = value.Trim();
            return trimmed.Length > max ? trimmed[..max] : trimmed;
        }

        private static WebEnquiryDto ToDto(WebEnquiry e) => new()
        {
            Id = e.PublicId,
            ReferenceCode = e.ReferenceCode,
            FormType = e.FormType,
            FormLabel = WebFormTypes.Label(e.FormType),
            CampusName = e.CampusName,
            FullName = e.FullName,
            Mobile = e.Mobile,
            Email = e.Email,
            City = e.City,
            Street = e.Street,
            Landmark = e.Landmark,
            ReferredByName = e.ReferredByName,
            ReferredByMobile = e.ReferredByMobile,
            Message = e.Message,
            Extra = DeserialiseExtra(e.Payload),
            SourcePage = e.SourcePage,
            IsSuspectedSpam = e.IsSuspectedSpam,
            Status = e.Status,
            ReviewedByName = e.ReviewedByName,
            ReviewedAt = e.ReviewedAt,
            ReviewNote = e.ReviewNote,
            LinkedPersonId = e.LinkedPersonPublicId,
            LinkedPersonName = e.LinkedPersonName,
            MatchingPeople = e.MatchingPeople,
            SubmittedAt = e.SubmittedAt,
            RowVersion = e.RowVersion
        };

        /// <summary>
        /// Stored JSON came from an anonymous caller, so a malformed or hostile value
        /// must not take the whole list down. A payload that will not parse is dropped
        /// and the rest of the enquiry still renders.
        /// </summary>
        private static Dictionary<string, string> DeserialiseExtra(string? payload)
        {
            if (string.IsNullOrWhiteSpace(payload)) return new Dictionary<string, string>();

            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, string>>(payload)
                       ?? new Dictionary<string, string>();
            }
            catch (JsonException)
            {
                return new Dictionary<string, string>();
            }
        }

        private static ApiResponse<EnquiryReceiptDto> Warn(string message) =>
            new(ResponseType.Warning, message, default!);

        private static ApiResponse<T> Warn<T>(string message) =>
            new(ResponseType.Warning, message, default!);

        private static ApiResponse<WebEnquiryListDto> WarnList(string message) =>
            new(ResponseType.Warning, message, default!);
    }
}
