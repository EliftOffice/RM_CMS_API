using Dapper;
using Microsoft.AspNetCore.Connections;
using RM_CMS.Data;
using RM_CMS.Data.DTO;
using RM_CMS.Data.DTO.Jobs;
using RM_CMS.Data.DTO.Pastors;
using RM_CMS.Data.DTO.TeamLeads;
using RM_CMS.Utilities;
using EscalationReasonDTO = RM_CMS.Data.DTO.Pastors.EscalationReasonDTO;
using PipelineHealthDTO = RM_CMS.Data.DTO.Pastors.PipelineHealthDTO;
using TeamLeadPerformanceDTO = RM_CMS.Data.DTO.Pastors.TeamLeadPerformanceDTO;
using TrendDTO = RM_CMS.Data.DTO.Pastors.TrendDTO;

namespace RM_CMS.DAL.Pastors
{
    public interface IPastorDashboardDAL
    {
        Task<ApiResponse<SystemHealthDTO>> GetSystemHealthAsync();
        Task<ApiResponse<KPIDTO>> GetKpisAsync();
        Task<ApiResponse<List<TeamLeadPerformanceDTO>>> GetTeamLeadPerformanceAsync();
        Task<ApiResponse<List<PipelineHealthDTO>>> GetPipelineHealthAsync();
        Task<ApiResponse<EscalationSummaryDTO>> GetEscalationsAsync();
        Task<ApiResponse<List<EscalationReasonDTO>>> GetTopEscalationReasonsAsync();
        Task<ApiResponse<List<TrendDTO>>> GetTrendsAsync();
        Task<ApiResponse<ImpactDTO>> GetImpactAsync();
        Task<ApiResponse<DevelopmentPipelineDTO>> GetDevelopmentPipelineAsync();
        Task<ApiResponse<List<EscalationPendingDTO>>> GetPastorCrisisEscalationsPendingAsync();
        Task<ApiResponse<bool>> ReEscalateToTeamLeadAsync(string escalationId, string notes);
        Task<ApiResponse<List<PastorPendingAssignmentDto>>> PastorPendingCrisisEscalations();
    }

    public class PastorDashboardDAL : IPastorDashboardDAL
    {
        private readonly IDbConnectionFactory _dbConnectionFactory;

        public PastorDashboardDAL(IDbConnectionFactory dbConnectionFactory)
        {
            _dbConnectionFactory = dbConnectionFactory;
        }

        // 1. SYSTEM HEALTH
        public async Task<ApiResponse<SystemHealthDTO>> GetSystemHealthAsync()
        {
            try
            {
                using var connection = _dbConnectionFactory.GetConnection();

                const string query = @"
                    SELECT 
                        (SELECT COUNT(*) FROM volunteers WHERE status = 'Active') as ActiveVolunteers,
                        (SELECT COUNT(*) FROM team_leads WHERE status = 'Active') as ActiveTeamLeads,
                        (SELECT COUNT(*) FROM people 
                         WHERE visit_type = 'First-Time Visitor'
                           AND DATE_FORMAT(first_visit_date, '%Y-%m') = DATE_FORMAT(CURDATE(), '%Y-%m')
                        ) as VisitorsMTD,
                        (SELECT COUNT(DISTINCT person_id) FROM follow_ups 
                        WHERE contact_status = 'Contacted' 
                        AND DATE_FORMAT(attempt_date, '%Y-%m') = DATE_FORMAT(CURDATE(), '%Y-%m') 
                        AND (response_type != 'No Response'OR next_action = 'Mark Unresponsive')
                        ) as FollowUpsCompletedMTD,
                        (SELECT AVG(vnps_score) FROM vnps_surveys 
                         WHERE quarter = CONCAT('Q', QUARTER(CURDATE()))
                           AND year = YEAR(CURDATE())
                        ) as SystemVNPS,
                        (SELECT 
                           ROUND(COUNT(CASE WHEN status = 'Active' THEN 1 END) * 100.0 / COUNT(*))
                         FROM volunteers
                         WHERE start_date <= DATE_SUB(CURDATE(), INTERVAL 3 MONTH)
                        ) as VolunteerRetention,
                        (
                        SELECT 
                            ROUND(
                                COUNT(DISTINCT CASE 
                                    WHEN contact_status = 'Contacted'
                                     AND (
                                            response_type != 'No Response'
                                            OR next_action = 'Mark Unresponsive'
                                         )
                                    THEN person_id 
                                END
                                ) * 100.0 
                                /
                                COUNT(DISTINCT person_id)
                            )
                        FROM follow_ups
                        WHERE DATE_FORMAT(attempt_date, '%Y-%m') = DATE_FORMAT(CURDATE(), '%Y-%m')
                        ) AS CompletionRateMTD,
                        (SELECT AVG(TIMESTAMPDIFF(HOUR, p.assigned_date, f.attempt_date) / 24)
                         FROM follow_ups f
                         JOIN people p ON f.person_id = p.person_id
                         WHERE f.attempt_number = 1
                           AND DATE_FORMAT(f.attempt_date, '%Y-%m') = DATE_FORMAT(CURDATE(), '%Y-%m')
                        ) as AvgResponseTimeDays
                ";

                var result = await connection.QueryFirstOrDefaultAsync<SystemHealthDTO>(query);

                return new ApiResponse<SystemHealthDTO>(
                    result != null ? ResponseType.Success : ResponseType.Warning,
                    result != null ? "System health retrieved successfully" : "No data found",
                    result ?? new SystemHealthDTO()
                );
            }
            catch (Exception)
            {
                return new ApiResponse<SystemHealthDTO>(
                    ResponseType.Error,
                    "Error retrieving system health",
                    new SystemHealthDTO()
                );
            }
        }

        // 2. KPIs
        public async Task<ApiResponse<KPIDTO>> GetKpisAsync()
        {
            try
            {
                using var connection = _dbConnectionFactory.GetConnection();

                const string query = @"
                    SELECT 

                        -- ✅ Completion Rate (MTD - PERSON BASED)
                        (
                            SELECT 
                                COUNT(DISTINCT CASE 
                                    WHEN contact_status = 'Contacted'
                                     AND (
                                            response_type != 'No Response'
                                            OR next_action = 'Mark Unresponsive'
                                         )
                                    THEN person_id
                                END) * 100.0 
                                / NULLIF(COUNT(DISTINCT person_id), 0)
                            FROM follow_ups
                            WHERE attempt_date >= DATE_FORMAT(CURDATE(), '%Y-%m-01')
                              AND attempt_date < DATE_FORMAT(CURDATE() + INTERVAL 1 MONTH, '%Y-%m-01')
                        ) as CompletionRateCurrent,

                        -- ✅ Completion Rate (Last Month)
                        (
                            SELECT 
                                COUNT(DISTINCT CASE 
                                    WHEN contact_status = 'Contacted'
                                     AND (
                                            response_type != 'No Response'
                                            OR next_action = 'Mark Unresponsive'
                                         )
                                    THEN person_id
                                END) * 100.0 
                                / NULLIF(COUNT(DISTINCT person_id), 0)
                            FROM follow_ups
                            WHERE attempt_date >= DATE_FORMAT(DATE_SUB(CURDATE(), INTERVAL 1 MONTH), '%Y-%m-01')
                              AND attempt_date < DATE_FORMAT(CURDATE(), '%Y-%m-01')
                        ) as CompletionRateLast,

                        -- ✅ First Contact <48h (correct)
                        (
                            SELECT 
                                SUM(CASE 
                                    WHEN TIMESTAMPDIFF(HOUR, p.assigned_date, f.attempt_date) <= 48 THEN 1 
                                    ELSE 0 
                                END) * 100.0 
                                / NULLIF(COUNT(*), 0)
                            FROM follow_ups f
                            JOIN people p ON f.person_id = p.person_id
                            WHERE f.attempt_number = 1
                              AND f.attempt_date >= DATE_FORMAT(CURDATE(), '%Y-%m-01')
                              AND f.attempt_date < DATE_FORMAT(CURDATE() + INTERVAL 1 MONTH, '%Y-%m-01')
                        ) as FirstContact48h,

                        -- ✅ Escalation Rate (MTD)
                       (
                        SELECT 
                            ROUND(
                                COUNT(*) * 100.0 /
                                NULLIF((
                                    SELECT COUNT(*) 
                                    FROM follow_ups 
                                    WHERE attempt_date >= DATE_FORMAT(CURDATE(), '%Y-%m-01')
                                      AND attempt_date < DATE_FORMAT(CURDATE() + INTERVAL 1 MONTH, '%Y-%m-01')
                                ), 0)
                            )
                        FROM escalations
                        WHERE escalation_date >= DATE_FORMAT(CURDATE(), '%Y-%m-01')
                          AND escalation_date < DATE_FORMAT(CURDATE() + INTERVAL 1 MONTH, '%Y-%m-01')
                    ) AS EscalationRate,

                        -- ✅ Crisis Handled Safely
                        (
                            SELECT 
                                SUM(CASE 
                                    WHEN crisis_protocol_followed = TRUE THEN 1 
                                    ELSE 0 
                                END) * 100.0 
                                / NULLIF(COUNT(*), 0)
                            FROM escalations
                            WHERE escalation_tier = 'Emergency'
                              AND escalation_date >= DATE_FORMAT(CURDATE(), '%Y-%m-01')
                              AND escalation_date < DATE_FORMAT(CURDATE() + INTERVAL 1 MONTH, '%Y-%m-01')
                        ) as CrisisHandledSafely
                    ";

                var result = await connection.QueryFirstOrDefaultAsync<KPIDTO>(query);

                return new ApiResponse<KPIDTO>(
                    result != null ? ResponseType.Success : ResponseType.Warning,
                    result != null ? "KPI data retrieved successfully" : "No data found",
                    result ?? new KPIDTO()
                );
            }
            catch
            {
                return new ApiResponse<KPIDTO>(
                    ResponseType.Error,
                    "Error retrieving KPI data",
                    new KPIDTO()
                );
            }
        }

        // 3. TEAM LEAD PERFORMANCE
        public async Task<ApiResponse<List<TeamLeadPerformanceDTO>>> GetTeamLeadPerformanceAsync()
        {
            try
            {
                using var connection = _dbConnectionFactory.GetConnection();

                const string query = @"
SELECT 
    tl.team_lead_id AS TeamLeadId,
    CONCAT(tl.first_name, ' ', tl.last_name) AS TeamLeadName,
    tl.current_volunteers AS TeamSize,

    -- ✅ Completion Rate (PERSON-BASED LOGIC)
    (
        SELECT 
            ROUND(
                COUNT(DISTINCT CASE 
                    WHEN f.contact_status = 'Contacted'
                     AND (
                            f.response_type != 'No Response'
                            OR f.next_action = 'Mark Unresponsive'
                         )
                    THEN f.person_id
                END) * 100.0 
                / NULLIF(COUNT(DISTINCT f.person_id), 0),
                2
            )
        FROM follow_ups f
        JOIN volunteers v ON f.volunteer_id = v.volunteer_id
        WHERE v.team_lead = tl.team_lead_id
          AND f.attempt_date >= DATE_FORMAT(CURDATE(), '%Y-%m-01')
          AND f.attempt_date < DATE_FORMAT(CURDATE() + INTERVAL 1 MONTH, '%Y-%m-01')
    ) AS CompletionRate,

    -- ✅ Team vNPS (NULL SAFE)
    (
        SELECT IFNULL(ROUND(AVG(vn.vnps_score), 0), 0)
        FROM vnps_surveys vn
        JOIN volunteers v ON vn.volunteer_id = v.volunteer_id
        WHERE v.team_lead = tl.team_lead_id
    ) AS TeamVNPS,

    -- ✅ Retention Rate (SAFE DIVISION)
    (
        SELECT 
            ROUND(
                COUNT(CASE WHEN v.status = 'Active' THEN 1 END) * 100.0 
                / NULLIF(COUNT(*), 0),
                2
            )
        FROM volunteers v
        WHERE v.team_lead = tl.team_lead_id
    ) AS RetentionRate

FROM team_leads tl
WHERE tl.status = 'Active';
";

                var result = (await connection.QueryAsync<TeamLeadPerformanceDTO>(query)).ToList();

                return new ApiResponse<List<TeamLeadPerformanceDTO>>(
                    result.Any() ? ResponseType.Success : ResponseType.Warning,
                    result.Any() ? "Team lead performance retrieved" : "No data found",
                    result
                );
            }
            catch (Exception)
            {
                return new ApiResponse<List<TeamLeadPerformanceDTO>>(
                    ResponseType.Error,
                    "Error retrieving team lead performance",
                    new List<TeamLeadPerformanceDTO>()
                );
            }
        }

        // 4. PIPELINE HEALTH
        public async Task<ApiResponse<List<PipelineHealthDTO>>> GetPipelineHealthAsync()
        {
            try
            {
                using var connection = _dbConnectionFactory.GetConnection();

                const string query = @"
                    SELECT 
                        follow_up_status as FollowUpStatus,
                        COUNT(*) as Count,
                        AVG(DATEDIFF(CURDATE(), created_at)) as AvgDaysInStage
                    FROM people
                    GROUP BY follow_up_status
                ";

                var result = (await connection.QueryAsync<PipelineHealthDTO>(query)).ToList();

                return new ApiResponse<List<PipelineHealthDTO>>(
                    result.Any() ? ResponseType.Success : ResponseType.Warning,
                    "Pipeline health retrieved",
                    result
                );
            }
            catch (Exception)
            {
                return new ApiResponse<List<PipelineHealthDTO>>(
                    ResponseType.Error,
                    "Error retrieving pipeline health",
                    new List<PipelineHealthDTO>()
                );
            }
        }

        // 5. ESCALATIONS
        public async Task<ApiResponse<EscalationSummaryDTO>> GetEscalationsAsync()
        {
            try
            {
                using var connection = _dbConnectionFactory.GetConnection();

                const string query = @"
            SELECT 
                -- Totals
                COUNT(*) as TotalEscalations,
                SUM(CASE WHEN escalation_tier = 'Standard' THEN 1 ELSE 0 END) as StandardCount,
                SUM(CASE WHEN escalation_tier = 'Urgent' THEN 1 ELSE 0 END) as UrgentCount,
                SUM(CASE WHEN escalation_tier = 'Emergency' THEN 1 ELSE 0 END) as EmergencyCount,

                -- Resolution Times (days)
                AVG(CASE 
                    WHEN escalation_tier = 'Standard' 
                    THEN TIMESTAMPDIFF(HOUR, escalation_date, COALESCE(resolved_date, NOW())) / 24 
                END) as AvgResolutionStandard,

                AVG(CASE 
                    WHEN escalation_tier = 'Urgent' 
                    THEN TIMESTAMPDIFF(HOUR, escalation_date, COALESCE(resolved_date, NOW())) / 24 
                END) as AvgResolutionUrgent,

                -- Pending
                SUM(CASE 
                    WHEN status IN ('New','In Progress') AND escalation_tier = 'Standard' 
                    THEN 1 ELSE 0 
                END) as PendingStandard,

                SUM(CASE 
                    WHEN status IN ('New','In Progress') AND escalation_tier = 'Urgent' 
                    THEN 1 ELSE 0 
                END) as PendingUrgent,

                SUM(CASE 
                    WHEN status IN ('New','In Progress') AND escalation_tier = 'Emergency' 
                    THEN 1 ELSE 0 
                END) as PendingEmergency

            FROM escalations
            WHERE escalation_date >= DATE_FORMAT(CURDATE(), '%Y-%m-01')
              AND escalation_date < DATE_FORMAT(CURDATE() + INTERVAL 1 MONTH, '%Y-%m-01');
        ";

                var result = await connection.QueryFirstOrDefaultAsync<EscalationSummaryDTO>(query);

                return new ApiResponse<EscalationSummaryDTO>(
                    ResponseType.Success,
                    "Escalation summary retrieved",
                    result ?? new EscalationSummaryDTO()
                );
            }
            catch (Exception)
            {
                return new ApiResponse<EscalationSummaryDTO>(
                    ResponseType.Error,
                    "Error retrieving escalations",
                    new EscalationSummaryDTO()
                );
            }
        }

        // 6. TOP ESCALATION REASONS       
        public async Task<ApiResponse<List<EscalationReasonDTO>>> GetTopEscalationReasonsAsync()
        {
            try
            {
                using var connection = _dbConnectionFactory.GetConnection();

                const string query = @"
            SELECT 
                escalation_reason as Reason, 
                COUNT(*) as Count
            FROM escalations
            WHERE escalation_date >= DATE_FORMAT(CURDATE(), '%Y-%m-01')
              AND escalation_date < DATE_FORMAT(CURDATE() + INTERVAL 1 MONTH, '%Y-%m-01')
            GROUP BY escalation_reason
            ORDER BY Count DESC
            LIMIT 5;
        ";

                var result = (await connection.QueryAsync<EscalationReasonDTO>(query)).ToList();

                return new ApiResponse<List<EscalationReasonDTO>>(
                    ResponseType.Success,
                    "Top escalation reasons retrieved",
                    result
                );
            }
            catch (Exception)
            {
                return new ApiResponse<List<EscalationReasonDTO>>(
                    ResponseType.Error,
                    "Error retrieving escalation reasons",
                    new List<EscalationReasonDTO>()
                );
            }
        }

        // 7. TRENDS
        public async Task<ApiResponse<List<TrendDTO>>> GetTrendsAsync()
        {
            try
            {
                using var connection = _dbConnectionFactory.GetConnection();

                const string query = @"
            SELECT 
                DATE_FORMAT(month_date, '%b') as MonthName,

                -- First-Time Visitors
                (
                    SELECT COUNT(*) 
                    FROM people 
                    WHERE visit_type = 'First-Time Visitor'
                      AND DATE_FORMAT(first_visit_date, '%Y-%m') = DATE_FORMAT(month_date, '%Y-%m')
                ) as Visitors,

                -- Completion Rate 
                (
                    SELECT 
                        COUNT(DISTINCT CASE 
                            WHEN contact_status = 'Contacted'
                             AND (
                                    response_type != 'No Response'
                                    OR next_action = 'Mark Unresponsive'
                                 )
                            THEN person_id
                        END) * 100.0
                        / NULLIF(COUNT(DISTINCT person_id), 0)
                    FROM follow_ups
                    WHERE attempt_date >= DATE_FORMAT(month_date, '%Y-%m-01')
                      AND attempt_date < DATE_FORMAT(DATE_ADD(month_date, INTERVAL 1 MONTH), '%Y-%m-01')
                ) as CompletionRate,

                -- vNPS
                (
                    SELECT IFNULL(ROUND(AVG(vnps_score),0),0)
                    FROM vnps_surveys 
                    WHERE quarter = CONCAT('Q', QUARTER(month_date))
                      AND year = YEAR(month_date)
                ) as VNPS,

                -- Volunteer Count
                (
                    SELECT COUNT(*) 
                    FROM volunteers 
                    WHERE status = 'Active'
                      AND start_date <= LAST_DAY(month_date)
                      AND (end_date IS NULL OR end_date > LAST_DAY(month_date))
                ) as VolunteerCount,

                -- Crisis Cases
                (
                    SELECT COUNT(*) 
                    FROM escalations 
                    WHERE escalation_tier = 'Emergency'
                      AND escalation_date >= DATE_FORMAT(month_date, '%Y-%m-01')
                      AND escalation_date < DATE_FORMAT(DATE_ADD(month_date, INTERVAL 1 MONTH), '%Y-%m-01')
                ) as CrisisCount,

                -- Turnover
                (
                    SELECT COUNT(*) 
                    FROM volunteers 
                    WHERE status = 'Exited'
                      AND end_date >= DATE_FORMAT(month_date, '%Y-%m-01')
                      AND end_date < DATE_FORMAT(DATE_ADD(month_date, INTERVAL 1 MONTH), '%Y-%m-01')
                ) as TurnoverCount

            FROM (
                SELECT DATE_SUB(CURDATE(), INTERVAL 2 MONTH) as month_date
                UNION
                SELECT DATE_SUB(CURDATE(), INTERVAL 1 MONTH)
                UNION
                SELECT CURDATE()
            ) months
            ORDER BY month_date;
        ";

                var result = (await connection.QueryAsync<TrendDTO>(query)).ToList();

                return new ApiResponse<List<TrendDTO>>(
                    ResponseType.Success,
                    "Trends retrieved",
                    result
                );
            }
            catch (Exception)
            {
                return new ApiResponse<List<TrendDTO>>(
                    ResponseType.Error,
                    "Error retrieving trends",
                    new List<TrendDTO>()
                );
            }
        }

        public async Task<ApiResponse<ImpactDTO>> GetImpactAsync()
        {
            try
            {
                using var connection = _dbConnectionFactory.GetConnection();

                const string query = @"
            SELECT 
                -- Total Conversations (ONLY CONTACTED)
                COUNT(*) as TotalConversations,

                -- Small Group Connections
                (
                    SELECT COUNT(*) 
                    FROM notes 
                    WHERE entity_type = 'Follow-Up'
                      AND (
                            note_text LIKE '%small group%' 
                            OR tags LIKE '%small-group%'
                          )
                      AND created_at >= DATE_FORMAT(CURDATE(), '%Y-%m-01')
                      AND created_at < DATE_FORMAT(CURDATE() + INTERVAL 1 MONTH, '%Y-%m-01')
                ) as SmallGroupConnections,

                -- Prayer Requests
                (
                    SELECT COUNT(*) 
                    FROM people 
                    WHERE prayer_requests IS NOT NULL 
                      AND prayer_requests != ''
                      AND first_visit_date >= DATE_FORMAT(CURDATE(), '%Y-%m-01')
                      AND first_visit_date < DATE_FORMAT(CURDATE() + INTERVAL 1 MONTH, '%Y-%m-01')
                ) as PrayerCount,

                -- Benevolence Connections
                (
                    SELECT COUNT(*) 
                    FROM escalations 
                    WHERE outcome = 'Benevolence Provided'
                      AND escalation_date >= DATE_FORMAT(CURDATE(), '%Y-%m-01')
                      AND escalation_date < DATE_FORMAT(CURDATE() + INTERVAL 1 MONTH, '%Y-%m-01')
                ) as BenevolenceCount,

                -- Counseling Referrals
                (
                    SELECT COUNT(*) 
                    FROM escalations 
                    WHERE outcome = 'Counseling Referral'
                      AND escalation_date >= DATE_FORMAT(CURDATE(), '%Y-%m-01')
                      AND escalation_date < DATE_FORMAT(CURDATE() + INTERVAL 1 MONTH, '%Y-%m-01')
                ) as CounselingCount,

                -- Serve Team Connections
                (
                    SELECT COUNT(*) 
                    FROM notes 
                    WHERE entity_type = 'Follow-Up'
                      AND (
                            note_text LIKE '%serving%' 
                            OR tags LIKE '%serve%'
                          )
                      AND created_at >= DATE_FORMAT(CURDATE(), '%Y-%m-01')
                      AND created_at < DATE_FORMAT(CURDATE() + INTERVAL 1 MONTH, '%Y-%m-01')
                ) as ServeConnections

            FROM follow_ups
            WHERE contact_status = 'Contacted'
              AND attempt_date >= DATE_FORMAT(CURDATE(), '%Y-%m-01')
              AND attempt_date < DATE_FORMAT(CURDATE() + INTERVAL 1 MONTH, '%Y-%m-01');
        ";

                var result = await connection.QueryFirstOrDefaultAsync<ImpactDTO>(query);

                return new ApiResponse<ImpactDTO>(
                    ResponseType.Success,
                    "Impact data retrieved",
                    result ?? new ImpactDTO()
                );
            }
            catch (Exception)
            {
                return new ApiResponse<ImpactDTO>(
                    ResponseType.Error,
                    "Error retrieving impact data",
                    new ImpactDTO()
                );
            }
        }

        // 9. DEVELOPMENT PIPELINE
        public async Task<ApiResponse<DevelopmentPipelineDTO>> GetDevelopmentPipelineAsync()
        {
            try
            {
                using var connection = _dbConnectionFactory.GetConnection();

                const string query = @"
                                    SELECT 
                                        -- Level 0
                                        (SELECT COUNT(*) 
                                         FROM volunteers 
                                         WHERE level = 'Level 0') as Level0,

                                        -- Level 1 Active
                                        (SELECT COUNT(*) 
                                         FROM volunteers 
                                         WHERE level = 'Level 1' 
                                           AND status = 'Active') as Level1Active,

                                        -- Level 1 Care Path
                                        (SELECT COUNT(*) 
                                         FROM volunteers 
                                         WHERE level = 'Level 1' 
                                           AND status = 'Care Path') as Level1CarePath,

                                        -- Promotion Ready
                                        (SELECT COUNT(*) 
                                         FROM volunteers 
                                         WHERE level = 'Level 1'
                                           AND status = 'Active'
                                           AND DATEDIFF(CURDATE(), start_date) >= 180
                                           AND completion_rate >= 85
                                        ) as PromotionReady,

                                        -- Level 2
                                        (SELECT COUNT(*) 
                                         FROM volunteers 
                                         WHERE level = 'Level 2' 
                                           AND status = 'Active') as Level2
                                ";

                var result = await connection.QueryFirstOrDefaultAsync<DevelopmentPipelineDTO>(query);

                return new ApiResponse<DevelopmentPipelineDTO>(
                    ResponseType.Success,
                    "Development pipeline retrieved",
                    result ?? new DevelopmentPipelineDTO()
                );
            }
            catch (Exception)
            {
                return new ApiResponse<DevelopmentPipelineDTO>(
                    ResponseType.Error,
                    "Error retrieving development pipeline",
                    new DevelopmentPipelineDTO()
                );
            }
        }

        // 10. CRISIS ESCALATIONS PENDING (PASTOR VIEW)
        public async Task<ApiResponse<List<EscalationPendingDTO>>> GetPastorCrisisEscalationsPendingAsync()
        {
            try
            {
                using var connection = _dbConnectionFactory.GetConnection();

                const string query = @"
                    SELECT 
                        e.escalation_id AS EscalationId,
                        e.escalation_date AS EscalationDate,
                        e.escalation_tier AS EscalationTier,
                        e.escalation_reason AS EscalationReason,
                        CONCAT(p.first_name, ' ', p.last_name) AS PersonName,
                        CONCAT(v.first_name, ' ', v.last_name) AS VolunteerName,
                        e.status AS Status,
                        e.assigned_to AS AssignedTo,
                        DATEDIFF(CURRENT_DATE, e.escalation_date) AS DaysPending,
                        e.description AS Description
                    FROM escalations e
                    JOIN people p ON e.person_id = p.person_id
                    JOIN volunteers v ON e.volunteer_id = v.volunteer_id
                    WHERE e.status IN ('New', 'In Progress')
                      AND e.escalation_tier = 'Emergency'
                      AND (e.assigned_to IS NULL OR e.assigned_to != 'TeamLead')
                    ORDER BY e.escalation_date DESC;
                ";

                var result = (await connection.QueryAsync<EscalationPendingDTO>(query)).ToList();

                return new ApiResponse<List<EscalationPendingDTO>>(
                    result.Any() ? ResponseType.Success : ResponseType.Warning,
                    result.Any() ? "Crisis escalations retrieved successfully" : "No pending crisis escalations found",
                    result
                );
            }
            catch (Exception ex)
            {
                return new ApiResponse<List<EscalationPendingDTO>>(
                    ResponseType.Error,
                    $"Error retrieving crisis escalations: {ex.Message}",
                    new List<EscalationPendingDTO>()
                );
            }
        }

        // 11. RE-ESCALATE TO TEAM LEAD
        public async Task<ApiResponse<bool>> ReEscalateToTeamLeadAsync(string escalationId, string notes)
        {
            try
            {
                using var connection = _dbConnectionFactory.GetConnection();
                const string query = @"
                    UPDATE escalations
                    SET assigned_to = 'TeamLead',
                        resolution_notes = CONCAT(IFNULL(resolution_notes, ''), '\n[Pastor Note]: ', @Notes),
                        status = 'In Progress',
                        updated_at = NOW()
                    WHERE escalation_id = @EscalationId;
                ";

                var rows = await connection.ExecuteAsync(query, new { EscalationId = escalationId, Notes = notes });

                return new ApiResponse<bool>(
                    rows > 0 ? ResponseType.Success : ResponseType.Warning,
                    rows > 0 ? "Escalation successfully returned to Team Lead" : "Escalation not found or no changes made",
                    rows > 0
                );
            }
            catch (Exception ex)
            {
                return new ApiResponse<bool>(
                    ResponseType.Error,
                    $"Error re-escalating to Team Lead: {ex.Message}",
                    false
                );
            }
        }


        public async Task<ApiResponse<List<PastorPendingAssignmentDto>>> PastorPendingCrisisEscalations()
        {
            try
            {
                using (var connection = _dbConnectionFactory.GetConnection())
                {
                    const string query = @"SELECT
    COUNT(*) AS PendingAssignmentsCount,

    GROUP_CONCAT(
        CONCAT(
            '🚨 ',
            p.last_name, ' ', p.first_name,
            ' 📞 ', p.phone,
            '\n   ➜ ', e.description
        )
        SEPARATOR '\n\n'
    ) AS Description,

    CONCAT(u.last_name, ' ', u.first_name) AS PastorName,
    u.telegram_chat_id AS TelegramChatId

FROM users u

CROSS JOIN escalations e

INNER JOIN people p
    ON p.person_id = e.person_id

WHERE u.role_type = 'Pastor'
  AND u.status = 'Active'
  AND e.status IN ('NEW', 'In Progress')
  AND e.escalation_reason = 'Crisis'

GROUP BY u.user_id;
";

                    var volunteers = await connection
                        .QueryAsync<PastorPendingAssignmentDto>(query);

                    var list = volunteers?.ToList()
                               ?? new List<PastorPendingAssignmentDto>();

                    return new ApiResponse<List<PastorPendingAssignmentDto>>(
                        ResponseType.Success,
                        "Pending assignment volunteers retrieved successfully",
                        list
                    );
                }
            }
            catch (Exception ex)
            {
                return new ApiResponse<List<PastorPendingAssignmentDto>>(
                    ResponseType.Error,
                    $"Error retrieving volunteers: {ex.Message}",
                    null
                );
            }
        }



    }
}