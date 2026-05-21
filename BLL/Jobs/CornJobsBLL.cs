using RM_CMS.BLL.Peoples;
using RM_CMS.BLL.TeamLeads;
using RM_CMS.BLL.Volunteers;
using RM_CMS.DAL.TeamLeads;
using RM_CMS.Data.DTO.Jobs;
using RM_CMS.Data.DTO.Peoples;
using RM_CMS.Data.Models;
using RM_CMS.Utilities;

namespace RM_CMS.BLL.Jobs
{

    public interface ICornJobsBLL
    {
        Task<ApiResponse<string>> AssignNewPeople();
        Task<ApiResponse<string>> SendRemindersToVolunteers();
        Task<ApiResponse<string>> SendRemindersToTeamLeads();

    }
    public class CornJobsBLL: ICornJobsBLL
    {
        private readonly IPeoplesBLL _peoplesBLL;
        private readonly IVolunteersBLL _volunteersBLL;
        private readonly ITeamLeadDashBoardBLL _TeamLedDAshboardBLL;
        public CornJobsBLL(IPeoplesBLL peoplesBLL,IVolunteersBLL volunteersBLL,ITeamLeadDashBoardBLL TeamLeadDashBoardBLL)
        {
            _peoplesBLL = peoplesBLL;
            _volunteersBLL = volunteersBLL;
            _TeamLedDAshboardBLL = TeamLeadDashBoardBLL;
        }

        public async Task<ApiResponse<string>> AssignNewPeople()
        {
            try
            {
                string AssignementLog = "";

                var result = await _peoplesBLL.GetUnassignedPersonIdsAsync();

                if (result.Data != null && result.Data.Count() > 0) {

                    foreach(string person in result.Data)
                    {
                        await _volunteersBLL.AssignToVolunteerAsync(person);
                    }                
                }
                return new ApiResponse<string>(ResponseType.Success, "Assignment Completed", AssignementLog);
            }
            catch (Exception ex)
            {
                return new ApiResponse<string>(ResponseType.Error, $"Error retrieving person: {ex.Message}", "");
            }
        }
        public async Task<ApiResponse<string>> SendRemindersToVolunteers()
        {
            try
            {
                var result = await _volunteersBLL
                    .GetVolunteersWithPendingAssignmentsAsync();

                if (result.Data == null || !result.Data.Any())
                {
                    return new ApiResponse<string>(
                        ResponseType.Warning,
                        "No pending assignments found",
                        ""
                    );
                }

                int sentCount = 0;

                foreach (VolunteerPendingAssignmentDto volunteer in result.Data)
                {
                    if (volunteer.TelegramChatId == null)
                        continue;

                    var messageResult = await _volunteersBLL.SendTelegramMessageAsync(volunteer.TelegramChatId.ToString()!,
                            $@"🔔 Reminder to  {volunteer.FirstName} 

మీకు <b>{volunteer.PendingAssignmentsCount}</b> pending follow-ups ఉన్నాయి.

త్వరగా complete చేయండి
👉 https://rmoffice.online

🙏 ధన్యవాదాలు"
                        );

                    if (messageResult.Data)
                    {
                        sentCount++;
                    }
                }

                return new ApiResponse<string>(
                    ResponseType.Success,
                    $"{sentCount} reminder messages sent successfully",
                    ""
                );
            }
            catch (Exception ex)
            {
                return new ApiResponse<string>(
                    ResponseType.Error,
                    $"Error sending reminders: {ex.Message}",
                    ""
                );
            }
        }

        public async Task<ApiResponse<string>> SendRemindersToTeamLeads()
        {
            try
            {
                var result = await _TeamLedDAshboardBLL.GetTeamLeadsWithPendingAssignmentsAsync();

                if (result.Data == null || !result.Data.Any())
                {
                    return new ApiResponse<string>(
                        ResponseType.Warning,
                        "No pending assignments found",
                        ""
                    );
                }

                int sentCount = 0;

                foreach (TeamLeadPendingAssignmentDto TeamLead in result.Data)
                {
                    if (TeamLead.TelegramChatId == null)
                        continue;

                    var messageResult = await _volunteersBLL.SendTelegramMessageAsync(TeamLead.TelegramChatId.ToString()!,
                            $@"🔔 Reminder ➜ {TeamLead.TeamLeadName} 

మీకు <b>{TeamLead.PendingAssignmentsCount}</b> pending follow-ups ఉన్నాయి.

{TeamLead.Description}

త్వరగా complete చేయండి 
👉 https://rmoffice.online

🙏 ధన్యవాదాలు"
                        );

                    if (messageResult.Data)
                    {
                        sentCount++;
                    }
                }

                return new ApiResponse<string>(
                    ResponseType.Success,
                    $"{sentCount} reminder messages sent successfully",
                    ""
                );
            }
            catch (Exception ex)
            {
                return new ApiResponse<string>(
                    ResponseType.Error,
                    $"Error sending reminders: {ex.Message}",
                    ""
                );
            }
        }

    }
}
