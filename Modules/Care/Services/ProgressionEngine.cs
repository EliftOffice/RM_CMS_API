using RM_CMS.Modules.Care.Data;
using RM_CMS.Modules.Care.Domain;
using RM_CMS.Modules.Identity.Domain;

namespace RM_CMS.Modules.Care.Services
{
    /// <summary>
    /// Decides what happens to a case after a contact is logged, and applies it.
    ///
    /// The MVP made this decision in a hard-coded C# switch over lower-cased strings
    /// ("needs follow-up" / "needs followup" / "crisis" ...), so a wording change in
    /// the UI silently produced "Unknown response type" at runtime, and changing the
    /// workflow meant a deployment.
    ///
    /// Here the decision is DATA: a row in <c>care_progression_rule</c> matched on
    /// (stage, step, outcome, intent). Administrators change the workflow by editing
    /// rules. Anything unmatched falls through to MANUAL_REVIEW rather than being
    /// guessed at, so an unforeseen combination reaches a human instead of quietly
    /// doing the wrong thing.
    ///
    /// This type only decides and mutates the CASE. Creating the next interaction row
    /// and raising escalations belongs to the service, which owns those transactions.
    /// </summary>
    public interface IProgressionEngine
    {
        /// <summary>
        /// Works out what should happen next and mutates <paramref name="careCase"/>
        /// in memory. The caller persists it.
        /// </summary>
        Task<ProgressionDecision> DecideAsync(CareCase careCase, CareInteraction completed, DateTime nowUtc);
    }

    /// <summary>
    /// What the engine concluded. Carries both the mutated case and the side effects
    /// the service still has to perform.
    /// </summary>
    public sealed class ProgressionDecision
    {
        public string Action { get; set; } = ProgressionAction.ManualReview;
        public string Explanation { get; set; } = string.Empty;
        public string? MatchedRule { get; set; }

        /// <summary>Create a nurture interaction for this step, due on this date.</summary>
        public int? CreateStepNumber { get; set; }
        public DateTime? CreateStepDueOn { get; set; }
        public string? CreateStepMethod { get; set; }

        /// <summary>Create a retry of the initial follow-up, due on this date.</summary>
        public DateTime? CreateRetryOn { get; set; }

        /// <summary>Raise an escalation at this tier.</summary>
        public bool RaiseEscalation { get; set; }
        public string? EscalationTier { get; set; }

        /// <summary>Close the case with this reason.</summary>
        public bool CloseCase { get; set; }
        public string? CloseReason { get; set; }

        public bool NeedsHumanDecision { get; set; }
    }

    public sealed class ProgressionEngine : IProgressionEngine
    {
        private readonly ICareLookupRepository _lookups;
        private readonly ILogger<ProgressionEngine> _logger;

        public ProgressionEngine(ICareLookupRepository lookups, ILogger<ProgressionEngine> logger)
        {
            _lookups = lookups;
            _logger = logger;
        }

        public async Task<ProgressionDecision> DecideAsync(CareCase careCase, CareInteraction completed, DateTime nowUtc)
        {
            var today = nowUtc.Date;

            // ---- counters, before any routing decision ----
            careCase.ContactAttemptCount += 1;
            careCase.LastContactAt = completed.OccurredAt ?? nowUtc;
            careCase.FirstContactAt ??= completed.OccurredAt ?? nowUtc;

            if (completed.MadeContact == true)
                careCase.ConsecutiveNoContact = 0;
            else
                careCase.ConsecutiveNoContact += 1;

            var plan = careCase.NurturePlanId.HasValue
                ? await _lookups.GetPlanByIdAsync(careCase.NurturePlanId.Value)
                : await _lookups.GetDefaultPlanAsync(careCase.CampusId);

            // A plan may switch outcome evaluation off entirely, in which case the
            // sequence advances regardless of how the contact went.
            if (plan is not null && !plan.EvaluateOutcome && careCase.Stage == CaseStage.Nurture)
            {
                _logger.LogInformation(
                    "Plan '{Plan}' does not evaluate outcomes; advancing case {Case} unconditionally",
                    plan.Name, careCase.PublicId);

                return ContinueNurture(careCase, plan, today, "Plan advances without evaluating the outcome.");
            }

            var rule = await _lookups.MatchRuleAsync(
                completed.Stage, completed.SequenceNumber, completed.OutcomeCode, completed.IntentCode);

            if (rule is null)
            {
                // Should not happen — the seeded rules include a catch-all — but a
                // missing fallback must not silently strand the case.
                _logger.LogWarning(
                    "No progression rule matched for case {Case} (stage {Stage}, outcome {Outcome}, intent {Intent})",
                    careCase.PublicId, completed.Stage, completed.OutcomeCode, completed.IntentCode);

                return ManualReview(careCase, nowUtc, "No rule matched this combination.");
            }

            var label = rule.Description ?? $"rule {rule.PublicId}";

            _logger.LogInformation(
                "Case {Case}: {Outcome}/{Intent} -> {Action} ({Rule})",
                careCase.PublicId, completed.OutcomeCode, completed.IntentCode, rule.Action, label);

            var decision = rule.Action switch
            {
                ProgressionAction.StartNurture     => StartNurture(careCase, plan, today, rule),
                ProgressionAction.ContinueNurture  => ContinueNurture(careCase, plan, today, null, rule),
                ProgressionAction.JumpToStep       => JumpToStep(careCase, plan, today, rule),
                ProgressionAction.ScheduleRetry    => await ScheduleRetryAsync(careCase, today, rule),
                ProgressionAction.Escalate         => Escalate(careCase, completed, rule),
                ProgressionAction.SendToReview     => SendToReview(careCase, nowUtc),
                ProgressionAction.CloseCase        => Close(careCase, rule),
                _                                  => ManualReview(careCase, nowUtc, "Queued for a decision.")
            };

            decision.MatchedRule = label;
            return decision;
        }

        // ------------------------------------------------------------------
        // Actions
        // ------------------------------------------------------------------

        private ProgressionDecision StartNurture(CareCase c, NurturePlan? plan, DateTime today, ProgressionRule rule)
        {
            if (plan is null || plan.MaxStepNumber == 0)
                return ManualReview(c, today, "No nurture plan is configured, so the sequence cannot start.");

            var first = plan.StepNumber(1)!;
            var gap = rule.OverrideGapDays ?? first.GapDays ?? plan.DefaultGapDays;
            var due = today.AddDays(gap);

            c.NurturePlanId = plan.Id;
            c.Stage = CaseStage.Nurture;
            c.Status = CaseStatus.InProgress;
            c.CurrentStepNumber = 0;      // step 1 is created but not yet done
            c.NextStepDueOn = due;
            c.NextActionOn = due;

            return new ProgressionDecision
            {
                Action = ProgressionAction.StartNurture,
                Explanation = $"Nurture started. Step 1 ({first.Label ?? first.MethodCode}) is due {due:d MMM yyyy}.",
                CreateStepNumber = 1,
                CreateStepDueOn = due,
                CreateStepMethod = first.MethodCode
            };
        }

        private ProgressionDecision ContinueNurture(
            CareCase c, NurturePlan? plan, DateTime today, string? explanationOverride = null, ProgressionRule? rule = null)
        {
            if (plan is null || plan.MaxStepNumber == 0)
                return ManualReview(c, today, "No nurture plan is configured.");

            var completedStep = Math.Max(c.CurrentStepNumber, 0) + 1;
            c.CurrentStepNumber = completedStep;

            var next = plan.StepNumber(completedStep + 1);

            // The plan is exhausted: hand to a team lead for the Permanent/Failed
            // decision rather than closing on the system's own authority.
            if (next is null)
                return SendToReview(c, today, $"All {plan.MaxStepNumber} nurture steps are done.");

            var gap = rule?.OverrideGapDays ?? next.GapDays ?? plan.DefaultGapDays;
            var due = today.AddDays(gap);

            c.Stage = CaseStage.Nurture;
            c.Status = CaseStatus.InProgress;
            c.NextStepDueOn = due;
            c.NextActionOn = due;

            return new ProgressionDecision
            {
                Action = ProgressionAction.ContinueNurture,
                Explanation = explanationOverride
                    ?? $"Step {next.StepNumber} ({next.Label ?? next.MethodCode}) is due {due:d MMM yyyy}.",
                CreateStepNumber = next.StepNumber,
                CreateStepDueOn = due,
                CreateStepMethod = next.MethodCode
            };
        }

        private ProgressionDecision JumpToStep(CareCase c, NurturePlan? plan, DateTime today, ProgressionRule rule)
        {
            if (plan is null || rule.JumpToStep is null)
                return ManualReview(c, today, "The rule asks to jump to a step, but no target is configured.");

            var target = plan.StepNumber(rule.JumpToStep.Value);

            if (target is null)
                return ManualReview(c, today, $"Step {rule.JumpToStep} does not exist in this plan.");

            var gap = rule.OverrideGapDays ?? target.GapDays ?? plan.DefaultGapDays;
            var due = today.AddDays(gap);

            c.NurturePlanId = plan.Id;
            c.Stage = CaseStage.Nurture;
            c.Status = CaseStatus.InProgress;
            c.CurrentStepNumber = target.StepNumber - 1;
            c.NextStepDueOn = due;
            c.NextActionOn = due;

            return new ProgressionDecision
            {
                Action = ProgressionAction.JumpToStep,
                Explanation = $"Jumped to step {target.StepNumber}, due {due:d MMM yyyy}.",
                CreateStepNumber = target.StepNumber,
                CreateStepDueOn = due,
                CreateStepMethod = target.MethodCode
            };
        }

        private async Task<ProgressionDecision> ScheduleRetryAsync(CareCase c, DateTime today, ProgressionRule rule)
        {
            var maxAttempts = await _lookups.GetIntSettingAsync("assignment.max_retry_attempts", 3);
            var delayDays = rule.OverrideGapDays ?? await _lookups.GetIntSettingAsync("assignment.retry_delay_days", 3);

            // Retrying forever is how someone stays "in progress" indefinitely with
            // nobody noticing. The attempt limit closes the case honestly instead.
            if (c.ContactAttemptCount >= maxAttempts)
            {
                c.Stage = CaseStage.Closed;
                c.Status = CaseStatus.Closed;
                c.NextStepDueOn = null;
                c.NextActionOn = null;

                return new ProgressionDecision
                {
                    Action = ProgressionAction.CloseCase,
                    CloseCase = true,
                    CloseReason = CaseCloseReason.Unreachable,
                    Explanation = $"Closed after {c.ContactAttemptCount} attempts with no contact."
                };
            }

            var due = today.AddDays(delayDays);

            c.Status = CaseStatus.InProgress;
            c.NextActionOn = due;

            return new ProgressionDecision
            {
                Action = ProgressionAction.ScheduleRetry,
                Explanation =
                    $"Retry {c.ContactAttemptCount + 1} of {maxAttempts} is due {due:d MMM yyyy}.",
                CreateRetryOn = due
            };
        }

        private ProgressionDecision Escalate(CareCase c, CareInteraction completed, ProgressionRule rule)
        {
            // Escalating PAUSES the case: the scheduler creates no further contact
            // while the concern is open. Closing the escalation resumes it from here.
            c.Status = CaseStatus.Escalated;
            c.NextStepDueOn = null;

            return new ProgressionDecision
            {
                Action = ProgressionAction.Escalate,
                RaiseEscalation = true,
                EscalationTier = null,   // taken from the outcome/reason defaults
                Explanation = "Escalated to the team lead. Follow-up is paused until it is resolved."
            };
        }

        private ProgressionDecision SendToReview(CareCase c, DateTime nowUtc, string? reason = null)
        {
            c.Stage = CaseStage.Review;
            c.Status = CaseStatus.InProgress;
            c.NextStepDueOn = null;
            c.NextActionOn = null;
            c.AwaitingReviewSince = nowUtc;

            return new ProgressionDecision
            {
                Action = ProgressionAction.SendToReview,
                NeedsHumanDecision = true,
                Explanation = (reason is null ? "" : reason + " ") +
                              "Sent to the team lead to decide the outcome."
            };
        }

        private ProgressionDecision Close(CareCase c, ProgressionRule rule)
        {
            var reason = rule.CloseReason ?? CaseCloseReason.Other;

            c.Stage = CaseStage.Closed;
            c.Status = CaseStatus.Closed;
            c.NextStepDueOn = null;
            c.NextActionOn = null;

            return new ProgressionDecision
            {
                Action = ProgressionAction.CloseCase,
                CloseCase = true,
                CloseReason = reason,
                Explanation = $"Case closed ({Humanise(reason)})."
            };
        }

        private ProgressionDecision ManualReview(CareCase c, DateTime nowUtc, string reason)
        {
            c.AwaitingReviewSince = nowUtc;
            c.NextStepDueOn = null;

            return new ProgressionDecision
            {
                Action = ProgressionAction.ManualReview,
                NeedsHumanDecision = true,
                Explanation = reason + " A team lead needs to decide what happens next."
            };
        }

        private static string Humanise(string code) =>
            code.Replace('_', ' ').ToLowerInvariant();
    }
}
