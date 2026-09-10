namespace RM_CMS.Modules.Identity.Domain
{
    /// <summary>
    /// A sign-in that has passed its first factor and is waiting for the person to
    /// confirm it on Telegram.
    /// </summary>
    /// <remarks>
    /// The row exists only for the few minutes between "the credential checked out"
    /// and "the session was issued". It is not an audit record — what happened is
    /// written to <c>security_event</c> like every other authentication event.
    ///
    /// Two secrets, doing different jobs. <see cref="PublicId"/> is what the waiting
    /// BROWSER holds; it identifies the pending sign-in and is quoted back when
    /// polling. The token behind <see cref="TokenHash"/> is what the TELEGRAM button
    /// carries, and it is what proves the tap came from the phone that was asked.
    /// Neither one is enough on its own, which is the point: the browser can wait but
    /// cannot approve, and the phone can approve but never sees the session.
    /// </remarks>
    public sealed class LoginChallenge
    {
        public long Id { get; set; }
        public string PublicId { get; set; } = string.Empty;

        public long UserAccountId { get; set; }

        /// <summary>SHA-256 of the token in the Telegram button. Never the token.</summary>
        public string TokenHash { get; set; } = string.Empty;

        /// <summary>The chat the prompt was sent to, as it stood when it was sent.</summary>
        public string ChatId { get; set; } = string.Empty;

        public string Status { get; set; } = LoginChallengeStatus.Pending;

        public int SendCount { get; set; }

        public string? RequestIp { get; set; }
        public string? UserAgent { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public DateTime? ApprovedAt { get; set; }
        public DateTime? ConsumedAt { get; set; }

        public bool HasExpired(DateTime utcNow) => ExpiresAt <= utcNow;
    }

    public static class LoginChallengeStatus
    {
        /// <summary>Sent, or about to be, and waiting for a tap.</summary>
        public const string Pending = "PENDING";

        /// <summary>They tapped Confirm. The browser may now collect its session.</summary>
        public const string Approved = "APPROVED";

        /// <summary>The session has been issued. A challenge is good for exactly one.</summary>
        public const string Consumed = "CONSUMED";

        /// <summary>
        /// They tapped "This was not me". Kept distinct from expiry because it means
        /// somebody had that account's credential and it was not them.
        /// </summary>
        public const string Declined = "DECLINED";

        public const string Expired = "EXPIRED";

        public static readonly string[] All = { Pending, Approved, Consumed, Declined, Expired };
    }

    /// <summary>
    /// What the sign-in screen is told while it waits. Deliberately not the raw
    /// status: the browser has no business knowing the difference between a
    /// challenge that expired and one somebody actively declined.
    /// </summary>
    public static class LoginChallengeOutcome
    {
        public const string Waiting = "WAITING";
        public const string Approved = "APPROVED";

        /// <summary>Expired, declined, already used, or never existed.</summary>
        public const string Failed = "FAILED";
    }
}
