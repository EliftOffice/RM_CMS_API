using RM_CMS.Modules.Notifications.Domain;

namespace RM_CMS.Modules.Notifications.Services
{
    /// <summary>
    /// Tells a volunteer that a follow-up is now theirs.
    ///
    /// This exists as its own service because a case can be placed with somebody by
    /// four different routes, in two different modules:
    ///
    ///   • a team lead assigns it by hand
    ///   • the intake desk assigns it the moment a visitor is recorded
    ///   • the assign-unassigned job places whatever is queued
    ///   • the advance-nurture job moves a case to a volunteer with capacity
    ///
    /// Only the first of those ever told the volunteer. The other three moved the
    /// case, bumped the load counters and created the work item — and the volunteer
    /// found out by happening to open the site. This makes the alert part of being
    /// assigned rather than part of one particular screen's flow.
    ///
    /// The message itself is composed at SEND time by
    /// <see cref="INotificationComposer"/> from the CASE_ASSIGNED template, which is
    /// what carries the visitor's details and the link they follow to log the
    /// attempt. Nothing about the wording lives here.
    /// </summary>
    public interface IAssignmentNotifier
    {
        /// <summary>
        /// Queues the alert. Never throws: the case IS assigned by the time this
        /// runs, and a failure to tell somebody must not undo the placement or fail
        /// the job run that made it.
        /// </summary>
        /// <param name="careCaseId">The case, as the alert's related entity.</param>
        /// <param name="volunteerPersonId">
        /// The volunteer's PERSON id — a volunteer is reachable through their person
        /// record, not their volunteer row.
        /// </param>
        /// <returns>True when an alert was queued for a reachable recipient.</returns>
        Task<bool> NotifyCaseAssignedAsync(long careCaseId, long volunteerPersonId);
    }

    public sealed class AssignmentNotifier : IAssignmentNotifier
    {
        private readonly INotificationQueue _notifications;
        private readonly ILogger<AssignmentNotifier> _logger;

        public AssignmentNotifier(INotificationQueue notifications, ILogger<AssignmentNotifier> logger)
        {
            _notifications = notifications;
            _logger = logger;
        }

        public async Task<bool> NotifyCaseAssignedAsync(long careCaseId, long volunteerPersonId)
        {
            try
            {
                var recipient = await _notifications.FindByPersonAsync(volunteerPersonId);

                if (recipient is null)
                {
                    // No sign-in, so nothing they could act on. Worth a line: a team
                    // full of volunteers without accounts is a setup problem, and this
                    // is where it first shows.
                    _logger.LogInformation(
                        "Case {CaseId} assigned to a person with no active account; no alert queued.",
                        careCaseId);

                    return false;
                }

                var reachable = await _notifications.QueueAsync(
                    new[] { recipient }, NotificationType.CaseAssigned,
                    RelatedEntityType.CareCase, careCaseId);

                return reachable > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Could not queue the assignment alert for case {CaseId}. The assignment itself stands.",
                    careCaseId);

                return false;
            }
        }
    }
}
