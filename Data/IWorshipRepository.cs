using RM_CMS.DTOs.Worship;

namespace RM_CMS.DAL.Worship
{
    public interface IWorshipRepository
    {
        Task<RecommendedSongsDto> GenerateAndAssignSongsAsync(GenerateSongsRequestDto request);
    }
}