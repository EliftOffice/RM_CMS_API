namespace RM_CMS.Utilities
{
    /// <summary>
    /// Where a capacity week begins.
    ///
    /// This exists because a volunteer's weekly allowance is only as meaningful as
    /// the boundary it resets on, and that boundary has to be the SAME one
    /// everywhere. The assignment engine deciding it is Monday while a team lead's
    /// dashboard believes it is Sunday does not read as a bug — it reads as the
    /// numbers being wrong, with nothing on screen to explain why.
    ///
    /// The day is configurable (<c>assignment.week_starts_on</c>) rather than fixed.
    /// A church whose week runs Sunday to Saturday would otherwise have every
    /// allowance reset in the middle of the Sunday service — exactly when the
    /// visitors being assigned have walked in.
    /// </summary>
    public static class CapacityWeek
    {
        /// <summary>The application's day numbering: 1 = Monday ... 7 = Sunday.</summary>
        /// <remarks>
        /// Matches <c>huddle.day_of_week</c> and what the church says aloud, NOT
        /// <see cref="DayOfWeek"/>, which starts at Sunday = 0. The two have been
        /// confused before; converting in one place is the point of this file.
        /// </remarks>
        public const int Monday = 1;

        public const string WeekStartsOnKey = "assignment.week_starts_on";

        /// <summary>
        /// Midnight on the most recent <paramref name="weekStartsOn"/> at or before
        /// <paramref name="instant"/>. On the start day itself that is today, so a
        /// volunteer's allowance is fresh from midnight rather than a day later.
        /// </summary>
        /// <param name="weekStartsOn">1 = Monday ... 7 = Sunday. Out-of-range values fall back to Monday.</param>
        public static DateTime StartOfWeek(DateTime instant, int weekStartsOn = Monday)
        {
            if (weekStartsOn is < 1 or > 7) weekStartsOn = Monday;

            // .NET counts Sunday as 0; this app counts Monday as 1. Shift into the
            // app's numbering first, then measure the gap, so the modulo arithmetic
            // below is reading one scale rather than straddling two.
            var today = ((int)instant.DayOfWeek + 6) % 7 + 1;   // Mon=1 ... Sun=7

            var daysSinceStart = (today - weekStartsOn + 7) % 7;

            return instant.Date.AddDays(-daysSinceStart);
        }
    }
}
