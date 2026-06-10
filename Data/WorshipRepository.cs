using Dapper;
using MySqlConnector;
using RM_CMS.DTOs.Worship;
using System.Data;

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

                return new RecommendedSongsDto
                {
                    PraiseSongs = praiseSongs,
                    MidSongs = midSongs,
                    WorshipSongs = worshipSongs
                };
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
    }
}