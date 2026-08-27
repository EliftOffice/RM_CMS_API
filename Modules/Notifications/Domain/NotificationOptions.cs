namespace RM_CMS.Modules.Notifications.Domain
{
    /// <summary>
    /// How the sender behaves. Bound from the <c>Notifications</c> configuration
    /// section.
    ///
    /// These are deployment facts rather than business rules, which is why they live
    /// in configuration and not in <c>app_setting</c>: the public URL differs per
    /// environment, and how many times to retry a failed HTTP call is not something a
    /// pastor should be tuning from the admin screen.
    /// </summary>
    public sealed class NotificationOptions
    {
        public const string SectionName = "Notifications";

        /// <summary>
        /// Where the links inside alerts point. Alerts are useless without a way to
        /// act on them, and a localhost link in a production message is worse than no
        /// link at all — so this is set per environment.
        /// </summary>
        public string PublicBaseUrl { get; set; } = "https://rmoffice.online";

        /// <summary>
        /// How many times one delivery may be attempted before it is closed as FAILED.
        /// Capped by <c>notification_delivery.attempt_count</c> being a TINYINT.
        /// </summary>
        public int MaxAttempts { get; set; } = 3;

        /// <summary>
        /// The most messages one sweep will send. Telegram rate-limits bots at roughly
        /// 30 messages a second, and a sweep that trips that limit gets the whole batch
        /// throttled rather than just its tail.
        /// </summary>
        public int BatchSize { get; set; } = 100;
    }
}
