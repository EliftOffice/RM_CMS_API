using Dapper;
using RM_CMS.Data;
using RM_CMS.Data.DTO;
using RM_CMS.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RM_CMS.DAL.Admin
{
    public class SystemConfigRepository : ISystemConfigRepository
    {
        private readonly IDbConnectionFactory _dbConnectionFactory;

        public SystemConfigRepository(IDbConnectionFactory dbConnectionFactory)
        {
            _dbConnectionFactory = dbConnectionFactory;
        }

        public async Task<ApiResponse<IEnumerable<SystemConfigDto>>> GetAllAsync()
        {
            try
            {
                using var connection = _dbConnectionFactory.GetConnection();
                const string query = "SELECT config_key, config_value, config_type, description FROM system_config ORDER BY config_key;";
                var data = await connection.QueryAsync<SystemConfigDto>(query);
                return new ApiResponse<IEnumerable<SystemConfigDto>>(ResponseType.Success, "Configuration retrieved successfully.", data);
            }
            catch (Exception ex)
            {
                return new ApiResponse<IEnumerable<SystemConfigDto>>(ResponseType.Error, $"Error retrieving configuration: {ex.Message}", null);
            }
        }

        public async Task<ApiResponse<bool>> UpdateAsync(IEnumerable<UpdateSystemConfigDto> configs)
        {
            try
            {
                using var connection = _dbConnectionFactory.GetConnection();
                connection.Open();
                using var transaction = connection.BeginTransaction();

                const string sql = @"
                    UPDATE system_config 
                    SET config_value = @ConfigValue, updated_at = CURRENT_TIMESTAMP
                    WHERE config_key = @ConfigKey;";

                int totalRowsAffected = 0;
                foreach (var config in configs)
                {
                    totalRowsAffected += await connection.ExecuteAsync(sql, config, transaction);
                }

                transaction.Commit();

                if (totalRowsAffected < configs.Count())
                {
                    return new ApiResponse<bool>(ResponseType.Warning, $"Some settings might not have been updated. {totalRowsAffected}/{configs.Count()} records affected.", false);
                }

                return new ApiResponse<bool>(ResponseType.Success, "Configuration updated successfully.", true);
            }
            catch (Exception ex)
            {
                return new ApiResponse<bool>(ResponseType.Error, $"Error updating configuration: {ex.Message}", false);
            }
        }
    }
}