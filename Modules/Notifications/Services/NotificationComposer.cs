using Microsoft.Extensions.Options;
using RM_CMS.Modules.Care.Data;
using RM_CMS.Modules.Care.Domain;
using RM_CMS.Modules.Notifications.Domain;

namespace RM_CMS.Modules.Notifications.Services
{
    /// <summary>
    /// Builds the text of an alert at send time.
    ///
    /// This exists as a separate step because <c>notification_delivery</c> deliberately
    /// has no body column — alerts quote pastoral detail and names, and a queue table
    /// is the wrong place for that to accumulate. The consequence is that the body must
    /// be re-derived from the related entity when the message actually goes out, which
    /// is what this does.
    ///
    /// It also means a message reflects the entity as it stands NOW, not as it stood
    /// when the alert was queued. For a chase-up that is the behaviour you want: if the
    /// escalation was acknowledged in the meantime, <see cref="ComposeAsync"/> says so
    /// rather than nagging about something already handled.
    /// </summary>
    public interface INotificationComposer
    {
        /// <summary>
        /// The message to send, or a reason it cannot be built. A null message with a
        /// reason means the delivery should be closed rather than retried — the entity
        /// is gone, or the alert no longer applies.
        /// </summary>
        Task<(string? Message, string? SkipReason)> ComposeAsync(NotificationDelivery delivery);
    }

    public sealed class NotificationComposer : INotificationComposer
    {
        private readonly IEscalationRepository _escalations;
        private readonly TimeProvider _clock;
        private readonly string _baseUrl;

        public NotificationComposer(
            IEscalationRepository escalations,
            TimeProvider clock,
            IOptions<NotificationOptions> options)
        {
            _escalations = escalations;
            _clock = clock;
            _baseUrl = options.Value.PublicBaseUrl.TrimEnd('/');
        }

        public async Task<(string? Message, string? SkipReason)> ComposeAsync(NotificationDelivery delivery)
        {
            return delivery.NotificationType switch
            {
                NotificationType.EscalationUnacknowledged => await ComposeEscalationAsync(delivery, pastor: false),
                NotificationType.EscalationPastorAlert    => await ComposeEscalationAsync(delivery, pastor: true),
                NotificationType.EscalationRaised         => await ComposeEscalationAsync(delivery, pastor: false),
                NotificationType.HuddleReminder           => ComposeHuddleReminder(),

                // A type with no template is a coding gap, not a transient fault, so
                // it is closed rather than retried until attempts run out.
                _ => (null, $"No message template for '{delivery.NotificationType}'.")
            };
        }

        // ==================================================================
        // Huddle reminder
        // ==================================================================

        /// <summary>
        /// The weekly huddle reminder.
        ///
        /// Deliberately carries NO pastoral detail — it goes to a whole team, and the
        /// names and circumstances discussed at the huddle are exactly what should not
        /// be broadcast to a group chat. It says when and where to turn up; the agenda
        /// itself is behind a login.
        /// </summary>
        private (string?, string?) ComposeHuddleReminder()
        {
            var message =
$@"🤝 Team Huddle — ఈ రోజు

🙏 Praise the Lord,

ఈ రోజు మన <b>Team Huddle</b> ఉంది. ఈ వారం చేసిన follow-ups గురించి కలిసి మాట్లాడుకుందాం.

దయచేసి సమయానికి రండి. 🙏

👉 {_baseUrl}/pages/dashboard/team-lead.html";

            return (message, null);
        }

        // ==================================================================
        // Escalation chase-up
        // ==================================================================
        private async Task<(string?, string?)> ComposeEscalationAsync(NotificationDelivery delivery, bool pastor)
        {
            if (delivery.RelatedEntityId is null)
                return (null, "The alert names no escalation.");

            var escalation = await _escalations.GetByIdAsync(delivery.RelatedEntityId.Value);

            if (escalation is null)
                return (null, "That escalation no longer exists.");

            // Somebody picked it up between queueing and sending. Chasing them now
            // would train people to ignore these messages, which is the one thing an
            // escalation alert cannot afford.
            if (escalation.AcknowledgedAt is not null)
                return (null, "Acknowledged before the alert was sent.");

            if (EscalationStatus.IsTerminal(escalation.Status))
                return (null, $"Escalation already {escalation.Status}.");

            var now = _clock.GetUtcNow().UtcDateTime;
            var waited = FormatWaited(now - escalation.RaisedAt);

            var title = pastor
                ? "🔔 Pastor Alert — ఎవరూ స్పందించలేదు"
                : escalation.Tier == EscalationTier.Emergency
                    ? "🚨 EMERGENCY — వెంటనే స్పందించండి"
                    : "⚠️ Escalation Pending";

            var person = Escape(escalation.PersonName ?? "—");
            var reason = Escape(escalation.ReasonLabel ?? escalation.ReasonCode);
            var raisedBy = Escape(escalation.RaisedByName ?? "—");
            var reference = Escape(escalation.ReferenceCode ?? escalation.PublicId);

            var lead = pastor && !string.IsNullOrWhiteSpace(escalation.AssignedToName)
                ? $"\nTeam Lead: <b>{Escape(escalation.AssignedToName!)}</b> ఇంకా acknowledge చేయలేదు.\n"
                : string.Empty;

            var protocol = escalation.ReasonRequiresProtocol
                ? "\n📋 దీనికి safeguarding protocol పాటించాలి.\n"
                : string.Empty;

            var message =
$@"{title}

🙏 Praise the Lord,

<b>{person}</b> గారి కోసం ఒక escalation <b>{waited}</b> నుండి pending లో ఉంది.

Reason: <b>{reason}</b>
Tier: <b>{Escape(escalation.Tier)}</b>
Raised by: {raisedBy}
Ref: <code>{reference}</code>
{lead}{protocol}
దయచేసి వెంటనే చూసి acknowledge చేయండి.

👉 {_baseUrl}/pages/care/my-assignments.html";

            return (message, null);
        }

        // ==================================================================
        // Helpers
        // ==================================================================

        /// <summary>
        /// How long it has been waiting, in the coarsest unit that is still honest.
        /// "2 days" reads as urgent in a way "51 hours" does not.
        /// </summary>
        private static string FormatWaited(TimeSpan waited)
        {
            if (waited.TotalHours < 1) return $"{Math.Max(1, (int)waited.TotalMinutes)} నిమిషాల";
            if (waited.TotalHours < 24) return $"{(int)waited.TotalHours} గంటల";

            return $"{(int)waited.TotalDays} రోజుల";
        }

        /// <summary>
        /// Messages are sent with parse_mode=HTML, so a name carrying an angle bracket
        /// or an ampersand would break the whole message — Telegram rejects the send
        /// outright rather than rendering it plainly.
        /// </summary>
        private static string Escape(string value) =>
            value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
