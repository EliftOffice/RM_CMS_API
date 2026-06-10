using RM_CMS.DTOs.Worship;

namespace RM_CMS.BLL.Worship
{
    public interface IWorshipService
    {
        Task<RecommendedSongsDto> GenerateSongsAsync();
    }
}