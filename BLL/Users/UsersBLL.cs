using RM_CMS.DAL.Users;
using RM_CMS.Data.DTO.Users;
using RM_CMS.Utilities;

namespace RM_CMS.BLL.Users
{
    public interface IUsersBLL
    {
        Task<ApiResponse<UserDto>> CheckUserAsync(string mobileNumber);
        Task<ApiResponse<UserDto>> RegisterUserAsync(string name, string mobileNumber);
    }

    public class UsersBLL : IUsersBLL
    {
        private readonly IUsersDAL _usersDAL;

        public UsersBLL(IUsersDAL usersDAL)
        {
            _usersDAL = usersDAL;
        }

        public async Task<ApiResponse<UserDto>> CheckUserAsync(string mobileNumber)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(mobileNumber))
                {
                    return new ApiResponse<UserDto>(ResponseType.Warning, "Mobile number is required", null);
                }

                return await _usersDAL.GetByMobileAsync(mobileNumber.Trim());
            }
            catch (Exception ex)
            {
                return new ApiResponse<UserDto>(ResponseType.Error, $"Error checking user: {ex.Message}", null);
            }
        }

        public async Task<ApiResponse<UserDto>> RegisterUserAsync(string name, string mobileNumber)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(mobileNumber))
                {
                    return new ApiResponse<UserDto>(ResponseType.Warning, "Name and mobile number are required", null);
                }

                var mobile = mobileNumber.Trim();
                var existing = await _usersDAL.GetByMobileAsync(mobile);

                if (existing.ResponseType == ResponseType.Success && existing.Data != null)
                {
                    return new ApiResponse<UserDto>(ResponseType.Success, "User already exists", existing.Data);
                }

                return await _usersDAL.CreateAsync(name.Trim(), mobile);
            }
            catch (Exception ex)
            {
                return new ApiResponse<UserDto>(ResponseType.Error, $"Error registering user: {ex.Message}", null);
            }
        }
    }
}
