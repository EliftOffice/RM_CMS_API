using RM_CMS.Modules.Campuses.Data;
using RM_CMS.Modules.Care.Data;
using RM_CMS.Modules.Dashboards.Data;
using RM_CMS.Modules.Dashboards.Domain;
using RM_CMS.Modules.Identity.Data;
using RM_CMS.Modules.Identity.Services;
using RM_CMS.Modules.Volunteers.Data;
using RM_CMS.Utilities;

namespace RM_CMS.Modules.Dashboards.Services
{
    /// <summary>
    /// Assembles the pastor dashboard.
    ///
    /// A pastor oversees several teams, so the shape here is a step UP from the team
    /// lead dashboard rather than a copy of it: aggregate totals, a per-team
    /// leaderboard to say WHICH team needs attention, and only the escalations that
    /// actually reached pastor level — not a merged view of every team lead's own
    /// queue, which would bury the ones a pastor is personally on the hook for.
    ///
    /// Scope is resolved from the signed-in account exactly like
    /// <c>PipelineService</c> already does it: <c>ICurrentIdentity.CampusId</c> null
    /// means organisation-wide (how a single-campus church's pastor is set up, so
    /// they never have to name their own one campus), set means that campus only.
    /// It is never taken from a parameter.
    /// </summary>
    public interface IPastorDashboardService
    {
        Task<ApiResponse<PastorDashboard>> GetAsync();
    }

    public sealed class PastorDashboardService : IPastorDashboardService
    {
        private const int UrgentEscalationLimit = 10;
        private const int AwaitingReviewLimit = 10;
        private const int NurtureItemLimit = 12;

        private readonly IPastorDashboardRepository _pastor;
        private readonly ITeamLeadDashboardRepository _teamLeadReads;
        private readonly ITeamRepository _teams;
        private readonly IVolunteerRepository _volunteers;
        private readonly ICampusRepository _campuses;
        private readonly IUserAccountRepository _accounts;
        private readonly ICareLookupRepository _lookups;
        private readonly RM_CMS.Modules.Settings.Data.ISettingRepository _settings;
        private readonly ICurrentIdentity _current;
        private readonly TimeProvider _clock;
        private readonly ILogger<PastorDashboardService> _logger;

        public PastorDashboardService(
            IPastorDashboardRepository pastor,
            ITeamLeadDashboardRepository teamLeadReads,
            ITeamRepository teams,
            IVolunteerRepository volunteers,
            ICampusRepository campuses,
            IUserAccountRepository accounts,
            ICareLookupRepository lookups,
            RM_CMS.Modules.Settings.Data.ISettingRepository settings,
            ICurrentIdentity current,
            TimeProvider clock,
            ILogger<PastorDashboardService> logger)
        {
            _pastor = pastor;
            _teamLeadReads = teamLeadReads;
            _teams = teams;
            _volunteers = volunteers;
            _campuses = campuses;
            _accounts = accounts;
            _lookups = lookups;
            _settings = settings;
            _current = current;
            _clock = clock;
            _logger = logger;
        }

        public async Task<ApiResponse<PastorDashboard>> GetAsync()
        {
            var accountPublicId = _current.AccountId;

            if (string.IsNullOrWhiteSpace(accountPublicId))
                return Warn("You are not signed in.");

            var account = await _accounts.GetByPublicIdAsync(accountPublicId);

            if (account is null) return Warn("Your account could not be found.");

            var now = _clock.GetUtcNow().UtcDateTime;

            // Null means organisation-wide. Resolved through the volunteer module's
            // resolver purely because it is already injected everywhere else in this
            // slice — any campus resolver would do, they all read the same table.
            var campusKey = await _volunteers.ResolveCampusIdAsync(_current.CampusId);

            // Resolved directly from the campus record, NOT from the first team in
            // scope — a campus with no teams yet (a genuine, early state; see the
            // campus screen) still has a name, and inferring it from a team list that
            // might be empty would silently blank the header for exactly that campus.
            var campusName = campusKey is null
                ? null
                : (await _campuses.GetByIdAsync(campusKey.Value))?.Name;

            var teams = await _teams.ListAsync(campusKey, includeInactive: false);
            var teamIds = teams.Select(t => t.Id).ToList();

            if (teamIds.Count == 0)
            {
                _logger.LogInformation(
                    "Pastor dashboard requested by account {AccountId}; no teams in scope.",
                    accountPublicId);
            }

            var graceDays = await _lookups.GetIntSettingAsync("assignment.retry_delay_days", 3);

            var escalations = await _pastor.GetPastorAlertedEscalationsAsync(teamIds, now, UrgentEscalationLimit);
            var cases = await _teamLeadReads.GetCasesAsync(teamIds, now, AwaitingReviewLimit);
            var contacts = await _teamLeadReads.GetContactsAsync(teamIds, now, graceDays);
            var nurture = await _teamLeadReads.GetNurtureAsync(teamIds, now, NurtureItemLimit);

            // Same Monday-of-the-current-week rule the team lead card uses, so a
            // volunteer's trend never disagrees between the two screens.
            var weekStart = now.Date.AddDays(-(((int)now.DayOfWeek + 6) % 7));
            var volunteers = await _teamLeadReads.GetVolunteerLoadAsync(teamIds, weekStart);

            var teamHealth = await _pastor.GetTeamHealthAsync(teamIds, now);

            // At-risk counts are folded into the leaderboard from the SAME volunteer
            // list already fetched above, rather than a fourth query re-deriving the
            // health-flag trend logic.
            var atRiskByTeam = volunteers
                .Where(v => v.HealthFlag == VolunteerHealth.Red)
                .GroupBy(v => v.TeamName ?? string.Empty)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            var teamHealthWithRisk = teamHealth.Select(t => new TeamHealthRow
            {
                TeamId = t.TeamId,
                TeamName = t.TeamName,
                LeadName = t.LeadName,
                MemberCount = t.MemberCount,
                OpenEscalations = t.OpenEscalations,
                UnacknowledgedEscalations = t.UnacknowledgedEscalations,
                OverdueContacts = t.OverdueContacts,
                AwaitingReviewCases = t.AwaitingReviewCases,
                AtRiskVolunteers = atRiskByTeam.TryGetValue(t.TeamName, out var n) ? n : 0
            })
            // Worst team first: most unacknowledged escalations, then most overdue
            // contacts. That is the order a pastor should be looking at teams in.
            .OrderByDescending(t => t.UnacknowledgedEscalations)
            .ThenByDescending(t => t.OverdueContacts)
            .ToList();

            var atRiskVolunteers = volunteers
                .Where(v => v.HealthFlag == VolunteerHealth.Red)
                .Select(v => new AtRiskVolunteerRow
                {
                    VolunteerId = v.PublicId,
                    Name = v.Name,
                    TeamName = v.TeamName,
                    CurrentCaseLoad = v.CurrentCaseLoad,
                    CapacityMaxPerWeek = v.CapacityMaxPerWeek
                })
                .ToList();

            // The same window HuddleService itself uses, read from the same settings
            // — a pastor's "waiting" figure must mean what a lead's own agenda means.
            var huddleDay = await _settings.GetIntAsync("huddle.day_of_week", 6);
            var lookbackDays = await _settings.GetIntAsync("huddle.lookback_days", 7);
            var toDate = now.Date.AddDays(1);
            var fromDate = toDate.AddDays(-Math.Clamp(lookbackDays, 1, 60));

            var huddleCompliance = await _pastor.GetHuddleComplianceAsync(teamIds, fromDate, toDate);

            var dashboard = new PastorDashboard
            {
                ScopeLabel = campusKey is null ? "the whole organisation" : "your campus",
                CampusName = campusName,
                Escalations = escalations,
                Cases = cases,
                Contacts = contacts,
                Nurture = nurture,
                Teams = teamHealthWithRisk,
                HuddleCompliance = huddleCompliance,
                AtRiskVolunteers = atRiskVolunteers,
                GeneratedAt = now
            };

            return new ApiResponse<PastorDashboard>(
                ResponseType.Success, BuildMessage(dashboard), dashboard);
        }

        /// <summary>
        /// Leads with whatever most needs a pastor's OWN attention, not a generic
        /// "OK" — matches the priority a phone notification would carry.
        /// </summary>
        private static string BuildMessage(PastorDashboard dashboard)
        {
            if (dashboard.Escalations.Unacknowledged > 0)
                return $"{dashboard.Escalations.Unacknowledged} escalation(s) reached you and still need acknowledging.";

            if (dashboard.AtRiskVolunteers.Count > 0)
                return $"{dashboard.AtRiskVolunteers.Count} volunteer(s) across your teams are at risk.";

            if (dashboard.Cases.AwaitingReview > 0)
                return $"{dashboard.Cases.AwaitingReview} case(s) awaiting review across your teams.";

            return dashboard.Teams.Count == 0
                ? "There are no teams in your scope yet."
                : "Nothing needs your attention right now.";
        }

        private static ApiResponse<PastorDashboard> Warn(string message) =>
            new(ResponseType.Warning, message, default!);
    }
}
