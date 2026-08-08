using System.Security.Claims;

namespace RM_CMS.Security
{
    /// <summary>
    /// Read-only view of the caller, resolved from the validated access token.
    /// Inject this instead of reading <c>HttpContext.User</c> in BLL classes — it keeps
    /// the business layer free of ASP.NET types and makes ownership checks testable.
    /// </summary>
    public interface ICurrentUser
    {
        bool IsAuthenticated { get; }
        string? UserId { get; }
        string? DisplayName { get; }
        string? VolunteerId { get; }
        string? TeamLeadId { get; }
        IReadOnlyList<string> Roles { get; }

        bool IsInRole(string role);
        bool IsAdmin { get; }

        /// <summary>
        /// True when the caller may act on behalf of <paramref name="volunteerId"/> —
        /// either it is their own volunteer record, or they hold an elevated role.
        /// Use this to close IDOR holes on <c>/api/volunteers/{id}/...</c> style routes.
        /// </summary>
        bool CanActForVolunteer(string? volunteerId);

        /// <summary>As above, for team-lead-scoped routes.</summary>
        bool CanActForTeamLead(string? teamLeadId);
    }

    public sealed class CurrentUser : ICurrentUser
    {
        private readonly ClaimsPrincipal? _principal;

        public CurrentUser(IHttpContextAccessor accessor) => _principal = accessor.HttpContext?.User;

        public bool IsAuthenticated => _principal?.Identity?.IsAuthenticated == true;

        public string? UserId => _principal?.FindFirst(AppClaimTypes.UserId)?.Value;

        public string? DisplayName => _principal?.FindFirst(AppClaimTypes.DisplayName)?.Value;

        public string? VolunteerId => _principal?.FindFirst(AppClaimTypes.VolunteerId)?.Value;

        public string? TeamLeadId => _principal?.FindFirst(AppClaimTypes.TeamLeadId)?.Value;

        public IReadOnlyList<string> Roles =>
            _principal?.FindAll(AppClaimTypes.Role).Select(c => c.Value).ToList()
            ?? new List<string>();

        public bool IsInRole(string role) =>
            _principal?.HasClaim(AppClaimTypes.Role, role) == true;

        public bool IsAdmin => IsInRole(Security.Roles.Admin);

        public bool CanActForVolunteer(string? volunteerId)
        {
            if (!IsAuthenticated) return false;

            // Admins, pastors and team leads legitimately act across volunteers.
            if (IsAdmin || IsInRole(Security.Roles.Pastor) || IsInRole(Security.Roles.TeamLead))
                return true;

            return !string.IsNullOrWhiteSpace(volunteerId) &&
                   string.Equals(VolunteerId, volunteerId, StringComparison.OrdinalIgnoreCase);
        }

        public bool CanActForTeamLead(string? teamLeadId)
        {
            if (!IsAuthenticated) return false;

            if (IsAdmin || IsInRole(Security.Roles.Pastor))
                return true;

            return !string.IsNullOrWhiteSpace(teamLeadId) &&
                   string.Equals(TeamLeadId, teamLeadId, StringComparison.OrdinalIgnoreCase);
        }
    }
}
