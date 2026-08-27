using Microsoft.Extensions.Options;
using RM_CMS.Modules.Care.Data;
using RM_CMS.Modules.Notifications.Data;
using RM_CMS.Modules.Notifications.Domain;
using RM_CMS.Modules.Telegram.Services;

namespace RM_CMS.Modules.Notifications.Services
{
    /// <summary>
    /// Drains <c>notification_delivery</c> — the half of the alert chain that actually
    /// tells somebody.
    ///
    /// Everything upstream of this already worked: the chase-up found the unacknowledged
    /// escalation, resolved who owns it, and recorded that an alert was owed. Nothing
    /// read those rows, so an escalation could be recorded as chased while the team lead
    /// heard nothing. This is what closes that gap.
    ///
    /// Three outcomes per row, and the distinction matters when reading the queue back:
    /// <list type="bullet">
    ///   <item><b>SENT</b> — Telegram accepted it.</item>
    ///   <item><b>SKIPPED</b> — undeliverable by nature (no chat id, entity gone, alert
    ///   no longer applies). Never retried, because retrying cannot change it.</item>
    ///   <item><b>FAILED</b> — attempted and refused, out of attempts. Retried until
    ///   then, because Telegram being unreachable for a minute is not permanent.</item>
    /// </list>
    /// </summary>
    public interface INotificationSender
    {
        /// <summary>
        /// Sends the pending queue. Returns what happened, for the job report.
        /// </summary>
        Task<NotificationSendResult> DrainAsync(int limit, CancellationToken cancellationToken = default);
    }

    /// <summary>The tally of one drain. Mirrors the three terminal states.</summary>
    public sealed class NotificationSendResult
    {
        public int Sent { get; set; }
        public int Skipped { get; set; }
        public int Failed { get; set; }

        /// <summary>Set when the whole drain could not run — an unconfigured bot, say.</summary>
        public string? Halted { get; set; }

        public List<string> Notes { get; } = new();

        public void Note(string note)
        {
            if (Notes.Count < 10) Notes.Add(note);
        }

        public int Total => Sent + Skipped + Failed;
    }

    public sealed class NotificationSender : INotificationSender
    {
        private readonly INotificationRepository _deliveries;
        private readonly INotificationComposer _composer;
        private readonly ITelegramClient _telegram;
        private readonly IEscalationRepository _escalations;
        private readonly NotificationOptions _options;
        private readonly TimeProvider _clock;
        private readonly ILogger<NotificationSender> _logger;

        public NotificationSender(
            INotificationRepository deliveries,
            INotificationComposer composer,
            ITelegramClient telegram,
            IEscalationRepository escalations,
            IOptions<NotificationOptions> options,
            TimeProvider clock,
            ILogger<NotificationSender> logger)
        {
            _deliveries = deliveries;
            _composer = composer;
            _telegram = telegram;
            _escalations = escalations;
            _options = options.Value;
            _clock = clock;
            _logger = logger;
        }

        public async Task<NotificationSendResult> DrainAsync(
            int limit, CancellationToken cancellationToken = default)
        {
            var result = new NotificationSendResult();

            // Without a bot the queue is left exactly as it is. Marking everything
            // FAILED here would burn the attempt budget on a misconfiguration, so that
            // once the token was finally supplied the backlog would be dead rather
            // than deliverable.
            if (!_telegram.IsConfigured)
            {
                result.Halted = "The Telegram bot is not configured; the queue was left untouched.";
                _logger.LogWarning("Notification drain skipped: the Telegram bot is not configured.");

                return result;
            }

            var maxAttempts = Math.Clamp(_options.MaxAttempts, 1, 250);
            var batchSize = limit > 0 ? limit : _options.BatchSize;

            var pending = await _deliveries.FindPendingAsync(Math.Clamp(batchSize, 1, 1000), maxAttempts);

            if (pending.Count == 0)
            {
                result.Note("Nothing waiting to be sent.");
                return result;
            }

            foreach (var delivery in pending)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    result.Note("Cancelled part-way; the remainder stays queued.");
                    break;
                }

                try
                {
                    await SendOneAsync(delivery, maxAttempts, result, cancellationToken);
                }
                catch (Exception ex)
                {
                    // One malformed row must not abandon the rest of the queue.
                    _logger.LogError(ex, "Delivery {DeliveryId} threw while sending", delivery.Id);

                    await FailAsync(delivery, maxAttempts, ex.Message, result);
                }
            }

            _logger.LogInformation(
                "Notification drain: {Sent} sent, {Skipped} skipped, {Failed} failed.",
                result.Sent, result.Skipped, result.Failed);

            return result;
        }

        private async Task SendOneAsync(
            NotificationDelivery delivery, int maxAttempts,
            NotificationSendResult result, CancellationToken cancellationToken)
        {
            // Only Telegram exists today. Another channel's rows are left PENDING
            // rather than failed, so adding a sender for one later finds its backlog
            // intact instead of a table of rows it must resurrect.
            if (!string.Equals(delivery.Channel, NotificationChannel.Telegram, StringComparison.Ordinal))
            {
                result.Skipped++;
                result.Note($"{delivery.Channel} has no sender yet.");
                return;
            }

            // The address is written by the queue from person_contact.normalized_value,
            // which for a TELEGRAM row is the numeric chat id. Anything that will not
            // parse is a bad row — historically an '@username' written into the wrong
            // column — and no number of retries will fix it, so it is closed.
            if (string.IsNullOrWhiteSpace(delivery.RecipientAddress) ||
                !long.TryParse(delivery.RecipientAddress, out var chatId))
            {
                await SkipAsync(delivery, "Recipient address is not a Telegram chat id.", result);
                return;
            }

            var (message, skipReason) = await _composer.ComposeAsync(delivery);

            if (message is null)
            {
                await SkipAsync(delivery, skipReason ?? "The message could not be composed.", result);
                return;
            }

            var ok = await _telegram.SendMessageAsync(chatId, message, cancellationToken);

            if (!ok)
            {
                await FailAsync(delivery, maxAttempts, "Telegram refused the message.", result);
                return;
            }

            var now = _clock.GetUtcNow().UtcDateTime;

            if (await _deliveries.MarkSentAsync(delivery.Id, now))
            {
                result.Sent++;
                await StampEntityNotifiedAsync(delivery, now);
            }
            else
            {
                // Another sender closed the row between the read and the update. The
                // message did go out, so this is not a failure — it is simply not
                // this sweep's to count.
                _logger.LogWarning("Delivery {DeliveryId} was already closed by another sweep.", delivery.Id);
            }
        }

        /// <summary>
        /// Stamps <c>escalation.notified_at</c> the first time an alert about it lands.
        /// The column exists to answer "when was anybody actually told?", which until
        /// there was a sender nothing could answer.
        /// </summary>
        private async Task StampEntityNotifiedAsync(NotificationDelivery delivery, DateTime now)
        {
            if (delivery.RelatedEntityId is null) return;

            if (!string.Equals(delivery.RelatedEntityType, RelatedEntityType.Escalation, StringComparison.Ordinal))
                return;

            await _escalations.MarkNotifiedAsync(delivery.RelatedEntityId.Value, now);
        }

        private async Task SkipAsync(
            NotificationDelivery delivery, string reason, NotificationSendResult result)
        {
            await _deliveries.MarkSkippedAsync(delivery.Id, reason);

            result.Skipped++;
            result.Note($"{delivery.PublicId}: {reason}");
        }

        private async Task FailAsync(
            NotificationDelivery delivery, int maxAttempts, string reason, NotificationSendResult result)
        {
            var giveUp = delivery.AttemptCount + 1 >= maxAttempts;

            await _deliveries.MarkAttemptFailedAsync(delivery.Id, reason, giveUp);

            result.Failed++;
            result.Note(giveUp
                ? $"{delivery.PublicId}: {reason} Gave up after {maxAttempts} attempts."
                : $"{delivery.PublicId}: {reason} Will retry.");
        }
    }
}
