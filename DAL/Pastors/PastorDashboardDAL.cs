using Dapper;
using RM_CMS.Data;
using RM_CMS.Data.DTO;
using RM_CMS.Data.DTO.Pastors;
using RM_CMS.Data.DTO.TeamLeads;
using RM_CMS.Utilities;

namespace RM_CMS.DAL.Pastors
{
    public interface IPastorDashboardDAL
    {
        Task<ApiResponse<SystemHealthDTO>> GetSystemHealthAsync();
        Task<ApiResponse<KPIsDataDTO>> GetKPIsAsync();
        Task<ApiResponse<List<TeamLeadPerformanceDTO>>> GetTeamLeadPerformanceAsync();
        Task<ApiResponse<PipelineHealthDTO>> GetPipelineHealthAsync();
        Task<ApiResponse<EscalationMetricsDTO>> GetEscalationMetricsAsync();
        Task<ApiResponse<List<TrendDTO>>> GetTrendsAsync();

    }

    public class PastorDashboardDAL : IPastorDashboardDAL
    {
        private readonly IDbConnectionFactory _dbConnectionFactory;

        public PastorDashboardDAL(IDbConnectionFactory dbConnectionFactory)
        {
            _dbConnectionFactory = dbConnectionFactory;
        }

        public async Task<ApiResponse<SystemHealthDTO>> GetSystemHealthAsync()
        {
            try
            {
                using (var connection = _dbConnectionFactory.GetConnection())
                {
                    // Query to get all system health metrics
                    const string query = @"
                    SELECT 
                        -- Active Volunteers
                        (SELECT COUNT(*) FROM volunteers WHERE status = 'Active') as active_volunteers,

                        -- Active Team Leads
                        (SELECT COUNT(*) FROM team_leads WHERE status = 'Active') as active_team_leads,

                        -- First-Time Visitors MTD
                        (SELECT COUNT(*) FROM people 
                         WHERE visit_type = 'First-Time Visitor'
                           AND DATE_FORMAT(first_visit_date, '%Y-%m') = DATE_FORMAT(CURDATE(), '%Y-%m')
                        ) as first_time_visitors_mtd,

                        -- Follow-Ups Completed MTD
                        (SELECT COUNT(*) FROM follow_ups 
                         WHERE contact_status = 'Contacted'
                           AND DATE_FORMAT(attempt_date, '%Y-%m') = DATE_FORMAT(CURDATE(), '%Y-%m')
                        ) as follow_ups_completed_mtd,

                        -- System vNPS (average - using vnps_score from volunteer table)
                        COALESCE((SELECT AVG(vnps_score) FROM volunteers WHERE vnps_score IS NOT NULL), 0) as system_vnps,

                        -- Volunteer Retention (% of volunteers from 3 months ago still active)
                        COALESCE((SELECT 
                            (COUNT(CASE WHEN status = 'Active' THEN 1 END) * 100.0 / NULLIF(COUNT(*), 0))
                         FROM volunteers
                         WHERE start_date <= DATE_SUB(CURDATE(), INTERVAL 3 MONTH)), 0) as volunteer_retention,

                        -- Completion Rate MTD
                        COALESCE((SELECT 
                            (SUM(CASE WHEN contact_status = 'Contacted' THEN 1 ELSE 0 END) * 100.0 / NULLIF(COUNT(*), 0))
                         FROM follow_ups
                         WHERE DATE_FORMAT(attempt_date, '%Y-%m') = DATE_FORMAT(CURDATE(), '%Y-%m')), 0) as completion_rate_mtd
                ";

                    var result = await connection.QueryFirstOrDefaultAsync<SystemHealthDTO>(query);

                    if (result == null)
                        result = new SystemHealthDTO();

                    return new ApiResponse<SystemHealthDTO>(
                        ResponseType.Success,
                        "System health metrics retrieved successfully",
                        result
                    );
                }
            }
            catch (Exception ex)
            {
                return new ApiResponse<SystemHealthDTO>(
                    ResponseType.Error,
                    $"Error retrieving system health: {ex.Message}",
                    null
                );
            }
        }

        public async Task<ApiResponse<KPIsDataDTO>> GetKPIsAsync()
        {
            try
            {
                using (var connection = _dbConnectionFactory.GetConnection())
                {
                    const string query = @"
                SELECT 
                    -- Current Month Completion Rate
                    COALESCE((
                        SELECT 
                            (SUM(CASE WHEN contact_status = 'Contacted' THEN 1 ELSE 0 END) * 100.0 / NULLIF(COUNT(*),0))
                        FROM follow_ups
                        WHERE DATE_FORMAT(attempt_date, '%Y-%m') = DATE_FORMAT(CURDATE(), '%Y-%m')
                    ), 0) as completion_rate_current,

                    -- Last Month Completion Rate
                    COALESCE((
                        SELECT 
                            (SUM(CASE WHEN contact_status = 'Contacted' THEN 1 ELSE 0 END) * 100.0 / NULLIF(COUNT(*),0))
                        FROM follow_ups
                        WHERE DATE_FORMAT(attempt_date, '%Y-%m') = DATE_FORMAT(DATE_SUB(CURDATE(), INTERVAL 1 MONTH), '%Y-%m')
                    ), 0) as completion_rate_last,

                    -- First Contact <48h
                    COALESCE((
                        SELECT 
                            (SUM(CASE 
                                WHEN TIMESTAMPDIFF(HOUR, p.assigned_date, f.attempt_date) <= 48 
                                THEN 1 ELSE 0 END) * 100.0 / NULLIF(COUNT(*),0))
                        FROM follow_ups f
                        JOIN people p ON f.person_id = p.person_id
                        WHERE f.attempt_number = 1
                          AND DATE_FORMAT(f.attempt_date, '%Y-%m') = DATE_FORMAT(CURDATE(), '%Y-%m')
                    ), 0) as first_contact_48h,

                    -- Escalation Rate
                    COALESCE((
                        SELECT 
                            (COUNT(*) * 100.0 / NULLIF((
                                SELECT COUNT(*) FROM follow_ups 
                                WHERE DATE_FORMAT(attempt_date, '%Y-%m') = DATE_FORMAT(CURDATE(), '%Y-%m')
                            ),0))
                        FROM escalations
                        WHERE DATE_FORMAT(escalation_date, '%Y-%m') = DATE_FORMAT(CURDATE(), '%Y-%m')
                    ), 0) as escalation_rate,

                    -- Crisis handled safely
                    COALESCE((
                        SELECT 
                            (SUM(CASE WHEN crisis_protocol_followed = TRUE THEN 1 ELSE 0 END) * 100.0 / NULLIF(COUNT(*),0))
                        FROM escalations
                        WHERE escalation_tier = 'Emergency'
                          AND DATE_FORMAT(escalation_date, '%Y-%m') = DATE_FORMAT(CURDATE(), '%Y-%m')
                    ), 0) as crisis_handled_safely
            ";

                    var result = await connection.QueryFirstOrDefaultAsync<KPIsDataDTO>(query);

                    if (result == null)
                        result = new KPIsDataDTO();

                    return new ApiResponse<KPIsDataDTO>(
                        ResponseType.Success,
                        "KPIs retrieved successfully",
                        result
                    );
                }
            }
            catch (Exception)
            {
                return new ApiResponse<KPIsDataDTO>(
                    ResponseType.Error,
                    "Error retrieving KPIs",
                    null
                );
            }
        }
        public async Task<ApiResponse<List<TeamLeadPerformanceDTO>>> GetTeamLeadPerformanceAsync()
        {
            try
            {
                using (var connection = _dbConnectionFactory.GetConnection())
                {
                    const string query = @"
                SELECT 
                    tl.team_lead_id,
                    CONCAT(tl.first_name, ' ', tl.last_name) as team_lead_name,
                    tl.current_volunteers as team_size,

                    -- Completion Rate
                    COALESCE((
                        SELECT 
                            (SUM(CASE WHEN f.contact_status = 'Contacted' THEN 1 ELSE 0 END) * 100.0 / NULLIF(COUNT(*),0))
                        FROM follow_ups f
                        JOIN volunteers v ON f.volunteer_id = v.volunteer_id
                        WHERE v.team_lead = tl.team_lead_id
                          AND DATE_FORMAT(f.attempt_date, '%Y-%m') = DATE_FORMAT(CURDATE(), '%Y-%m')
                    ),0) as completion_rate,

                    -- Team vNPS
                    COALESCE((
                        SELECT AVG(vn.vnps_score)
                        FROM vnps_surveys vn
                        JOIN volunteers v ON vn.volunteer_id = v.volunteer_id
                        WHERE v.team_lead = tl.team_lead_id
                          AND vn.quarter = CONCAT('Q', QUARTER(CURDATE()))
                          AND vn.year = YEAR(CURDATE())
                    ),0) as team_vnps,

                    -- Retention
                    COALESCE((
                        SELECT 
                            (COUNT(CASE WHEN v.status = 'Active' THEN 1 END) * 100.0 / NULLIF(COUNT(*),0))
                        FROM volunteers v
                        WHERE v.team_lead = tl.team_lead_id
                          AND v.start_date <= DATE_SUB(CURDATE(), INTERVAL 3 MONTH)
                    ),0) as retention_rate

                FROM team_leads tl
                WHERE tl.status = 'Active'
                ORDER BY completion_rate DESC;
            ";

                    var result = (await connection.QueryAsync<TeamLeadPerformanceDTO>(query)).ToList();

                    // Flag logic
                    foreach (var tl in result)
                    {
                        int belowTargetCount =
                            (tl.completion_rate < 85 ? 1 : 0) +
                            (tl.team_vnps < 50 ? 1 : 0) +
                            (tl.retention_rate < 90 ? 1 : 0);

                        tl.below_target_count = belowTargetCount;

                        if (belowTargetCount >= 2) tl.flag = "🔴";
                        else if (belowTargetCount == 1) tl.flag = "🟡";
                        else tl.flag = "🟢";
                    }

                    return new ApiResponse<List<TeamLeadPerformanceDTO>>(
                        ResponseType.Success,
                        "Team lead performance retrieved",
                        result
                    );
                }
            }
            catch (Exception)
            {
                return new ApiResponse<List<TeamLeadPerformanceDTO>>(
                    ResponseType.Error,
                    "Error retrieving team lead performance",
                    null
                );
            }
        }

        public async Task<ApiResponse<PipelineHealthDTO>> GetPipelineHealthAsync()
        {
            try
            {
                using (var connection = _dbConnectionFactory.GetConnection())
                {
                    const string query = @"
                SELECT 
                    follow_up_status,
                    COUNT(*) as count,
                    AVG(DATEDIFF(CURDATE(), 
                        CASE 
                            WHEN follow_up_status = 'NEW' THEN created_at
                            WHEN follow_up_status = 'ASSIGNED' THEN assigned_date
                            ELSE last_contact_date
                        END
                    )) as avg_days_in_stage
                FROM people
                WHERE DATE_FORMAT(first_visit_date, '%Y-%m') = DATE_FORMAT(CURDATE(), '%Y-%m')
                GROUP BY follow_up_status;
            ";

                    var data = (await connection.QueryAsync<PipelineStageDTO>(query)).ToList();

                    var totalVisitors = data.Sum(x => x.count);

                    foreach (var stage in data)
                    {
                        stage.percentage = totalVisitors == 0 ? 0 :
                            Math.Round((stage.count * 100.0) / totalVisitors, 0);
                    }

                    // Success rate (COMPLETE)
                    var contacted = data.FirstOrDefault(x => x.follow_up_status == "COMPLETE")?.count ?? 0;
                    var successRate = totalVisitors == 0 ? 0 :
                        Math.Round((contacted * 100.0) / totalVisitors, 0);

                    var result = new PipelineHealthDTO
                    {
                        Stages = data,
                        SuccessRate = successRate
                    };

                    return new ApiResponse<PipelineHealthDTO>(
                        ResponseType.Success,
                        "Pipeline health retrieved",
                        result
                    );
                }
            }
            catch (Exception)
            {
                return new ApiResponse<PipelineHealthDTO>(
                    ResponseType.Error,
                    "Error retrieving pipeline health",
                    null
                );
            }
        }

        public async Task<ApiResponse<EscalationMetricsDTO>> GetEscalationMetricsAsync()
        {
            try
            {
                using (var connection = _dbConnectionFactory.GetConnection())
                {
                    // 1. Main Escalation Metrics
                    const string mainQuery = @"
                SELECT 
                    COUNT(*) as total_escalations,

                    SUM(CASE WHEN escalation_tier = 'Standard' THEN 1 ELSE 0 END) as standard_count,
                    SUM(CASE WHEN escalation_tier = 'Urgent' THEN 1 ELSE 0 END) as urgent_count,
                    SUM(CASE WHEN escalation_tier = 'Emergency' THEN 1 ELSE 0 END) as emergency_count,

                    AVG(CASE WHEN escalation_tier = 'Standard' 
                        THEN TIMESTAMPDIFF(HOUR, escalation_date, COALESCE(resolved_date, CURDATE())) / 24 
                    END) as avg_resolution_standard,

                    AVG(CASE WHEN escalation_tier = 'Urgent' 
                        THEN TIMESTAMPDIFF(HOUR, escalation_date, COALESCE(resolved_date, CURDATE())) / 24 
                    END) as avg_resolution_urgent,

                    SUM(CASE WHEN status IN ('New','In Progress') AND escalation_tier = 'Standard' THEN 1 ELSE 0 END) as pending_standard,
                    SUM(CASE WHEN status IN ('New','In Progress') AND escalation_tier = 'Urgent' THEN 1 ELSE 0 END) as pending_urgent,
                    SUM(CASE WHEN status IN ('New','In Progress') AND escalation_tier = 'Emergency' THEN 1 ELSE 0 END) as pending_emergency

                FROM escalations
                WHERE DATE_FORMAT(escalation_date, '%Y-%m') = DATE_FORMAT(CURDATE(), '%Y-%m');
            ";

                    var metrics = await connection.QueryFirstOrDefaultAsync<EscalationMetricsDTO>(mainQuery);

                    if (metrics == null)
                        metrics = new EscalationMetricsDTO();

                    // 2. Top Reasons
                    const string reasonQuery = @"
                SELECT 
                    escalation_reason,
                    COUNT(*) as count
                FROM escalations
                WHERE DATE_FORMAT(escalation_date, '%Y-%m') = DATE_FORMAT(CURDATE(), '%Y-%m')
                GROUP BY escalation_reason
                ORDER BY count DESC
                LIMIT 5;
            ";

                    var reasons = (await connection.QueryAsync<EscalationReasonDTO>(reasonQuery)).ToList();

                    metrics.top_reasons = reasons;

                    return new ApiResponse<EscalationMetricsDTO>(
                        ResponseType.Success,
                        "Escalation metrics retrieved",
                        metrics
                    );
                }
            }
            catch (Exception)
            {
                return new ApiResponse<EscalationMetricsDTO>(
                    ResponseType.Error,
                    "Error retrieving escalation metrics",
                    null
                );
            }
        }
        public async Task<ApiResponse<List<TrendDTO>>> GetTrendsAsync()
        {
            try
            {
                using (var connection = _dbConnectionFactory.GetConnection())
                {
                    const string query = @"
                SELECT 
                    DATE_FORMAT(month_date, '%b') as month_name,

                    -- Visitors
                    COALESCE((
                        SELECT COUNT(*) FROM people 
                        WHERE visit_type = 'First-Time Visitor'
                          AND DATE_FORMAT(first_visit_date, '%Y-%m') = DATE_FORMAT(month_date, '%Y-%m')
                    ),0) as visitors,

                    -- Completion Rate
                    COALESCE((
                        SELECT 
                            (SUM(CASE WHEN contact_status = 'Contacted' THEN 1 ELSE 0 END) * 100.0 / NULLIF(COUNT(*),0))
                        FROM follow_ups
                        WHERE DATE_FORMAT(attempt_date, '%Y-%m') = DATE_FORMAT(month_date, '%Y-%m')
                    ),0) as completion_rate,

                    -- vNPS
                    COALESCE((
                        SELECT AVG(vnps_score) FROM vnps_surveys 
                        WHERE quarter = CONCAT('Q', QUARTER(month_date))
                          AND year = YEAR(month_date)
                    ),0) as vnps,

                    -- Volunteer Count
                    COALESCE((
                        SELECT COUNT(*) FROM volunteers 
                        WHERE status = 'Active'
                          AND start_date <= LAST_DAY(month_date)
                          AND (end_date IS NULL OR end_date > LAST_DAY(month_date))
                    ),0) as volunteer_count,

                    -- Crisis Count
                    COALESCE((
                        SELECT COUNT(*) FROM escalations 
                        WHERE escalation_tier = 'Emergency'
                          AND DATE_FORMAT(escalation_date, '%Y-%m') = DATE_FORMAT(month_date, '%Y-%m')
                    ),0) as crisis_count,

                    -- Turnover
                    COALESCE((
                        SELECT COUNT(*) FROM volunteers 
                        WHERE status = 'Exited'
                          AND DATE_FORMAT(end_date, '%Y-%m') = DATE_FORMAT(month_date, '%Y-%m')
                    ),0) as turnover_count

                FROM (
                    SELECT DATE_SUB(CURDATE(), INTERVAL 2 MONTH) as month_date
                    UNION SELECT DATE_SUB(CURDATE(), INTERVAL 1 MONTH)
                    UNION SELECT CURDATE()
                ) months
                ORDER BY month_date;
            ";

                    var result = (await connection.QueryAsync<TrendDTO>(query)).ToList();

                    return new ApiResponse<List<TrendDTO>>(
                        ResponseType.Success,
                        "Trend data retrieved",
                        result
                    );
                }
            }
            catch (Exception)
            {
                return new ApiResponse<List<TrendDTO>>(
                    ResponseType.Error,
                    "Error retrieving trends",
                    null
                );
            }
        }


    }
}
