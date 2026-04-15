using Dapper;
using RM_CMS.Data;
using RM_CMS.Data.DTO.TeamLeads;
using RM_CMS.Utilities;
using System.Data;
using System.Globalization;

namespace RM_CMS.DAL.TeamLeads
{
    public interface ITeamLeadDashBoardDAL
    {
        Task<ApiResponse<TeamLeadMetricsDTO>> GetTeamHealthMetricsAsyncV1(string teamLeadId);


        Task<(int Green, int Yellow, int Red)> GetThresholdsAsync();
        Task<string> GetTeamLeadNameAsync(string teamLeadId);
        Task<List<VolunteerDTO>> GetVolunteersAsync(string teamLeadId);
        Task<int> GetCompletedCountAsync(string volunteerId, int week);
        Task<TeamPerformanceDTO> GetTeamPerformanceAsync(string teamLeadId, int week);
        Task<List<CheckInDTO>> GetUpcomingCheckInsAsync(string teamLeadId);
        Task<List<EscalationPendingDTO>> GetEscalationsAsync(string teamLeadId);
        Task<ApiResponse<List<TeamLeadDTO>>> GetTeamLeadsAsync();


        #region [Enhanced Trend]
        Task<List<int>> GetLast4WeeksCountsAsync(string volunteerId, int currentWeek);
        #endregion
        //Task<ApiResponse<TeamLeadMetricsDTO>> GetTeamHealthMetricsAsync(string teamLeadId);

        Task<ApiResponse<bool>> SaveTeamLeadAsync(TeamLeadDTO teamLead);
    }

    public class TeamLeadDashBoardDAL : ITeamLeadDashBoardDAL
    {
        private readonly IDbConnectionFactory _dbConnectionFactory;

        public TeamLeadDashBoardDAL(IDbConnectionFactory dbConnectionFactory)
        {
            _dbConnectionFactory = dbConnectionFactory;
        }
        public async Task<ApiResponse<TeamLeadMetricsDTO>> GetTeamHealthMetricsAsyncV1(string teamLeadId)
        {
            try
            {
                using (var connection = _dbConnectionFactory.GetConnection())
                {
                    var metrics = new TeamLeadMetricsDTO();

                    //Get Threshold Values
                    const string thresholdQuery = @"SELECT config_key, config_value 
                                                        FROM system_config 
                                                        WHERE config_key IN ('green_threshold','red_threshold','yellow_threshold');";

                    var thresholdData = await connection.QueryAsync<(string Key, int Value)>(thresholdQuery);

                    var green_threshold = thresholdData.FirstOrDefault(x => x.Key == "green_threshold").Value;
                    var yellow_threshold = thresholdData.FirstOrDefault(x => x.Key == "yellow_threshold").Value;
                    var red_threshold = thresholdData.FirstOrDefault(x => x.Key == "red_threshold").Value;

                    // Current & Last Week
                    var today = DateTime.Now.Date;
                    var dayOfWeek = (int)today.DayOfWeek;
                    // Calculate Monday of current week (assuming Monday start)
                    var monday = today.AddDays(-(dayOfWeek == 0 ? 6 : dayOfWeek - 1));

                    // Week string like "Week of January 27, 2025"
                    metrics.WeekOf = "Week of " + monday.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);

                    // Get team lead name
                    const string teamLeadQuery = @"SELECT CONCAT(first_name, ' ', last_name) FROM team_leads WHERE team_lead_id = @TeamLeadId";
                    var teamLeadName = await connection.QueryFirstOrDefaultAsync<string>(teamLeadQuery, new { TeamLeadId = teamLeadId });
                    metrics.TeamLeadName = teamLeadName ?? "";

                    // Current & Last Week numbers for DB queries
                    var currentWeek = ISOWeek.GetWeekOfYear(DateTime.Now);
                    var lastWeek = currentWeek - 1;

                    // 1. Get volunteers
                    const string volunteersQuery = @"
                SELECT volunteer_id, first_name, last_name,concat(capacity_band,' | ',capacity_min,'-',capacity_max,'/week') capacity_band, capacity_min, capacity_max
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
                        //    const string thisWeekQuery = @"
                        //SELECT COUNT(*) FROM follow_ups
                        //WHERE volunteer_id = @VolunteerId
                        //  AND week_number = @Week
                        //  AND contact_status = 'Contacted';";



                        const string thisWeekQuery = @"SELECT COUNT(DISTINCT person_id)
                                                            FROM follow_ups
                                                            WHERE volunteer_id =@VolunteerId
                                                              AND week_number = @Week
                                                              AND contact_status = 'Contacted'
                                                              AND (
                                                                    response_type != 'No Response'
                                                                    OR next_action = 'Mark Unresponsive'
                                                                  );";

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

                        if (completionRate >= green_threshold)
                            flag = "🟢";
                        else if (completionRate >= yellow_threshold)
                            flag = "🟡";

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
                                         COUNT(DISTINCT f.person_id) as total_follow_ups,

                                         COUNT(DISTINCT CASE 
                                             WHEN f.contact_status = 'Contacted'
                                              AND (
                                                     f.response_type != 'No Response'
                                                     OR f.next_action = 'Mark Unresponsive'
                                                  )
                                             THEN f.person_id 
                                         END) as completed,

                                         SUM(CASE WHEN f.response_type = 'Normal' THEN 1 ELSE 0 END) as normal,
                                         SUM(CASE WHEN f.response_type = 'Needs Follow-Up' THEN 1 ELSE 0 END) as needs_follow_up,
                                         SUM(CASE WHEN f.response_type = 'Crisis' THEN 1 ELSE 0 END) as crisis,

                                         IFNULL(ROUND(AVG(f.call_duration_min), 2), 0) as avg_duration

                                     FROM follow_ups f
                                     WHERE f.week_number = @Week
                                       AND f.volunteer_id IN (
                                           SELECT volunteer_id 
                                           FROM volunteers 
                                           WHERE team_lead = @TeamLeadId
                                       );
                                 ";

                    metrics.TeamPerformance = await connection.QueryFirstOrDefaultAsync<TeamPerformanceDTO>(
                        teamPerformanceQuery,
                        new { Week = currentWeek, TeamLeadId = teamLeadId }
                    );

                    // 3. Upcoming Check-ins
            //        const string checkInsQuery = @"
            //    SELECT first_name, last_name, next_check_in,
            //           DAYNAME(next_check_in) as day_of_week
            //    FROM volunteers
            //    WHERE team_lead = @TeamLeadId
            //      AND next_check_in BETWEEN CURDATE() AND DATE_ADD(CURDATE(), INTERVAL 7 DAY)
            //    ORDER BY next_check_in;
            //";
                    const string checkInsQuery = @"SELECT 
                                                    volunteer_id,    
                                                    first_name, 
                                                    last_name, 
                                                    next_check_in,
                                                    DAYNAME(next_check_in) AS day_of_week
                                                FROM volunteers
                                                WHERE team_lead = @TeamLeadId
                                                  AND next_check_in <= DATE_ADD(CURDATE(), INTERVAL (5 - WEEKDAY(CURDATE())) DAY)
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

                    // 5. Escalations Pending
                    // 5. Escalations Pending
                    const string EscalationsPendingQuery = @"
SELECT 
    e.escalation_id AS EscalationId,
    e.escalation_date AS EscalationDate,
    e.escalation_tier AS EscalationTier,
    e.escalation_reason AS EscalationReason,
    CONCAT(p.first_name, ' ', p.last_name) AS PersonName,
    CONCAT(v.first_name, ' ', v.last_name) AS VolunteerName,
    e.status AS Status,
    e.assigned_to AS AssignedTo,
    DATEDIFF(CURRENT_DATE, e.escalation_date) AS DaysPending,description
FROM escalations e
JOIN people p ON e.person_id = p.person_id
JOIN volunteers v ON e.volunteer_id = v.volunteer_id
WHERE e.status IN ('New', 'In Progress')
  AND e.team_lead_id = @TeamLeadId
ORDER BY e.escalation_tier DESC, e.escalation_date;
";

                    metrics.EscalationsPending = (await connection.QueryAsync<EscalationPendingDTO>(
                        EscalationsPendingQuery,
                        new { TeamLeadId = teamLeadId }
                    )).ToList();


                    return new ApiResponse<TeamLeadMetricsDTO>(
                        ResponseType.Success,
                        "Team metrics retrieved",
                        metrics
                    );
                }
            }
            catch (Exception ex)
            {
                return new ApiResponse<TeamLeadMetricsDTO>(
                    ResponseType.Error,
                    "Error retrieving team metrics",
                    null
                );
            }
        }


        #region [Enhanced Metrics]
        public async Task<(int Green, int Yellow, int Red)> GetThresholdsAsync()
        {
            using var connection = _dbConnectionFactory.GetConnection();

            const string query = @"SELECT config_key, config_value 
                                  FROM system_config 
                                  WHERE config_key IN ('green_threshold','red_threshold','yellow_threshold');";

            var data = await connection.QueryAsync<(string Key, int Value)>(query);

            return (
                data.First(x => x.Key == "green_threshold").Value,
                data.First(x => x.Key == "yellow_threshold").Value,
                data.First(x => x.Key == "red_threshold").Value
            );
        }

        public async Task<string> GetTeamLeadNameAsync(string teamLeadId)
        {
            using var connection = _dbConnectionFactory.GetConnection();

            const string query = @"SELECT CONCAT(first_name, ' ', last_name) 
                                   FROM team_leads 
                                   WHERE team_lead_id = @TeamLeadId";

            return await connection.QueryFirstOrDefaultAsync<string>(query, new { TeamLeadId = teamLeadId }) ?? "";
        }

        public async Task<List<VolunteerDTO>> GetVolunteersAsync(string teamLeadId)
        {
            using var connection = _dbConnectionFactory.GetConnection();

            const string query = @"
                SELECT volunteer_id, first_name, last_name,
                       concat(capacity_band,' | ',capacity_min,'-',capacity_max,'/week') capacity_band,
                       capacity_min, capacity_max, emotional_tone
                FROM volunteers
                WHERE team_lead = @TeamLeadId AND status = 'Active';";

            return (await connection.QueryAsync<VolunteerDTO>(query, new { TeamLeadId = teamLeadId })).ToList();
        }

        public async Task<int> GetCompletedCountAsync(string volunteerId, int week)
        {
            using var connection = _dbConnectionFactory.GetConnection();

            const string query = @"
                SELECT COUNT(DISTINCT person_id)
                FROM follow_ups
                WHERE volunteer_id = @VolunteerId
                  AND week_number = @Week
                  AND contact_status = 'Contacted'
                  AND (
                        response_type != 'No Response'
                        OR next_action = 'Mark Unresponsive'
                      );";

            return await connection.ExecuteScalarAsync<int>(
                query,
                new { VolunteerId = volunteerId, Week = week }
            );
        }

        public async Task<TeamPerformanceDTO> GetTeamPerformanceAsync(string teamLeadId, int week)
        {
            using var connection = _dbConnectionFactory.GetConnection();

            const string query = @"
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
                  );";

            return await connection.QueryFirstOrDefaultAsync<TeamPerformanceDTO>(
                query,
                new { Week = week, TeamLeadId = teamLeadId }
            );
        }

        public async Task<List<CheckInDTO>> GetUpcomingCheckInsAsync(string teamLeadId)
        {
            using var connection = _dbConnectionFactory.GetConnection();

            const string query = @"
                SELECT first_name, last_name, next_check_in,
                       DAYNAME(next_check_in) as day_of_week
                FROM volunteers
                WHERE team_lead = @TeamLeadId
                  AND next_check_in BETWEEN CURDATE() AND DATE_ADD(CURDATE(), INTERVAL 7 DAY)
                ORDER BY next_check_in;";

            return (await connection.QueryAsync<CheckInDTO>(
                query,
                new { TeamLeadId = teamLeadId }
            )).ToList();
        }

        public async Task<List<EscalationPendingDTO>> GetEscalationsAsync(string teamLeadId)
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
                    DATEDIFF(CURRENT_DATE, e.escalation_date) AS DaysPending
                FROM escalations e
                JOIN people p ON e.person_id = p.person_id
                JOIN volunteers v ON e.volunteer_id = v.volunteer_id
                WHERE e.status IN ('New', 'In Progress')
                  AND e.team_lead_id = @TeamLeadId
                ORDER BY e.escalation_tier DESC, e.escalation_date;";

            return (await connection.QueryAsync<EscalationPendingDTO>(
                query,
                new { TeamLeadId = teamLeadId }
            )).ToList();
        }
        #endregion

        #region [Enhanced Trend]
        public async Task<List<int>> GetLast4WeeksCountsAsync(string volunteerId, int currentWeek)
        {
            using var connection = _dbConnectionFactory.GetConnection();

            const string query = @"
        SELECT week_number, COUNT(DISTINCT person_id) AS count
        FROM follow_ups
        WHERE volunteer_id = @VolunteerId
          AND week_number BETWEEN @StartWeek AND @EndWeek
          AND contact_status = 'Contacted'
          AND (
                response_type != 'No Response'
                OR next_action = 'Mark Unresponsive'
              )
        GROUP BY week_number
        ORDER BY week_number;";

            var startWeek = currentWeek - 3;

            var data = await connection.QueryAsync<(int Week, int Count)>(
                query,
                new { VolunteerId = volunteerId, StartWeek = startWeek, EndWeek = currentWeek }
            );

            // Fill missing weeks with 0
            var result = new List<int>();

            for (int i = startWeek; i <= currentWeek; i++)
            {
                var weekData = data.FirstOrDefault(x => x.Week == i);
                result.Add(weekData.Count);
            }

            return result;
        }
        #endregion





        public async Task<ApiResponse<bool>> SaveTeamLeadAsync(TeamLeadDTO teamLead)
        {
            try
            {
                using (var connection = _dbConnectionFactory.GetConnection())
                {
                    // 🔹 1. Generate Team Lead ID
                    const string seqQuery = @"
                SELECT IFNULL(MAX(CAST(SUBSTRING(team_lead_id, 3) AS UNSIGNED)), 0)
                FROM team_leads;
            ";

                    var seqResult = await connection.ExecuteScalarAsync<int>(seqQuery);

                    var nextNum = seqResult + 1;

                    teamLead.TeamLeadId = $"TL{nextNum.ToString().PadLeft(3, '0')}";

                    // 🔹 2. Insert Team Lead
                    const string insertQuery = @"
                INSERT INTO team_leads
                (
                    team_lead_id,
                    first_name,
                    last_name,
                    email,
                    phone,
                    role_type,
                    campus,
                    start_date,
                    max_volunteers,
                    current_volunteers,
                    boundary_incidents,
                    created_at,
                    updated_at
                )
                VALUES
                (
                    @TeamLeadId,
                    @FirstName,
                    @LastName,
                    @Email,
                    @Phone,
                    @RoleType,
                    @Campus,
                    NOW(),
                    @MaxVolunteers,
                    0,
                    0,
                    NOW(),
                    NOW()
                );
            ";

                    var result = await connection.ExecuteAsync(insertQuery, teamLead);

                    return new ApiResponse<bool>(
                        ResponseType.Success,
                        $"Team Lead created successfully with ID {teamLead.TeamLeadId}",
                        result > 0
                    );
                }
            }
            catch (Exception ex)
            {
                return new ApiResponse<bool>(
                    ResponseType.Error,
                    $"Error saving team lead: {ex.Message}",
                    false
                );
            }
        }


        public async Task<ApiResponse<List<TeamLeadDTO>>> GetTeamLeadsAsync()
        {
            try
            {
                using (var connection = _dbConnectionFactory.GetConnection())
                {
                    const string query = @"
                SELECT id,team_lead_id, CONCAT(first_name, ' ', last_name) AS name
                FROM team_leads
                WHERE status = 'Active'
                ORDER BY first_name, last_name;";

                    var result = (await connection.QueryAsync<TeamLeadDTO>(query)).ToList();

                    return new ApiResponse<List<TeamLeadDTO>>(
                        ResponseType.Success,
                        "TeamLeads fetched successfully",
                        result
                    );
                }
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