using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RM_CMS.BLL.Jobs;
using RM_CMS.BLL.Peoples;
using RM_CMS.BLL.Volunteers;
using RM_CMS.Controllers.Volunteers;
using RM_CMS.DAL.CommonDAL;
using RM_CMS.Data.Models;
using RM_CMS.Utilities;

namespace RM_CMS.Controllers.CornJobs
{
    [Route("api/[controller]")]
    [ApiController]
    public class CornJobs : ControllerBase
    {
        private readonly ICornJobsBLL _ICornJobsBLL;        
        private readonly ILogger<CornJobs> _logger;
       
        public CornJobs(ICornJobsBLL cornJobsBLL, ILogger<CornJobs> logger)
        {
            _ICornJobsBLL = cornJobsBLL;
            _logger = logger;
           
        }

        [HttpPost("send-reminders")]
        public async Task<ActionResult<ApiResponse<string>>> SendReminders()
        {
            _ICornJobsBLL.SendRemindersToVolunteers();
            _ICornJobsBLL.SendRemindersToTeamLeads();

            return Ok(new
            {
                success = true,
                message = "Reminder job executed"
            });
        }

       

        [HttpPost("assign-new-people")]
        public async Task<ActionResult<ApiResponse<string>>> AssignNewPeople()
        {
            _ICornJobsBLL.AssignNewPeople();

            return Ok(new
            {
                success = true,
                message = "New people assigned"
            });
        }

       

    }
}
