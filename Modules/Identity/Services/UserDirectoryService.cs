using RM_CMS.Modules.Identity.Api;
using RM_CMS.Modules.Identity.Data;
using RM_CMS.Modules.Identity.Domain;
using RM_CMS.Modules.People.Api;
using RM_CMS.Modules.People.Services;
using RM_CMS.Modules.Volunteers.Api;
using RM_CMS.Modules.Volunteers.Services;
using RM_CMS.Utilities;

// Three modules each define their own PagedResult; this file touches all three,
// so the identity one is named explicitly rather than left to using-order.
using PagedResult = RM_CMS.Modules.Identity.Api.PagedResult<RM_CMS.Modules.Identity.Api.DirectoryUserDto>;

namespace RM_CMS.Modules.Identity.Services
{
    /// <summary>
    /// User management: who exists, what they may do, and how someone moves up.
    ///
    /// The central rule is that a PERSON is the identity and everything else hangs off
    /// them. Promotion therefore never copies a person — it grants a role, and adds a
    /// volunteer record only when the new role needs one. Their cases, follow-ups,
    /// nurture history and notes reference <c>person_id</c> and <c>volunteer_id</c>,
    /// so none of it moves or is rewritten when authority changes.
    ///
    /// This type orchestrates rather than reimplements: account creation goes through
    /// <see cref="IIdentityService"/> (which owns username uniqueness and the password
    /// policy), person creation through <see cref="IPeopleService"/> (which owns
    /// duplicate detection), and volunteer enrolment through
    /// <see cref="IVolunteerService"/> (which owns capacity bands and reference codes).
    /// Duplicating any of those here is how they drift.
    /// </summary>
    public interface IUserDirectoryService
    {
        Task<ApiResponse<PagedResult>> ListAsync(
            int page, int pageSize, string? search, string? roleCode, bool? isActive);

        Task<ApiResponse<DirectoryUserDetailDto>> GetAsync(string personPublicId);

        /// <summary>Moves an existing person up the ladder without duplicating them.</summary>
        Task<ApiResponse<UserChangeResultDto>> PromoteAsync(string personPublicId, PromoteRequest request, RequestContext context);

        /// <summary>Creates a user directly, optionally recording the person too.</summary>
        Task<ApiResponse<UserChangeResultDto>> CreateAsync(CreateUserRequest request, RequestContext context);
    }

    public sealed class UserDirectoryService : IUserDirectoryService
    {
        /// <summary>
        /// The promotion ladder. DATA_ENTRY and ADMIN sit outside it deliberately:
        /// a data-entry operator is not a junior volunteer, and an administrator is
        /// not a senior pastor. Those are granted directly, not climbed to.
        /// </summary>
        private static readonly string[] Ladder =
        {
            RoleCodes.Volunteer, RoleCodes.TeamLead, RoleCodes.Pastor
        };

        private static readonly Dictionary<string, int> Rank = new(StringComparer.Ordinal)
        {
            [RoleCodes.DataEntry] = 10,
            [RoleCodes.Volunteer] = 20,
            [RoleCodes.TeamLead]  = 30,
            [RoleCodes.Pastor]    = 40,
            [RoleCodes.Admin]     = 50
        };

        private readonly IUserDirectoryRepository _directory;
        private readonly IUserAccountRepository _accounts;
        private readonly IIdentityService _identity;
        private readonly IPeopleService _people;
        private readonly IVolunteerService _volunteers;
        private readonly ICurrentIdentity _current;
        private readonly ILogger<UserDirectoryService> _logger;

        public UserDirectoryService(
            IUserDirectoryRepository directory,
            IUserAccountRepository accounts,
            IIdentityService identity,
            IPeopleService people,
            IVolunteerService volunteers,
            ICurrentIdentity current,
            ILogger<UserDirectoryService> logger)
        {
            _directory = directory;
            _accounts = accounts;
            _identity = identity;
            _people = people;
            _volunteers = volunteers;
            _current = current;
            _logger = logger;
        }

        // ==================================================================
        // Listing
        // ==================================================================
        public async Task<ApiResponse<PagedResult>> ListAsync(
            int page, int pageSize, string? search, string? roleCode, bool? isActive)
        {
            if (!string.IsNullOrWhiteSpace(roleCode) && !RoleCodes.IsKnown(roleCode))
                return Warn<PagedResult>($"Unknown role '{roleCode}'.");

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var query = new UserDirectoryQuery
            {
                Search = search,
                RoleCode = roleCode,
                IsActive = isActive,
                Skip = (page - 1) * pageSize,
                Take = pageSize
            };

            var rows = await _directory.ListAsync(query);
            var total = await _directory.CountAsync(query);

            return Ok(new PagedResult
            {
                Items = rows.Select(Map).ToList(),
                Page = page,
                PageSize = pageSize,
                TotalCount = total
            }, $"{total} user(s).");
        }

        public async Task<ApiResponse<DirectoryUserDetailDto>> GetAsync(string personPublicId)
        {
            var row = await _directory.GetByPersonPublicIdAsync(personPublicId);

            if (row is null) return Warn<DirectoryUserDetailDto>("That person was not found.");

            var detail = new DirectoryUserDetailDto { User = Map(row) };

            if (row.UserAccountId.HasValue)
            {
                var history = await _directory.GetRoleHistoryAsync(row.UserAccountId.Value);

                detail.RoleHistory = history.Select(h => new RoleHistoryDto
                {
                    RoleCode = h.RoleCode,
                    CampusName = h.CampusName,
                    GrantedAt = h.GrantedAt,
                    GrantedBy = h.GrantedByName
                }).ToList();
            }

            return Ok(detail, "User detail");
        }

        // ==================================================================
        // Promotion
        // ==================================================================
        public async Task<ApiResponse<UserChangeResultDto>> PromoteAsync(string personPublicId, PromoteRequest request, RequestContext context)
        {
            var target = (request.TargetRole ?? string.Empty).Trim().ToUpperInvariant();

            if (!RoleCodes.IsKnown(target))
                return Warn<UserChangeResultDto>($"Unknown role '{request.TargetRole}'.");

            var row = await _directory.GetByPersonPublicIdAsync(personPublicId);

            if (row is null)
                return Warn<UserChangeResultDto>("That person was not found.");

            // Standing, not just grants — a volunteer with no login holds no roles but
            // is plainly already a volunteer. See Map() for why the two differ.
            var standing = Standing(row);

            if (standing.Contains(target, StringComparer.Ordinal))
                return Warn<UserChangeResultDto>($"{row.FullName} is already {Label(target)}.");

            // Promotion moves UP. Granting something sideways or below is a role change,
            // not a promotion, and goes through the change-role action so the intent is
            // explicit rather than buried in a button called "Promote".
            var highest = HighestRank(standing);

            if (Rank.TryGetValue(target, out var targetRank) && targetRank <= highest)
            {
                return Warn<UserChangeResultDto>(
                    $"{row.FullName} already holds an equal or higher role. Use Change role instead.");
            }

            var result = new UserChangeResultDto();

            return await ApplyRoleAsync(row, target, result, context, new RoleApplication
            {
                CampusId = request.CampusId,
                CapacityBandCode = request.CapacityBandCode,
                TeamId = request.TeamId,
                LeadsTeamId = request.LeadsTeamId,
                GrantSystemAccess = request.GrantSystemAccess,
                InitialPassword = request.InitialPassword,
                MustChangePassword = true
            });
        }

        // ==================================================================
        // Direct creation
        // ==================================================================
        public async Task<ApiResponse<UserChangeResultDto>> CreateAsync(CreateUserRequest request, RequestContext context)
        {
            var target = (request.RoleCode ?? string.Empty).Trim().ToUpperInvariant();

            if (!RoleCodes.IsKnown(target))
                return Warn<UserChangeResultDto>($"Unknown role '{request.RoleCode}'.");

            var result = new UserChangeResultDto();
            string personPublicId;

            if (!string.IsNullOrWhiteSpace(request.PersonId))
            {
                personPublicId = request.PersonId!;
            }
            else
            {
                // Recording the person goes through PeopleService so duplicate detection
                // applies here exactly as it does on the intake screen — creating a user
                // must not become a back door for a second copy of somebody.
                if (string.IsNullOrWhiteSpace(request.GivenName))
                    return Warn<UserChangeResultDto>("A first name is required to create a new person.");

                if (string.IsNullOrWhiteSpace(request.Mobile))
                    return Warn<UserChangeResultDto>("A mobile number is required — it is also the username.");

                var contacts = new List<ContactRequest>
                {
                    new() { ContactType = "MOBILE", Value = request.Mobile!, IsPrimary = true }
                };

                if (!string.IsNullOrWhiteSpace(request.Email))
                    contacts.Add(new ContactRequest { ContactType = "EMAIL", Value = request.Email!, IsPrimary = false });

                var created = await _people.CreateAsync(new CreatePersonRequest
                {
                    GivenName = request.GivenName!,
                    FamilyName = request.FamilyName,
                    CampusId = request.CampusId,
                    Contacts = contacts
                });

                if (created.ResponseType != ResponseType.Success || created.Data is null)
                    // Carry the code as well as the text: the screen uses it to add
                    // the remedy that fits IT — "find them instead" — which the people
                    // module cannot know about.
                    return Warn<UserChangeResultDto>(created.Message, created.Code);

                personPublicId = created.Data.Id;
                result.PersonCreated = true;
                result.Notes.Add($"Recorded {created.Data.FullName} as a new person.");
            }

            var row = await _directory.GetByPersonPublicIdAsync(personPublicId);

            if (row is null)
                return Warn<UserChangeResultDto>("That person was not found.");

            if (SplitRoles(row.RoleCodes).Contains(target, StringComparer.Ordinal))
                return Warn<UserChangeResultDto>($"They already hold the {Label(target)} role.");

            return await ApplyRoleAsync(row, target, result, context, new RoleApplication
            {
                CampusId = request.CampusId,
                CapacityBandCode = request.CapacityBandCode,
                TeamId = request.TeamId,
                LeadsTeamId = request.LeadsTeamId,
                StartedOn = request.StartedOn,
                GrantSystemAccess = request.GrantSystemAccess,
                InitialPassword = request.InitialPassword,
                MustChangePassword = request.MustChangePassword
            });
        }

        // ==================================================================
        // The shared core: give a person a role, and whatever that role needs
        // ==================================================================
        private sealed class RoleApplication
        {
            public string? CampusId { get; init; }
            public string? CapacityBandCode { get; init; }
            public string? TeamId { get; init; }
            public string? LeadsTeamId { get; init; }
            public DateTime? StartedOn { get; init; }
            public bool GrantSystemAccess { get; init; }
            public string? InitialPassword { get; init; }
            public bool MustChangePassword { get; init; }
        }

        private async Task<ApiResponse<UserChangeResultDto>> ApplyRoleAsync(
            DirectoryRow row, string target, UserChangeResultDto result, RequestContext context, RoleApplication options)
        {
            // Only a volunteer may exist without a login. Every other role is exercised
            // through the UI, so an account for them is not optional.
            var needsAccount = options.GrantSystemAccess || target != RoleCodes.Volunteer;

            var accountId = row.UserAccountId;
            var accountPublicId = row.AccountPublicId;

            if (needsAccount && accountId is null)
            {
                var username = await _directory.GetPrimaryMobileAsync(row.PersonId);

                if (string.IsNullOrWhiteSpace(username))
                {
                    return Warn<UserChangeResultDto>(
                        $"{row.FullName} has no mobile number on record, and the mobile number is the username. " +
                        "Add one to their profile first.");
                }

                var creation = await _identity.CreateAccountAsync(new CreateAccountRequest
                {
                    PersonId = row.PersonPublicId,
                    Username = username!,
                    InitialPassword = options.InitialPassword,

                    // Carried from the caller. Dropping it here was the whole bug:
                    // RoleApplication held the administrator's choice and then never
                    // passed it on, so CreateAccountAsync fell back to its default.
                    MustChangePassword = options.MustChangePassword,
                    Roles = new List<RoleGrantRequest>
                    {
                        new() { RoleCode = target, CampusId = options.CampusId }
                    }
                }, _current.AccountId, context);

                if (creation.ResponseType != ResponseType.Success || creation.Data is null)
                    return Warn<UserChangeResultDto>(creation.Message);

                accountPublicId = creation.Data.Account.Id;
                accountId = (await _accounts.GetByPublicIdAsync(accountPublicId))?.Id;

                result.AccountCreated = true;
                result.RoleGranted = true;
                result.GeneratedPassword = creation.Data.GeneratedPassword;
                result.Notes.Add($"Created a sign-in for {username} with the {Label(target)} role.");
            }
            else if (accountId is not null)
            {
                // The account already exists, so this is purely an additional grant.
                var actingId = await ResolveActingUserIdAsync();
                var campusId = await _directory.ResolveCampusIdAsync(options.CampusId);

                result.RoleGranted = await _directory.GrantRoleAsync(accountId.Value, target, campusId, actingId);

                if (result.RoleGranted)
                    result.Notes.Add($"Granted the {Label(target)} role.");
            }
            else
            {
                result.Notes.Add(
                    $"{row.FullName} has no sign-in. They can be assigned work as a volunteer, " +
                    "but cannot use the system until an account is created.");
            }

            // ---- the volunteer record ----
            // Needed by VOLUNTEER, and kept for a promoted volunteer so a player-coach
            // team lead can still carry cases. This is why promotion is additive.
            if (target == RoleCodes.Volunteer && row.VolunteerId is null)
            {
                if (string.IsNullOrWhiteSpace(options.CapacityBandCode))
                    return Warn<UserChangeResultDto>("Choose how many people they can follow up each week.");

                var enrolled = await _volunteers.EnrolAsync(new EnrolVolunteerRequest
                {
                    PersonId = row.PersonPublicId,
                    CapacityBandCode = options.CapacityBandCode!,
                    CampusId = options.CampusId,
                    TeamId = options.TeamId,

                    // Null defaults to today inside the volunteer service, which is the
                    // right answer for someone enrolled as they walk in.
                    StartedOn = options.StartedOn
                });

                if (enrolled.ResponseType != ResponseType.Success)
                    return Warn<UserChangeResultDto>(enrolled.Message);

                result.VolunteerRecordCreated = true;
                result.Notes.Add("Created their volunteer record.");
            }

            // ---- put a team lead in charge of a team ----
            if (target == RoleCodes.TeamLead && !string.IsNullOrWhiteSpace(options.LeadsTeamId) && accountId is not null)
            {
                var teamId = await _directory.ResolveTeamIdAsync(options.LeadsTeamId);

                if (teamId is null)
                {
                    result.Notes.Add("The chosen team was not found, so nobody was put in charge of it.");
                }
                else
                {
                    var actingId = await ResolveActingUserIdAsync();

                    result.TeamLeadAssigned = await _directory.SetTeamLeadAsync(teamId.Value, accountId.Value, actingId);

                    if (result.TeamLeadAssigned)
                        result.Notes.Add("Put them in charge of the team.");
                }
            }

            _logger.LogInformation(
                "Role {Role} applied to person {PersonId} (account created: {AccountCreated}, volunteer created: {VolunteerCreated})",
                target, row.PersonPublicId, result.AccountCreated, result.VolunteerRecordCreated);

            var refreshed = await _directory.GetByPersonPublicIdAsync(row.PersonPublicId);
            result.User = refreshed is null ? Map(row) : Map(refreshed);

            return Ok(result, $"{result.User.FullName} is now {Label(target)}.");
        }

        // ==================================================================
        // Mapping and helpers
        // ==================================================================
        private static DirectoryUserDto Map(DirectoryRow row)
        {
            var roles = SplitRoles(row.RoleCodes);
            var standing = Standing(row);

            var highest = standing.Count == 0 ? null : standing.OrderByDescending(RankOf).First();

            return new DirectoryUserDto
            {
                PersonId = row.PersonPublicId,
                PersonReferenceCode = row.PersonReferenceCode,
                FullName = row.FullName,
                LifecycleStatus = row.LifecycleStatus,
                Mobile = row.PrimaryMobile,
                Email = row.PrimaryEmail,
                CampusId = row.CampusPublicId,
                CampusName = row.CampusName,

                AccountId = row.AccountPublicId,
                Username = row.Username,
                HasAccount = row.UserAccountId.HasValue,
                IsActive = row.IsActive ?? false,
                MustChangePassword = row.MustChangePassword ?? false,
                LastLoginAt = row.LastLoginAt,
                AccountRowVersion = row.AccountRowVersion,

                Roles = roles,
                HighestRole = highest,
                PromotableTo = NextRungs(standing),

                HasTelegram = row.HasTelegram,

                IsVolunteer = row.VolunteerId.HasValue,
                VolunteerId = row.VolunteerPublicId,
                VolunteerReferenceCode = row.VolunteerReferenceCode,
                VolunteerStatus = row.VolunteerStatus,
                CapacityBandCode = row.CapacityBandCode,
                CurrentCaseLoad = row.CurrentCaseLoad,
                TeamName = row.TeamName,

                CreatedAt = row.CreatedAt
            };
        }

        /// <summary>
        /// What this person effectively is: their granted roles, plus VOLUNTEER when a
        /// volunteer record exists without the matching grant (a helper who carries
        /// cases but has no login).
        /// </summary>
        private static List<string> Standing(DirectoryRow row)
        {
            var standing = SplitRoles(row.RoleCodes);

            if (row.VolunteerId.HasValue && !standing.Contains(RoleCodes.Volunteer, StringComparer.Ordinal))
                standing.Add(RoleCodes.Volunteer);

            return standing;
        }

        private static List<string> SplitRoles(string? concatenated) =>
            string.IsNullOrWhiteSpace(concatenated)
                ? new List<string>()
                : concatenated.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        private static int RankOf(string role) => Rank.TryGetValue(role, out var r) ? r : 0;

        private static int HighestRank(IEnumerable<string> roles) =>
            roles.Select(RankOf).DefaultIfEmpty(0).Max();

        /// <summary>
        /// The rungs above where they stand. A person with no roles at all — a plain
        /// visitor — can be promoted to Volunteer, which is the entry to the ladder.
        /// </summary>
        private static List<string> NextRungs(IEnumerable<string> roles)
        {
            var held = roles.ToList();
            var highest = HighestRank(held);

            return Ladder
                .Where(r => RankOf(r) > highest && !held.Contains(r, StringComparer.Ordinal))
                .ToList();
        }

        private static string Label(string roleCode) => roleCode switch
        {
            RoleCodes.Admin     => "Administrator",
            RoleCodes.Pastor    => "Pastor",
            RoleCodes.TeamLead  => "Team Lead",
            RoleCodes.Volunteer => "Volunteer",
            RoleCodes.DataEntry => "Data Entry Operator",
            _ => roleCode
        };

        private async Task<long?> ResolveActingUserIdAsync()
        {
            var publicId = _current.AccountId;

            if (string.IsNullOrWhiteSpace(publicId)) return null;

            return (await _accounts.GetByPublicIdAsync(publicId))?.Id;
        }

        private static ApiResponse<T> Ok<T>(T data, string message) => new(ResponseType.Success, message, data);
        private static ApiResponse<T> Warn<T>(string message, string? code = null) =>
            new(ResponseType.Warning, message, default!, code);
    }
}
