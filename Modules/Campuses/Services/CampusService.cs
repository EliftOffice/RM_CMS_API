using RM_CMS.Modules.Campuses.Api;
using RM_CMS.Modules.Campuses.Data;
using RM_CMS.Modules.Campuses.Domain;
using RM_CMS.Modules.Identity.Data;
using RM_CMS.Modules.Identity.Domain;
using RM_CMS.Modules.Identity.Services;
using RM_CMS.Utilities;

namespace RM_CMS.Modules.Campuses.Services
{
    /// <summary>
    /// Campus administration.
    ///
    /// Every write here is administrator-only, enforced at the controller. What this
    /// class adds on top is the rules that keep the tenancy boundary coherent: codes
    /// stay unique and stable, a campus with people still attached cannot be retired
    /// out from under them, and the organisation can never end up with no active
    /// campus at all — intake writes <c>person.campus_id</c>, so zero campuses means
    /// nobody can be recorded.
    /// </summary>
    public interface ICampusService
    {
        Task<ApiResponse<IReadOnlyList<CampusDto>>> ListAsync(bool includeInactive);

        /// <summary>Active campuses only, thin shape, for pickers.</summary>
        Task<ApiResponse<IReadOnlyList<CampusOptionDto>>> OptionsAsync();

        Task<ApiResponse<CampusDto>> CreateAsync(CreateCampusRequest request);
        Task<ApiResponse<CampusDto>> UpdateAsync(string publicId, UpdateCampusRequest request);
    }

    public sealed class CampusService : ICampusService
    {
        private const string DefaultTimezone = "Asia/Kolkata";

        private readonly ICampusRepository _campuses;
        private readonly IUserAccountRepository _accounts;
        private readonly ICurrentIdentity _current;
        private readonly ILogger<CampusService> _logger;

        public CampusService(
            ICampusRepository campuses,
            IUserAccountRepository accounts,
            ICurrentIdentity current,
            ILogger<CampusService> logger)
        {
            _campuses = campuses;
            _accounts = accounts;
            _current = current;
            _logger = logger;
        }

        public async Task<ApiResponse<IReadOnlyList<CampusDto>>> ListAsync(bool includeInactive)
        {
            var rows = await _campuses.ListAsync(includeInactive);

            return Ok<IReadOnlyList<CampusDto>>(
                rows.Select(ToDto).ToList(),
                rows.Count == 1 ? "1 campus." : $"{rows.Count} campuses.");
        }

        /// <summary>
        /// Used by the visitor-entry and add-user pickers. Scoped: an account tied to
        /// one campus is offered only that one, so a data-entry operator at one site
        /// cannot file a visitor against another.
        /// </summary>
        public async Task<ApiResponse<IReadOnlyList<CampusOptionDto>>> OptionsAsync()
        {
            var rows = await _campuses.ListAsync(includeInactive: false);

            var visible = rows
                .Where(c => _current.CanAccessCampus(c.PublicId))
                .Select(c => new CampusOptionDto { Id = c.PublicId, Code = c.Code, Name = c.Name })
                .ToList();

            return Ok<IReadOnlyList<CampusOptionDto>>(visible, $"{visible.Count} campus option(s).");
        }

        public async Task<ApiResponse<CampusDto>> CreateAsync(CreateCampusRequest request)
        {
            var code = request.Code.Trim().ToUpperInvariant();
            var name = request.Name.Trim();

            if (await _campuses.CodeExistsAsync(code))
                return Warn($"A campus with code '{code}' already exists.");

            var timezone = (request.Timezone ?? DefaultTimezone).Trim();

            if (!IsKnownTimezone(timezone))
                return Warn($"'{timezone}' is not a time zone this server recognises.");

            var id = await _campuses.CreateAsync(new Campus
            {
                PublicId = Ulid.NewUlid(),
                Code = code,
                Name = name,
                Timezone = timezone
            }, await ActingUserIdAsync());

            var created = await _campuses.GetByIdAsync(id);

            _logger.LogWarning(
                "Campus {Code} ({Name}) created by {AccountId}", code, name, _current.AccountId);

            return Ok(ToDto(created!), $"{name} created.");
        }

        public async Task<ApiResponse<CampusDto>> UpdateAsync(string publicId, UpdateCampusRequest request)
        {
            var campus = await _campuses.GetByPublicIdAsync(publicId);

            if (campus is null) return Warn("Campus not found.");

            var name = request.Name.Trim();
            var timezone = (request.Timezone ?? campus.Timezone).Trim();

            if (!IsKnownTimezone(timezone))
                return Warn($"'{timezone}' is not a time zone this server recognises.");

            if (!request.IsActive && campus.IsActive)
            {
                // Retiring a campus that still holds people would strand them: their
                // records stay readable, but nothing new can be filed against them and
                // the reason would not be obvious from any screen.
                if (!campus.CanRetire)
                {
                    return Warn(
                        $"{campus.Name} still has {Describe(campus)}. " +
                        "Move them to another campus before retiring it.");
                }

                // The last active campus cannot go. Intake stamps person.campus_id, so
                // an organisation with none can no longer record a visitor at all.
                var active = await _campuses.ListAsync(includeInactive: false);

                if (active.Count(c => c.Id != campus.Id) == 0)
                {
                    return Warn(
                        "This is the only active campus. Create another before retiring this one — " +
                        "recording a visitor needs a campus to file them against.");
                }
            }

            var updated = await _campuses.UpdateAsync(
                campus.Id, request.RowVersion, name, timezone, request.IsActive, await ActingUserIdAsync());

            if (!updated)
                return Warn("This record was changed by someone else. Reload and try again.");

            if (campus.IsActive && !request.IsActive)
                _logger.LogWarning("Campus {Code} retired by {AccountId}", campus.Code, _current.AccountId);

            var fresh = await _campuses.GetByIdAsync(campus.Id);
            return Ok(ToDto(fresh!), $"{name} updated.");
        }

        // ==================================================================
        // Helpers
        // ==================================================================

        /// <summary>
        /// .NET 6+ resolves IANA ids on Windows through ICU, so 'Asia/Kolkata' works on
        /// the developer's machine and on the Linux container alike. Validating here
        /// means a typo is caught when it is typed rather than when a report is run.
        /// </summary>
        private static bool IsKnownTimezone(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;

            return TimeZoneInfo.TryFindSystemTimeZoneById(id, out _);
        }

        private static string Describe(Campus c)
        {
            var parts = new List<string>();

            if (c.PersonCount > 0) parts.Add($"{c.PersonCount} visitor(s)");
            if (c.StaffCount > 0) parts.Add($"{c.StaffCount} person(s) with a sign-in");
            if (c.VolunteerCount > 0) parts.Add($"{c.VolunteerCount} active volunteer(s)");
            if (c.TeamCount > 0) parts.Add($"{c.TeamCount} active team(s)");
            if (c.OpenCaseCount > 0) parts.Add($"{c.OpenCaseCount} open case(s)");

            return string.Join(", ", parts);
        }

        private static CampusDto ToDto(Campus c) => new()
        {
            Id = c.PublicId,
            Code = c.Code,
            Name = c.Name,
            Timezone = c.Timezone,
            IsActive = c.IsActive,
            PersonCount = c.PersonCount,
            StaffCount = c.StaffCount,
            VolunteerCount = c.VolunteerCount,
            TeamCount = c.TeamCount,
            OpenCaseCount = c.OpenCaseCount,
            CanRetire = c.CanRetire,
            CreatedAt = c.CreatedAt,
            RowVersion = c.RowVersion
        };

        private async Task<long?> ActingUserIdAsync()
        {
            if (string.IsNullOrWhiteSpace(_current.AccountId)) return null;

            var account = await _accounts.GetByPublicIdAsync(_current.AccountId);
            return account?.Id;
        }

        private static ApiResponse<T> Ok<T>(T data, string message) => new(ResponseType.Success, message, data);
        private static ApiResponse<CampusDto> Warn(string message) => new(ResponseType.Warning, message, default!);
    }
}
