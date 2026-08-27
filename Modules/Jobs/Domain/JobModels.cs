namespace RM_CMS.Modules.Jobs.Domain
{
    /// <summary>One execution of a scheduled job. Mirrors <c>job_run</c>.</summary>
    public sealed class JobRun
    {
        public long Id { get; set; }
        public string JobName { get; set; } = string.Empty;
        public DateTime StartedAt { get; set; }
        public DateTime? FinishedAt { get; set; }
        public string Status { get; set; } = JobStatus.Running;
        public int ItemsProcessed { get; set; }
        public int ItemsFailed { get; set; }
        public string? Detail { get; set; }
        public long? TriggeredBy { get; set; }
    }

    public static class JobStatus
    {
        public const string Running = "RUNNING";
        public const string Succeeded = "SUCCEEDED";
        public const string Failed = "FAILED";

        /// <summary>Some items succeeded and some did not.</summary>
        public const string Partial = "PARTIAL";
    }

    /// <summary>
    /// Job names. These are written to <c>job_run.job_name</c> and queried by it, so
    /// they are constants rather than free strings at the call site.
    /// </summary>
    public static class JobNames
    {
        public const string AssignUnassigned = "assign-unassigned";
        public const string AdvanceNurture = "advance-nurture";
        public const string MarkOverdue = "mark-overdue";
        public const string ChaseEscalations = "chase-escalations";

        /// <summary>
        /// Reminds each team lead and their volunteers that the huddle is today.
        /// Does nothing on the other six days.
        /// </summary>
        public const string HuddleReminder = "huddle-reminder";

        /// <summary>Drains the notification queue. Runs last: the jobs above fill it.</summary>
        public const string SendNotifications = "send-notifications";

        public static readonly string[] All =
            { AssignUnassigned, AdvanceNurture, MarkOverdue, ChaseEscalations,
              HuddleReminder, SendNotifications };

        public static bool IsKnown(string? v) => v is not null && All.Contains(v, StringComparer.Ordinal);
    }

    /// <summary>
    /// What a job did. Returned to the caller and persisted to <c>job_run</c>.
    ///
    /// <see cref="Failed"/> counts items that could not be processed, not an error in
    /// the job itself — one case failing to assign must not abandon the rest of the
    /// queue, so failures are counted and the sweep continues.
    /// </summary>
    public sealed class JobReport
    {
        public JobReport(string jobName) => JobName = jobName;

        public string JobName { get; }
        public int Processed { get; private set; }
        public int Failed { get; private set; }
        public int Skipped { get; private set; }

        private readonly List<string> _notes = new();

        /// <summary>Up to a handful of explanatory notes; the detail column is 500 chars.</summary>
        public IReadOnlyList<string> Notes => _notes;

        public void RecordProcessed() => Processed++;
        public void RecordSkipped() => Skipped++;

        public void RecordFailed(string note)
        {
            Failed++;
            AddNote(note);
        }

        /// <summary>
        /// Counts a failure whose explanation has already been noted. Without this a
        /// caller that records its own per-item detail has to pass a second, vaguer
        /// note alongside it, and the detail column fills with "failed" beside the
        /// line that actually says why.
        /// </summary>
        public void RecordFailed() => Failed++;

        public void AddNote(string note)
        {
            if (_notes.Count < 10) _notes.Add(note);
        }

        public string Status => Failed == 0
            ? JobStatus.Succeeded
            : Processed > 0 ? JobStatus.Partial : JobStatus.Failed;

        /// <summary>Truncated to fit <c>job_run.detail</c> (VARCHAR(500)).</summary>
        public string? Detail
        {
            get
            {
                if (_notes.Count == 0) return null;

                var joined = string.Join(" | ", _notes);
                return joined.Length <= 500 ? joined : joined[..497] + "...";
            }
        }
    }
}
