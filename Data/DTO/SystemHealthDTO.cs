using System.Timers;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace RM_CMS.Data.DTO
{
    public class SystemHealthDTO
    {
       public int active_volunteers { get; set; }  =0;     
       public int active_team_leads { get; set; }=0;
        public int first_time_visitors_mtd { get; set; } = 0;          
       public int follow_ups_completed_mtd { get; set; } = 0;
       public int? system_vnps { get; set; }= 0;
       public double? volunteer_retention { get; set; } = 0;
       public double? completion_rate_mtd { get; set; } = 0;
       public string OverallHealthStatus { get; set; }= string.Empty;
    }
}
