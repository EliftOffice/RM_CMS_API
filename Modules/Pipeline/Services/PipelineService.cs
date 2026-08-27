using RM_CMS.Modules.Care.Data;
using RM_CMS.Modules.Dashboards.Data;
using RM_CMS.Modules.Identity.Data;
using RM_CMS.Modules.Identity.Domain;
using RM_CMS.Modules.Identity.Services;
using RM_CMS.Modules.Pipeline.Api;
using RM_CMS.Modules.Pipeline.Data;
using RM_CMS.Modules.Pipeline.Domain;
using RM_CMS.Utilities;

namespace RM_CMS.Modules.Pipeline.Services
{
    /// <summary>
    /// The people pipeline — every visitor and where their journey has reached.
    ///
    /// The whole of this class is really one decision, made in <see cref="ResolveScopeAsync"/>:
    ///
    ///   TEAM LEAD  → only cases belonging to the teams they lead
    ///   PASTOR     → their campus, or organisation-wide if their role grant has no campus
    ///   ADMIN      → everything
    ///
    /// A team lead has no claim on a person who was never assigned to their team, so
    /// people with no case at all are simply not in their list. That is not a filter
    /// they can turn off — the scope goes into the SQL, and a team lead who edits the
    /// request gets their own teams back either way.
    /// </summary>
    public interface IPipelineService
    {
        Task<ApiResponse<PipelineResultDto>> SearchAsync(
            int page, int pageSize, string? search, string? stage, string? status, bool includeUnstarted);
    }

    public sealed class PipelineService : IPipelineService
    {
        private readonly IPipelineRepository _pipeline;
        private readonly ITeamLeadDashboardRepository _teams;
        private readonly ICareCaseRepository _cases;
        private readonly IUserAccountRepository _accounts;
        private readonly ICurrentIdentity _current;
        private readonly TimeProvider _clock;
        private readonly ILogger<PipelineService> _logger;

        public PipelineService(
            IPipelineRepository pipeline,
            ITeamLeadDashboardRepository teams,
            ICareCaseRepository cases,
            IUserAccountRepository accounts,
            ICurrentIdentity current,
            TimeProvider clock,
            ILogger<PipelineService> logger)
        {
            _pipeline = pipeline;
            _teams = teams;
            _cases = cases;
            _accounts = accounts;
            _current = current;
            _clock = clock;
            _logger = logger;
        }

        public async Task<ApiResponse<PipelineResultDto>> SearchAsync(
            int page, int pageSize, string? search, string? stage, string? status, bool includeUnstarted)
        {
            var scope = await ResolveScopeAsync();

            if (scope is null) return Warn("You are not signed in.");

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);

            var query = new PipelineQuery
            {
                Search = string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
                Stage = string.IsNullOrWhiteSpace(stage) ? null : stage.Trim().ToUpperInvariant(),
                Status = string.IsNullOrWhiteSpace(status) ? null : status.Trim().ToUpperInvariant(),
                IncludeUnstarted = includeUnstarted,
                Skip = (page - 1) * pageSize,
                Take = pageSize
            };

            var today = _clock.GetUtcNow().UtcDateTime;

            var rows = await _pipeline.SearchAsync(query, scope.TeamIds, scope.CampusId, today);
            var total = await _pipeline.CountAsync(query, scope.TeamIds, scope.CampusId);
            var summary = await _pipeline.GetSummaryAsync(scope.TeamIds, scope.CampusId);

            var result = new PipelineResultDto
            {
                Scope = scope.Label,
                Page = page,
                PageSize = pageSize,
                TotalCount = total,
                Summary = summary,
                Items = rows.Select(ToDto).ToList()
            };

            return Ok(result, $"{total} {(total == 1 ? "person" : "people")} in {scope.Label}.");
        }

        // ==================================================================
        // Scope
        // ==================================================================
        private sealed record Scope(IReadOnlyList<long>? TeamIds, long? CampusId, string Label);

        /// <summary>
        /// Who the caller may see. Null team ids mean "not team-scoped" — a pastor or
        /// administrator — and are NOT the same as an empty list, which is a team lead
        /// who leads nothing and must therefore see nobody.
        /// </summary>
        private async Task<Scope?> ResolveScopeAsync()
        {
            var accountPublicId = _current.AccountId;

            if (string.IsNullOrWhiteSpace(accountPublicId)) return null;

            var account = await _accounts.GetByPublicIdAsync(accountPublicId);

            if (account is null) return null;

            if (_current.IsAdmin)
            {
                return new Scope(null, null, "the whole organisation");
            }

            if (_current.IsInRole(RoleCodes.Pastor))
            {
                // A pastor's campus comes from their role grant. No campus on the grant
                // means organisation-wide, which is how the seeded pastor accounts are
                // set up and is deliberate: a single-campus church should not have to
                // name its one campus everywhere.
                var campusId = await _cases.ResolveCampusIdAsync(_current.CampusId);

                return new Scope(null, campusId, campusId is null ? "the whole organisation" : "your campus");
            }

            var teamIds = await _teams.GetLedTeamIdsAsync(account.Id);

            if (teamIds.Count == 0)
            {
                _logger.LogInformation(
                    "Pipeline requested by account {AccountId}, which leads no team.", accountPublicId);
            }

            return new Scope(teamIds, null, "your team");
        }

        // ==================================================================
        // Mapping
        // ==================================================================
        private static PipelineRowDto ToDto(PipelineRow r) => new()
        {
            PersonId = r.PersonId,
            PersonName = r.PersonName,
            Phone = r.Phone,
            CaseId = r.CaseId,
            CaseReference = r.CaseReference,
            Stage = r.Stage,
            Status = r.Status,
            VolunteerName = r.VolunteerName,
            TeamName = r.TeamName,
            CampusName = r.CampusName,
            NurtureProgress = r.NurtureProgress,
            CurrentStepNumber = r.CurrentStepNumber,
            PlanStepCount = r.PlanStepCount,
            NextStepDueOn = r.NextStepDueOn,
            FirstVisitOn = r.FirstVisitOn,
            OpenedAt = r.OpenedAt,
            LastContactAt = r.LastContactAt,
            ClosedAt = r.ClosedAt,
            CloseReason = r.CloseReason,
            HasOpenEscalation = r.HasOpenEscalation,
            ContactAttemptCount = r.ContactAttemptCount,
            DaysSinceContact = r.DaysSinceContact
        };

        private static ApiResponse<PipelineResultDto> Ok(PipelineResultDto data, string message) =>
            new(ResponseType.Success, message, data);

        private static ApiResponse<PipelineResultDto> Warn(string message) =>
            new(ResponseType.Warning, message, default!);
    }
}
