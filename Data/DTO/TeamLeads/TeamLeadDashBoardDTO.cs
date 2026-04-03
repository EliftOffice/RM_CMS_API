namespace RM_CMS.Data.DTO.TeamLeads
{
    public class TeamLeadMetricsDTO
    {
        public List<VolunteerMetricsDTO> Volunteers { get; set; } = new();
        public TeamPerformanceDTO TeamPerformance { get; set; }
        public List<AttentionDTO> AttentionNeeded { get; set; } = new();
        public List<CheckInDTO> UpcomingCheckIns { get; set; } = new();
    }

    public class VolunteerMetricsDTO
    {
        public string VolunteerId { get; set; }
        public string Name { get; set; }
        public string CapacityBand { get; set; }
        public string ThisWeek { get; set; }
        public int LastWeek { get; set; }
        public string Trend { get; set; }
        public string Flag { get; set; }
        public double CompletionRate { get; set; }
    }

    public class TeamPerformanceDTO
    {
        public int total_follow_ups { get; set; }
        public int completed { get; set; }
        public int normal { get; set; }
        public int needs_follow_up { get; set; }
        public int crisis { get; set; }
        public double avg_duration { get; set; }
    }

    public class AttentionDTO
    {
        public string Volunteer { get; set; }
        public string Message { get; set; }
        public string Priority { get; set; }
    }

    public class CheckInDTO
    {
        public string first_name { get; set; }
        public string last_name { get; set; }
        public DateTime next_check_in { get; set; }
        public string day_of_week { get; set; }
    }

    public class VolunteerDTO
    {
        public string volunteer_id { get; set; }
        public string first_name { get; set; }
        public string last_name { get; set; }
        public string capacity_band { get; set; }
        public int capacity_min { get; set; }
        public int capacity_max { get; set; }
    }

    public class TeamLeadPerformanceDTO
    {
        public string team_lead_id { get; set; }
        public string team_lead_name { get; set; }
        public int team_size { get; set; }

        public double completion_rate { get; set; }
        public double team_vnps { get; set; }
        public double retention_rate { get; set; }

        public string flag { get; set; }
        public int below_target_count { get; set; }
    }

    public class PipelineHealthDTO
    {
        public List<PipelineStageDTO> Stages { get; set; } = new();
        public double SuccessRate { get; set; }
    }

    public class PipelineStageDTO
    {
        public string follow_up_status { get; set; }
        public int count { get; set; }
        public double avg_days_in_stage { get; set; }
        public double percentage { get; set; }
    }
    public class EscalationMetricsDTO
    {
        public int total_escalations { get; set; }

        public int standard_count { get; set; }
        public int urgent_count { get; set; }
        public int emergency_count { get; set; }

        public double avg_resolution_standard { get; set; }
        public double avg_resolution_urgent { get; set; }

        public int pending_standard { get; set; }
        public int pending_urgent { get; set; }
        public int pending_emergency { get; set; }

        public List<EscalationReasonDTO> top_reasons { get; set; } = new();
    }

    public class EscalationReasonDTO
    {
        public string escalation_reason { get; set; }
        public int count { get; set; }
    }
    public class TrendDTO
    {
        public string month_name { get; set; }

        public int visitors { get; set; }
        public double completion_rate { get; set; }
        public double vnps { get; set; }
        public int volunteer_count { get; set; }
        public int crisis_count { get; set; }
        public int turnover_count { get; set; }
    }
}