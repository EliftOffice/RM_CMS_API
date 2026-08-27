using RM_CMS.Modules.Care.Data;
using RM_CMS.Modules.Dashboards.Data;
using RM_CMS.Modules.Dashboards.Domain;
using RM_CMS.Modules.Identity.Data;
using RM_CMS.Modules.Identity.Services;
using RM_CMS.Utilities;

namespace RM_CMS.Modules.Dashboards.Services
{
    /// <summary>
    /// Assembles the team lead dashboard.
    ///
    /// The whole point of this class is the first two lines of <see cref="GetAsync"/>:
    /// the teams shown are resolved from the SIGNED-IN ACCOUNT, never from a parameter.
    /// The MVP page took <c>?teamleadid=</c> from the query string and trusted it, so
    /// any authenticated user could read any team lead's queue — including the
    /// safeguarding escalations on it — by editing the URL. That is not a bug to port
    /// forward.
    /// </summary>
    public interface ITeamLeadDashboardService
    {
        Task<ApiResponse<TeamLeadDashboard>> GetAsync();
    }

    public sealed class TeamLeadDashboardService : ITeamLeadDashboardService
    {
        /// <summary>Enough to work from without turning the page into a list view.</summary>
        private const int UrgentEscalationLimit = 10;
        private const int AwaitingReviewLimit = 10;
        private const int NurtureItemLimit = 12;

        private readonly ITeamLeadDashboardRepository _dashboard;
        private readonly IUserAccountRepository _accounts;
        private readonly ICareLookupRepository _lookups;
        private readonly ICurrentIdentity _current;
        private readonly TimeProvider _clock;
        private readonly ILogger<TeamLeadDashboardService> _logger;

        public TeamLeadDashboardService(
            ITeamLeadDashboardRepository dashboard,
            IUserAccountRepository accounts,
            ICareLookupRepository lookups,
            ICurrentIdentity current,
            TimeProvider clock,
            ILogger<TeamLeadDashboardService> logger)
        {
            _dashboard = dashboard;
            _accounts = accounts;
            _lookups = lookups;
            _current = current;
            _clock = clock;
            _logger = logger;
        }

        public async Task<ApiResponse<TeamLeadDashboard>> GetAsync()
        {
            var accountPublicId = _current.AccountId;

            if (string.IsNullOrWhiteSpace(accountPublicId))
                return Warn("You are not signed in.");

            var account = await _accounts.GetByPublicIdAsync(accountPublicId);

            if (account is null) return Warn("Your account could not be found.");

            var now = _clock.GetUtcNow().UtcDateTime;

            var teams = await _dashboard.GetLedTeamsAsync(account.Id);
            var teamIds = await _dashboard.GetLedTeamIdsAsync(account.Id);

            // Leading no team is a real state, not an error: a newly promoted lead, or
            // one whose team was reassigned. They still see escalations assigned to
            // them personally, so the page is not empty when it should not be.
            if (teams.Count == 0)
            {
                _logger.LogInformation(
                    "Team lead dashboard requested by account {AccountId}, which leads no team.",
                    accountPublicId);
            }

            // The same grace period the mark-overdue job uses, so the page and the job
            // agree on what "overdue" means. Reading it here rather than hard-coding it
            // means changing the setting moves both.
            var graceDays = await _lookups.GetIntSettingAsync("assignment.retry_delay_days", 3);

            var escalations = await _dashboard.GetEscalationsAsync(
                account.Id, teamIds, now, UrgentEscalationLimit);

            var cases = await _dashboard.GetCasesAsync(teamIds, now, AwaitingReviewLimit);
            var contacts = await _dashboard.GetContactsAsync(teamIds, now, graceDays);
            var nurture = await _dashboard.GetNurtureAsync(teamIds, now, NurtureItemLimit);
            // The Monday of the current week. Week 1 of the trend is the last
            // COMPLETE week before it — a part-finished week would show the whole
            // team falling every Monday morning.
            var weekStart = now.Date.AddDays(-(((int)now.DayOfWeek + 6) % 7));

            var volunteers = await _dashboard.GetVolunteerLoadAsync(teamIds, weekStart);

            var dashboard = new TeamLeadDashboard
            {
                Teams = teams,
                Escalations = escalations,
                Nurture = nurture,
                Cases = cases,
                Contacts = contacts,
                Volunteers = volunteers,
                GeneratedAt = now
            };

            return new ApiResponse<TeamLeadDashboard>(
                ResponseType.Success, BuildMessage(dashboard), dashboard);
        }

        /// <summary>
        /// A one-line summary that leads with whatever most needs attention. The message
        /// is what a phone notification would show, so it should not just say "OK".
        /// </summary>
        private static string BuildMessage(TeamLeadDashboard dashboard)
        {
            if (dashboard.Escalations.Unacknowledged > 0)
            {
                return $"{dashboard.Escalations.Unacknowledged} escalation(s) need acknowledging.";
            }

            if (dashboard.Cases.AwaitingReview > 0)
            {
                return $"{dashboard.Cases.AwaitingReview} case(s) awaiting your review.";
            }

            if (dashboard.Contacts.Overdue > 0)
            {
                return $"{dashboard.Contacts.Overdue} overdue follow-up(s).";
            }

            return dashboard.Teams.Count == 0
                ? "You do not lead a team yet."
                : "Nothing needs your attention right now.";
        }

        private static ApiResponse<TeamLeadDashboard> Warn(string message) =>
            new(ResponseType.Warning, message, default!);
    }
}
