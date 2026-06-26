using Microsoft.Extensions.Logging;
using RM_CMS.DAL.CommonDAL;
using RM_CMS.DAL.Volunteers;
using RM_CMS.Utilities;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace RM_CMS.BLL.Admin
{
    public class NotificationService : INotificationService
    {
        private readonly IVolunteersDAL _volunteersDal;
        private readonly ITelegram _telegram;
       
        private readonly ILogger<NotificationService> _logger;

        public NotificationService(IVolunteersDAL volunteersDal, ITelegram telegram, ILogger<NotificationService> logger)
        {
            _volunteersDal = volunteersDal;
            _telegram = telegram;
            _logger = logger;
        }

        public async Task<ApiResponse<object>> BroadcastTelegramToVolunteersAsync(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return new ApiResponse<object>(ResponseType.Warning, "Message cannot be empty.", null);
            }

            var volunteersResponse = await _volunteersDal.GetActiveVolunteersWithChatIdAsync();

            if (volunteersResponse.ResponseType != ResponseType.Success || volunteersResponse.Data == null)
            {
                return new ApiResponse<object>(ResponseType.Error, "Could not retrieve volunteers.", null);
            }

            var volunteers = volunteersResponse.Data;
            if (!volunteers.Any())
            {
                return new ApiResponse<object>(ResponseType.Warning, "No active volunteers with a linked Telegram account found.", null);
            }

            int successCount = 0;
            var botToken = (await _telegram.GetTelegramBotToken()).Data;
            if (string.IsNullOrEmpty(botToken))
            {
                 return new ApiResponse<object>(ResponseType.Error, "Telegram Bot Token is not configured.", null);
            }

            foreach (var volunteer in volunteers)
            {
                try
                {
                    // Assuming ITelegram has a public method to send messages
                    await _volunteersDal.SendTelegramMessageAsync(volunteer.TelegramChatId, message);
                    successCount++;
                    await Task.Delay(300);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error sending Telegram message to volunteer {volunteer.VolunteerId}.");
                }
            }

            return new ApiResponse<object>(ResponseType.Success, $"Broadcast sent. {successCount} of {volunteers.Count} messages were successful.", new { TotalAttempted = volunteers.Count, TotalSuccess = successCount });
        }
    }
}