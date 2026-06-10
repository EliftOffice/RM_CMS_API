using Dapper;
using MySqlConnector;
using RM_CMS.DTOs.Worship;
using System.Data;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace RM_CMS.DAL.Worship
{
    public class WorshipRepository : IWorshipRepository
    {
        private readonly string _connectionString;

        public WorshipRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task<RecommendedSongsDto> GenerateAndAssignSongsAsync(GenerateSongsRequestDto request)
        {
            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            using var transaction = await connection.BeginTransactionAsync();

            try
            {
                // Convert DateTime to proper MySQL DATE format string to fix "0" or "0000-00-00" updates
                string formattedServiceDate = request.ServiceDate.ToString("yyyy-MM-dd");

                // Check database for the maximum ServiceType assigned today and increment by 1
                var maxServiceTypeQuery = "SELECT COALESCE(MAX(service_type), 0) FROM songs WHERE service_date = @ServiceDate";
                int currentMaxServiceType = await connection.ExecuteScalarAsync<int>(maxServiceTypeQuery, new { ServiceDate = formattedServiceDate }, transaction);
                
                request.ServiceType = currentMaxServiceType + 1;

                // Select songs individually based on categories
                var praiseSongs = await GetSongsByCategoryAsync(connection, transaction, "praise", request, 6, formattedServiceDate);
                var midSongs = await GetSongsByCategoryAsync(connection, transaction, "mid", request, 3, formattedServiceDate);
                var worshipSongs = await GetSongsByCategoryAsync(connection, transaction, "worship", request, 4, formattedServiceDate);

                var allSelectedSongs = praiseSongs.Concat(midSongs).Concat(worshipSongs).ToList();
                var selectedIds = allSelectedSongs.Select(s => s.Id).ToList();

                if (selectedIds.Any())
                {
                    // Update the songs to mark them as assigned for this specific Service Type & Date
                    var updateQuery = @"
                        UPDATE songs 
                        SET service_date = @ServiceDate,
                            service_type = @ServiceType
                        WHERE id IN @Ids";

                    await connection.ExecuteAsync(updateQuery, new 
                    { 
                        ServiceDate = formattedServiceDate, 
                        ServiceType = request.ServiceType, 
                        Ids = selectedIds 
                    }, transaction);
                }

                await transaction.CommitAsync();

                // Update the DTOs in memory to reflect the newly assigned values
                foreach (var song in allSelectedSongs)
                {
                    song.ServiceDate = request.ServiceDate.Date;
                    song.ServiceType = request.ServiceType;
                }

                var recommendedSongs = new RecommendedSongsDto
                {
                    PraiseSongs = praiseSongs,
                    MidSongs = midSongs,
                    WorshipSongs = worshipSongs
                };

                // టెలిగ్రామ్ కి మెసేజ్ పంపే ఫంక్షన్ ని ఇక్కడ కాల్ చేస్తున్నాం (Fire and forget లాగా వదిలేస్తున్నాం)
                _ = SendTelegramMessageAsync(recommendedSongs, request.ServiceType);

                return recommendedSongs;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        private async Task<IEnumerable<SongDto>> GetSongsByCategoryAsync(
            IDbConnection connection, IDbTransaction transaction, string category, GenerateSongsRequestDto request, int limit, string formattedDate)
        {
            // Query sorts NULL last_sung_date first, then oldest dates next.
            var query = @"
                SELECT id AS Id, title AS Title, type AS Category, last_sung_date AS LastSungDate, 
                       service_date AS ServiceDate, service_type AS ServiceType, service_category AS ServiceCategory
                FROM songs 
                WHERE type = @Category
                  AND (service_category IS NULL OR service_category = @ServiceCategory)
                  AND (service_date IS NULL OR service_date != @ServiceDate)
                ORDER BY last_sung_date IS NULL DESC, last_sung_date ASC
                LIMIT @Limit";

            return await connection.QueryAsync<SongDto>(query, new { Category = category, ServiceCategory = request.ServiceCategory, ServiceDate = formattedDate, Limit = limit }, transaction);
        }

        private async Task SendTelegramMessageAsync(RecommendedSongsDto songs, int serviceType)
        {
            try
            {
                var sb = new StringBuilder();

            // Calculate next Sunday's date
            DateTime today = DateTime.Today;
            int daysUntilSunday = ((int)DayOfWeek.Sunday - (int)today.DayOfWeek + 7) % 7;
            if (daysUntilSunday == 0) daysUntilSunday = 7;
            DateTime nextSunday = today.AddDays(daysUntilSunday);

            sb.AppendLine($"<b>📅 {nextSunday.ToString("dd MMMM yyyy").ToUpper()}</b>");
            sb.AppendLine();
                sb.AppendLine($"<b>🎵 Service {serviceType} Songs List</b>");
                sb.AppendLine();

                sb.AppendLine("<b>🎤 Praise Songs</b>");
                int index = 1;
                foreach (var song in songs.PraiseSongs)
                {
                    sb.AppendLine($"{index++}. {song.Title}");
                }

                sb.AppendLine();
                sb.AppendLine("<b>🙏 Mid Songs</b>");
              //  index = 1;
                foreach (var song in songs.MidSongs)
                {
                    sb.AppendLine($"{index++}. {song.Title}");
                }

                sb.AppendLine();
                sb.AppendLine("<b>🕊 Worship Songs</b>");
               // index = 1;
                foreach (var song in songs.WorshipSongs)
                {
                    sb.AppendLine($"{index++}. {song.Title}");
                }

                string botToken;
                string chatId;

                using (var connection = new MySqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    botToken = await connection.ExecuteScalarAsync<string>("SELECT value FROM system_config WHERE `key` = 'telegram_bot_token'");
                    chatId = await connection.ExecuteScalarAsync<string>("SELECT value FROM system_config WHERE `key` = 'telegram_group_chat_id'");
                }

                if (string.IsNullOrEmpty(botToken) || string.IsNullOrEmpty(chatId)) return;

                string url = $"https://api.telegram.org/bot{botToken}/sendMessage";

                var payload = new
                {
                    chat_id = chatId,
                    text = sb.ToString(),
                    parse_mode = "HTML"
                };

                using var httpClient = new HttpClient();
                var content = new StringContent(
                    JsonSerializer.Serialize(payload),
                    Encoding.UTF8,
                    "application/json");

                await httpClient.PostAsync(url, content);
            }
            catch (Exception)
            {
            }
        }
    }
}