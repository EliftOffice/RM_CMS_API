using RM_CMS.Modules.Identity.Data;
using RM_CMS.Modules.Identity.Domain;
using RM_CMS.Modules.Identity.Services;
using RM_CMS.Modules.Volunteers.Api;
using RM_CMS.Modules.Volunteers.Data;
using RM_CMS.Modules.Volunteers.Domain;
using RM_CMS.Utilities;

namespace RM_CMS.Modules.Volunteers.Services
{
    public interface IVolunteerService
    {
        Task<ApiResponse<PagedResult<VolunteerSummaryDto>>> SearchAsync(
            int page, int pageSize, string? search, string? status, string? teamId, bool? hasCapacity);

        Task<ApiResponse<VolunteerDto>> GetAsync(string publicId);
        Task<ApiResponse<VolunteerDto>> EnrolAsync(EnrolVolunteerRequest request);
        Task<ApiResponse<VolunteerDto>> UpdateAsync(string publicId, UpdateVolunteerRequest request);
        Task<ApiResponse<VolunteerDto>> ChangeCapacityAsync(string publicId, ChangeCapacityRequest request);
        Task<ApiResponse<VolunteerDto>> UpdateSafeguardingAsync(string publicId, SafeguardingRequest request);

        Task<ApiResponse<IReadOnlyList<CapacityChangeDto>>> GetCapacityHistoryAsync(string publicId);
        Task<ApiResponse<IReadOnlyList<CapacityBandDto>>> GetCapacityBandsAsync();

        /// <summary>The shortlist the assignment job would pick from, for a campus.</summary>
        Task<ApiResponse<IReadOnlyList<EligibleVolunteerDto>>> FindEligibleAsync(string? campusId, bool crisisCapable);

        // ---- teams ----
        Task<ApiResponse<IReadOnlyList<TeamDto>>> ListTeamsAsync(string? campusId, bool includeInactive);
        Task<ApiResponse<TeamDto>> CreateTeamAsync(CreateTeamRequest request);
        Task<ApiResponse<TeamDto>> UpdateTeamAsync(string publicId, UpdateTeamRequest request);

        /// <summary>What the caller may do on the team management screen.</summary>
        Task<ApiResponse<TeamAccessDto>> GetTeamAccessAsync();

        /// <summary>Accounts that could be set as a team's lead, in the caller's scope.</summary>
        Task<ApiResponse<IReadOnlyList<LeadCandidateDto>>> ListLeadCandidatesAsync();
    }

    public sealed class VolunteerService : IVolunteerService
    {
        private const string NotFound = "Volunteer not found.";

        private readonly IVolunteerRepository _volunteers;
        private readonly ITeamRepository _teams;
        private readonly ICurrentIdentity _current;
        private readonly IUserAccountRepository _accounts;
        private readonly RM_CMS.Modules.Settings.Data.ISettingRepository _settings;
        private readonly TimeProvider _clock;
        private readonly ILogger<VolunteerService> _logger;

        public VolunteerService(
            IVolunteerRepository volunteers,
            ITeamRepository teams,
            ICurrentIdentity current,
            IUserAccountRepository accounts,
            RM_CMS.Modules.Settings.Data.ISettingRepository settings,
            TimeProvider clock,
            ILogger<VolunteerService> logger)
        {
            _volunteers = volunteers;
            _teams = teams;
            _current = current;
            _accounts = accounts;
            _settings = settings;
            _clock = clock;
            _logger = logger;
        }

        // ==================================================================
        // Reads
        // ==================================================================

        public async Task<ApiResponse<PagedResult<VolunteerSummaryDto>>> SearchAsync(
            int page, int pageSize, string? search, string? status, string? teamId, bool? hasCapacity)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);

            if (!string.IsNullOrWhiteSpace(status) && !VolunteerStatus.IsKnown(status))
                return Warn<PagedResult<VolunteerSummaryDto>>($"Unknown status '{status}'.");

            var campusKey = await ScopedCampusKeyAsync();
            var teamKey = await _volunteers.ResolveTeamIdAsync(teamId);

            if (!string.IsNullOrWhiteSpace(teamId) && teamKey is null)
                return Warn<PagedResult<VolunteerSummaryDto>>("Unknown team.");

            var query = new VolunteerQuery
            {
                Search = search,
                Status = status,
                CampusId = campusKey,
                TeamId = teamKey,
                HasCapacity = hasCapacity,
                Skip = (page - 1) * pageSize,
                Take = pageSize
            };

            var total = await _volunteers.CountAsync(query);
            var rows = await _volunteers.SearchAsync(query);

            return Ok(new PagedResult<VolunteerSummaryDto>
            {
                Items = rows.Select(ToSummary).ToList(),
                Page = page,
                PageSize = pageSize,
                TotalCount = total
            }, "Volunteers retrieved");
        }

        public async Task<ApiResponse<VolunteerDto>> GetAsync(string publicId)
        {
            var volunteer = await _volunteers.GetByPublicIdAsync(publicId);

            if (volunteer is null || !CanAccess(volunteer))
                return Warn<VolunteerDto>(NotFound);

            return Ok(ToDto(volunteer), "Volunteer retrieved");
        }

        public async Task<ApiResponse<IReadOnlyList<CapacityChangeDto>>> GetCapacityHistoryAsync(string publicId)
        {
            var volunteer = await _volunteers.GetByPublicIdAsync(publicId);

            if (volunteer is null || !CanAccess(volunteer))
                return Warn<IReadOnlyList<CapacityChangeDto>>(NotFound);

            var history = await _volunteers.GetCapacityHistoryAsync(volunteer.Id);

            return Ok<IReadOnlyList<CapacityChangeDto>>(history.Select(h => new CapacityChangeDto
            {
                FromBandCode = h.FromBandCode,
                ToBandCode = h.ToBandCode,
                Reason = h.Reason,
                Notes = h.Notes,
                ChangedAt = h.ChangedAt,
                ChangedBy = h.ChangedByName
            }).ToList(), "Capacity history");
        }

        public async Task<ApiResponse<IReadOnlyList<CapacityBandDto>>> GetCapacityBandsAsync()
        {
            var bands = await _volunteers.GetCapacityBandsAsync();

            return Ok<IReadOnlyList<CapacityBandDto>>(bands.Select(b => new CapacityBandDto
            {
                Code = b.Code,
                Label = b.Label,
                MinPerWeek = b.MinPerWeek,
                MaxPerWeek = b.MaxPerWeek,
                Description = b.Description
            }).ToList(), "Capacity bands");
        }

        public async Task<ApiResponse<IReadOnlyList<EligibleVolunteerDto>>> FindEligibleAsync(string? campusId, bool crisisCapable)
        {
            var campusKey = await ScopedCampusKeyAsync(campusId);

            if (campusKey is null)
                return Warn<IReadOnlyList<EligibleVolunteerDto>>("A campus is required to find eligible volunteers.");

            var rows = await _volunteers.FindEligibleAsync(campusKey.Value, crisisCapable, 20);

            var message = rows.Count > 0
                ? "Eligible volunteers, least loaded first"
                : crisisCapable
                    ? "No volunteer at this campus is both available and crisis-trained."
                    : "No volunteer at this campus has spare capacity.";

            return Ok<IReadOnlyList<EligibleVolunteerDto>>(rows.Select(v => new EligibleVolunteerDto
            {
                Id = v.PublicId,
                FullName = v.FullName,
                TeamName = v.TeamName,
                CurrentCaseLoad = v.CurrentCaseLoad,
                CapacityMaxPerWeek = v.CapacityMaxPerWeek,
                RemainingCapacity = v.RemainingCapacity,
                IsCrisisEligible = v.IsCrisisEligible,
                LastAssignedAt = v.LastAssignedAt
            }).ToList(), message);
        }

        // ==================================================================
        // Enrol
        // ==================================================================

        public async Task<ApiResponse<VolunteerDto>> EnrolAsync(EnrolVolunteerRequest request)
        {
            try
            {
                var personKey = await _volunteers.ResolvePersonIdAsync(request.PersonId);

                if (personKey is null)
                    return Warn<VolunteerDto>("That person does not exist. Record them first, then enrol them.");

                // One volunteer record per person — the unique key enforces it, but a
                // readable message beats a duplicate-key exception.
                var existing = await _volunteers.GetByPersonIdAsync(personKey.Value);

                if (existing is not null)
                    return Warn<VolunteerDto>($"{existing.FullName} is already enrolled as {existing.ReferenceCode}.");

                var band = await _volunteers.GetCapacityBandAsync(request.CapacityBandCode);

                if (band is null)
                    return Warn<VolunteerDto>($"Unknown capacity band '{request.CapacityBandCode}'.");

                // Campus: explicit, else the caller's, else refuse — a volunteer with no
                // campus could never be found by the assignment query.
                var campusKey = await _volunteers.ResolveCampusIdAsync(request.CampusId ?? _current.DefaultCampusId);

                if (campusKey is null)
                    return Warn<VolunteerDto>("A campus is required. Supply campusId.");

                if (!_current.CanAccessCampus(request.CampusId ?? _current.DefaultCampusId))
                    return Warn<VolunteerDto>("You cannot enrol a volunteer at that campus.");

                long? teamKey = null;

                if (!string.IsNullOrWhiteSpace(request.TeamId))
                {
                    var team = await _teams.GetByPublicIdAsync(request.TeamId);

                    if (team is null)
                        return Warn<VolunteerDto>("Unknown team.");

                    if (team.CampusId != campusKey.Value)
                        return Warn<VolunteerDto>("That team belongs to a different campus.");

                    if (!team.HasRoom)
                        return Warn<VolunteerDto>(
                            $"{team.Name} is at its limit of {team.MaxMembers} volunteers. " +
                            "Raise the limit or choose another team.");

                    teamKey = team.Id;
                }

                var volunteer = new Volunteer
                {
                    PublicId = Ulid.NewUlid(),
                    PersonId = personKey.Value,
                    CampusId = campusKey.Value,
                    TeamId = teamKey,
                    Status = VolunteerStatus.Active,
                    ServiceLevel = string.IsNullOrWhiteSpace(request.ServiceLevel) ? "LEVEL_0" : request.ServiceLevel.Trim(),
                    CapacityBandCode = band.Code,
                    StartedOn = request.StartedOn ?? _clock.GetUtcNow().UtcDateTime.Date
                };

                var id = await _volunteers.EnrolAsync(volunteer, await ActingUserIdAsync());

                _logger.LogInformation(
                    "Volunteer {PublicId} enrolled from person {PersonId} by {Account}",
                    volunteer.PublicId, request.PersonId, _current.AccountId);

                var created = await _volunteers.GetByIdAsync(id);

                return created is null
                    ? Fail<VolunteerDto>("The volunteer was enrolled but could not be read back.")
                    : Ok(ToDto(created), $"Enrolled as {created.ReferenceCode}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enrolling a volunteer");
                return Fail<VolunteerDto>("Unable to enrol the volunteer.");
            }
        }

        // ==================================================================
        // Update
        // ==================================================================

        public async Task<ApiResponse<VolunteerDto>> UpdateAsync(string publicId, UpdateVolunteerRequest request)
        {
            var volunteer = await _volunteers.GetByPublicIdAsync(publicId);

            if (volunteer is null || !CanAccess(volunteer))
                return Warn<VolunteerDto>(NotFound);

            if (!VolunteerStatus.IsKnown(request.Status))
                return Warn<VolunteerDto>($"Unknown status '{request.Status}'.");

            if (!BurnoutRisk.IsKnown(request.BurnoutRisk))
                return Warn<VolunteerDto>($"Unknown burnout risk '{request.BurnoutRisk}'.");

            // A volunteer may only be made ACTIVE once they can actually be worked
            // with: a login to see the case, and a verified Telegram to be told about
            // it. Marking somebody active without those produces a volunteer the
            // assignment picker will never return — active on the screen, invisible to
            // the system — and the team lead has no way to see why.
            //
            // The eligibility query enforces the same rule independently, because
            // reachability can lapse after activation (somebody disconnects Telegram).
            // This check is what makes the refusal explainable at the moment of the
            // decision rather than silent later.
            if (string.Equals(request.Status, VolunteerStatus.Active, StringComparison.Ordinal) &&
                !volunteer.IsReachable)
            {
                return Warn<VolunteerDto>(
                    $"{volunteer.FullName} cannot be set active yet. " +
                    string.Join(" ", volunteer.BlockingReasons()));
            }

            // Standing a volunteer down while they still hold open cases would orphan
            // those people silently. Refuse and say what has to happen first — the
            // team lead reassigns, then stands them down.
            if (VolunteerStatus.RequiresCaseHandover(request.Status) && volunteer.CurrentCaseLoad > 0)
            {
                return Warn<VolunteerDto>(
                    $"{volunteer.FullName} still holds {volunteer.CurrentCaseLoad} open " +
                    $"{(volunteer.CurrentCaseLoad == 1 ? "case" : "cases")}. " +
                    "Reassign them before setting the volunteer to " +
                    $"{request.Status.ToLowerInvariant().Replace('_', ' ')}.");
            }

            long? teamKey = volunteer.TeamId;

            if (request.TeamId is not null)
            {
                if (string.IsNullOrWhiteSpace(request.TeamId))
                {
                    teamKey = null; // explicit removal from the team
                }
                else
                {
                    var team = await _teams.GetByPublicIdAsync(request.TeamId);

                    if (team is null)
                        return Warn<VolunteerDto>("Unknown team.");

                    if (team.CampusId != volunteer.CampusId)
                        return Warn<VolunteerDto>("That team belongs to a different campus.");

                    // Only enforce the ceiling when they are actually moving teams.
                    if (team.Id != volunteer.TeamId && !team.HasRoom)
                        return Warn<VolunteerDto>($"{team.Name} is at its limit of {team.MaxMembers} volunteers.");

                    teamKey = team.Id;
                }
            }

            // Leaving is dated; a pause is not.
            DateTime? endedOn = VolunteerStatus.RequiresCaseHandover(request.Status)
                ? request.EndedOn ?? _clock.GetUtcNow().UtcDateTime.Date
                : null;

            var updated = await _volunteers.UpdateAsync(
                volunteer.Id, request.RowVersion, request.Status, teamKey,
                request.ServiceLevel, request.BurnoutRisk, endedOn, await ActingUserIdAsync());

            if (!updated)
                return Warn<VolunteerDto>("This record was changed by someone else. Reload and try again.");

            if (request.Status != volunteer.Status)
            {
                _logger.LogInformation(
                    "Volunteer {PublicId} status {From} -> {To} by {Account}",
                    publicId, volunteer.Status, request.Status, _current.AccountId);
            }

            var fresh = await _volunteers.GetByIdAsync(volunteer.Id);
            return Ok(ToDto(fresh!), "Volunteer updated");
        }

        public async Task<ApiResponse<VolunteerDto>> ChangeCapacityAsync(string publicId, ChangeCapacityRequest request)
        {
            var volunteer = await _volunteers.GetByPublicIdAsync(publicId);

            if (volunteer is null || !CanAccess(volunteer))
                return Warn<VolunteerDto>(NotFound);

            if (!CapacityChangeReasons.IsKnown(request.Reason))
                return Warn<VolunteerDto>($"Unknown reason '{request.Reason}'.");

            var band = await _volunteers.GetCapacityBandAsync(request.CapacityBandCode);

            if (band is null)
                return Warn<VolunteerDto>($"Unknown capacity band '{request.CapacityBandCode}'.");

            if (string.Equals(band.Code, volunteer.CapacityBandCode, StringComparison.Ordinal))
                return Warn<VolunteerDto>($"{volunteer.FullName} is already on the {band.Label} band.");

            // Stepping someone DOWN below their current load is allowed — that is
            // usually the point, e.g. burnout risk — but it must be visible rather
            // than silently leaving them over the new ceiling.
            var overCommitted = volunteer.CurrentCaseLoad > band.MaxPerWeek;

            var changed = await _volunteers.ChangeCapacityAsync(
                volunteer.Id, request.RowVersion, volunteer.CapacityBandCode, band.Code,
                request.Reason ?? CapacityChangeReasons.Other, request.Notes, await ActingUserIdAsync());

            if (!changed)
                return Warn<VolunteerDto>("This record was changed by someone else. Reload and try again.");

            _logger.LogInformation(
                "Volunteer {PublicId} capacity {From} -> {To} ({Reason}) by {Account}",
                publicId, volunteer.CapacityBandCode, band.Code, request.Reason, _current.AccountId);

            var fresh = await _volunteers.GetByIdAsync(volunteer.Id);

            var message = overCommitted
                ? $"Moved to {band.Label}. They currently hold {volunteer.CurrentCaseLoad} cases, " +
                  $"above the new limit of {band.MaxPerWeek} — they will take no new work until it falls."
                : $"Moved to {band.Label}.";

            return Ok(ToDto(fresh!), message);
        }

        public async Task<ApiResponse<VolunteerDto>> UpdateSafeguardingAsync(string publicId, SafeguardingRequest request)
        {
            var volunteer = await _volunteers.GetByPublicIdAsync(publicId);

            if (volunteer is null || !CanAccess(volunteer))
                return Warn<VolunteerDto>(NotFound);

            var today = _clock.GetUtcNow().UtcDateTime.Date;

            // A future-dated clearance would silently grant crisis eligibility before
            // the check has actually happened.
            if (request.BackgroundCheckedOn > today ||
                request.ConfidentialitySignedOn > today ||
                request.CrisisTrainedOn > today)
            {
                return Warn<VolunteerDto>("Safeguarding dates cannot be in the future.");
            }

            var updated = await _volunteers.UpdateSafeguardingAsync(
                volunteer.Id, request.RowVersion,
                request.BackgroundCheckedOn, request.ConfidentialitySignedOn, request.CrisisTrainedOn,
                await ActingUserIdAsync());

            if (!updated)
                return Warn<VolunteerDto>("This record was changed by someone else. Reload and try again.");

            var fresh = await _volunteers.GetByIdAsync(volunteer.Id);

            // Safeguarding changes decide who may hear a crisis disclosure, so they
            // are logged at warning level for the audit trail.
            _logger.LogWarning(
                "Safeguarding updated for volunteer {PublicId} by {Account}; crisis eligible: {Eligible}",
                publicId, _current.AccountId, fresh!.IsCrisisEligible);

            return Ok(ToDto(fresh), fresh.IsCrisisEligible
                ? "Safeguarding updated. This volunteer may now take crisis cases."
                : $"Safeguarding updated. Still missing: {string.Join(", ", fresh.MissingSafeguarding())}.");
        }

        // ==================================================================
        // Teams
        // ==================================================================

        public async Task<ApiResponse<IReadOnlyList<TeamDto>>> ListTeamsAsync(string? campusId, bool includeInactive)
        {
            var campusKey = await ScopedCampusKeyAsync(campusId);
            var teams = await _teams.ListAsync(campusKey, includeInactive);

            return Ok<IReadOnlyList<TeamDto>>(teams.Select(ToDto).ToList(), "Teams retrieved");
        }

        public async Task<ApiResponse<TeamDto>> CreateTeamAsync(CreateTeamRequest request)
        {
            var campusKey = await _volunteers.ResolveCampusIdAsync(request.CampusId ?? _current.DefaultCampusId);

            if (campusKey is null)
                return Warn<TeamDto>("A campus is required. Supply campusId.");

            if (!_current.CanAccessCampus(request.CampusId ?? _current.DefaultCampusId))
                return Warn<TeamDto>("You cannot create a team at that campus.");

            var name = request.Name.Trim();

            if (await _teams.NameExistsAsync(campusKey.Value, name))
                return Warn<TeamDto>($"A team called '{name}' already exists at this campus.");

            var leadKey = await _teams.ResolveLeadAccountIdAsync(request.LeadAccountId);

            if (!string.IsNullOrWhiteSpace(request.LeadAccountId) && leadKey is null)
                return Warn<TeamDto>("That team lead account does not exist or is disabled.");

            var leadMismatch = await LeadIsAtAnotherCampusAsync(leadKey, campusKey.Value);

            if (leadMismatch is not null) return Warn<TeamDto>(leadMismatch);

            var id = await _teams.CreateAsync(new Team
            {
                PublicId = Ulid.NewUlid(),
                CampusId = campusKey.Value,
                Name = name,
                LeadUserId = leadKey,
                MaxMembers = request.MaxMembers
            }, await ActingUserIdAsync());

            var created = await _teams.GetByIdAsync(id);
            return Ok(ToDto(created!), "Team created");
        }

        /// <summary>
        /// Who may do what on the team management screen.
        ///
        /// An administrator always has it. A pastor or team lead has it only when an
        /// administrator has switched the matching grant on, because the team a
        /// volunteer sits in decides whose pastoral records their lead can read —
        /// widening that is a deliberate act, not a default.
        /// </summary>
        private async Task<TeamAccess> ResolveTeamAccessAsync()
        {
            if (_current.IsAdmin) return TeamAccess.Administrator;

            if (_current.IsInRole(RoleCodes.Pastor))
            {
                return await _settings.GetBoolAsync("team.manage_by_pastor", false)
                    ? TeamAccess.CampusWide
                    : TeamAccess.None;
            }

            if (_current.IsInRole(RoleCodes.TeamLead))
            {
                return await _settings.GetBoolAsync("team.manage_by_team_lead", false)
                    ? TeamAccess.OwnTeamOnly
                    : TeamAccess.None;
            }

            return TeamAccess.None;
        }

        private enum TeamAccess
        {
            None,

            /// <summary>Rename and resize the one team they lead. Nothing else.</summary>
            OwnTeamOnly,

            /// <summary>Every team at their own campus, including the lead.</summary>
            CampusWide,

            /// <summary>Every team everywhere, plus create.</summary>
            Administrator
        }

        public async Task<ApiResponse<TeamAccessDto>> GetTeamAccessAsync()
        {
            var access = await ResolveTeamAccessAsync();

            // The screen renders from this rather than from the role, so the server
            // stays the single authority on what is allowed. A client that ignores it
            // still hits the same checks in UpdateTeamAsync.
            return Ok(new TeamAccessDto
            {
                CanOpen          = access != TeamAccess.None,
                CanCreate        = access == TeamAccess.Administrator,
                CanEditAnyTeam   = access is TeamAccess.Administrator or TeamAccess.CampusWide,
                CanEditOwnTeam   = access == TeamAccess.OwnTeamOnly,
                CanReassignLead  = access is TeamAccess.Administrator or TeamAccess.CampusWide,
                CanDeactivate    = access is TeamAccess.Administrator or TeamAccess.CampusWide,
                CanSeeAllCampuses = access == TeamAccess.Administrator,
                Scope            = access.ToString().ToUpperInvariant()
            }, "Team access resolved");
        }

        public async Task<ApiResponse<IReadOnlyList<LeadCandidateDto>>> ListLeadCandidatesAsync()
        {
            var access = await ResolveTeamAccessAsync();

            // Only somebody who can actually reassign a lead needs the list of people
            // who could be one. A team lead cannot, so they do not get the roster.
            if (access is not (TeamAccess.Administrator or TeamAccess.CampusWide))
            {
                return Warn<IReadOnlyList<LeadCandidateDto>>(
                    "You do not have permission to change who leads a team.");
            }

            // An administrator sees every campus; a pastor sees their own.
            var campusKey = access == TeamAccess.Administrator
                ? null
                : await _volunteers.ResolveCampusIdAsync(_current.CampusId);

            var rows = await _teams.ListLeadCandidatesAsync(campusKey);

            return Ok<IReadOnlyList<LeadCandidateDto>>(rows.Select(r => new LeadCandidateDto
            {
                AccountId = r.AccountId,
                Name = r.Name,
                RoleCode = r.RoleCode,
                CampusName = r.CampusName,
                LeadsTeam = r.LeadsTeam
            }).ToList(), "Lead candidates retrieved");
        }

        public async Task<ApiResponse<TeamDto>> UpdateTeamAsync(string publicId, UpdateTeamRequest request)
        {
            var access = await ResolveTeamAccessAsync();

            if (access == TeamAccess.None)
                return Warn<TeamDto>("You do not have permission to manage teams.");

            var team = await _teams.GetByPublicIdAsync(publicId);

            if (team is null || !_current.CanAccessCampus(team.CampusPublicId))
                return Warn<TeamDto>("Team not found.");

            // A team lead may only touch the team they actually lead. Without this the
            // grant would let any team lead rename or resize every other team on the
            // campus, which is not what "edit your own team" means.
            if (access == TeamAccess.OwnTeamOnly &&
                !string.Equals(team.LeadUserPublicId, _current.AccountId, StringComparison.Ordinal))
            {
                return Warn<TeamDto>("You can only edit the team you lead.");
            }

            var name = request.Name.Trim();

            if (await _teams.NameExistsAsync(team.CampusId, name, team.Id))
                return Warn<TeamDto>($"A team called '{name}' already exists at this campus.");

            // A team lead gets the name and the size. The lead and the active flag are
            // pinned to what is already stored and the request's values are ignored.
            //
            // Reassigning the lead is the dangerous one: a team lead who could set
            // lead_user_id could hand themselves another team, and with it every
            // escalation and pastoral note raised on that team's people. Retiring a
            // team is the other — it would let them take their own team out of the
            // assignment pool. Both stay with a pastor or an administrator.
            var restricted = access == TeamAccess.OwnTeamOnly;

            long? leadKey;

            if (restricted)
            {
                leadKey = team.LeadUserId;
            }
            else
            {
                leadKey = await _teams.ResolveLeadAccountIdAsync(request.LeadAccountId);

                if (!string.IsNullOrWhiteSpace(request.LeadAccountId) && leadKey is null)
                    return Warn<TeamDto>("That team lead account does not exist or is disabled.");

                // Same rule as creation: the team cannot move, so the lead has to be
                // at the campus the team already belongs to.
                var leadMismatch = await LeadIsAtAnotherCampusAsync(leadKey, team.CampusId);

                if (leadMismatch is not null) return Warn<TeamDto>(leadMismatch);
            }

            var isActive = restricted ? team.IsActive : request.IsActive;

            // Lowering the ceiling below the current membership would leave the team
            // permanently over limit with no way to explain it.
            if (request.MaxMembers < team.MemberCount)
            {
                return Warn<TeamDto>(
                    $"{team.Name} already has {team.MemberCount} volunteers. " +
                    $"Move some out before lowering the limit to {request.MaxMembers}.");
            }

            if (!isActive && team.MemberCount > 0)
            {
                return Warn<TeamDto>(
                    $"{team.Name} still has {team.MemberCount} active volunteers. " +
                    "Move them to another team before retiring it.");
            }

            var updated = await _teams.UpdateAsync(
                team.Id, request.RowVersion, name, leadKey,
                request.MaxMembers, isActive, await ActingUserIdAsync());

            if (!updated)
                return Warn<TeamDto>("This record was changed by someone else. Reload and try again.");

            var fresh = await _teams.GetByIdAsync(team.Id);
            return Ok(ToDto(fresh!), "Team updated");
        }

        // ==================================================================
        // Helpers
        // ==================================================================

        private bool CanAccess(Volunteer volunteer) => _current.CanAccessCampus(volunteer.CampusPublicId);

        /// <summary>
        /// Resolves the campus to filter by. A campus-scoped account is forced onto
        /// its own campus regardless of what it asked for.
        /// </summary>
        private async Task<long?> ScopedCampusKeyAsync(string? requested = null)
        {
            var effective = string.IsNullOrWhiteSpace(_current.CampusId) ? requested : _current.CampusId;
            return await _volunteers.ResolveCampusIdAsync(effective);
        }

        /// <summary>
        /// Refuses a lead whose own campus is not the team's, and explains why.
        /// Returns null when the pairing is fine.
        ///
        /// Now that an administrator can create a team at any campus, nothing else
        /// stops a lead at one site being put in charge of another site's team — and
        /// a lead reads every escalation raised on their team's people, so that would
        /// quietly punch a hole straight through the campus boundary.
        /// </summary>
        private async Task<string?> LeadIsAtAnotherCampusAsync(long? leadAccountId, long teamCampusId)
        {
            if (leadAccountId is null) return null;

            var leadCampus = await _teams.GetLeadCampusIdAsync(leadAccountId.Value);

            // No campus on their person record is a data gap, not a mismatch: refusing
            // here would block a legitimate lead for a reason they cannot fix.
            if (leadCampus is null || leadCampus == teamCampusId) return null;

            return "That person belongs to a different campus. " +
                   "A team lead must be at the same campus as the team they lead.";
        }

        private async Task<long?> ActingUserIdAsync()
        {
            if (string.IsNullOrWhiteSpace(_current.AccountId)) return null;

            var account = await _accounts.GetByPublicIdAsync(_current.AccountId);
            return account?.Id;
        }

        private static ApiResponse<T> Ok<T>(T data, string message) => new(ResponseType.Success, message, data);
        private static ApiResponse<T> Warn<T>(string message) => new(ResponseType.Warning, message, default!);
        private static ApiResponse<T> Fail<T>(string message) => new(ResponseType.Error, message, default!);

        private static VolunteerDto ToDto(Volunteer v) => new()
        {
            Id = v.PublicId,
            ReferenceCode = v.ReferenceCode,
            PersonId = v.PersonPublicId,
            FullName = v.FullName,
            PrimaryPhone = v.PrimaryPhone,
            PrimaryEmail = v.PrimaryEmail,
            CampusId = v.CampusPublicId,
            CampusName = v.CampusName,
            TeamId = v.TeamPublicId,
            TeamName = v.TeamName,
            Status = v.Status,
            ServiceLevel = v.ServiceLevel,
            CapacityBandCode = v.CapacityBandCode,
            CapacityBandLabel = v.CapacityBandLabel,
            CapacityMaxPerWeek = v.CapacityMaxPerWeek,
            CurrentCaseLoad = v.CurrentCaseLoad,
            RemainingCapacity = v.RemainingCapacity,
            LifetimeCasesAssigned = v.LifetimeCasesAssigned,
            LifetimeCasesClosed = v.LifetimeCasesClosed,
            StartedOn = v.StartedOn,
            EndedOn = v.EndedOn,
            BurnoutRisk = v.BurnoutRisk,
            LastCheckInOn = v.LastCheckInOn,
            NextCheckInOn = v.NextCheckInOn,
            BackgroundCheckedOn = v.BackgroundCheckedOn,
            ConfidentialitySignedOn = v.ConfidentialitySignedOn,
            CrisisTrainedOn = v.CrisisTrainedOn,
            BoundaryViolationCount = v.BoundaryViolationCount,
            IsCrisisEligible = v.IsCrisisEligible,
            MissingSafeguarding = v.MissingSafeguarding().ToList(),
            RowVersion = v.RowVersion,
            CreatedAt = v.CreatedAt
        };

        private static VolunteerSummaryDto ToSummary(Volunteer v) => new()
        {
            Id = v.PublicId,
            ReferenceCode = v.ReferenceCode,
            FullName = v.FullName,
            PrimaryPhone = v.PrimaryPhone,
            TeamName = v.TeamName,
            Status = v.Status,
            CapacityBandCode = v.CapacityBandCode,
            CurrentCaseLoad = v.CurrentCaseLoad,
            CapacityMaxPerWeek = v.CapacityMaxPerWeek,
            BurnoutRisk = v.BurnoutRisk,
            IsCrisisEligible = v.IsCrisisEligible,
            NextCheckInOn = v.NextCheckInOn
        };

        private static TeamDto ToDto(Team t) => new()
        {
            Id = t.PublicId,
            Name = t.Name,
            CampusId = t.CampusPublicId,
            CampusName = t.CampusName,
            LeadAccountId = t.LeadUserPublicId,
            LeadName = t.LeadName,
            MaxMembers = t.MaxMembers,
            MemberCount = t.MemberCount,
            HasRoom = t.HasRoom,
            IsActive = t.IsActive,
            RowVersion = t.RowVersion
        };
    }
}
