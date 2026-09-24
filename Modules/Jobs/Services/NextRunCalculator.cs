using RM_CMS.Modules.Jobs.Domain;

namespace RM_CMS.Modules.Jobs.Services
{
    /// <summary>
    /// Turns "Monday at six" into an instant in UTC.
    /// </summary>
    /// <remarks>
    /// The one place that conversion happens. It is deliberately NOT done in SQL:
    /// the timezone database lives in the runtime, and a second implementation in a
    /// MySQL expression would drift from this one the first time a zone changed.
    ///
    /// Everything stored is UTC; everything an administrator types is local. This
    /// class is the boundary, and it is pure — no clock of its own, no database —
    /// so the awkward cases can be reasoned about by reading it.
    /// </remarks>
    public static class NextRunCalculator
    {
        /// <summary>
        /// The next occurrence of <paramref name="schedule"/> strictly after
        /// <paramref name="afterUtc"/>, or null when the schedule cannot produce one.
        /// </summary>
        /// <remarks>
        /// STRICTLY after, which matters more than it looks. The scheduler calls this
        /// with "now" at the moment it claims a run; if the comparison were
        /// inclusive, a job whose time had just arrived would be handed back the same
        /// instant it just ran and would loop until the minute passed.
        ///
        /// EVERY counts from the moment it is asked rather than from a fixed grid, so
        /// a five-minute drain that takes twenty seconds runs at 00:00, 00:05:20,
        /// 00:10:40 and so on. The drift is deliberate: pinning to a grid would make
        /// a run that overran collide with the next one, and this scheduler would
        /// rather be a few seconds late than overlap itself.
        /// </remarks>
        public static DateTime? NextOccurrence(JobSchedule schedule, DateTime afterUtc)
        {
            if (!schedule.IsEnabled) return null;

            // EVERY is answered before the timezone is even resolved. An interval is
            // the same length of time in every zone, so converting to local and back
            // would be work that cannot change the answer — and would drag a DST gap
            // into a rule that has no wall-clock time in it at all.
            if (string.Equals(schedule.Cadence, JobCadence.Every, StringComparison.Ordinal))
            {
                var minutes = schedule.IntervalMinutes ?? 0;

                if (minutes < JobCadence.MinIntervalMinutes ||
                    minutes > JobCadence.MaxIntervalMinutes) return null;

                return afterUtc.AddMinutes(minutes);
            }

            if (!TryResolveZone(schedule.Timezone, out var zone)) return null;

            // Out of range means the row is unschedulable rather than due now. Saying
            // null here is what keeps a malformed rule quiet instead of running every
            // single tick.
            if (schedule.TimeOfDay < TimeSpan.Zero || schedule.TimeOfDay >= TimeSpan.FromDays(1))
                return null;

            var localAfter = TimeZoneInfo.ConvertTimeFromUtc(afterUtc, zone);

            DateTime localNext;

            if (string.Equals(schedule.Cadence, JobCadence.Daily, StringComparison.Ordinal))
            {
                localNext = localAfter.Date + schedule.TimeOfDay;

                if (localNext <= localAfter) localNext = localNext.AddDays(1);
            }
            else if (string.Equals(schedule.Cadence, JobCadence.Weekly, StringComparison.Ordinal))
            {
                if (schedule.DayOfWeek is not (>= 1 and <= 7)) return null;

                localNext = localAfter.Date + schedule.TimeOfDay;

                // At most seven steps: six to reach the right weekday, and one more for
                // the case where today IS the right weekday but the time has passed.
                var guard = 0;

                while ((IsoDay(localNext.DayOfWeek) != schedule.DayOfWeek || localNext <= localAfter) &&
                       guard++ < 8)
                {
                    localNext = localNext.AddDays(1);
                }

                if (guard >= 8) return null;
            }
            else
            {
                return null;
            }

            return ToUtc(localNext, zone);
        }

        /// <summary>
        /// ISO numbering, 1 = Monday ... 7 = Sunday.
        /// </summary>
        /// <remarks>
        /// .NET counts from Sunday = 0, which is the opposite end of the week from the
        /// numbering <c>assignment.week_starts_on</c> and <c>job_schedule</c> already
        /// use. Converting in one named method rather than inline is what stops a
        /// stray off-by-one scheduling Sunday's sweep on Saturday.
        /// </remarks>
        public static int IsoDay(DayOfWeek day) => day == System.DayOfWeek.Sunday ? 7 : (int)day;

        public static bool TryResolveZone(string? id, out TimeZoneInfo zone)
        {
            zone = TimeZoneInfo.Utc;

            if (string.IsNullOrWhiteSpace(id)) return false;

            // Matches how CampusService validates a timezone. Accepts IANA ids on
            // every platform the application runs on.
            return TimeZoneInfo.TryFindSystemTimeZoneById(id, out zone!);
        }

        /// <summary>
        /// Converts a local wall-clock time to UTC, surviving the two hours a year
        /// that do not behave.
        /// </summary>
        /// <remarks>
        /// Asia/Kolkata has no daylight saving, so neither branch fires today. They
        /// are here because the zone is a per-row setting: the moment somebody picks
        /// a zone that does observe it, ConvertTimeToUtc THROWS on the hour that does
        /// not exist in spring, and an unhandled throw in the scheduler's tick would
        /// stop every job rather than one.
        ///
        ///   Invalid (the clock jumped forward over it): walk forward a minute at a
        ///   time until the wall clock exists again, which lands on the first real
        ///   instant after the gap.
        ///
        ///   Ambiguous (the clock fell back and the hour ran twice): take the first
        ///   pass. Running early is recoverable; running an hour late is the one
        ///   thing a morning sweep must not do.
        /// </remarks>
        private static DateTime ToUtc(DateTime local, TimeZoneInfo zone)
        {
            var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);

            var guard = 0;

            while (zone.IsInvalidTime(unspecified) && guard++ < 24 * 60)
                unspecified = unspecified.AddMinutes(1);

            if (zone.IsAmbiguousTime(unspecified))
            {
                var offsets = zone.GetAmbiguousTimeOffsets(unspecified);
                var earliest = offsets.Max();   // the largest offset is the earliest instant

                return DateTime.SpecifyKind(unspecified - earliest, DateTimeKind.Utc);
            }

            return TimeZoneInfo.ConvertTimeToUtc(unspecified, zone);
        }
    }
}
