using Dapper;
using RM_CMS.Data;
using RM_CMS.Modules.Care.Domain;

namespace RM_CMS.Modules.Care.Data
{
    /// <summary>
    /// Reads the admin-configured vocabulary and rules that drive the workflow:
    /// progression rules, nurture plans, outcomes, intents and note types.
    /// </summary>
    public interface ICareLookupRepository
    {
        /// <summary>
        /// The rule that applies to a completed interaction.
        ///
        /// Evaluated priority-first: the lowest priority number that matches wins,
        /// with specificity only as a tie-break. Ranking by specificity instead let a
        /// narrow rule like (NURTURE, SPOKE, any) outrank (any, any, NOT_INTERESTED),
        /// so someone who said "not interested" mid-sequence kept getting calls.
        /// </summary>
        Task<ProgressionRule?> MatchRuleAsync(string? stage, int? stepNumber, string? outcomeCode, string? intentCode);

        Task<IReadOnlyList<ProgressionRule>> GetAllRulesAsync();

        Task<NurturePlan?> GetDefaultPlanAsync(long? campusId);
        Task<NurturePlan?> GetPlanByIdAsync(long id);

        Task<bool> OutcomeExistsAsync(string code);
        Task<bool> IntentExistsAsync(string code);

        /// <summary>
        /// True when this intent is an explicit request to stop. The service sets
        /// person.do_not_contact, so the block follows the PERSON and survives them
        /// being recorded again later as a new visitor.
        /// </summary>
        Task<bool> IntentImpliesDoNotContactAsync(string? code);

        /// <summary>Behaviour flags for an outcome: does it escalate, does it retry.</summary>
        Task<(bool OpensEscalation, bool SchedulesRetry, bool ContactMade, string? DefaultTier)?>
            GetOutcomeBehaviourAsync(string code);

        Task<IReadOnlyList<(string Code, string Label)>> GetOutcomesAsync();
        Task<IReadOnlyList<(string Code, string Label)>> GetIntentsAsync();
        Task<IReadOnlyList<(string Code, string Label)>> GetContactMethodsAsync();
        Task<IReadOnlyList<(string Code, string Label, bool RequiresProtocol)>> GetEscalationReasonsAsync();
        Task<IReadOnlyList<(string Code, string Label)>> GetEscalationOutcomesAsync();

        Task<int> GetIntSettingAsync(string key, int fallback);
    }

    public sealed class CareLookupRepository : ICareLookupRepository
    {
        private readonly IDbConnectionFactory _dbFactory;

        public CareLookupRepository(IDbConnectionFactory dbFactory) => _dbFactory = dbFactory;

        public async Task<ProgressionRule?> MatchRuleAsync(
            string? stage, int? stepNumber, string? outcomeCode, string? intentCode)
        {
            const string sql = @"
                SELECT  id                AS Id,
                        public_id         AS PublicId,
                        from_stage        AS FromStage,
                        from_step_number  AS FromStepNumber,
                        outcome_code      AS OutcomeCode,
                        intent_code       AS IntentCode,
                        action            AS Action,
                        jump_to_step      AS JumpToStep,
                        close_reason      AS CloseReason,
                        override_gap_days AS OverrideGapDays,
                        priority          AS Priority,
                        description       AS Description
                FROM care_progression_rule
                WHERE is_active = 1
                  AND (from_stage       IS NULL OR from_stage       = @Stage)
                  AND (from_step_number IS NULL OR from_step_number = @StepNumber)
                  AND (outcome_code     IS NULL OR outcome_code     = @OutcomeCode)
                  AND (intent_code      IS NULL OR intent_code      = @IntentCode)
                ORDER BY priority ASC,
                         (from_stage       IS NOT NULL)
                       + (from_step_number IS NOT NULL)
                       + (outcome_code     IS NOT NULL)
                       + (intent_code      IS NOT NULL) DESC
                LIMIT 1;";

            using var connection = _dbFactory.GetConnection();
            return await connection.QueryFirstOrDefaultAsync<ProgressionRule>(sql, new
            {
                Stage = stage,
                StepNumber = stepNumber,
                OutcomeCode = outcomeCode,
                IntentCode = intentCode
            });
        }

        public async Task<IReadOnlyList<ProgressionRule>> GetAllRulesAsync()
        {
            const string sql = @"
                SELECT  id AS Id, public_id AS PublicId, from_stage AS FromStage,
                        from_step_number AS FromStepNumber, outcome_code AS OutcomeCode,
                        intent_code AS IntentCode, action AS Action, jump_to_step AS JumpToStep,
                        close_reason AS CloseReason, override_gap_days AS OverrideGapDays,
                        priority AS Priority, description AS Description
                FROM care_progression_rule
                WHERE is_active = 1
                ORDER BY priority ASC;";

            using var connection = _dbFactory.GetConnection();
            return (await connection.QueryAsync<ProgressionRule>(sql)).ToList();
        }

        public async Task<NurturePlan?> GetDefaultPlanAsync(long? campusId)
        {
            // A campus-specific plan wins over the organisation-wide default.
            const string planSql = @"
                SELECT  id AS Id, public_id AS PublicId, name AS Name,
                        evaluate_outcome AS EvaluateOutcome, assignment_mode AS AssignmentMode,
                        default_gap_days AS DefaultGapDays, missed_after_days AS MissedAfterDays
                FROM nurture_plan
                WHERE is_active = 1
                  AND (campus_id = @CampusId OR campus_id IS NULL)
                ORDER BY (campus_id IS NOT NULL) DESC, is_default DESC, id
                LIMIT 1;";

            using var connection = _dbFactory.GetConnection();
            var plan = await connection.QueryFirstOrDefaultAsync<NurturePlan>(planSql, new { CampusId = campusId });

            if (plan is not null)
                plan.Steps = await LoadStepsAsync(connection, plan.Id);

            return plan;
        }

        public async Task<NurturePlan?> GetPlanByIdAsync(long id)
        {
            const string sql = @"
                SELECT  id AS Id, public_id AS PublicId, name AS Name,
                        evaluate_outcome AS EvaluateOutcome, assignment_mode AS AssignmentMode,
                        default_gap_days AS DefaultGapDays, missed_after_days AS MissedAfterDays
                FROM nurture_plan WHERE id = @Id LIMIT 1;";

            using var connection = _dbFactory.GetConnection();
            var plan = await connection.QueryFirstOrDefaultAsync<NurturePlan>(sql, new { Id = id });

            if (plan is not null)
                plan.Steps = await LoadStepsAsync(connection, plan.Id);

            return plan;
        }

        private static async Task<List<NurturePlanStep>> LoadStepsAsync(System.Data.IDbConnection connection, long planId)
        {
            const string sql = @"
                SELECT  id AS Id, step_number AS StepNumber, method_code AS MethodCode,
                        gap_days AS GapDays, label AS Label, guidance AS Guidance
                FROM nurture_plan_step
                WHERE nurture_plan_id = @PlanId AND is_active = 1
                ORDER BY step_number;";

            return (await connection.QueryAsync<NurturePlanStep>(sql, new { PlanId = planId })).ToList();
        }

        public async Task<bool> OutcomeExistsAsync(string code)
        {
            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM care_outcome WHERE code = @Code AND is_active = 1;",
                new { Code = code }) > 0;
        }

        public async Task<bool> IntentExistsAsync(string code)
        {
            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM visitor_intent WHERE code = @Code AND is_active = 1;",
                new { Code = code }) > 0;
        }

        public async Task<bool> IntentImpliesDoNotContactAsync(string? code)
        {
            if (string.IsNullOrWhiteSpace(code)) return false;

            using var connection = _dbFactory.GetConnection();
            return await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM visitor_intent WHERE code = @Code AND implies_do_not_contact = 1;",
                new { Code = code }) > 0;
        }

        public async Task<(bool OpensEscalation, bool SchedulesRetry, bool ContactMade, string? DefaultTier)?>
            GetOutcomeBehaviourAsync(string code)
        {
            const string sql = @"
                SELECT opens_escalation AS OpensEscalation,
                       schedules_retry  AS SchedulesRetry,
                       contact_made     AS ContactMade,
                       default_tier     AS DefaultTier
                FROM care_outcome WHERE code = @Code LIMIT 1;";

            using var connection = _dbFactory.GetConnection();
            var row = await connection.QueryFirstOrDefaultAsync<
                (bool OpensEscalation, bool SchedulesRetry, bool ContactMade, string? DefaultTier)?>(
                sql, new { Code = code });

            return row;
        }

        public async Task<IReadOnlyList<(string Code, string Label)>> GetOutcomesAsync() =>
            await CodeLabelAsync("SELECT code AS Code, label AS Label FROM care_outcome WHERE is_active = 1 ORDER BY sort_order;");

        public async Task<IReadOnlyList<(string Code, string Label)>> GetIntentsAsync() =>
            await CodeLabelAsync("SELECT code AS Code, label AS Label FROM visitor_intent WHERE is_active = 1 ORDER BY sort_order;");

        public async Task<IReadOnlyList<(string Code, string Label)>> GetContactMethodsAsync() =>
            await CodeLabelAsync("SELECT code AS Code, label AS Label FROM contact_method WHERE is_active = 1 ORDER BY sort_order;");

        public async Task<IReadOnlyList<(string Code, string Label)>> GetEscalationOutcomesAsync() =>
            await CodeLabelAsync("SELECT code AS Code, label AS Label FROM escalation_outcome WHERE is_active = 1 ORDER BY sort_order;");

        public async Task<IReadOnlyList<(string Code, string Label, bool RequiresProtocol)>> GetEscalationReasonsAsync()
        {
            const string sql = @"
                SELECT code AS Code, label AS Label, requires_protocol AS RequiresProtocol
                FROM escalation_reason WHERE is_active = 1 ORDER BY sort_order;";

            using var connection = _dbFactory.GetConnection();
            return (await connection.QueryAsync<(string, string, bool)>(sql)).ToList();
        }

        private async Task<IReadOnlyList<(string Code, string Label)>> CodeLabelAsync(string sql)
        {
            using var connection = _dbFactory.GetConnection();
            return (await connection.QueryAsync<(string, string)>(sql)).ToList();
        }

        public async Task<int> GetIntSettingAsync(string key, int fallback)
        {
            const string sql = @"SELECT setting_value FROM app_setting WHERE setting_key = @Key LIMIT 1;";

            using var connection = _dbFactory.GetConnection();
            var raw = await connection.ExecuteScalarAsync<string?>(sql, new { Key = key });

            return int.TryParse(raw, out var value) ? value : fallback;
        }
    }
}
