using RM_CMS.Modules.CheckIns.Api;
using RM_CMS.Modules.CheckIns.Data;
using RM_CMS.Modules.CheckIns.Domain;
using RM_CMS.Modules.Dashboards.Data;
using RM_CMS.Modules.Identity.Data;
using RM_CMS.Modules.Identity.Domain;
using RM_CMS.Modules.Identity.Services;
using RM_CMS.Modules.Volunteers.Api;
using RM_CMS.Modules.Volunteers.Services;
using RM_CMS.Utilities;

namespace RM_CMS.Modules.CheckIns.Services
{
    /// <summary>
    /// Volunteer check-ins: the team lead's pastoral duty toward their own people.
    ///
    /// Two rules do the work here:
    ///
    /// 1. WHO HELD IT COMES FROM THE TOKEN, never the payload. A check-in is a record
    ///    that a named person had a pastoral conversation with a volunteer; accepting
    ///    that name from the browser would make it unreliable exactly where it matters,
    ///    such as a boundary issue nobody will admit to having been told about.
    ///
    /// 2. A LEAD MAY ONLY CHECK IN ON THEIR OWN TEAM. Pastors and administrators are
    ///    above that scope; everyone else is confined to the volunteers they lead.
    ///
    /// A capacity change made during the conversation is delegated to
    /// <see cref="IVolunteerService"/> rather than written here, so the capacity history
    /// row and the band validation happen the one way they happen everywhere.
    /// </summary>
    public interface ICheckInService
    {
        Task<ApiResponse<CheckInDto>> RecordAsync(RecordCheckInRequest request);

        Task<ApiResponse<IReadOnlyList<CheckInDto>>> GetForVolunteerAsync(string volunteerPublicId, int limit);

        /// <summary>Who on the caller's teams is due — the team lead's working list.</summary>
        Task<ApiResponse<IReadOnlyList<CheckInDueDto>>> GetDueAsync();
    }

    public sealed class CheckInService : ICheckInService
    {
        /// <summary>
        /// How long since the last check-in counts as overdue when none was scheduled.
        /// The rhythm the church works to is monthly, and this is the grace on top.
        /// </summary>
        private const int DefaultOverdueDays = 35;

        private readonly ICheckInRepository _checkIns;
        private readonly ITeamLeadDashboardRepository _teams;
        private readonly IVolunteerService _volunteers;
        private readonly IUserAccountRepository _accounts;
        private readonly ICurrentIdentity _current;
        private readonly TimeProvider _clock;
        private readonly ILogger<CheckInService> _logger;

        public CheckInService(
            ICheckInRepository checkIns,
            ITeamLeadDashboardRepository teams,
            IVolunteerService volunteers,
            IUserAccountRepository accounts,
            ICurrentIdentity current,
            TimeProvider clock,
            ILogger<CheckInService> logger)
        {
            _checkIns = checkIns;
            _teams = teams;
            _volunteers = volunteers;
            _accounts = accounts;
            _current = current;
            _clock = clock;
            _logger = logger;
        }

        // ==================================================================
        // Record
        // ==================================================================
        public async Task<ApiResponse<CheckInDto>> RecordAsync(RecordCheckInRequest request)
        {
            if (!CheckInMeetingType.IsKnown(request.MeetingType))
                return Warn<CheckInDto>($"Unknown meeting type '{request.MeetingType}'.");

            if (!CheckInTone.IsKnown(request.EmotionalTone))
                return Warn<CheckInDto>($"Unknown emotional tone '{request.EmotionalTone}'.");

            var account = await ResolveAccountAsync();

            if (account is null) return Warn<CheckInDto>("You are not signed in.");

            var volunteer = await _checkIns.ResolveVolunteerAsync(request.VolunteerId);

            if (volunteer is null) return Warn<CheckInDto>("That volunteer was not found.");

            if (!await MayActOnAsync(account.Id, volunteer.Value.TeamId))
            {
                _logger.LogWarning(
                    "Account {AccountId} attempted a check-in on volunteer {VolunteerId}, whose team they do not lead.",
                    _current.AccountId, request.VolunteerId);

                return Warn<CheckInDto>("You can only record check-ins for volunteers on your own team.");
            }

            var now = _clock.GetUtcNow().UtcDateTime;
            var heldOn = (request.HeldOn ?? now).Date;

            if (heldOn > now.Date)
                return Warn<CheckInDto>("A check-in cannot be dated in the future.");

            if (request.NextCheckInOn is { } next && next.Date <= heldOn)
                return Warn<CheckInDto>("The next check-in must be after this one.");

            var checkIn = new VolunteerCheckIn
            {
                PublicId = Ulid.NewUlid(),
                VolunteerId = volunteer.Value.VolunteerId,
                ConductedBy = account.Id,
                HeldOn = heldOn,
                DurationMinutes = request.DurationMinutes,
                MeetingType = request.MeetingType,
                EmotionalTone = request.EmotionalTone,
                Concerns = request.Concerns,
                TrainingNeeds = request.TrainingNeeds,
                ActionItems = request.ActionItems,
                CapacityReviewed = request.CapacityReviewed || !string.IsNullOrWhiteSpace(request.NewCapacityBandCode),
                BoundaryIssuesRaised = request.BoundaryIssuesRaised,
                FollowUpRequired = request.FollowUpRequired,
                NextCheckInOn = request.NextCheckInOn?.Date,
                CreatedAt = now
            };

            var id = await _checkIns.RecordAsync(checkIn, account.Id);
            checkIn.Id = id;

            // The band change is a separate, auditable act. It runs after the check-in
            // is safely recorded: if it fails, the conversation is still on file rather
            // than lost because a band code was wrong.
            var capacityNote = await ApplyCapacityChangeAsync(request, heldOn);

            if (checkIn.NeedsAttention)
            {
                _logger.LogInformation(
                    "Check-in {PublicId} flagged for attention (tone {Tone}, boundary {Boundary}, follow-up {FollowUp}).",
                    checkIn.PublicId, checkIn.EmotionalTone, checkIn.BoundaryIssuesRaised, checkIn.FollowUpRequired);
            }

            var saved = await _checkIns.GetByPublicIdAsync(checkIn.PublicId) ?? checkIn;

            return new ApiResponse<CheckInDto>(
                ResponseType.Success, BuildMessage(saved, capacityNote), ToDto(saved));
        }

        /// <summary>
        /// Applies a band change if one was asked for. Returns a note for the caller's
        /// message, or null. A failure here is reported, never swallowed — a lead who
        /// thinks they have reduced somebody's load and has not is worse off than one
        /// who knows it did not take.
        /// </summary>
        private async Task<string?> ApplyCapacityChangeAsync(RecordCheckInRequest request, DateTime heldOn)
        {
            if (string.IsNullOrWhiteSpace(request.NewCapacityBandCode)) return null;

            var current = await _volunteers.GetAsync(request.VolunteerId);

            if (current.Data is null) return "The capacity band could not be changed.";

            if (string.Equals(current.Data.CapacityBandCode, request.NewCapacityBandCode, StringComparison.Ordinal))
                return null;   // already there; nothing to record

            var result = await _volunteers.ChangeCapacityAsync(request.VolunteerId, new ChangeCapacityRequest
            {
                CapacityBandCode = request.NewCapacityBandCode!,
                Reason = "CHECK_IN",
                Notes = $"Agreed at the check-in on {heldOn:yyyy-MM-dd}.",
                RowVersion = current.Data.RowVersion
            });

            return result.ResponseType == ResponseType.Success
                ? $"Capacity moved to {request.NewCapacityBandCode}."
                : $"The check-in was saved, but the capacity band was not changed: {result.Message}";
        }

        private static string BuildMessage(VolunteerCheckIn checkIn, string? capacityNote)
        {
            var message = checkIn.NeedsAttention
                ? "Check-in recorded, and flagged for follow-up."
                : "Check-in recorded.";

            return capacityNote is null ? message : message + " " + capacityNote;
        }

        // ==================================================================
        // Read
        // ==================================================================
        public async Task<ApiResponse<IReadOnlyList<CheckInDto>>> GetForVolunteerAsync(
            string volunteerPublicId, int limit)
        {
            var account = await ResolveAccountAsync();

            if (account is null) return Warn<IReadOnlyList<CheckInDto>>("You are not signed in.");

            var volunteer = await _checkIns.ResolveVolunteerAsync(volunteerPublicId);

            if (volunteer is null) return Warn<IReadOnlyList<CheckInDto>>("That volunteer was not found.");

            if (!await MayActOnAsync(account.Id, volunteer.Value.TeamId))
                return Warn<IReadOnlyList<CheckInDto>>("You can only see check-ins for your own team.");

            var rows = await _checkIns.GetForVolunteerAsync(volunteer.Value.VolunteerId, Math.Clamp(limit, 1, 100));

            return Ok<IReadOnlyList<CheckInDto>>(
                rows.Select(ToDto).ToList(), $"{rows.Count} check-in(s).");
        }

        public async Task<ApiResponse<IReadOnlyList<CheckInDueDto>>> GetDueAsync()
        {
            var account = await ResolveAccountAsync();

            if (account is null) return Warn<IReadOnlyList<CheckInDueDto>>("You are not signed in.");

            var teamIds = await _teams.GetLedTeamIdsAsync(account.Id);

            if (teamIds.Count == 0)
            {
                return Ok<IReadOnlyList<CheckInDueDto>>(
                    Array.Empty<CheckInDueDto>(), "You do not lead a team yet.");
            }

            var today = _clock.GetUtcNow().UtcDateTime.Date;
            var due = await _checkIns.FindDueAsync(teamIds, today, DefaultOverdueDays);

            var never = due.Count(d => d.LastCheckInOn is null);

            var message = due.Count == 0
                ? "Nobody is due a check-in."
                : never > 0
                    ? $"{due.Count} due, {never} of whom have never had one."
                    : $"{due.Count} due a check-in.";

            return Ok<IReadOnlyList<CheckInDueDto>>(due.Select(d => new CheckInDueDto
            {
                VolunteerId = d.VolunteerId,
                ReferenceCode = d.ReferenceCode,
                VolunteerName = d.VolunteerName,
                TeamName = d.TeamName,
                LastCheckInOn = d.LastCheckInOn,
                NextCheckInOn = d.NextCheckInOn,
                DaysSinceLastCheckIn = d.DaysSinceLastCheckIn,
                LastEmotionalTone = d.LastEmotionalTone,
                NeverCheckedIn = d.LastCheckInOn is null
            }).ToList(), message);
        }

        // ==================================================================
        // Helpers
        // ==================================================================

        /// <summary>
        /// Whether the caller may act on a volunteer with this team. Pastors and
        /// administrators are organisation-wide; a team lead is confined to the teams
        /// they actually lead, resolved from the database rather than from a claim.
        /// </summary>
        private async Task<bool> MayActOnAsync(long accountId, long? volunteerTeamId)
        {
            if (_current.IsAdmin || _current.IsInRole(RoleCodes.Pastor)) return true;

            if (volunteerTeamId is null) return false;

            var ledTeams = await _teams.GetLedTeamIdsAsync(accountId);

            return ledTeams.Contains(volunteerTeamId.Value);
        }

        private async Task<Identity.Domain.UserAccount?> ResolveAccountAsync()
        {
            var publicId = _current.AccountId;

            return string.IsNullOrWhiteSpace(publicId)
                ? null
                : await _accounts.GetByPublicIdAsync(publicId);
        }

        private static CheckInDto ToDto(VolunteerCheckIn c) => new()
        {
            Id = c.PublicId,
            VolunteerId = c.VolunteerPublicId ?? string.Empty,
            VolunteerName = c.VolunteerName ?? string.Empty,
            ConductedByName = c.ConductedByName,
            HeldOn = c.HeldOn,
            DurationMinutes = c.DurationMinutes,
            MeetingType = c.MeetingType,
            EmotionalTone = c.EmotionalTone,
            Concerns = c.Concerns,
            TrainingNeeds = c.TrainingNeeds,
            ActionItems = c.ActionItems,
            CapacityReviewed = c.CapacityReviewed,
            BoundaryIssuesRaised = c.BoundaryIssuesRaised,
            FollowUpRequired = c.FollowUpRequired,
            NextCheckInOn = c.NextCheckInOn,
            NeedsAttention = c.NeedsAttention
        };

        private static ApiResponse<T> Ok<T>(T data, string message) => new(ResponseType.Success, message, data);
        private static ApiResponse<T> Warn<T>(string message) => new(ResponseType.Warning, message, default!);
    }
}
