using RM_CMS.DAL.Pastors;
using RM_CMS.Data.DTO;
using RM_CMS.Data.DTO.Pastors;
using RM_CMS.Utilities;

namespace RM_CMS.BLL.Pastors
{
    public interface IPastorDashboardBLL
    {
        Task<ApiResponse<PastorDTO>> GetPastorDashboardAsync();
    }

    public class PastorDashBoardBLL : IPastorDashboardBLL
    {
        private readonly IPastorDashboardDAL _pastorDashboardDAL;

        public PastorDashBoardBLL(IPastorDashboardDAL pastorDashboard)
        {
            _pastorDashboardDAL = pastorDashboard;
        }

        public async Task<ApiResponse<PastorDTO>> GetPastorDashboardAsync()
        {
            try
            {
                var response = new PastorDTO();

                response.SystemHealth = await BuildSystemHealthAsync();
                response.KPIs = await BuildKpisAsync(response.SystemHealth);
                response.TeamLeadPerformance = await BuildTeamLeadPerformanceAsync();
                response.PipelineHealth = await BuildPipelineHealthAsync();
                response.Escalations = await BuildEscalationsAsync();
                response.Trends = await BuildTrendsAsync();
                response.Impact = await BuildImpactAsync();
                response.DevelopmentPipeline = await BuildDevelopmentPipelineAsync();
                response.Alerts = BuildAlerts(response);

                return new ApiResponse<PastorDTO>(
                    ResponseType.Success,
                    "Pastor dashboard retrieved successfully",
                    response
                );
            }
            catch (Exception)
            {
                return new ApiResponse<PastorDTO>(
                    ResponseType.Error,
                    "Error retrieving pastor dashboard",
                    new PastorDTO()
                );
            }
        }

        // 1. SYSTEM HEALTH
        private async Task<SystemHealthDTO> BuildSystemHealthAsync()
        {
            var res = await _pastorDashboardDAL.GetSystemHealthAsync();
            var data = res.Data;

            var vnps = data.SystemVNPS ?? 0;
            var retention = data.VolunteerRetention ?? 0;
            var completion = data.CompletionRateMTD ?? 0;

            if (vnps >= 50 && retention >= 90 && completion >= 85)
                data.OverallFlag = "🟢 HEALTHY";
            else if (vnps >= 40 && retention >= 85 && completion >= 80)
                data.OverallFlag = "🟡 NEEDS ATTENTION";
            else
                data.OverallFlag = "🔴 AT RISK";

            return data;
        }

        // 2. KPIs
        private async Task<KPIDashboardDTO> BuildKpisAsync(SystemHealthDTO systemHealth)
        {
            var res = await _pastorDashboardDAL.GetKpisAsync();
            var kpi = res.Data;

            var dto = new KPIDashboardDTO
            {
                CompletionRate = new KPIItemDTO
                {
                    Current = kpi.CompletionRateCurrent ?? 0,
                    Target = 85,
                    Trend = (kpi.CompletionRateCurrent ?? 0) - (kpi.CompletionRateLast ?? 0),
                    Status = (kpi.CompletionRateCurrent ?? 0) >= 85 ? "🟢" : "🔴"
                },
                FirstContact48h = new KPIItemDTO
                {
                    Current = kpi.FirstContact48h ?? 0,
                    Target = 90,
                    Status = (kpi.FirstContact48h ?? 0) >= 90 ? "🟢" : "🔴"
                },
                EscalationRate = new KPIItemDTO
                {
                    Current = kpi.EscalationRate ?? 0,
                    Target = 15,
                    Status = (kpi.EscalationRate ?? 0) < 15 ? "🟢" : "🔴"
                },
                CrisisHandledSafely = new KPIItemDTO
                {
                    Current = kpi.CrisisHandledSafely ?? 100,
                    Target = 100,
                    Status = (kpi.CrisisHandledSafely ?? 0) == 100 ? "🟢" : "🔴"
                },
                VolunteerRetention = new KPIItemDTO
                {
                    Current = systemHealth.VolunteerRetention ?? 0,
                    Target = 90,
                    Status = (systemHealth.VolunteerRetention ?? 0) >= 90 ? "🟢" : "🔴"
                },
                SystemVNPS = new KPIItemDTO
                {
                    Current = systemHealth.SystemVNPS ?? 0,
                    Target = 50,
                    Status = (systemHealth.SystemVNPS ?? 0) >= 50 ? "🟢" : "🔴"
                }
            };

            var list = new List<KPIItemDTO>
            {
                dto.CompletionRate,
                dto.FirstContact48h,
                dto.EscalationRate,
                dto.CrisisHandledSafely,
                dto.VolunteerRetention,
                dto.SystemVNPS
            };

            dto.OnTargetCount = list.Count(x => x.Status == "🟢");
            dto.TotalCount = list.Count;

            return dto;
        }

        // 3. TEAM LEADS
        private async Task<List<TeamLeadPerformanceDTO>> BuildTeamLeadPerformanceAsync()
        {
            var res = await _pastorDashboardDAL.GetTeamLeadPerformanceAsync();

            return res.Data.Select(tl =>
            {
                int below =
                    (tl.CompletionRate < 85 ? 1 : 0) +
                    (tl.TeamVNPS < 50 ? 1 : 0) +
                    (tl.RetentionRate < 90 ? 1 : 0);

                string flag = below >= 2 ? "🔴" : below == 1 ? "🟡" : "🟢";

                tl.BelowTargetCount = below;
                tl.Flag = flag;

                return tl;
            }).ToList();
        }

        // 4. PIPELINE
        private async Task<PipelineHealthSummaryDTO> BuildPipelineHealthAsync()
        {
            var res = await _pastorDashboardDAL.GetPipelineHealthAsync();
            var data = res.Data;

            var total = data.Sum(x => x.Count);

            var stages = data.Select(p =>
            {
                p.Percentage = total == 0 ? 0 : (p.Count * 100.0 / total);
                return p;
            }).ToList();

            var success = stages.FirstOrDefault(s => s.FollowUpStatus == "COMPLETE")?.Count ?? 0;

            return new PipelineHealthSummaryDTO
            {
                Stages = stages,
                SuccessRate = total == 0 ? 0 : (success * 100.0 / total)
            };
        }

        // 5. ESCALATIONS
        private async Task<EscalationDashboardDTO> BuildEscalationsAsync()
        {
            var summary = await _pastorDashboardDAL.GetEscalationsAsync();
            var reasons = await _pastorDashboardDAL.GetTopEscalationReasonsAsync();

            return new EscalationDashboardDTO
            {
                Summary = summary.Data,
                TopReasons = reasons.Data
            };
        }

        // 6. TRENDS
        private async Task<List<TrendDTO>> BuildTrendsAsync()
        {
            var res = await _pastorDashboardDAL.GetTrendsAsync();
            return res.Data;
        }

        // 7. IMPACT
        private async Task<ImpactDTO> BuildImpactAsync()
        {
            var res = await _pastorDashboardDAL.GetImpactAsync();
            return res.Data;
        }

        // 8. DEVELOPMENT
        private async Task<DevelopmentPipelineDTO> BuildDevelopmentPipelineAsync()
        {
            var res = await _pastorDashboardDAL.GetDevelopmentPipelineAsync();
            return res.Data;
        }

        // 9. ALERTS
        private AlertsDTO BuildAlerts(PastorDTO data)
        {
            var alerts = new AlertsDTO
            {
                Urgent = new List<AlertItemDTO>(),
                Important = new List<AlertItemDTO>(),
                Strategic = new List<AlertItemDTO>()
            };

            // ================= URGENT =================
            if (data.TeamLeadPerformance != null)
            {
                foreach (var tl in data.TeamLeadPerformance.Where(t => t.Flag == "🔴"))
                {
                    alerts.Urgent.Add(new AlertItemDTO
                    {
                        Message = $"{tl.TeamLeadName} (Team Lead) - Below target on {tl.BelowTargetCount} metrics",
                        Action = "Schedule 1-on-1 this week"
                    });
                }
            }

            // ================= IMPORTANT =================
            if (data.Escalations?.TopReasons != null)
            {
                var financial = data.Escalations.TopReasons
                    .FirstOrDefault(r => r.Reason == "Financial Crisis");

                if (financial != null && financial.Count > 8)
                {
                    alerts.Important.Add(new AlertItemDTO
                    {
                        Message = $"Financial escalations increasing ({financial.Count} cases vs avg 8)",
                        Action = "Review benevolence fund capacity"
                    });
                }
            }

            if (data.Trends != null && data.Trends.Count >= 3)
            {
                var first = data.Trends.First().Visitors;
                var last = data.Trends.Last().Visitors;

                if (first > 0)
                {
                    var growth = ((last - first) * 100.0) / first;

                    if (growth > 20)
                    {
                        alerts.Important.Add(new AlertItemDTO
                        {
                            Message = $"Visitor volume +{Math.Round(growth)}% in 3 months",
                            Action = "Plan to recruit 5-8 more volunteers in Q2"
                        });
                    }
                }
            }

            // ================= STRATEGIC =================
            if (data.DevelopmentPipeline != null && data.DevelopmentPipeline.PromotionReady > 0)
            {
                alerts.Strategic.Add(new AlertItemDTO
                {
                    Message = $"{data.DevelopmentPipeline.PromotionReady} volunteers ready for Level 2 promotion",
                    Action = "Finalize Level 2 Prayer Ministry launch date"
                });
            }

            if (data.SystemHealth != null &&
                data.SystemHealth.CompletionRateMTD >= 85 &&
                data.SystemHealth.SystemVNPS >= 50)
            {
                alerts.Strategic.Add(new AlertItemDTO
                {
                    Message = $"System running smoothly at {Math.Round(data.SystemHealth.CompletionRateMTD ?? 0)}% completion rate",
                    Action = "Consider expanding to additional campuses"
                });
            }

            return alerts;
        }
    }
}