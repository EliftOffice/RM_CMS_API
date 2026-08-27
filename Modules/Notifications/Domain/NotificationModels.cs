namespace RM_CMS.Modules.Notifications.Domain
{
    /// <summary>
    /// A queued alert. Mirrors <c>notification_delivery</c>.
    ///
    /// NOTE the deliberate absence of a message body. Alerts quote pastoral detail
    /// and names, so only the outcome is retained — see CONVENTIONS.md. The body is
    /// composed at send time from the related entity and never persisted.
    /// </summary>
    public sealed class NotificationDelivery
    {
        public long Id { get; set; }
        public string PublicId { get; set; } = string.Empty;

        public string Channel { get; set; } = NotificationChannel.Telegram;
        public long? RecipientPersonId { get; set; }
        public string? RecipientAddress { get; set; }

        public string NotificationType { get; set; } = string.Empty;
        public string? RelatedEntityType { get; set; }
        public long? RelatedEntityId { get; set; }

        public string Status { get; set; } = NotificationStatus.Pending;
        public int AttemptCount { get; set; }
        public string? FailureReason { get; set; }

        public DateTime QueuedAt { get; set; }
        public DateTime? SentAt { get; set; }
    }

    /// <summary>
    /// Someone an alert can be sent to, resolved from an account plus its person's
    /// contact rows.
    /// </summary>
    public sealed class NotificationRecipient
    {
        public long UserAccountId { get; set; }
        public long PersonId { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string? TelegramAddress { get; set; }
        public long? CampusId { get; set; }

        /// <summary>
        /// False when the person has no Telegram contact row. The delivery is still
        /// recorded — as SKIPPED — because "nobody could be reached" is exactly the
        /// fact an unacknowledged escalation needs to surface.
        /// </summary>
        public bool IsReachable => !string.IsNullOrWhiteSpace(TelegramAddress);
    }

    public static class NotificationChannel
    {
        public const string Telegram = "TELEGRAM";
        public const string Sms = "SMS";
        public const string Email = "EMAIL";
        public const string WhatsApp = "WHATSAPP";
        public const string Push = "PUSH";
    }

    public static class NotificationStatus
    {
        public const string Pending = "PENDING";
        public const string Sent = "SENT";
        public const string Failed = "FAILED";

        /// <summary>No usable address, so nothing was attempted.</summary>
        public const string Skipped = "SKIPPED";
    }

    /// <summary>
    /// Why an alert was raised. Kept short and stable — these values are queried
    /// when tracing back from a delivery failure.
    /// </summary>
    public static class NotificationType
    {
        public const string EscalationRaised = "ESCALATION_RAISED";

        /// <summary>Chase-up to the assigned team lead.</summary>
        public const string EscalationUnacknowledged = "ESCALATION_UNACKNOWLEDGED";

        /// <summary>The escalated chase-up, once the pastor threshold passes.</summary>
        public const string EscalationPastorAlert = "ESCALATION_PASTOR_ALERT";

        /// <summary>The weekly huddle is today — sent to the lead and their team.</summary>
        public const string HuddleReminder = "HUDDLE_REMINDER";

        public const string CaseAssigned = "CASE_ASSIGNED";
        public const string ContactOverdue = "CONTACT_OVERDUE";
        public const string NurtureStepDue = "NURTURE_STEP_DUE";
    }

    public static class RelatedEntityType
    {
        public const string Escalation = "ESCALATION";
        public const string CareCase = "CARE_CASE";
        public const string CareInteraction = "CARE_INTERACTION";

        /// <summary>A huddle reminder is about a team, not a case.</summary>
        public const string Team = "TEAM";
    }
}
