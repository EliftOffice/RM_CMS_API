using RM_CMS.BLL.Nurture;
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
        Task<ApiResponse<string>> ProcessNurtureSteps();
    }
    public class CornJobsBLL: ICornJobsBLL
    {
        private readonly IPeoplesBLL _peoplesBLL;
        private readonly IVolunteersBLL _volunteersBLL;
        private readonly ITeamLeadDashBoardBLL _TeamLedDAshboardBLL;
        private readonly INurtureBLL _nurtureBLL;

        public CornJobsBLL(IPeoplesBLL peoplesBLL, IVolunteersBLL volunteersBLL, ITeamLeadDashBoardBLL TeamLeadDashBoardBLL, INurtureBLL nurtureBLL)
        {
            _peoplesBLL = peoplesBLL;
            _volunteersBLL = volunteersBLL;
            _TeamLedDAshboardBLL = TeamLeadDashBoardBLL;
            _nurtureBLL = nurtureBLL;
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

                    //For testing purpose, sending to my telegram

                    await _volunteersBLL.SendTelegramMessageAsync("1671347213",
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

        // ─────────────────────────────────────────────────────────────
        // NURTURE CRON — runs daily
        // Finds steps due today and sends Telegram reminder to volunteer
        // Also notifies TL for any sequences that just hit InReview
        // ─────────────────────────────────────────────────────────────
        public async Task<ApiResponse<string>> ProcessNurtureSteps()
        {
            try
            {
                // Get all volunteers who have nurture steps due today
                // We query via a shared "all active volunteer IDs" approach
                var allVolunteers = await _volunteersBLL.GetVolunteersAsync();
                if (allVolunteers.Data == null || !allVolunteers.Data.Any())
                    return new ApiResponse<string>(ResponseType.Warning, "No volunteers found", "");

                int notified = 0;

                foreach (var volunteer in allVolunteers.Data.Where(v => v.Status == "Active"))
                {
                    var dueSteps = await _nurtureBLL.GetDueStepsForVolunteerAsync(volunteer.VolunteerId);
                    if (dueSteps.Data == null || !dueSteps.Data.Any()) continue;

                    if (string.IsNullOrEmpty(volunteer.TelegramChatId)) continue;

                    foreach (var step in dueSteps.Data)
                    {
                        string methodEmoji = step.Method == "Call" ? "📞" : "🏠";
                        string message = $@"
🌱 Nurture Reminder — Step {step.StepNumber}/7

Praise the Lord {volunteer.FirstName},

{methodEmoji} <b>{step.Method}</b> — {step.PersonName} ({step.PersonPhone})

మీరు ఈ రోజు {step.PersonName} ని {(step.Method == "Call" ? "call" : "visit")} చేయాలి.
(Step {step.StepNumber} of 7)

👉 https://rmoffice.online

🙏 ధన్యవాదాలు";

                        await _volunteersBLL.SendTelegramMessageAsync(volunteer.TelegramChatId, message);
                        notified++;
                    }
                }

                return new ApiResponse<string>(ResponseType.Success, $"{notified} nurture step reminders sent", "");
            }
            catch (Exception ex)
            {
                return new ApiResponse<string>(ResponseType.Error, $"Error processing nurture steps: {ex.Message}", "");
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

                    //For testing purpose, sending to my telegram
                    await _volunteersBLL.SendTelegramMessageAsync("1671347213",
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
