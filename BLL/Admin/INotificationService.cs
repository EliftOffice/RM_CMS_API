using RM_CMS.Data.DTO;
using RM_CMS.Utilities;
using System.Threading.Tasks;

namespace RM_CMS.BLL.Admin
{
    public interface INotificationService
    {
        Task<ApiResponse<object>> BroadcastTelegramToVolunteersAsync(string message);
    }
}