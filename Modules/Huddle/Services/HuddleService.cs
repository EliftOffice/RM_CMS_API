using RM_CMS.Modules.Dashboards.Data;
using RM_CMS.Modules.Huddle.Api;
using RM_CMS.Modules.Huddle.Data;
using RM_CMS.Modules.Huddle.Domain;
using RM_CMS.Modules.Identity.Data;
using RM_CMS.Modules.Settings.Data;
using RM_CMS.Utilities;
using RM_CMS.Modules.Identity.Services;

namespace RM_CMS.Modules.Huddle.Services
{
    /// <summary>
    /// The weekly team huddle: the lead reviews the week's contacts and records whether
    /// each volunteer's escalation judgement was right.
    ///
    /// This is the only mechanism in the system that can catch an UNDER-ESCALATION — a
    /// volunteer who heard something serious and did not pass it on. The escalation
    /// chase-up cannot: it can only chase escalations that were raised. That is why the
    /// huddle is worth rebuilding rather than quietly dropping.
    ///
    /// Scope, as everywhere else on the team lead's pages, comes from the caller's own
    /// account. There is no team parameter.
    /// </summary>
    public interface IHuddleService
    {
        Task<ApiResponse<HuddleAgendaDto>> GetAgendaAsync(int? lookbackDays);

        Task<ApiResponse<VerdictResultDto>> SubmitAsync(SubmitVerdictsRequest request);
    }

    public sealed class HuddleService : IHuddleService
    {
        private const int MaxAgendaItems = 200;

        private readonly IHuddleRepository _huddle;
        private readonly ITeamLeadDashboardRepository _teams;
        private readonly ISettingRepository _settings;
        private readonly IUserAccountRepository _accounts;
        private readonly ICurrentIdentity _current;
        private readonly TimeProvider _clock;
        private readonly ILogger<HuddleService> _logger;

        public HuddleService(
            IHuddleRepository huddle,
            ITeamLeadDashboardRepository teams,
            ISettingRepository settings,
            IUserAccountRepository accounts,
            ICurrentIdentity current,
            TimeProvider clock,
            ILogger<HuddleService> logger)
        {
            _huddle = huddle;
            _teams = teams;
            _settings = settings;
            _accounts = accounts;
            _current = current;
            _clock = clock;
            _logger = logger;
        }

        // ==================================================================
        // Agenda
        // ==================================================================
        public async Task<ApiResponse<HuddleAgendaDto>> GetAgendaAsync(int? lookbackDays)
        {
            var account = await ResolveAccountAsync();

            if (account is null) return Warn<HuddleAgendaDto>("You are not signed in.");

            var teamIds = await _teams.GetLedTeamIdsAsync(account.Id);

            var now = _clock.GetUtcNow().UtcDateTime;
            var huddleDay = await _settings.GetIntAsync("huddle.day_of_week", 6);
            var lookback = lookbackDays ?? await _settings.GetIntAsync("huddle.lookback_days", 7);

            lookback = Math.Clamp(lookback, 1, 60);

            var toDate = now.Date.AddDays(1);            // include everything logged today
            var fromDate = toDate.AddDays(-lookback);

            if (teamIds.Count == 0)
            {
                return Ok(new HuddleAgendaDto
                {
                    FromDate = fromDate,
                    ToDate = toDate,
                    IsHuddleDay = IsHuddleDay(now, huddleDay),
                    HuddleDayOfWeek = huddleDay
                }, "You do not lead a team yet.");
            }

            var items = await _huddle.GetAgendaAsync(teamIds, fromDate, toDate, MaxAgendaItems);
            var older = await _huddle.CountOlderUnassessedAsync(teamIds, fromDate);
            var assessed = await _huddle.CountAssessedInWindowAsync(teamIds, fromDate, toDate);

            var agenda = new HuddleAgendaDto
            {
                FromDate = fromDate,
                ToDate = toDate,
                IsHuddleDay = IsHuddleDay(now, huddleDay),
                HuddleDayOfWeek = huddleDay,
                OlderUnassessedCount = older,
                AssessedThisWeek = assessed,
                Items = items.Select(i => new HuddleItemDto
                {
                    InteractionId = i.InteractionId,
                    CaseReference = i.CaseReference,
                    VolunteerName = i.VolunteerName,
                    PersonName = i.PersonName,
                    Status = i.Status,
                    Outcome = i.OutcomeLabel ?? i.OutcomeCode,
                    Intent = i.IntentLabel ?? i.IntentCode,
                    OccurredAt = i.OccurredAt,
                    Notes = i.Notes,
                    RaisedEscalation = i.RaisedEscalation
                }).ToList()
            };

            var message = items.Count == 0
                ? older > 0
                    ? $"Nothing from this week. {older} older contact(s) still unassessed."
                    : "Nothing to review."
                : older > 0
                    ? $"{items.Count} to review, and {older} older still waiting."
                    : $"{items.Count} to review.";

            return Ok(agenda, message);
        }

        /// <summary>
        /// The app's convention is 1 = Monday ... 7 = Sunday, which is how the church
        /// says it aloud and what the MVP's system_config held. .NET's DayOfWeek is
        /// 0 = Sunday, so the conversion happens here rather than renumbering the
        /// setting and confusing anyone reading it.
        /// </summary>
        private static bool IsHuddleDay(DateTime nowUtc, int huddleDayOfWeek)
        {
            var todayIso = nowUtc.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)nowUtc.DayOfWeek;

            return todayIso == huddleDayOfWeek;
        }

        // ==================================================================
        // Verdicts
        // ==================================================================
        public async Task<ApiResponse<VerdictResultDto>> SubmitAsync(SubmitVerdictsRequest request)
        {
            var account = await ResolveAccountAsync();

            if (account is null) return Warn<VerdictResultDto>("You are not signed in.");

            var teamIds = await _teams.GetLedTeamIdsAsync(account.Id);

            if (teamIds.Count == 0)
                return Warn<VerdictResultDto>("You do not lead a team.");

            var unknown = request.Verdicts
                .Where(v => !HuddleAssessment.IsKnown(v.Assessment))
                .Select(v => v.Assessment)
                .Distinct()
                .ToList();

            if (unknown.Count > 0)
                return Warn<VerdictResultDto>($"Unknown assessment '{unknown[0]}'.");

            // A miscalibration without a reason is not worth recording: the verdict
            // exists to be discussed at the volunteer's next check-in, and "you
            // over-escalated" with nothing attached is not a conversation.
            var missingNote = request.Verdicts
                .Where(v => HuddleAssessment.IsMiscalibration(v.Assessment) &&
                            string.IsNullOrWhiteSpace(v.Note))
                .ToList();

            if (missingNote.Count > 0)
            {
                return Warn<VerdictResultDto>(
                    $"{missingNote.Count} verdict(s) mark a miscalibration without a note. " +
                    "Say briefly what should have happened — it is what the volunteer will be shown.");
            }

            var now = _clock.GetUtcNow().UtcDateTime;

            var verdicts = request.Verdicts.Select(v => new HuddleVerdict
            {
                InteractionId = v.InteractionId,
                Assessment = v.Assessment,
                Note = v.Note
            }).ToList();

            var recorded = await _huddle.SaveVerdictsAsync(verdicts, teamIds, account.Id, now);

            var under = request.Verdicts.Count(v =>
                string.Equals(v.Assessment, HuddleAssessment.UnderEscalated, StringComparison.Ordinal));

            var over = request.Verdicts.Count(v =>
                string.Equals(v.Assessment, HuddleAssessment.OverEscalated, StringComparison.Ordinal));

            if (under > 0)
            {
                // Worth a log line of its own: an under-escalation means a concern was
                // heard and not passed on, and that is the failure this whole meeting
                // exists to find.
                _logger.LogWarning(
                    "Huddle by account {AccountId} recorded {Count} UNDER-ESCALATION verdict(s).",
                    _current.AccountId, under);
            }

            var result = new VerdictResultDto
            {
                Submitted = verdicts.Count,
                Recorded = recorded,
                Rejected = verdicts.Count - recorded,
                UnderEscalated = under,
                OverEscalated = over
            };

            var message = result.Rejected > 0
                ? $"{recorded} recorded, {result.Rejected} rejected — those contacts are not on your teams."
                : under > 0
                    ? $"{recorded} recorded, including {under} under-escalation(s) to raise at their check-in."
                    : $"{recorded} recorded.";

            return Ok(result, message);
        }

        // ==================================================================
        // Helpers
        // ==================================================================
        private async Task<Identity.Domain.UserAccount?> ResolveAccountAsync()
        {
            var publicId = _current.AccountId;

            return string.IsNullOrWhiteSpace(publicId)
                ? null
                : await _accounts.GetByPublicIdAsync(publicId);
        }

        private static ApiResponse<T> Ok<T>(T data, string message) => new(ResponseType.Success, message, data);
        private static ApiResponse<T> Warn<T>(string message) => new(ResponseType.Warning, message, default!);
    }
}
