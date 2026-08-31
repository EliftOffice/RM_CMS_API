using RM_CMS.Modules.Identity.Domain;

namespace RM_CMS.Modules.Identity.Services
{
    /// <summary>
    /// Read-only view of the caller, resolved from the validated access token.
    ///
    /// Inject this rather than reading <c>HttpContext.User</c> directly: it keeps
    /// ASP.NET types out of the service layer and makes ownership checks testable.
    ///
    /// Every identifier here is a PUBLIC id, because that is all a token carries.
    /// </summary>
    public interface ICurrentIdentity
    {
        bool IsAuthenticated { get; }

        /// <summary>Public id of the signed-in account.</summary>
        string? AccountId { get; }

        string? PersonId { get; }
        string? DisplayName { get; }
        string? VolunteerId { get; }
        string? TeamId { get; }

        /// <summary>The campus this account is scoped to. Null means organisation-wide.</summary>
        string? CampusId { get; }

        /// <summary>
        /// The campus new records default to when the caller does not name one.
        /// Falls back to the caller's own home campus, so an organisation-wide
        /// account still files somewhere sensible. Never use this for authorization —
        /// that is <see cref="CanAccessCampus"/>.
        /// </summary>
        string? DefaultCampusId { get; }

        IReadOnlyList<string> Roles { get; }
        bool IsInRole(string roleCode);
        bool IsAdmin { get; }

        /// <summary>
        /// True when the caller may act on behalf of <paramref name="volunteerId"/> —
        /// either it is their own volunteer record or they hold an elevated role.
        /// Use this to close object-level authorization holes on volunteer-scoped routes.
        /// </summary>
        bool CanActForVolunteer(string? volunteerId);

        /// <summary>
        /// True when the caller may read or write data belonging to
        /// <paramref name="campusId"/>. An account with no campus claim is
        /// organisation-wide and passes for every campus.
        /// </summary>
        bool CanAccessCampus(string? campusId);
    }

    public sealed class CurrentIdentity : ICurrentIdentity
    {
        private readonly IHttpContextAccessor _accessor;

        public CurrentIdentity(IHttpContextAccessor accessor) => _accessor = accessor;

        private System.Security.Claims.ClaimsPrincipal? Principal => _accessor.HttpContext?.User;

        public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

        public string? AccountId => Claim(ClaimNames.Subject);
        public string? PersonId => Claim(ClaimNames.PersonId);
        public string? DisplayName => Claim(ClaimNames.DisplayName);
        public string? VolunteerId => Claim(ClaimNames.VolunteerId);
        public string? TeamId => Claim(ClaimNames.TeamId);
        public string? CampusId => Claim(ClaimNames.CampusId);

        /// <summary>
        /// Scope first, home campus second. An organisation-wide caller has no scope
        /// claim, so without the fallback every record they create would land with no
        /// campus at all.
        /// </summary>
        public string? DefaultCampusId => CampusId ?? Claim(ClaimNames.HomeCampusId);

        public IReadOnlyList<string> Roles =>
            Principal?.FindAll(ClaimNames.Role).Select(c => c.Value).ToList() ?? new List<string>();

        public bool IsInRole(string roleCode) =>
            Principal?.HasClaim(ClaimNames.Role, roleCode) == true;

        public bool IsAdmin => IsInRole(RoleCodes.Admin);

        public bool CanActForVolunteer(string? volunteerId)
        {
            if (!IsAuthenticated) return false;

            // Admins, pastors and team leads legitimately act across volunteers.
            if (IsAdmin || IsInRole(RoleCodes.Pastor) || IsInRole(RoleCodes.TeamLead))
                return true;

            return !string.IsNullOrWhiteSpace(volunteerId) &&
                   string.Equals(VolunteerId, volunteerId, StringComparison.Ordinal);
        }

        public bool CanAccessCampus(string? campusId)
        {
            if (!IsAuthenticated) return false;

            // No campus claim == organisation-wide access.
            var mine = CampusId;
            if (string.IsNullOrWhiteSpace(mine)) return true;

            // A campus-scoped account may only reach its own campus. Unscoped data
            // (campusId null) is readable by anyone authenticated.
            return string.IsNullOrWhiteSpace(campusId) ||
                   string.Equals(mine, campusId, StringComparison.Ordinal);
        }

        private string? Claim(string type) => Principal?.FindFirst(type)?.Value;
    }
}
