using Dapper;
using RM_CMS.Data;
using RM_CMS.Data.DTO.Users;
using RM_CMS.Data.Models;
using RM_CMS.Utilities;

namespace RM_CMS.DAL.Users
{
    public interface IUsersDAL
    {
        Task<ApiResponse<UserDto>> GetByMobileAsync(string mobileNumber);
        Task<ApiResponse<UserDto>> CreateAsync(string name, string mobileNumber);
    }

    public class UsersDAL : IUsersDAL
    {
        private readonly IDbConnectionFactory _dbFactory;

        public UsersDAL(IDbConnectionFactory dbFactory)
        {
            _dbFactory = dbFactory;
        }

        public async Task<ApiResponse<UserDto>> GetByMobileAsync(string mobileNumber)
        {
            try
            {
                const string sql = @"
                    SELECT
                        id AS Id,
                        name AS Name,
                        mobile_number AS MobileNumber
                    FROM app_users
                    WHERE mobile_number = @MobileNumber
                    LIMIT 1;";

                using var connection = _dbFactory.GetConnection();
                var user = await connection.QueryFirstOrDefaultAsync<UserDto>(sql, new { MobileNumber = mobileNumber });

                if (user == null)
                {
                    return new ApiResponse<UserDto>(ResponseType.Warning, "User not found", null);
                }

                return new ApiResponse<UserDto>(ResponseType.Success, "User found", user);
            }
            catch (Exception ex)
            {
                return new ApiResponse<UserDto>(ResponseType.Error, $"Error retrieving user: {ex.Message}", null);
            }
        }

        public async Task<ApiResponse<UserDto>> CreateAsync(string name, string mobileNumber)
        {
            try
            {
                const string sql = @"
                    INSERT INTO app_users (name, mobile_number)
                    VALUES (@Name, @MobileNumber);
                    SELECT LAST_INSERT_ID();";

                using var connection = _dbFactory.GetConnection();
                var id = await connection.ExecuteScalarAsync<long>(sql, new { Name = name, MobileNumber = mobileNumber });

                return new ApiResponse<UserDto>(ResponseType.Success, "User created successfully", new UserDto
                {
                    Id = id,
                    Name = name,
                    MobileNumber = mobileNumber
                });
            }
            catch (Exception ex)
            {
                return new ApiResponse<UserDto>(ResponseType.Error, $"Error creating user: {ex.Message}", null);
            }
        }
    }
}
