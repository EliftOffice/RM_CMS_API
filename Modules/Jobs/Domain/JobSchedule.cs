namespace RM_CMS.Modules.Jobs.Domain
{
    /// <summary>
    /// When a job should run. Mirrors <c>job_schedule</c>, one row per job name.
    /// </summary>
    /// <remarks>
    /// Two halves that must not be confused. <see cref="Cadence"/>,
    /// <see cref="DayOfWeek"/>, <see cref="TimeOfDay"/> and <see cref="Timezone"/>
    /// are the RULE an administrator wrote. <see cref="NextDueAt"/> is only the
    /// cached next occurrence of that rule in UTC, recomputed after every run and
    /// whenever the rule changes. Reading the cache is what makes the scheduler's
    /// once-a-minute tick an index lookup instead of six timezone conversions.
    /// </remarks>
    public sealed class JobSchedule
    {
        public long Id { get; set; }
        public string JobName { get; set; } = string.Empty;

        public bool IsEnabled { get; set; }

        public string Cadence { get; set; } = JobCadence.Weekly;

        /// <summary>ISO day, 1 = Monday ... 7 = Sunday. Null when <see cref="Cadence"/> is DAILY.</summary>
        public int? DayOfWeek { get; set; }

        /// <summary>Local wall-clock time in <see cref="Timezone"/>.</summary>
        public TimeSpan TimeOfDay { get; set; } = new(6, 0, 0);

        /// <summary>IANA id, e.g. Asia/Kolkata.</summary>
        public string Timezone { get; set; } = JobCadence.DefaultTimezone;

        /// <summary>The cached next occurrence, in UTC. Null means not scheduled.</summary>
        public DateTime? NextDueAt { get; set; }

        /// <summary>
        /// When the SCHEDULER last claimed this row. A run started by hand from the
        /// settings screen writes <c>job_run</c> and deliberately does not touch
        /// this — otherwise pressing "Run now" would quietly move the next sweep.
        /// </summary>
        public DateTime? LastRunAt { get; set; }

        public string? LastStatus { get; set; }

        public int RowVersion { get; set; }
    }

    public static class JobCadence
    {
        public const string Daily = "DAILY";
        public const string Weekly = "WEEKLY";

        /// <summary>
        /// Matches the default <c>campus.timezone</c>. Every instant this
        /// application stores is UTC, so the local rule needs a zone of its own
        /// rather than inheriting whatever the container happens to be set to.
        /// </summary>
        public const string DefaultTimezone = "Asia/Kolkata";

        public static bool IsKnown(string? v) =>
            string.Equals(v, Daily, StringComparison.Ordinal) ||
            string.Equals(v, Weekly, StringComparison.Ordinal);
    }

    /// <summary>What the schedule screen shows for one job.</summary>
    public sealed class JobScheduleDto
    {
        public string JobName { get; set; } = string.Empty;

        /// <summary>Wording for the screen, resolved from <see cref="JobCatalog"/>.</summary>
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        public bool IsEnabled { get; set; }
        public string Cadence { get; set; } = JobCadence.Weekly;
        public int? DayOfWeek { get; set; }

        /// <summary>HH:mm. Not HH:mm:ss — the tick is once a minute.</summary>
        public string TimeOfDay { get; set; } = "06:00";

        public string Timezone { get; set; } = JobCadence.DefaultTimezone;

        public DateTime? NextDueAt { get; set; }
        public DateTime? LastRunAt { get; set; }
        public string? LastStatus { get; set; }

        public int RowVersion { get; set; }

        /// <summary>
        /// True when this job only queues notifications rather than sending them.
        /// The screen warns when one of these is scheduled and send-notifications
        /// is not, which otherwise fills the queue and delivers nothing.
        /// </summary>
        public bool QueuesNotifications { get; set; }
    }

    /// <summary>An administrator's edit to one schedule.</summary>
    public sealed class JobScheduleUpdateRequest
    {
        public bool IsEnabled { get; set; }
        public string Cadence { get; set; } = JobCadence.Weekly;
        public int? DayOfWeek { get; set; }
        public string TimeOfDay { get; set; } = "06:00";
        public string? Timezone { get; set; }
        public int RowVersion { get; set; }
    }

    /// <summary>
    /// The human wording for each job, in one place.
    /// </summary>
    /// <remarks>
    /// The settings screen already carried its own copy of these titles in
    /// JavaScript. Putting them here means the schedule screen and the run-now
    /// screen cannot drift apart, and a job added to <see cref="JobNames"/> without
    /// wording shows its own name rather than a blank card.
    /// </remarks>
    public static class JobCatalog
    {
        public sealed record Entry(string Title, string Description, bool QueuesNotifications);

        private static readonly Dictionary<string, Entry> Entries = new(StringComparer.Ordinal)
        {
            [JobNames.AssignUnassigned] = new(
                "Assign new people",
                "Gives unassigned cases to the least-loaded volunteer with capacity.",
                QueuesNotifications: true),

            [JobNames.AdvanceNurture] = new(
                "Advance nurture",
                "Creates the next contact for anyone whose step has fallen due.",
                QueuesNotifications: true),

            [JobNames.MarkOverdue] = new(
                "Mark overdue contacts",
                "Marks planned contacts that are past their date as missed.",
                QueuesNotifications: false),

            [JobNames.ChaseEscalations] = new(
                "Chase escalations",
                "Reminds the team lead about unacknowledged concerns, then the pastor.",
                QueuesNotifications: true),

            [JobNames.HuddleReminder] = new(
                "Huddle reminder",
                "Tells each team the huddle is today. Does nothing on the other six days.",
                QueuesNotifications: true),

            [JobNames.SendNotifications] = new(
                "Send notifications",
                "Delivers everything the other jobs queued. Schedule it last.",
                QueuesNotifications: false)
        };

        public static Entry For(string jobName) =>
            Entries.TryGetValue(jobName, out var entry)
                ? entry
                : new Entry(jobName, string.Empty, QueuesNotifications: false);
    }
}
