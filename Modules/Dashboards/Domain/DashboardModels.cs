namespace RM_CMS.Modules.Dashboards.Domain
{
    /// <summary>
    /// What a team lead sees when they log in.
    ///
    /// The shape follows the role's actual duties rather than the database: a team lead
    /// exists to make sure nobody is dropped. So the escalations they owe an answer to
    /// come first, then the cases their team is sitting on, then the people doing the
    /// work. A number nobody would act on is not on this page.
    ///
    /// Everything here is scoped to the teams the caller LEADS, resolved from
    /// <c>team.lead_user_id</c> against the signed-in account. It is never taken from a
    /// parameter — the MVP page read <c>?teamleadid=</c> from the query string, which
    /// meant any signed-in user could read any team lead's queue by editing the URL.
    /// </summary>
    public sealed class TeamLeadDashboard
    {
        /// <summary>The teams this caller leads. Empty is a legitimate answer.</summary>
        public IReadOnlyList<TeamSummary> Teams { get; set; } = Array.Empty<TeamSummary>();

        public EscalationSummary Escalations { get; set; } = new();
        public NurtureSummary Nurture { get; set; } = new();
        public CaseSummary Cases { get; set; } = new();
        public ContactSummary Contacts { get; set; } = new();
        public IReadOnlyList<VolunteerLoad> Volunteers { get; set; } = Array.Empty<VolunteerLoad>();

        /// <summary>When this was assembled, so a stale open tab is obvious.</summary>
        public DateTime GeneratedAt { get; set; }
    }

    /// <summary>
    /// Cases actively being nurtured, and where each has reached in its plan.
    ///
    /// This is the one thing about nurture a lead cannot get elsewhere on the page.
    /// Overdue steps already show as "Overdue" and finished plans as "Awaiting
    /// Review"; what neither answers is how many journeys are quietly in progress
    /// and how far along they are.
    /// </summary>
    public sealed class NurtureSummary
    {
        public int Active { get; set; }

        /// <summary>Nurture cases whose next step is already past due.</summary>
        public int Overdue { get; set; }

        /// <summary>Paused by an open escalation — in the plan but not moving.</summary>
        public int Paused { get; set; }

        public IReadOnlyList<NurtureItem> Items { get; set; } = Array.Empty<NurtureItem>();
    }

    public sealed class NurtureItem
    {
        public string CaseId { get; set; } = string.Empty;
        public string? CaseReference { get; set; }
        public string PersonName { get; set; } = string.Empty;
        public string? VolunteerName { get; set; }

        public int CurrentStepNumber { get; set; }
        public int PlanStepCount { get; set; }
        public DateTime? NextStepDueOn { get; set; }

        public bool IsPaused { get; set; }

        /// <summary>Days past due, or null when it is not yet due.</summary>
        public int? DaysOverdue { get; set; }

        public string Progress => PlanStepCount > 0
            ? $"{CurrentStepNumber} of {PlanStepCount}"
            : $"step {CurrentStepNumber}";
    }

    public sealed class TeamSummary
    {
        public string PublicId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? CampusName { get; set; }
        public int MemberCount { get; set; }
        public int MaxMembers { get; set; }

        /// <summary>True when the team is at its span-of-control ceiling.</summary>
        public bool IsFull => MemberCount >= MaxMembers;
    }

    /// <summary>
    /// The escalation picture. <see cref="Unacknowledged"/> is the number that matters:
    /// it is the count of concerns raised to this lead that nobody has yet said they
    /// have seen.
    /// </summary>
    public sealed class EscalationSummary
    {
        public int Unacknowledged { get; set; }
        public int Open { get; set; }

        /// <summary>
        /// How long the longest-waiting unacknowledged escalation has been waiting.
        /// Null when there are none. A count alone hides the difference between one
        /// raised a minute ago and one raised on Friday.
        /// </summary>
        public double? OldestUnacknowledgedHours { get; set; }

        /// <summary>The ones to act on, most urgent first. Bounded, not the full list.</summary>
        public IReadOnlyList<EscalationItem> Urgent { get; set; } = Array.Empty<EscalationItem>();
    }

    public sealed class EscalationItem
    {
        public string PublicId { get; set; } = string.Empty;
        public string? ReferenceCode { get; set; }
        public string? PersonName { get; set; }
        public string ReasonCode { get; set; } = string.Empty;
        public string? ReasonLabel { get; set; }
        public bool RequiresProtocol { get; set; }
        public string Tier { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime RaisedAt { get; set; }
        public DateTime? AcknowledgedAt { get; set; }
        public string? RaisedByName { get; set; }

        public double WaitingHours { get; set; }
    }

    /// <summary>
    /// The team's case load by the state that decides what happens next. These are the
    /// four a lead can actually act on — an unassigned case needs a volunteer, an
    /// escalated one is paused pending them, and one awaiting review needs their sign-off.
    /// </summary>
    public sealed class CaseSummary
    {
        public int AwaitingAssignment { get; set; }
        public int InProgress { get; set; }
        public int Escalated { get; set; }
        public int AwaitingReview { get; set; }

        /// <summary>Cases waiting on this lead to approve closure, oldest first.</summary>
        public IReadOnlyList<ReviewItem> AwaitingReviewItems { get; set; } = Array.Empty<ReviewItem>();
    }

    public sealed class ReviewItem
    {
        public string PublicId { get; set; } = string.Empty;
        public string? ReferenceCode { get; set; }
        public string? PersonName { get; set; }
        public string? VolunteerName { get; set; }
        public DateTime? AwaitingReviewSince { get; set; }
        public double? WaitingDays { get; set; }
    }

    /// <summary>
    /// Follow-ups the team owes. Overdue is counted from the contact's scheduled date
    /// plus the same grace period the mark-overdue job uses, so this page and that job
    /// never disagree about what "overdue" means.
    /// </summary>
    public sealed class ContactSummary
    {
        public int DueToday { get; set; }
        public int Overdue { get; set; }
        public int MissedLast7Days { get; set; }
    }

    /// <summary>
    /// One volunteer's load. This is the view a lead needs to answer "who can take
    /// another one?" and "who is drowning?".
    /// </summary>
    public sealed class VolunteerLoad
    {
        public string PublicId { get; set; } = string.Empty;
        public string? ReferenceCode { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string CapacityBandCode { get; set; } = string.Empty;
        public string? CapacityBandLabel { get; set; }

        public int CurrentCaseLoad { get; set; }
        public int CapacityMaxPerWeek { get; set; }

        /// <summary>Open escalations raised by this volunteer, still unresolved.</summary>
        public int OpenEscalations { get; set; }

        public int RemainingCapacity => Math.Max(0, CapacityMaxPerWeek - CurrentCaseLoad);
        public bool HasSpareCapacity => RemainingCapacity > 0;

        // ---- completion trend ----
        //
        // Three weeks of raw counts rather than three percentages, because a week
        // where nothing was scheduled is NOT a 0% week — it is a week with no
        // information. Treating "no cases given" as total failure would flag a
        // volunteer for their team lead's quiet week, which is the opposite of what
        // this column is for.

        public int Week1Total { get; set; }     // most recent complete week
        public int Week1Done { get; set; }
        public int Week2Total { get; set; }
        public int Week2Done { get; set; }
        public int Week3Total { get; set; }
        public int Week3Done { get; set; }

        /// <summary>Completion rate for a week, or null when nothing was scheduled.</summary>
        private static double? Rate(int done, int total) =>
            total == 0 ? null : Math.Round(done * 100.0 / total, 1);

        public double? CompletionRate => Rate(Week1Done, Week1Total);
        public double? PreviousCompletionRate => Rate(Week2Done, Week2Total);

        /// <summary>
        /// This week against the one before: UP, FLAT, DOWN, or NONE when either
        /// week has no scheduled work to compare.
        /// </summary>
        public string Trend => Compare(Rate(Week1Done, Week1Total), Rate(Week2Done, Week2Total));

        /// <summary>The week before that, needed to spot two consecutive falls.</summary>
        public string PreviousTrend => Compare(Rate(Week2Done, Week2Total), Rate(Week3Done, Week3Total));

        private static string Compare(double? current, double? previous)
        {
            if (current is null || previous is null) return VolunteerTrend.None;

            if (current > previous) return VolunteerTrend.Up;
            if (current < previous) return VolunteerTrend.Down;

            return VolunteerTrend.Flat;
        }

        /// <summary>
        /// Two consecutive falls. One bad week is a bad week; two in a row is the
        /// shape that precedes a volunteer quietly dropping out, which is what the
        /// team lead needs to catch while there is still time to ask.
        /// </summary>
        public bool IsAtRisk =>
            Trend == VolunteerTrend.Down && PreviousTrend == VolunteerTrend.Down;

        /// <summary>
        /// The health flag: GREEN steady or improving, AMBER one week down,
        /// RED two consecutive weeks down.
        /// </summary>
        public string HealthFlag =>
            IsAtRisk ? VolunteerHealth.Red
            : Trend == VolunteerTrend.Down ? VolunteerHealth.Amber
            : VolunteerHealth.Green;
    }

    public static class VolunteerTrend
    {
        public const string Up = "UP";
        public const string Flat = "FLAT";
        public const string Down = "DOWN";

        /// <summary>Not enough scheduled work in one of the weeks to compare.</summary>
        public const string None = "NONE";
    }

    public static class VolunteerHealth
    {
        public const string Green = "GREEN";
        public const string Amber = "AMBER";
        public const string Red = "RED";
    }
}
