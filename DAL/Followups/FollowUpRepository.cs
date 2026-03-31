using RM_CMS.Data.Models;
using RM_CMS.Data;
using Dapper;
using System.Data;

namespace RM_CMS.DAL.Followups
{
    public interface IFollowUpRepository
    {
        Task<bool> CreateAsync(FollowUp followUp);
        Task<FollowUp?> GetByIdAsync(string followUpId);
        Task<IEnumerable<FollowUp>> GetByPersonAsync(string personId);
        Task<IEnumerable<FollowUp>> GetByVolunteerAsync(string volunteerId);
        Task<int> CountByPersonAsync(string personId);
        Task<int> GetMaxSequenceForYearAsync(int year);
    }

    public class FollowUpRepository : IFollowUpRepository
    {
        private readonly IDbConnectionFactory _dbConnectionFactory;

        public FollowUpRepository(IDbConnectionFactory dbConnectionFactory)
        {
            _dbConnectionFactory = dbConnectionFactory;
        }

        public async Task<bool> CreateAsync(FollowUp followUp)
        {
            using (var connection = _dbConnectionFactory.GetConnection())
            {
                connection.Open();
                var query = @"
                    INSERT INTO follow_ups (
                        follow_up_id, person_id, volunteer_id, attempt_number, attempt_date,
                        contact_method, contact_status, response_type, call_duration_min,
                        notes, week_number, escalation_appropriate, created_at
                    ) VALUES (
                        @FollowUpId, @PersonId, @VolunteerId, @AttemptNumber, @AttemptDate,
                        @ContactMethod, @ContactStatus, @ResponseType, @CallDurationMin,
                        @Notes, @WeekNumber, @EscalationAppropriate, @CreatedAt
                    )";

                var result = await connection.ExecuteAsync(query, followUp);
                return result > 0;
            }
        }

        public async Task<FollowUp?> GetByIdAsync(string followUpId)
        {
            using (var connection = _dbConnectionFactory.GetConnection())
            {
                connection.Open();
                var query = "SELECT * FROM follow_ups WHERE follow_up_id = @FollowUpId";
                return await connection.QueryFirstOrDefaultAsync<FollowUp>(query, new { FollowUpId = followUpId });
            }
        }

        public async Task<IEnumerable<FollowUp>> GetByPersonAsync(string personId)
        {
            using (var connection = _dbConnectionFactory.GetConnection())
            {
                connection.Open();
                var query = "SELECT * FROM follow_ups WHERE person_id = @PersonId ORDER BY attempt_date DESC";
                return await connection.QueryAsync<FollowUp>(query, new { PersonId = personId });
            }
        }

        public async Task<IEnumerable<FollowUp>> GetByVolunteerAsync(string volunteerId)
        {
            using (var connection = _dbConnectionFactory.GetConnection())
            {
                connection.Open();
                var query = "SELECT * FROM follow_ups WHERE volunteer_id = @VolunteerId ORDER BY attempt_date DESC";
                return await connection.QueryAsync<FollowUp>(query, new { VolunteerId = volunteerId });
            }
        }

        public async Task<int> CountByPersonAsync(string personId)
        {
            using (var connection = _dbConnectionFactory.GetConnection())
            {
                connection.Open();
                var query = "SELECT COUNT(*) FROM follow_ups WHERE person_id = @PersonId";
                return await connection.ExecuteScalarAsync<int>(query, new { PersonId = personId });
            }
        }

        public async Task<int> GetMaxSequenceForYearAsync(int year)
        {
            using (var connection = _dbConnectionFactory.GetConnection())
            {
                connection.Open();
                // Assumes follow_up_id format: FU{year}{sequence}, e.g. FU20250001
                var like = $"FU{year}%";
                var query = "SELECT MAX(CAST(SUBSTRING(follow_up_id, 7) AS UNSIGNED)) FROM follow_ups WHERE follow_up_id LIKE @Like";
                // SUBSTRING starting index depends on format: 'FU' + 4-digit year = 6 chars, so start at 7
                var result = await connection.ExecuteScalarAsync<object?>(query, new { Like = like });
                if (result == null || result == DBNull.Value) return 0;
                return Convert.ToInt32(result);
            }
        }
    }
}
