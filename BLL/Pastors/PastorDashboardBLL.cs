using RM_CMS.DAL.Pastors;
using RM_CMS.Data.DTO;
using RM_CMS.Data.DTO.Pastors;
using RM_CMS.Data.DTO.TeamLeads;
using RM_CMS.Utilities;

namespace RM_CMS.BLL.Pastors
{
    public interface IPastorDashboardBLL
    {
        Task<ApiResponse<SystemHealthDTO>> GetSystemHealthAsync();
        Task<ApiResponse<KPIsDTO>> GetKPIsAsync();
        Task<ApiResponse<List<TeamLeadPerformanceDTO>>> GetTeamLeadPerformanceAsync();

        Task<ApiResponse<PipelineHealthDTO>> GetPipelineHealthAsync();
    }

    public class PastorDashBoardBLL : IPastorDashboardBLL
    {
        private readonly IPastorDashboardDAL _pastorDashboardDAL;
        public PastorDashBoardBLL(IPastorDashboardDAL pastorDashboard)
        {
            _pastorDashboardDAL = pastorDashboard;
        }
        public async Task<ApiResponse<SystemHealthDTO>> GetSystemHealthAsync()
        {
            try
            {
                var result = await _pastorDashboardDAL.GetSystemHealthAsync();
                if (result == null || result.Data == null)
                {
                    return new ApiResponse<SystemHealthDTO>(
                        ResponseType.Error,
                        "No system health data found",
                        null
                    );
                }

                var health = result.Data;
                var vnps = health.system_vnps ?? 0;
                var retention = health.volunteer_retention ?? 0;
                var completion = health.completion_rate_mtd ?? 0;

                if (vnps >= 50 && retention >= 90 && completion >= 85)
                {
                    health.OverallHealthStatus = "🟢 HEALTHY";
                }
                else if (vnps >= 40 && retention >= 85 && completion >= 80)
                {
                    health.OverallHealthStatus = "🟡 NEEDS ATTENTION";
                }
                else
                {
                    health.OverallHealthStatus = "🔴 AT RISK";
                }
                return result;
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
        public async Task<ApiResponse<KPIsDTO>> GetKPIsAsync()
        {
            try
            {
                var kpiResult = await _pastorDashboardDAL.GetKPIsAsync();
                var healthResult = await _pastorDashboardDAL.GetSystemHealthAsync();

                if (kpiResult.Data == null || healthResult.Data == null)
                {
                    return new ApiResponse<KPIsDTO>(ResponseType.Error, "No data", null);
                }

                var k = kpiResult.Data;
                var h = healthResult.Data;

                var dto = new KPIsDTO
                {
                    CompletionRate = new KPIItemDTO
                    {
                        Current = k.completion_rate_current,
                        Target = 85,
                        Trend = k.completion_rate_current - k.completion_rate_last,
                        Status = k.completion_rate_current >= 85 ? "🟢" : "🔴"
                    },
                    FirstContact48h = new KPIItemDTO
                    {
                        Current = k.first_contact_48h,
                        Target = 90,
                        Status = k.first_contact_48h >= 90 ? "🟢" : "🔴"
                    },
                    EscalationRate = new KPIItemDTO
                    {
                        Current = k.escalation_rate,
                        Target = 15,
                        Status = k.escalation_rate < 15 ? "🟢" : "🔴"
                    },
                    CrisisHandledSafely = new KPIItemDTO
                    {
                        Current = k.crisis_handled_safely,
                        Target = 100,
                        Status = k.crisis_handled_safely == 100 ? "🟢" : "🔴"
                    },
                    VolunteerRetention = new KPIItemDTO
                    {
                        Current = h.volunteer_retention ?? 0,
                        Target = 90,
                        Status = (h.volunteer_retention ?? 0) >= 90 ? "🟢" : "🔴"
                    },
                    SystemVnps = new KPIItemDTO
                    {
                        Current = h.system_vnps ?? 0,
                        Target = 50,
                        Status = (h.system_vnps ?? 0) >= 50 ? "🟢" : "🔴"
                    }
                };

                var kpisList = new[]
                {
            dto.CompletionRate,
            dto.FirstContact48h,
            dto.EscalationRate,
            dto.CrisisHandledSafely,
            dto.VolunteerRetention,
            dto.SystemVnps
        };

                dto.OnTargetCount = kpisList.Count(x => x.Status == "🟢");
                dto.TotalCount = kpisList.Length;

                return new ApiResponse<KPIsDTO>(ResponseType.Success, "KPIs retrieved", dto);
            }
            catch
            {
                return new ApiResponse<KPIsDTO>(ResponseType.Error, "Error retrieving KPIs", null);
            }
        }

        public async Task<ApiResponse<List<TeamLeadPerformanceDTO>>> GetTeamLeadPerformanceAsync()
        {
            try
            {
                return await _pastorDashboardDAL.GetTeamLeadPerformanceAsync();
            }
            catch (Exception)
            {
                return new ApiResponse<List<TeamLeadPerformanceDTO>>(
                    ResponseType.Error,
                    "Error processing team lead performance",
                    null
                );
            }
        }

        public async Task<ApiResponse<PipelineHealthDTO>> GetPipelineHealthAsync()
        {
            try
            {
                return await _pastorDashboardDAL.GetPipelineHealthAsync();
            }
            catch (Exception)
            {
                return new ApiResponse<PipelineHealthDTO>(
                    ResponseType.Error,
                    "Error processing pipeline health",
                    null
                );
            }
        }

    }
}
