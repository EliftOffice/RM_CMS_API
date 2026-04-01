using System.Timers;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace RM_CMS.Data.DTO
{
    public class SystemHealthDTO
    {
       public int ActiveTeamLeads { get; set; }       
       public int ActiveVolunteers { get; set; }
       public int FirstTimeVisitorsMTD { get; set; }
       public int FollowUpsCompletedMTD { get; set; }
       public int SystemvNPS { get; set; }
       public int VolunteerRetention { get; set; }
       public double CompletionRateMTD { get; set; }

    }
}
