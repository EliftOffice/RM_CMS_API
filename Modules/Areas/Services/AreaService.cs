using RM_CMS.Modules.Areas.Api;
using RM_CMS.Modules.Areas.Data;
using RM_CMS.Modules.Areas.Domain;
using RM_CMS.Modules.Identity.Data;
using RM_CMS.Modules.Identity.Domain;
using RM_CMS.Modules.Identity.Services;
using RM_CMS.Modules.Settings.Data;
using RM_CMS.Utilities;

namespace RM_CMS.Modules.Areas.Services
{
    /// <summary>
    /// The locality list.
    ///
    /// Two audiences with different powers meet here, which is why the gate is in
    /// this class rather than on the controller:
    ///
    ///   * Anyone who may record a visitor may READ the list and may CREATE an area
    ///     by typing one that does not exist yet. That is part of recording a
    ///     visitor — refusing it would mean an operator with a genuinely new
    ///     neighbourhood in front of them has to stop and find an administrator.
    ///
    ///   * RENAMING and RETIRING are management, and belong to an administrator —
    ///     or to a data-entry operator once an administrator switches
    ///     <c>area.manage_by_data_entry</c> on. Those edits change what the whole
    ///     organisation reads, and a rename that merges two neighbourhoods cannot
    ///     be undone by editing it back.
    /// </summary>
    public interface IAreaService
    {
        /// <summary>Type-ahead options at one campus. Active areas only.</summary>
        Task<ApiResponse<IReadOnlyList<AreaOptionDto>>> SuggestAsync(string? campusPublicId, string? term);

        /// <summary>The management list. Refused unless the caller may manage areas.</summary>
        Task<ApiResponse<IReadOnlyList<AreaDto>>> ListAsync(bool includeInactive);

        Task<ApiResponse<AreaAccessDto>> GetAccessAsync();

        /// <summary>Creates an area from the management screen.</summary>
        Task<ApiResponse<AreaDto>> CreateAsync(CreateAreaRequest request);

        /// <summary>Renames or retires one.</summary>
        Task<ApiResponse<AreaDto>> UpdateAsync(string publicId, UpdateAreaRequest request);

        /// <summary>
        /// Turns what an intake screen sent into an area key, creating the area when
        /// the typed name matches nothing.
        ///
        /// This is the "if no match found, save the area and then save the person"
        /// step, done on the server rather than as two calls from the browser. Doing
        /// it here means two operators typing the same new area at the same moment
        /// get one row, not two — the client cannot arrange that, and the unique
        /// index would turn the loser's save into a failure rather than a match.
        /// </summary>
        Task<AreaResolution> ResolveAsync(string? areaPublicId, string? areaName,
                                          long? campusKey, string? campusPublicId,
                                          long? actingUserId);
    }

    /// <summary>
    /// Outcome of <see cref="IAreaService.ResolveAsync"/>. <see cref="Problem"/> null
    /// means it worked; <see cref="AreaId"/> null with no problem means nothing was
    /// asked for, which is legitimate — an out-of-town visitor has no area.
    /// </summary>
    public sealed class AreaResolution
    {
        public long? AreaId { get; init; }
        public string? Problem { get; init; }
        public bool Created { get; init; }

        public bool Failed => Problem is not null;

        public static AreaResolution None => new();
        public static AreaResolution Found(long id) => new() { AreaId = id };
        public static AreaResolution New(long id) => new() { AreaId = id, Created = true };
        public static AreaResolution Refused(string problem) => new() { Problem = problem };
    }

    public sealed class AreaService : IAreaService
    {
        /// <summary>
        /// Enough to fill the dropdown without turning it into a list to read. An
        /// operator who cannot find their area in fifteen matches should type more,
        /// not scroll.
        /// </summary>
        private const int SuggestLimit = 15;

        private readonly IAreaRepository _areas;
        private readonly ISettingRepository _settings;
        private readonly IUserAccountRepository _accounts;
        private readonly ICurrentIdentity _current;
        private readonly ILogger<AreaService> _logger;

        public AreaService(
            IAreaRepository areas,
            ISettingRepository settings,
            IUserAccountRepository accounts,
            ICurrentIdentity current,
            ILogger<AreaService> logger)
        {
            _areas = areas;
            _settings = settings;
            _accounts = accounts;
            _current = current;
            _logger = logger;
        }

        // ==================================================================
        // Reads
        // ==================================================================

        public async Task<ApiResponse<IReadOnlyList<AreaOptionDto>>> SuggestAsync(
            string? campusPublicId, string? term)
        {
            var campus = campusPublicId ?? _current.DefaultCampusId;

            if (!_current.CanAccessCampus(campus))
                return Warn<IReadOnlyList<AreaOptionDto>>("You cannot read areas for that campus.");

            var campusKey = await _areas.ResolveCampusIdAsync(campus);

            // No campus resolved means the caller has no home campus and named none.
            // An empty list is the honest answer: there is nowhere for an area to
            // belong, and inventing one would file people against the wrong site.
            if (campusKey is null)
                return Ok<IReadOnlyList<AreaOptionDto>>(Array.Empty<AreaOptionDto>(), "No areas.");

            var rows = await _areas.SuggestAsync(campusKey.Value, term, SuggestLimit);

            var options = rows
                .Select(a => new AreaOptionDto { Id = a.PublicId, Name = a.Name })
                .ToList();

            return Ok<IReadOnlyList<AreaOptionDto>>(options, $"{options.Count} area option(s).");
        }

        public async Task<ApiResponse<IReadOnlyList<AreaDto>>> ListAsync(bool includeInactive)
        {
            var access = await ResolveAccessAsync();

            if (access == AreaAccess.None)
                return Warn<IReadOnlyList<AreaDto>>(NoAccessMessage());

            // An administrator sees every campus. A granted operator sees their own,
            // and only their own: they can already read the names through the picker,
            // but the counts say how many people live where, which is not theirs to
            // learn about another site.
            long? campusKey = access == AreaAccess.Administrator
                ? null
                : await _areas.ResolveCampusIdAsync(_current.DefaultCampusId);

            if (access != AreaAccess.Administrator && campusKey is null)
                return Warn<IReadOnlyList<AreaDto>>("Your account is not attached to a campus.");

            var rows = await _areas.ListAsync(campusKey, includeInactive);

            return Ok<IReadOnlyList<AreaDto>>(
                rows.Select(ToDto).ToList(),
                rows.Count == 1 ? "1 area." : $"{rows.Count} areas.");
        }

        public async Task<ApiResponse<AreaAccessDto>> GetAccessAsync()
        {
            var access = await ResolveAccessAsync();

            return Ok(new AreaAccessDto
            {
                CanOpen           = access != AreaAccess.None,
                CanCreate         = access != AreaAccess.None,
                CanEdit           = access != AreaAccess.None,
                CanRetire         = access != AreaAccess.None,
                CanSeeAllCampuses = access == AreaAccess.Administrator,
                Scope             = access.ToString().ToUpperInvariant()
            }, "Area access resolved");
        }

        // ==================================================================
        // Writes — management screen
        // ==================================================================

        public async Task<ApiResponse<AreaDto>> CreateAsync(CreateAreaRequest request)
        {
            var access = await ResolveAccessAsync();

            if (access == AreaAccess.None) return Warn<AreaDto>(NoAccessMessage());

            var campusPublicId = request.CampusId ?? _current.DefaultCampusId;

            if (!_current.CanAccessCampus(campusPublicId))
                return Warn<AreaDto>("You cannot add an area to that campus.");

            // A granted operator adds to their own campus only, whatever they send.
            if (access != AreaAccess.Administrator)
                campusPublicId = _current.DefaultCampusId;

            var campusKey = await _areas.ResolveCampusIdAsync(campusPublicId);

            if (campusKey is null)
                return Warn<AreaDto>("Unknown campus.");

            var name = AreaNames.Display(request.Name);

            if (name.Length < 2)
                return Warn<AreaDto>("Give the area a name of at least two characters.");

            var normalized = AreaNames.Normalize(name);
            var existing = await _areas.FindByNameAsync(campusKey.Value, normalized);

            if (existing is not null)
            {
                return existing.IsActive
                    ? Warn<AreaDto>($"'{existing.Name}' already exists at this campus.")
                    : Warn<AreaDto>($"'{existing.Name}' exists at this campus but is retired. " +
                                    "Reactivate it rather than adding a second one.");
            }

            var id = await _areas.CreateAsync(new Area
            {
                PublicId = Ulid.NewUlid(),
                CampusId = campusKey.Value,
                Name = name,
                NormalizedName = normalized
            }, await ActingUserIdAsync());

            var created = await _areas.GetByIdAsync(id);

            return Ok(ToDto(created!), $"{name} added.");
        }

        public async Task<ApiResponse<AreaDto>> UpdateAsync(string publicId, UpdateAreaRequest request)
        {
            var access = await ResolveAccessAsync();

            if (access == AreaAccess.None) return Warn<AreaDto>(NoAccessMessage());

            var area = await _areas.GetByPublicIdAsync(publicId);

            if (area is null) return Warn<AreaDto>("Area not found.");

            if (!_current.CanAccessCampus(area.CampusPublicId))
                return Warn<AreaDto>("Area not found.");

            if (access != AreaAccess.Administrator &&
                !string.Equals(area.CampusPublicId, _current.DefaultCampusId, StringComparison.Ordinal))
            {
                return Warn<AreaDto>("You can only manage areas at your own campus.");
            }

            var name = AreaNames.Display(request.Name);

            if (name.Length < 2)
                return Warn<AreaDto>("Give the area a name of at least two characters.");

            var normalized = AreaNames.Normalize(name);

            // A rename onto another area's name would merge two neighbourhoods into
            // one label without moving anybody — the unique index refuses it anyway,
            // and this turns that into a sentence instead of a duplicate-key error.
            if (!string.Equals(normalized, area.NormalizedName, StringComparison.Ordinal))
            {
                var clash = await _areas.FindByNameAsync(area.CampusId, normalized);

                if (clash is not null && clash.Id != area.Id)
                    return Warn<AreaDto>($"'{clash.Name}' already exists at this campus.");
            }

            var updated = await _areas.UpdateAsync(
                area.Id, request.RowVersion, name, normalized, request.IsActive, await ActingUserIdAsync());

            if (!updated)
                return Warn<AreaDto>("This record was changed by someone else. Reload and try again.");

            if (area.IsActive && !request.IsActive)
            {
                _logger.LogInformation(
                    "Area {Name} retired by {AccountId} with {People} person(s) still filed against it",
                    area.Name, _current.AccountId, area.PersonCount);
            }

            var fresh = await _areas.GetByIdAsync(area.Id);

            // Retiring an area that people still point at is allowed — unlike a
            // campus, an area is only a label and nothing breaks — but say so, or
            // the operator will not realise the picker just lost an entry that 40
            // records still use.
            var message = area.IsActive && !request.IsActive && fresh!.PersonCount > 0
                ? $"{name} retired. {fresh.PersonCount} person(s) are still filed against it; " +
                  "they keep it, but it is no longer offered when recording someone."
                : $"{name} updated.";

            return Ok(ToDto(fresh!), message);
        }

        // ==================================================================
        // Find-or-create, used by intake and by user creation
        // ==================================================================

        public async Task<AreaResolution> ResolveAsync(
            string? areaPublicId, string? areaName, long? campusKey, string? campusPublicId,
            long? actingUserId)
        {
            // An id wins over typed text. The screen sends both — the id of whatever
            // was picked from the list, and the text still in the box — so that a
            // pick followed by an edit is not silently ignored. The picker clears the
            // id the moment the text stops matching, so an id arriving here means the
            // operator really did choose that row.
            if (!string.IsNullOrWhiteSpace(areaPublicId))
            {
                var picked = await _areas.GetByPublicIdAsync(areaPublicId!);

                if (picked is null)
                    return AreaResolution.Refused("That area no longer exists. Choose or type it again.");

                if (!_current.CanAccessCampus(picked.CampusPublicId))
                    return AreaResolution.Refused("That area belongs to another campus.");

                if (campusKey is not null && picked.CampusId != campusKey.Value)
                {
                    return AreaResolution.Refused(
                        $"'{picked.Name}' is an area at {picked.CampusName}, not at the campus this " +
                        "person is being recorded at.");
                }

                return AreaResolution.Found(picked.Id);
            }

            var name = AreaNames.Display(areaName);

            if (name.Length == 0) return AreaResolution.None;

            if (name.Length < 2)
                return AreaResolution.Refused("An area name needs at least two characters.");

            if (name.Length > AreaNames.MaxLength)
                return AreaResolution.Refused($"An area name cannot be longer than {AreaNames.MaxLength} characters.");

            if (campusKey is null)
            {
                return AreaResolution.Refused(
                    "An area belongs to a campus, and this record has none. " +
                    "Choose a campus first.");
            }

            if (!_current.CanAccessCampus(campusPublicId))
                return AreaResolution.Refused("You cannot add an area to that campus.");

            var normalized = AreaNames.Normalize(name);
            var existing = await _areas.FindByNameAsync(campusKey.Value, normalized);

            // A retired area is reused rather than duplicated. The name is in use as
            // far as the unique index is concerned, and someone typing it clearly
            // still considers the place real — but it is NOT reactivated here, which
            // would let intake quietly undo an administrator's decision.
            if (existing is not null) return AreaResolution.Found(existing.Id);

            try
            {
                var id = await _areas.CreateAsync(new Area
                {
                    PublicId = Ulid.NewUlid(),
                    CampusId = campusKey.Value,
                    Name = name,
                    NormalizedName = normalized
                }, actingUserId);

                _logger.LogInformation(
                    "Area {Name} created while recording someone, by {AccountId}", name, _current.AccountId);

                return AreaResolution.New(id);
            }
            catch (MySqlConnector.MySqlException ex) when (ex.Number is 1062)
            {
                // Another request created the same area between the lookup and the
                // insert. That is the normal outcome of two operators typing the same
                // new neighbourhood at once, not an error — re-read and use theirs.
                var raced = await _areas.FindByNameAsync(campusKey.Value, normalized);

                return raced is not null
                    ? AreaResolution.Found(raced.Id)
                    : AreaResolution.Refused("That area could not be saved. Try again.");
            }
        }

        // ==================================================================
        // Helpers
        // ==================================================================

        /// <summary>
        /// Who may manage areas. An administrator always may; a data-entry operator
        /// only once an administrator turns the grant on.
        ///
        /// Volunteers, team leads and pastors are deliberately absent. They can read
        /// the list through the picker like anyone recording a visitor, but the
        /// people who maintain this list are the ones who type into it.
        /// </summary>
        private async Task<AreaAccess> ResolveAccessAsync()
        {
            if (_current.IsAdmin) return AreaAccess.Administrator;

            if (_current.IsInRole(RoleCodes.DataEntry))
            {
                return await _settings.GetBoolAsync("area.manage_by_data_entry", false)
                    ? AreaAccess.OwnCampusOnly
                    : AreaAccess.None;
            }

            return AreaAccess.None;
        }

        private string NoAccessMessage() =>
            _current.IsInRole(RoleCodes.DataEntry)
                ? "Data entry operators can be given access to manage areas, but it is switched off. " +
                  "The setting is area.manage_by_data_entry."
                : "Your role cannot manage areas.";

        private static AreaDto ToDto(Area a) => new()
        {
            Id = a.PublicId,
            Name = a.Name,
            CampusId = a.CampusPublicId,
            CampusName = a.CampusName,
            IsActive = a.IsActive,
            PersonCount = a.PersonCount,
            VolunteerCount = a.VolunteerCount,
            CreatedAt = a.CreatedAt,
            RowVersion = a.RowVersion
        };

        private async Task<long?> ActingUserIdAsync()
        {
            if (string.IsNullOrWhiteSpace(_current.AccountId)) return null;

            var account = await _accounts.GetByPublicIdAsync(_current.AccountId);
            return account?.Id;
        }

        private static ApiResponse<T> Ok<T>(T data, string message) => new(ResponseType.Success, message, data);
        private static ApiResponse<T> Warn<T>(string message) => new(ResponseType.Warning, message, default!);
    }
}
