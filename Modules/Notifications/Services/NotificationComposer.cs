using Microsoft.Extensions.Options;
using RM_CMS.Modules.Care.Data;
using RM_CMS.Modules.Care.Domain;
using RM_CMS.Modules.MessageTemplates.Domain;
using RM_CMS.Modules.MessageTemplates.Services;
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
    ///
    /// WHAT CHANGED WHEN TEMPLATES ARRIVED: the wording used to be string literals in
    /// this file, so changing a word meant a deployment. This class now decides only
    /// WHICH FACTS a message gets — reading the entity, working out whether the alert
    /// still applies, formatting a duration — and hands them to
    /// <see cref="ITemplateService"/>, which owns the words. The two jobs were tangled
    /// before, and the wording is the half that pastors need to change.
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
        private readonly ICareCaseRepository _cases;
        private readonly ITemplateService _templates;
        private readonly TimeProvider _clock;
        private readonly string _baseUrl;

        public NotificationComposer(
            IEscalationRepository escalations,
            ICareCaseRepository cases,
            ITemplateService templates,
            TimeProvider clock,
            IOptions<NotificationOptions> options)
        {
            _escalations = escalations;
            _cases = cases;
            _templates = templates;
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
                NotificationType.HuddleReminder           => await ComposeHuddleReminderAsync(delivery),
                NotificationType.CaseAssigned             => await ComposeCaseAssignedAsync(delivery),

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
        ///
        /// The template placeholders offered for it reflect that: a team name and a
        /// link, and nothing about any person.
        /// </summary>
        private async Task<(string?, string?)> ComposeHuddleReminderAsync(NotificationDelivery delivery)
        {
            var values = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["TeamName"] = null,
                ["TeamLeadLink"] = $"{_baseUrl}/pages/dashboard/team-lead.html"
            };

            var message = await _templates.RenderAsync(
                TelegramTemplates.HuddleReminder, delivery.RecipientPersonId, values);

            return message is null
                ? (null, "The huddle reminder has no message template.")
                : (message, null);
        }

        // ==================================================================
        // Case assigned
        // ==================================================================

        /// <summary>
        /// A follow-up has been placed with this volunteer.
        ///
        /// Re-read at send time like everything else here, so a case reassigned between
        /// queueing and sending does not tell the wrong volunteer it is theirs.
        /// </summary>
        private async Task<(string?, string?)> ComposeCaseAssignedAsync(NotificationDelivery delivery)
        {
            if (delivery.RelatedEntityId is null)
                return (null, "The alert names no case.");

            var careCase = await _cases.GetByIdAsync(delivery.RelatedEntityId.Value);

            if (careCase is null)
                return (null, "That case no longer exists.");

            // Closed between queueing and sending. Telling a volunteer to follow up on
            // something already finished is how these messages start being ignored.
            if (string.Equals(careCase.Status, CaseStatus.Closed, StringComparison.Ordinal))
                return (null, "The case was closed before the alert was sent.");

            var values = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["PersonName"] = careCase.PersonName,
                ["Reference"] = careCase.ReferenceCode ?? careCase.PublicId,
                ["PersonPhone"] = careCase.PersonPhone,
                ["CampusName"] = careCase.CampusName,
                ["TeamName"] = careCase.TeamName,
                ["MyAssignmentsLink"] = $"{_baseUrl}/pages/care/my-assignments.html"
            };

            var message = await _templates.RenderAsync(
                TelegramTemplates.CaseAssigned, delivery.RecipientPersonId, values);

            return message is null
                ? (null, "The assignment alert has no message template.")
                : (message, null);
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

            var title = pastor
                ? "🔔 Pastor Alert — ఎవరూ స్పందించలేదు"
                : escalation.Tier == EscalationTier.Emergency
                    ? "🚨 EMERGENCY — వెంటనే స్పందించండి"
                    : "⚠️ Escalation Pending";

            var values = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["PersonName"] = escalation.PersonName ?? "—",
                ["Reason"] = escalation.ReasonLabel ?? escalation.ReasonCode,
                ["Tier"] = escalation.Tier,
                ["RaisedByName"] = escalation.RaisedByName ?? "—",
                ["Reference"] = escalation.ReferenceCode ?? escalation.PublicId,
                ["Waited"] = FormatWaited(now - escalation.RaisedAt),
                ["MyAssignmentsLink"] = $"{_baseUrl}/pages/care/my-assignments.html"
            };

            // These three are markup the server composes, not values somebody typed, so
            // they go through the raw channel. The names inside them are escaped here,
            // where they are put in — passing an already-built sentence through the
            // escaper would turn its own tags into visible text.
            var rawValues = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Title"] = title,

                ["TeamLeadNote"] = pastor && !string.IsNullOrWhiteSpace(escalation.AssignedToName)
                    ? $"\nTeam Lead: <b>{TemplateRenderer.Escape(escalation.AssignedToName!)}</b> ఇంకా acknowledge చేయలేదు.\n"
                    : string.Empty,

                ["ProtocolNote"] = escalation.ReasonRequiresProtocol
                    ? "\n📋 దీనికి safeguarding protocol పాటించాలి.\n"
                    : string.Empty
            };

            var code = pastor
                ? TelegramTemplates.EscalationPastorAlert
                : delivery.NotificationType == NotificationType.EscalationRaised
                    ? TelegramTemplates.EscalationRaised
                    : TelegramTemplates.EscalationUnacknowledged;

            var message = await _templates.RenderAsync(
                code, delivery.RecipientPersonId, values, rawValues);

            return message is null
                ? (null, $"No message template for '{code}'.")
                : (message, null);
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
    }
}
