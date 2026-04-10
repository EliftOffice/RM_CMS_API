using RM_CMS.DAL.TeamLeads;
using RM_CMS.Data.DTO.TeamLeads;
using RM_CMS.Utilities;
using System.Globalization;

namespace RM_CMS.BLL.TeamLeads
{
    public interface ITeamLeadDashBoardBLL
    {
        Task<ApiResponse<TeamLeadMetricsDTO>> GetTeamHealthMetricsAsync(string teamLeadId);
        Task<ApiResponse<TeamLeadMetricsDTO>> GetTeamHealthMetricsAsyncV1(string teamLeadId);
        Task<ApiResponse<bool>> SaveTeamLeadAsync(TeamLeadDTO teamLead);
        Task<ApiResponse<List<TeamLeadDTO>>> GetTeamLeadsAsync();
    }

    public class TeamLeadDashBoardBLL : ITeamLeadDashBoardBLL
    {
        private readonly ITeamLeadDashBoardDAL _dal;

        public TeamLeadDashBoardBLL(ITeamLeadDashBoardDAL dal)
        {
            _dal = dal;
        }


        public async Task<ApiResponse<TeamLeadMetricsDTO>> GetTeamHealthMetricsAsyncV1(string teamLeadId)
        { 
            try 
            { 
                return await _dal.GetTeamHealthMetricsAsyncV1(teamLeadId); }
            catch (Exception) { 
                return new ApiResponse<TeamLeadMetricsDTO>(ResponseType.Error, "Error processing team metrics", null); 
            } 
        }


        #region [Enhanced Metrics]
        public async Task<ApiResponse<TeamLeadMetricsDTO>> GetTeamHealthMetricsAsync(string teamLeadId)
        {
            try
            {
                var metrics = new TeamLeadMetricsDTO();

                var thresholds = await _dal.GetThresholdsAsync();
                var weekInfo = GetWeekInfo();

                metrics.WeekOf = weekInfo.WeekLabel;
                metrics.TeamLeadName = await _dal.GetTeamLeadNameAsync(teamLeadId);

                var volunteers = await _dal.GetVolunteersAsync(teamLeadId);

                metrics.Volunteers = await BuildVolunteerMetrics(volunteers, thresholds, weekInfo);

                metrics.TeamPerformance = await _dal.GetTeamPerformanceAsync(teamLeadId, weekInfo.CurrentWeek);

                metrics.UpcomingCheckIns = await _dal.GetUpcomingCheckInsAsync(teamLeadId);

                metrics.AttentionNeeded = GetAttentionNeeded(metrics.Volunteers);

                metrics.EscalationsPending = await _dal.GetEscalationsAsync(teamLeadId);

                return new ApiResponse<TeamLeadMetricsDTO>(
                    ResponseType.Success,
                    "Team metrics retrieved",
                    metrics
                );
            }
            catch
            {
                return new ApiResponse<TeamLeadMetricsDTO>(
                    ResponseType.Error,
                    "Error processing team metrics",
                    null
                );
            }
        }

        private async Task<List<VolunteerMetricsDTO>> BuildVolunteerMetrics(List<VolunteerDTO> volunteers, (int Green, int Yellow, int Red) thresholds, (int CurrentWeek, int LastWeek, string WeekLabel) weekInfo)
        {
            var list = new List<VolunteerMetricsDTO>();

            foreach (var v in volunteers)
            {
                var last4Weeks = await _dal.GetLast4WeeksCountsAsync(v.volunteer_id,weekInfo.CurrentWeek);


                #region [Basic Trend]
                //var thisWeek = await _dal.GetCompletedCountAsync(v.volunteer_id, weekInfo.CurrentWeek);
                //var lastWeek = await _dal.GetCompletedCountAsync(v.volunteer_id, weekInfo.LastWeek);

                //var trend = CalculateBasicTrend(thisWeek, lastWeek);
                #endregion

                #region [Enhanced Trend]

                var thisWeek = last4Weeks.Last();
                var lastWeek = last4Weeks.Count > 1 ? last4Weeks[^2] : 0;

                var trend = CalculateTrend(last4Weeks);

                #endregion


                var completionRate = CalculateCompletionRate(thisWeek, v.capacity_max);

                #region [Basic flag]
                // var flag = GetFlag(completionRate, thresholds);
                #endregion


                #region [Enhanced Flag]
                var escalationCount = 0; // TODO: fetch from DAL
                var emotionalState = v.emotional_tone; // TODO: derive from check-ins

                var flag = GetSmartFlag(completionRate,trend,escalationCount,emotionalState, thresholds);
                #endregion

                list.Add(new VolunteerMetricsDTO
                {
                    VolunteerId = v.volunteer_id,
                    Name = $"{v.first_name} {v.last_name}",
                    CapacityBand = v.capacity_band,
                    ThisWeek = $"{thisWeek}/{v.capacity_max}",
                    LastWeek = lastWeek,
                    Trend = trend,
                    Flag = flag,
                    CompletionRate = completionRate
                });
            }

            return list;
        }

        private (int CurrentWeek, int LastWeek, string WeekLabel) GetWeekInfo()
        {
            var today = DateTime.Now.Date;
            var dayOfWeek = (int)today.DayOfWeek;

            var monday = today.AddDays(-(dayOfWeek == 0 ? 6 : dayOfWeek - 1));

            return (
                ISOWeek.GetWeekOfYear(today),
                ISOWeek.GetWeekOfYear(today) - 1,
                "Week of " + monday.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture)
            );
        }       

        private double CalculateCompletionRate(int completed, int capacity)
        {
            if (capacity == 0) return 0;
            return completed * 100.0 / capacity;
        }



        private string GetFlag(double rate, (int Green, int Yellow, int Red) thresholds)
        {
            if (rate >= thresholds.Green) return "🟢";
            if (rate >= thresholds.Yellow) return "🟡";
            return "🔴";
        }
        private string CalculateBasicTrend(int current, int previous)
        {
            if (current > previous) return "⬆️";
            if (current < previous) return "⬇️";
            return "➡️";
        }

        private List<AttentionDTO> GetAttentionNeeded(List<VolunteerMetricsDTO> volunteers)
        {
            var list = new List<AttentionDTO>();

            foreach (var v in volunteers)
            {
                if (v.Flag == "🔴" && v.Trend == "⬇️")
                {
                    list.Add(new AttentionDTO
                    {
                        Volunteer = v.Name,
                        Message = "2 weeks declining performance",
                        Priority = "URGENT"
                    });
                }
                else if (v.Flag == "🟡" && v.CapacityBand == "Consistent")
                {
                    list.Add(new AttentionDTO
                    {
                        Volunteer = v.Name,
                        Message = "Check capacity",
                        Priority = "IMPORTANT"
                    });
                }
            }

            return list;
        }
        #endregion

        #region [Enhanced trend]
        private string CalculateTrend(List<int> weeks)
        {
            // Example: [2,3,3,4]

            if (weeks.Count < 4)
                return "➡️";

            var w1 = weeks[0];
            var w2 = weeks[1];
            var w3 = weeks[2];
            var w4 = weeks[3];

            // Strict Increasing
            if (w1 <= w2 && w2 <= w3 && w3 <= w4 && w4 > w1)
                return "⬆️";

            // Strict Decreasing
            if (w1 >= w2 && w2 >= w3 && w3 >= w4)
            {
                // Check steep drop
                if ((w1 - w4) >= 3)
                    return "⬇️⬇️ 🚨";

                return "⬇️";
            }

            // Stable (small variation)
            var max = weeks.Max();
            var min = weeks.Min();

            if ((max - min) <= 1)
                return "➡️";

            return "➡️";
        }
        #endregion

        #region [Enhanced flag]
        private string GetSmartFlag(double completionRate,string trend,int escalationCount,string emotionalState, (int Green, int Yellow, int Red) thresholds)
        {
            var score =
                (GetCompletionScore(completionRate) * 0.4) +
                (GetTrendScore(trend) * 0.3) +
                (GetEscalationScore(escalationCount) * 0.15) +
                (GetEmotionalScore(emotionalState) * 0.15);

            return MapScoreToFlag(score, thresholds);
        }
        private double GetCompletionScore(double rate)
        {
            if (rate >= 100) return 100;
            if (rate >= 80) return 80;
            if (rate >= 60) return 60;
            if (rate >= 40) return 40;
            return 20;
        }
        private double GetTrendScore(string trend)
        {
            return trend switch
            {
                "⬆️" => 100,
                "➡️" => 70,
                "⬇️" => 40,
                "⬇️⬇️ 🚨" => 10,
                _ => 50
            };
        }
        private double GetEscalationScore(int escalationCount)
        {
            if (escalationCount == 0) return 100;
            if (escalationCount <= 2) return 60;
            return 20;
        }
        private double GetEmotionalScore(string state)
        {
            return state switch
            {
                "Happy" => 100,
                "Neutral" => 70,
                "Struggling" => 30,
                _ => 50
            };
        }
        private string MapScoreToFlag(double score, (int Green, int Yellow, int Red) thresholds)
        {
            if (score >= thresholds.Green) return "🟢";
            if (score >= thresholds.Yellow) return "🟡";
            return "🔴";
        }
        #endregion



        public async Task<ApiResponse<bool>> SaveTeamLeadAsync(TeamLeadDTO teamLead)
        {
            try
            {
                // 🔹 1. Basic Validations
                

                if (string.IsNullOrWhiteSpace(teamLead.FirstName))
                {
                    return new ApiResponse<bool>(
                        ResponseType.Error,
                        "First Name is required",
                        false
                    );
                }

                if (string.IsNullOrWhiteSpace(teamLead.LastName))
                {
                    return new ApiResponse<bool>(
                        ResponseType.Error,
                        "Last Name is required",
                        false
                    );
                }

                if (string.IsNullOrWhiteSpace(teamLead.Phone))
                {
                    return new ApiResponse<bool>(
                        ResponseType.Error,
                        "Mobile number  is required",
                        false
                    );
                }

              
                // 🔹 4. Call DAL
                return await _dal.SaveTeamLeadAsync(teamLead);
            }
            catch (Exception ex)
            {
                return new ApiResponse<bool>(
                    ResponseType.Error,
                    $"BLL Error: {ex.Message}",
                    false
                );
            }
        }

        // 🔹 Helper Method
        private bool IsValidEmail(string email)
        {
            try
            {
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == email;
            }
            catch
            {
                return false;
            }
        }


        public async Task<ApiResponse<List<TeamLeadDTO>>> GetTeamLeadsAsync()
        {
            try
            {
                return await _dal.GetTeamLeadsAsync();
            }
            catch (Exception ex)
            {
                return new ApiResponse<List<TeamLeadDTO>>(
                    ResponseType.Error,
                    $"DAL Error fetching TeamLeads: {ex.Message}",
                    null
                );
            }
        }
    }
}