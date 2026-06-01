using RM_CMS.Data.DTO.TeamLeads;

namespace RM_CMS.Data.DTO.Pastors
{
    public class PastorDTO
    {
        public SystemHealthDTO SystemHealth { get; set; }
        public KPIDashboardDTO KPIs { get; set; }
        public List<TeamLeadPerformanceDTO> TeamLeadPerformance { get; set; }
        public PipelineHealthSummaryDTO PipelineHealth { get; set; }
        public EscalationDashboardDTO Escalations { get; set; }
        public int CrisisEscalationsCount { get; set; }
        public List<TrendDTO> Trends { get; set; }
        public ImpactDTO Impact { get; set; }
        public DevelopmentPipelineDTO DevelopmentPipeline { get; set; }
        public AlertsDTO Alerts { get; set; }
        public List<EscalationPendingDTO> CrisisEscalationsPending { get; set; } = new List<EscalationPendingDTO>();

    }

    // SYSTEM HEALTH
    public class SystemHealthDTO
    {
        public int ActiveVolunteers { get; set; }
        public int ActiveTeamLeads { get; set; }
        public int VisitorsMTD { get; set; }
        public int FollowUpsCompletedMTD { get; set; }
        public double? SystemVNPS { get; set; }
        public double? VolunteerRetention { get; set; }
        public double? CompletionRateMTD { get; set; }
        public double? AvgResponseTimeDays { get; set; }

        public string OverallFlag { get; set; }
    }

    // KPI DASHBOARD
    public class KPIDashboardDTO
    {
        public KPIItemDTO CompletionRate { get; set; }
        public KPIItemDTO FirstContact48h { get; set; }
        public KPIItemDTO EscalationRate { get; set; }
        public KPIItemDTO CrisisHandledSafely { get; set; }
        public KPIItemDTO VolunteerRetention { get; set; }
        public KPIItemDTO SystemVNPS { get; set; }

        public int OnTargetCount { get; set; }
        public int TotalCount { get; set; }
    }

    public class KPIItemDTO
    {
        public double Current { get; set; }
        public double Target { get; set; }
        public double? Trend { get; set; }
        public string Status { get; set; }
    }

    // TEAM LEAD PERFORMANCE
    public class TeamLeadPerformanceDTO
    {
        public string TeamLeadId { get; set; }
        public string TeamLeadName { get; set; }
        public int TeamSize { get; set; }
        public double? CompletionRate { get; set; }
        public double? TeamVNPS { get; set; }
        public double? RetentionRate { get; set; }

        public string Flag { get; set; }
        public int BelowTargetCount { get; set; }
    }

    // PIPELINE HEALTH
    public class PipelineHealthDTO
    {
        public string FollowUpStatus { get; set; }
        public int Count { get; set; }
        public double? AvgDaysInStage { get; set; }
        public double Percentage { get; set; }
    }

    public class PipelineHealthSummaryDTO
    {
        public List<PipelineHealthDTO> Stages { get; set; }
        public double SuccessRate { get; set; }
    }

    // ESCALATIONS
    public class EscalationSummaryDTO
    {
        public int TotalEscalations { get; set; }
        public int StandardCount { get; set; }
        public int UrgentCount { get; set; }
        public int EmergencyCount { get; set; }

        public decimal? AvgResolutionStandard { get; set; }
        public decimal? AvgResolutionUrgent { get; set; }

        public int PendingStandard { get; set; }
        public int PendingUrgent { get; set; }
        public int PendingEmergency { get; set; }
    }

    public class EscalationReasonDTO
    {
        public string Reason { get; set; }
        public int Count { get; set; }
    }

    public class EscalationDashboardDTO
    {
        public EscalationSummaryDTO Summary { get; set; }
        public List<EscalationReasonDTO> TopReasons { get; set; }
    }

    // TRENDS
    public class TrendDTO
    {
        public string MonthName { get; set; }
        public int Visitors { get; set; }
        public decimal? CompletionRate { get; set; }
        public int VNPS { get; set; }
        public int VolunteerCount { get; set; }
        public int CrisisCount { get; set; }
        public int TurnoverCount { get; set; }
    }

    // IMPACT
    public class ImpactDTO
    {
        public int TotalConversations { get; set; }
        public int SmallGroupConnections { get; set; }
        public int PrayerCount { get; set; }
        public int BenevolenceCount { get; set; }
        public int CounselingCount { get; set; }
        public int ServeConnections { get; set; }
    }

    // DEVELOPMENT PIPELINE
    public class DevelopmentPipelineDTO
    {
        public int Level0 { get; set; }
        public int Level1Active { get; set; }
        public int Level1CarePath { get; set; }
        public int PromotionReady { get; set; }
        public int Level2 { get; set; }
    }

    // ALERTS
    public class AlertsDTO
    {
        public List<AlertItemDTO> Urgent { get; set; }
        public List<AlertItemDTO> Important { get; set; }
        public List<AlertItemDTO> Strategic { get; set; }
    }

    public class AlertItemDTO
    {
        public string Message { get; set; }
        public string Action { get; set; }
    }


    public class KPIDTO
    {
        public double? CompletionRateCurrent { get; set; }
        public double? CompletionRateLast { get; set; }
        public double? FirstContact48h { get; set; }
        public double? EscalationRate { get; set; }
        public double? CrisisHandledSafely { get; set; }
    }
}