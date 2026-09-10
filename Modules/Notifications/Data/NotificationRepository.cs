using Dapper;
using RM_CMS.Data;
using RM_CMS.Modules.Notifications.Domain;

namespace RM_CMS.Modules.Notifications.Data
{
    /// <summary>
    /// Writes <c>notification_delivery</c> and resolves who an alert should go to.
    ///
    /// This is the QUEUE only — it records that an alert is owed and to whom. Nothing
    /// here talks to Telegram. Sending is a separate concern so that a channel outage
    /// cannot roll back the domain transaction that raised the alert.
    /// </summary>
    public interface INotificationRepository
    {
        /// <summary>
        /// Queues one alert. Returns the new row id.
        ///
        /// An unreachable recipient is still recorded, as SKIPPED — "nobody could be
        /// reached" is the fact that matters most on an unacknowledged escalation, and
        /// silently dropping it would make the chase-up look like it worked.
        /// </summary>
        Task<long> QueueAsync(NotificationDelivery delivery);

        /// <summary>
        /// Active accounts holding <paramref name="roleCode"/> who can receive alerts
        /// for <paramref name="campusId"/>. An account whose role grant has no campus
        /// is organisation-wide and matches every campus.
        /// </summary>
        Task<IReadOnlyList<NotificationRecipient>> FindByRoleAsync(string roleCode, long? campusId);

        /// <summary>The single account an escalation is assigned to, if any.</summary>
        Task<NotificationRecipient?> FindByUserAccountIdAsync(long userAccountId);

        /// <summary>
        /// The same, keyed on the person — a volunteer row names a person, not an
        /// account. Null when they have no active sign-in.
        /// </summary>
        Task<NotificationRecipient?> FindByPersonIdAsync(long personId);

        /// <summary>
        /// True when an alert of this type for this entity has already been queued
        /// since <paramref name="since"/>. Guards against a job run that overlaps a
        /// previous one queueing the same reminder twice.
        /// </summary>
        Task<bool> WasQueuedSinceAsync(string notificationType, string relatedEntityType,
                                       long relatedEntityId, DateTime since);

        /// <summary>
        /// The next batch of alerts still owed, oldest first.
        ///
        /// Rows that have already used up <paramref name="maxAttempts"/> are left out.
        /// The sender closes such a row as FAILED on its last attempt, so nothing is
        /// left PENDING-but-never-picked — a queue holding rows no sweep will ever
        /// touch again looks like work in progress when it is really work abandoned.
        /// </summary>
        Task<IReadOnlyList<NotificationDelivery>> FindPendingAsync(int limit, int maxAttempts);

        /// <summary>
        /// Closes a delivery as SENT. Returns false when the row was no longer
        /// PENDING, which means another sender took it — the guard is what makes two
        /// overlapping sweeps safe.
        /// </summary>
        Task<bool> MarkSentAsync(long id, DateTime sentAtUtc);

        /// <summary>
        /// Records a failed attempt. <paramref name="giveUp"/> closes the row as
        /// FAILED; otherwise it stays PENDING for the next sweep with the reason
        /// attached, so a queue inspected mid-retry still says what went wrong.
        /// </summary>
        Task<bool> MarkAttemptFailedAsync(long id, string failureReason, bool giveUp);

        /// <summary>
        /// Closes a delivery as SKIPPED — nothing was attempted and nothing will be.
        /// Used when the row itself can never be delivered (no address, an address
        /// that is not a chat id, a related entity that no longer exists) as opposed
        /// to a send that failed and might work on the next sweep.
        /// </summary>
        Task<bool> MarkSkippedAsync(long id, string reason);
    }

    public sealed class NotificationRepository : INotificationRepository
    {
        private readonly IDbConnectionFactory _dbFactory;

        public NotificationRepository(IDbConnectionFactory dbFactory) => _dbFactory = dbFactory;

        /// <summary>
        /// The Telegram address is pulled with a correlated subquery rather than a
        /// join: contacts are rows, and a person may hold several, so joining would
        /// multiply the recipient list.
        ///
        /// It reads <c>normalized_value</c>, NOT <c>value</c>. On a TELEGRAM row the
        /// displayed value is the @username and the chat id lives in the normalized
        /// column — and the chat id is the only one of the two the Bot API can send
        /// to. An unverified or opted-out row is excluded, so somebody who
        /// disconnected reads as unreachable and is recorded SKIPPED rather than
        /// queued for a send that could never land.
        /// </summary>
        private const string SelectRecipient = @"
            SELECT
                ua.id        AS UserAccountId,
                p.id         AS PersonId,
                p.full_name  AS DisplayName,
                (SELECT pc.normalized_value FROM person_contact pc
                  WHERE pc.person_id = p.id
                    AND pc.contact_type = 'TELEGRAM'
                    AND pc.is_verified = 1
                    AND pc.opted_out_at IS NULL
                  ORDER BY pc.is_primary DESC, pc.id LIMIT 1) AS TelegramAddress";

        public async Task<long> QueueAsync(NotificationDelivery delivery)
        {
            const string sql = @"
                INSERT INTO notification_delivery
                    (public_id, channel, recipient_person_id, recipient_address,
                     notification_type, related_entity_type, related_entity_id,
                     status, attempt_count, failure_reason, queued_at)
                VALUES
                    (@PublicId, @Channel, @RecipientPersonId, @RecipientAddress,
                     @NotificationType, @RelatedEntityType, @RelatedEntityId,
                     @Status, @AttemptCount, @FailureReason, @QueuedAt);

                SELECT LAST_INSERT_ID();";

            using var connection = _dbFactory.GetConnection();

            return await connection.ExecuteScalarAsync<long>(sql, new
            {
                delivery.PublicId,
                delivery.Channel,
                delivery.RecipientPersonId,
                delivery.RecipientAddress,
                delivery.NotificationType,
                delivery.RelatedEntityType,
                delivery.RelatedEntityId,
                delivery.Status,
                delivery.AttemptCount,
                delivery.FailureReason,
                delivery.QueuedAt
            });
        }

        public async Task<IReadOnlyList<NotificationRecipient>> FindByRoleAsync(string roleCode, long? campusId)
        {
            var sql = SelectRecipient + @",
                ur.campus_id AS CampusId
            FROM user_account ua
            JOIN user_role ur ON ur.user_account_id = ua.id
            JOIN person    p  ON p.id = ua.person_id
            WHERE ua.is_active = 1
              AND ur.role_code = @RoleCode
              AND p.deleted_at IS NULL
              -- A grant with no campus is organisation-wide.
              AND (ur.campus_id IS NULL OR @CampusId IS NULL OR ur.campus_id = @CampusId)
            ORDER BY p.full_name;";

            using var connection = _dbFactory.GetConnection();

            return (await connection.QueryAsync<NotificationRecipient>(sql, new
            {
                RoleCode = roleCode,
                CampusId = campusId
            })).ToList();
        }

        public async Task<NotificationRecipient?> FindByUserAccountIdAsync(long userAccountId)
        {
            var sql = SelectRecipient + @",
                NULL AS CampusId
            FROM user_account ua
            JOIN person p ON p.id = ua.person_id
            WHERE ua.id = @UserAccountId AND ua.is_active = 1 AND p.deleted_at IS NULL
            LIMIT 1;";

            using var connection = _dbFactory.GetConnection();

            return await connection.QueryFirstOrDefaultAsync<NotificationRecipient>(
                sql, new { UserAccountId = userAccountId });
        }

        public async Task<NotificationRecipient?> FindByPersonIdAsync(long personId)
        {
            // Keyed on the PERSON, because a volunteer row names a person and not an
            // account. Still joined through user_account: somebody with no sign-in has
            // no way to act on an alert, so telling them about it only means an
            // unanswerable message on their phone.
            var sql = SelectRecipient + @",
                NULL AS CampusId
            FROM user_account ua
            JOIN person p ON p.id = ua.person_id
            WHERE p.id = @PersonId AND ua.is_active = 1 AND p.deleted_at IS NULL
            LIMIT 1;";

            using var connection = _dbFactory.GetConnection();

            return await connection.QueryFirstOrDefaultAsync<NotificationRecipient>(
                sql, new { PersonId = personId });
        }

        public async Task<bool> WasQueuedSinceAsync(
            string notificationType, string relatedEntityType, long relatedEntityId, DateTime since)
        {
            const string sql = @"
                SELECT COUNT(1)
                FROM notification_delivery
                WHERE notification_type   = @NotificationType
                  AND related_entity_type = @RelatedEntityType
                  AND related_entity_id   = @RelatedEntityId
                  AND queued_at >= @Since;";

            using var connection = _dbFactory.GetConnection();

            return await connection.ExecuteScalarAsync<int>(sql, new
            {
                NotificationType = notificationType,
                RelatedEntityType = relatedEntityType,
                RelatedEntityId = relatedEntityId,
                Since = since
            }) > 0;
        }

        // ==================================================================
        // Draining the queue
        // ==================================================================

        private const string SelectDelivery = @"
            SELECT
                id                  AS Id,
                public_id           AS PublicId,
                channel             AS Channel,
                recipient_person_id AS RecipientPersonId,
                recipient_address   AS RecipientAddress,
                notification_type   AS NotificationType,
                related_entity_type AS RelatedEntityType,
                related_entity_id   AS RelatedEntityId,
                status              AS Status,
                attempt_count       AS AttemptCount,
                failure_reason      AS FailureReason,
                queued_at           AS QueuedAt,
                sent_at             AS SentAt
            FROM notification_delivery";

        public async Task<IReadOnlyList<NotificationDelivery>> FindPendingAsync(int limit, int maxAttempts)
        {
            // Oldest first. An escalation chase-up that has been waiting an hour
            // matters more than one queued a second ago, and ix_notification_status
            // (status, queued_at) covers exactly this order.
            var sql = SelectDelivery + @"
            WHERE status = 'PENDING'
              AND attempt_count < @MaxAttempts
            ORDER BY queued_at, id
            LIMIT @Limit;";

            using var connection = _dbFactory.GetConnection();

            return (await connection.QueryAsync<NotificationDelivery>(sql, new
            {
                Limit = limit,
                MaxAttempts = maxAttempts
            })).ToList();
        }

        public async Task<bool> MarkSentAsync(long id, DateTime sentAtUtc)
        {
            // The status guard is the concurrency control: whichever sender updates
            // the row first wins, and the other counts nothing rather than reporting
            // the same message as delivered twice.
            const string sql = @"
                UPDATE notification_delivery
                SET status         = 'SENT',
                    sent_at        = @SentAt,
                    attempt_count  = attempt_count + 1,
                    failure_reason = NULL
                WHERE id = @Id AND status = 'PENDING';";

            using var connection = _dbFactory.GetConnection();

            return await connection.ExecuteAsync(sql, new { Id = id, SentAt = sentAtUtc }) > 0;
        }

        public async Task<bool> MarkAttemptFailedAsync(long id, string failureReason, bool giveUp)
        {
            const string sql = @"
                UPDATE notification_delivery
                SET attempt_count  = attempt_count + 1,
                    failure_reason = @FailureReason,
                    status         = CASE WHEN @GiveUp = 1 THEN 'FAILED' ELSE 'PENDING' END
                WHERE id = @Id AND status = 'PENDING';";

            using var connection = _dbFactory.GetConnection();

            return await connection.ExecuteAsync(sql, new
            {
                Id = id,
                FailureReason = Truncate(failureReason),
                GiveUp = giveUp ? 1 : 0
            }) > 0;
        }

        public async Task<bool> MarkSkippedAsync(long id, string reason)
        {
            const string sql = @"
                UPDATE notification_delivery
                SET status         = 'SKIPPED',
                    failure_reason = @Reason
                WHERE id = @Id AND status = 'PENDING';";

            using var connection = _dbFactory.GetConnection();

            return await connection.ExecuteAsync(sql, new { Id = id, Reason = Truncate(reason) }) > 0;
        }

        /// <summary>failure_reason is VARCHAR(255); a longer reason must not fail the update.</summary>
        private static string Truncate(string value) =>
            value.Length <= 255 ? value : value[..252] + "...";
    }
}
