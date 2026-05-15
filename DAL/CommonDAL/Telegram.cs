using Dapper;
using Microsoft.Extensions.Configuration;
using RM_CMS.Data;
using RM_CMS.Data.DTO.Volunteers;
using RM_CMS.Data.Models;
using RM_CMS.Utilities;
using System.Net.Http;
using System.Text.Json;

namespace RM_CMS.DAL.CommonDAL
{
    public interface ITelegram
    {
        Task<ApiResponse<string>> GetTelegramBotToken();
        Task<ApiResponse<string>> GetTelegramBotUrl();
        Task<ApiResponse<TelegramApiConfigModel>> GetTelegramAPIConfig();
        Task<ApiResponse<string>> SendTelegramMessageByPhoneNumber(string phone, string message, string? otp = null, string? twoFactorPassword = null);
    }
    public class Telegram: ITelegram
    {
        private readonly IDbConnectionFactory _dbConnectionFactory;
        private readonly IHttpClientFactory _httpClientFactory;

        public Telegram(IDbConnectionFactory dbConnectionFactory, IHttpClientFactory httpClientFactory)
        {
            _dbConnectionFactory = dbConnectionFactory;
            _httpClientFactory = httpClientFactory;
        }

        public async Task<ApiResponse<string>> GetTelegramBotToken()
        {
            try
            {
                using (var connection = _dbConnectionFactory.GetConnection())
                {
                    const string query = @"
                SELECT config_value 
                FROM system_config 
                WHERE config_key = 'telegram_bot_token';
            ";

                    var token = await connection.QueryFirstOrDefaultAsync<string>(query);

                    if (string.IsNullOrWhiteSpace(token))
                    {
                        return new ApiResponse<string>(
                            ResponseType.Warning,
                            "Telegram bot token not found",
                            string.Empty
                        );
                    }

                    return new ApiResponse<string>(
                        ResponseType.Success,
                        "Telegram bot token retrieved successfully",
                        token
                    );
                }
            }
            catch (Exception ex)
            {
                return new ApiResponse<string>(
                    ResponseType.Error,
                    $"Error retrieving Telegram bot token: {ex.Message}",
                    string.Empty
                );
            }
        }

        public async Task<ApiResponse<string>> GetTelegramBotUrl()
        {
            try
            {
                using (var connection = _dbConnectionFactory.GetConnection())
                {
                    const string query = @"
                SELECT config_value 
                FROM system_config 
                WHERE config_key = 'telegram_bot_url';
            ";

                    var token = await connection.QueryFirstOrDefaultAsync<string>(query);

                    if (string.IsNullOrWhiteSpace(token))
                    {
                        return new ApiResponse<string>(
                            ResponseType.Warning,
                            "Telegram bot url not found",
                            string.Empty
                        );
                    }

                    return new ApiResponse<string>(
                        ResponseType.Success,
                        "Telegram bot url retrieved successfully",
                        token
                    );
                }
            }
            catch (Exception ex)
            {
                return new ApiResponse<string>(
                    ResponseType.Error,
                    $"Error retrieving Telegram bot url: {ex.Message}",
                    string.Empty
                );
            }
        }

        public async Task<ApiResponse<TelegramApiConfigModel>> GetTelegramAPIConfig()
        {
            try
            {
                using (var connection = _dbConnectionFactory.GetConnection())
                {
                    const string query = @"
                SELECT 
                    MAX(CASE WHEN config_key = 'telegram_api_id' THEN config_value END) AS ApiId,
                    MAX(CASE WHEN config_key = 'telegram_api_hash' THEN config_value END) AS ApiHash,
                    MAX(CASE WHEN config_key = 'telegram_phone_number' THEN config_value END) AS PhoneNumber
                FROM system_config;
            ";

                    var config = await connection.QueryFirstOrDefaultAsync<TelegramApiConfigModel>(query);

                    if (config == null ||
                        string.IsNullOrWhiteSpace(config.ApiId) ||
                        string.IsNullOrWhiteSpace(config.ApiHash) ||
                        string.IsNullOrWhiteSpace(config.PhoneNumber))
                    {
                        return new ApiResponse<TelegramApiConfigModel>(
                            ResponseType.Warning,
                            "Telegram API configuration not found",
                            null
                        );
                    }

                    return new ApiResponse<TelegramApiConfigModel>(
                        ResponseType.Success,
                        "Telegram API configuration retrieved successfully",
                        config
                    );
                }
            }
            catch (Exception ex)
            {
                return new ApiResponse<TelegramApiConfigModel>(
                    ResponseType.Error,
                    $"Error retrieving Telegram API configuration: {ex.Message}",
                    null
                );
            }
        }

        public async Task<ApiResponse<string>> SendTelegramMessageByPhoneNumber(string phone,string message, string? otp = null, string? twoFactorPassword = null)
        {
            try
            {
                // 1. get bot token
                var tokenResp = await GetTelegramBotToken();
                if (tokenResp.ResponseType != ResponseType.Success || string.IsNullOrWhiteSpace(tokenResp.Data))
                    return new ApiResponse<string>(ResponseType.Error, "Bot token not configured", string.Empty);

                var token = tokenResp.Data;

                // 2. find volunteer with this phone to get chat id
                using var conn = _dbConnectionFactory.GetConnection();
                const string q = @"SELECT telegram_chat_id FROM volunteers WHERE phone = @Phone LIMIT 1";
                var chatId = await conn.QueryFirstOrDefaultAsync<string>(q, new { Phone = phone });

                if (string.IsNullOrWhiteSpace(chatId))
                {
                    return new ApiResponse<string>(ResponseType.Warning, "No Telegram chat id for this phone", string.Empty);
                }

                // 3. send via bot api
                var client = _httpClientFactory.CreateClient();
                var url = $"https://api.telegram.org/bot{token}/sendMessage?chat_id={chatId}&text={Uri.EscapeDataString(message)}";
                var resp = await client.GetAsync(url);
                if (!resp.IsSuccessStatusCode)
                {
                    var body = await resp.Content.ReadAsStringAsync();
                    return new ApiResponse<string>(ResponseType.Error, $"Telegram API error: {body}", string.Empty);
                }

                return new ApiResponse<string>(ResponseType.Success, "Message sent", "SUCCESS");
            }
            catch (Exception ex)
            {
                return new ApiResponse<string>(ResponseType.Error, $"Error sending telegram message: {ex.Message}", string.Empty);
            }
        }
    }
}
