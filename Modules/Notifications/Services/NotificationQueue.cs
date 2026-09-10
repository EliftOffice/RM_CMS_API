using RM_CMS.Modules.Identity.Domain;
using RM_CMS.Modules.Notifications.Data;
using RM_CMS.Modules.Notifications.Domain;

namespace RM_CMS.Modules.Notifications.Services
{
    /// <summary>
    /// Queues alerts for delivery.
    ///
    /// Deliberately the only thing callers need: they say who and why, not how. When
    /// a real sender is added it drains this queue, and nothing that raises an alert
    /// has to change.
    /// </summary>
    public interface INotificationQueue
    {
        /// <summary>
        /// Queues one alert per recipient. Returns how many were queued as reachable
        /// — a caller that gets 0 back has told nobody, which is worth logging.
        /// </summary>
        Task<int> QueueAsync(IEnumerable<NotificationRecipient> recipients, string notificationType,
                             string relatedEntityType, long relatedEntityId);

        Task<IReadOnlyList<NotificationRecipient>> FindByRoleAsync(string roleCode, long? campusId);

        Task<NotificationRecipient?> FindAssigneeAsync(long? userAccountId);

        /// <summary>
        /// The recipient behind a volunteer, who is identified by their person id.
        /// </summary>
        Task<NotificationRecipient?> FindByPersonAsync(long? personId);

        Task<bool> AlreadyQueuedSinceAsync(string notificationType, string relatedEntityType,
                                           long relatedEntityId, DateTime since);
    }

    public sealed class NotificationQueue : INotificationQueue
    {
        private readonly INotificationRepository _repository;
        private readonly TimeProvider _clock;
        private readonly ILogger<NotificationQueue> _logger;

        public NotificationQueue(
            INotificationRepository repository,
            TimeProvider clock,
            ILogger<NotificationQueue> logger)
        {
            _repository = repository;
            _clock = clock;
            _logger = logger;
        }

        public async Task<int> QueueAsync(
            IEnumerable<NotificationRecipient> recipients, string notificationType,
            string relatedEntityType, long relatedEntityId)
        {
            var now = _clock.GetUtcNow().UtcDateTime;
            var reachable = 0;

            foreach (var recipient in recipients)
            {
                // An unreachable recipient is recorded rather than skipped silently.
                // On an unacknowledged escalation, "the team lead has no Telegram
                // contact" is the actual reason nobody responded, and it needs to be
                // visible somewhere other than a log line.
                await _repository.QueueAsync(new NotificationDelivery
                {
                    PublicId = Ulid.NewUlid(),
                    Channel = NotificationChannel.Telegram,
                    RecipientPersonId = recipient.PersonId,
                    RecipientAddress = recipient.TelegramAddress,
                    NotificationType = notificationType,
                    RelatedEntityType = relatedEntityType,
                    RelatedEntityId = relatedEntityId,
                    Status = recipient.IsReachable
                        ? NotificationStatus.Pending
                        : NotificationStatus.Skipped,
                    FailureReason = recipient.IsReachable
                        ? null
                        : "No Telegram contact on record for this person.",
                    QueuedAt = now
                });

                if (recipient.IsReachable) reachable++;
            }

            return reachable;
        }

        public Task<IReadOnlyList<NotificationRecipient>> FindByRoleAsync(string roleCode, long? campusId) =>
            _repository.FindByRoleAsync(roleCode, campusId);

        public async Task<NotificationRecipient?> FindAssigneeAsync(long? userAccountId) =>
            userAccountId is null ? null : await _repository.FindByUserAccountIdAsync(userAccountId.Value);

        public async Task<NotificationRecipient?> FindByPersonAsync(long? personId) =>
            personId is null ? null : await _repository.FindByPersonIdAsync(personId.Value);

        public Task<bool> AlreadyQueuedSinceAsync(
            string notificationType, string relatedEntityType, long relatedEntityId, DateTime since) =>
            _repository.WasQueuedSinceAsync(notificationType, relatedEntityType, relatedEntityId, since);
    }
}
