using Dapper;
using Microsoft.Extensions.Configuration;
using RM_CMS.Data;
using RM_CMS.Data.DTO.Volunteers;
using RM_CMS.Data.Models;
using RM_CMS.Utilities;
using TL;
using WTelegram;

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

        static Client client;

        private readonly IDbConnectionFactory _dbConnectionFactory;
        public Telegram(IDbConnectionFactory dbConnectionFactory)
        {
            _dbConnectionFactory = dbConnectionFactory;
           
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
                var cfgRes = await GetTelegramAPIConfig();
                if (cfgRes.ResponseType != ResponseType.Success || cfgRes.Data == null)
                    return new ApiResponse<string>(ResponseType.Error, "Telegram API configuration missing", string.Empty);

                var cfg = cfgRes.Data;

                string Config(string what)
                {
                    return what switch
                    {
                        "api_id" => cfg.ApiId,
                        "api_hash" => cfg.ApiHash,
                        "phone_number" => cfg.PhoneNumber,
                        "verification_code" => otp ?? string.Empty,
                        "password" => twoFactorPassword ?? string.Empty,
                        _ => null
                    };
                }

                using var client = new Client(Config);

                var me = await client.LoginUserIfNeeded();

                var result = await client.Contacts_ImportContacts(new[]
                {
                    new InputPhoneContact
                    {
                        client_id = 0,
                        phone = phone,
                        first_name = "RM",
                        last_name = "Volunteers"
                    }
                });

                if (result.users.Count == 0)
                {
                    return new ApiResponse<string>(ResponseType.Warning, "Telegram user not found", string.Empty);
                }

                var user = result.users.Values.First();

                await client.SendMessageAsync(user, message);

                return new ApiResponse<string>(ResponseType.Success, "Message sent", "SUCCESS");
            }
            catch (Exception ex)
            {
                return new ApiResponse<string>(ResponseType.Error, $"Error sending telegram message: {ex.Message}", string.Empty);
            }
        }

    }
}
