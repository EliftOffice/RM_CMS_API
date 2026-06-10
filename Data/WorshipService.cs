using RM_CMS.DAL.Worship;
using RM_CMS.DTOs.Worship;

namespace RM_CMS.BLL.Worship
{
    public class WorshipService : IWorshipService
    {
        private readonly IWorshipRepository _worshipRepository;

        public WorshipService(IWorshipRepository worshipRepository)
        {
            _worshipRepository = worshipRepository;
        }

        public async Task<RecommendedSongsDto> GenerateSongsAsync()
        {
            var today = DateTime.Today;
            string category = "NORMAL";

            // Check if today is Christmas
            if (today.Month == 12 && today.Day == 25)
            {
                category = "CHRISTMAS";
            }
            // Check if today is the 1st Sunday of the month
            else if (today.DayOfWeek == DayOfWeek.Sunday && today.Day <= 7)
            {
                category = "COMMUNION";
            }

            var requestDto = new GenerateSongsRequestDto { ServiceDate = today, ServiceCategory = category, ServiceType = 0 };

            return await _worshipRepository.GenerateAndAssignSongsAsync(requestDto);
        }
    }
}