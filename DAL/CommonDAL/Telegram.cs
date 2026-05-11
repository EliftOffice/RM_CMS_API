using Dapper;
using Microsoft.Extensions.Configuration;
using RM_CMS.Data;
using RM_CMS.Data.Models;
using RM_CMS.Utilities;

namespace RM_CMS.DAL.CommonDAL
{
    public interface ITelegram
    {
        Task<ApiResponse<string>> GetTelegramBotToken();
        Task<ApiResponse<string>> GetTelegramBotUrl();
    }
    public class Telegram: ITelegram
    {
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
                WHERE config_key = 'telegram_boot_url';
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
    }
}
