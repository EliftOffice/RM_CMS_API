using Dapper;
using RM_CMS.Data;
using RM_CMS.Data.DTO.TeamLeads;
using RM_CMS.Utilities;
using System.Globalization;

namespace RM_CMS.DAL.TeamLeads
{
    public interface ITeamLeadDashBoardDAL
    {
        Task<ApiResponse<TeamLeadMetricsDTO>> GetTeamHealthMetricsAsync(string teamLeadId);
    }

    public class TeamLeadDashBoardDAL: ITeamLeadDashBoardDAL
    {
        private readonly IDbConnectionFactory _dbConnectionFactory;
        public TeamLeadDashBoardDAL(IDbConnectionFactory dbConnectionFactory)
        {
            _dbConnectionFactory = dbConnectionFactory;
        }

        public async Task<ApiResponse<TeamLeadMetricsDTO>> GetTeamHealthMetricsAsync(string teamLeadId)
        {
            try
            {
                using (var connection = _dbConnectionFactory.GetConnection())
                {
                    var metrics = new TeamLeadMetricsDTO();

                    // Current & Last Week
                    var currentWeek = ISOWeek.GetWeekOfYear(DateTime.Now);
                    var lastWeek = currentWeek - 1;

                    // 1. Get volunteers
                    const string volunteersQuery = @"
                SELECT volunteer_id, first_name, last_name, capacity_band, capacity_min, capacity_max
                FROM volunteers
                WHERE team_lead = @TeamLeadId AND status = 'Active';
            ";

                    var volunteers = (await connection.QueryAsync<VolunteerDTO>(
                        volunteersQuery,
                        new { TeamLeadId = teamLeadId }
                    )).ToList();

                    var volunteerMetrics = new List<VolunteerMetricsDTO>();

                    foreach (var v in volunteers)
                    {
                        // This week
                        const string thisWeekQuery = @"
                    SELECT COUNT(*) FROM follow_ups
                    WHERE volunteer_id = @VolunteerId
                      AND week_number = @Week
                      AND contact_status = 'Contacted';
                ";

                        var thisWeekCompleted = await connection.ExecuteScalarAsync<int>(
                            thisWeekQuery,
                            new { VolunteerId = v.volunteer_id, Week = currentWeek }
                        );

                        // Last week
                        var lastWeekCompleted = await connection.ExecuteScalarAsync<int>(
                            thisWeekQuery,
                            new { VolunteerId = v.volunteer_id, Week = lastWeek }
                        );

                        // Trend
                        var trend = "➡️";
                        if (thisWeekCompleted > lastWeekCompleted) trend = "⬆️";
                        if (thisWeekCompleted < lastWeekCompleted) trend = "⬇️";

                        // Completion %
                        var completionRate = v.capacity_max == 0 ? 0 :
                            (thisWeekCompleted * 100.0 / v.capacity_max);

                        var flag = "🔴";
                        if (completionRate >= 90) flag = "🟢";
                        else if (completionRate >= 75) flag = "🟡";

                        volunteerMetrics.Add(new VolunteerMetricsDTO
                        {
                            VolunteerId = v.volunteer_id,
                            Name = $"{v.first_name} {v.last_name}",
                            CapacityBand = v.capacity_band,
                            ThisWeek = $"{thisWeekCompleted}/{v.capacity_max}",
                            LastWeek = lastWeekCompleted,
                            Trend = trend,
                            Flag = flag,
                            CompletionRate = completionRate
                        });
                    }

                    metrics.Volunteers = volunteerMetrics;

                    // 2. Team Performance
                    const string teamPerformanceQuery = @"
                SELECT 
                    COUNT(*) as total_follow_ups,
                    SUM(CASE WHEN contact_status = 'Contacted' THEN 1 ELSE 0 END) as completed,
                    SUM(CASE WHEN response_type = 'Normal' THEN 1 ELSE 0 END) as normal,
                    SUM(CASE WHEN response_type = 'Needs Follow-Up' THEN 1 ELSE 0 END) as needs_follow_up,
                    SUM(CASE WHEN response_type = 'Crisis' THEN 1 ELSE 0 END) as crisis,
                    AVG(call_duration_min) as avg_duration
                FROM follow_ups
                WHERE week_number = @Week
                  AND volunteer_id IN (
                      SELECT volunteer_id FROM volunteers WHERE team_lead = @TeamLeadId
                  );
            ";

                    metrics.TeamPerformance = await connection.QueryFirstOrDefaultAsync<TeamPerformanceDTO>(
                        teamPerformanceQuery,
                        new { Week = currentWeek, TeamLeadId = teamLeadId }
                    );

                    // 3. Upcoming Check-ins
                    const string checkInsQuery = @"
                SELECT first_name, last_name, next_check_in,
                       DAYNAME(next_check_in) as day_of_week
                FROM volunteers
                WHERE team_lead = @TeamLeadId
                  AND next_check_in BETWEEN CURDATE() AND DATE_ADD(CURDATE(), INTERVAL 7 DAY)
                ORDER BY next_check_in;
            ";

                    metrics.UpcomingCheckIns = (await connection.QueryAsync<CheckInDTO>(
                        checkInsQuery,
                        new { TeamLeadId = teamLeadId }
                    )).ToList();

                    // 4. Attention Needed (logic in DAL for simplicity)
                    metrics.AttentionNeeded = new List<AttentionDTO>();

                    foreach (var v in metrics.Volunteers)
                    {
                        if (v.Flag == "🔴" && v.Trend == "⬇️")
                        {
                            metrics.AttentionNeeded.Add(new AttentionDTO
                            {
                                Volunteer = v.Name,
                                Message = "2 weeks declining performance",
                                Priority = "URGENT"
                            });
                        }
                        else if (v.Flag == "🟡" && v.CapacityBand == "Consistent")
                        {
                            metrics.AttentionNeeded.Add(new AttentionDTO
                            {
                                Volunteer = v.Name,
                                Message = "Check capacity (might need reduction)",
                                Priority = "IMPORTANT"
                            });
                        }
                    }

                    return new ApiResponse<TeamLeadMetricsDTO>(
                        ResponseType.Success,
                        "Team metrics retrieved",
                        metrics
                    );
                }
            }
            catch (Exception)
            {
                return new ApiResponse<TeamLeadMetricsDTO>(
                    ResponseType.Error,
                    "Error retrieving team metrics",
                    null
                );
            }
        }
    }
}
