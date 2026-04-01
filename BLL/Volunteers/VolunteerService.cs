using RM_CMS.Data.Models;
using RM_CMS.Data.DTO;
using RM_CMS.DAL.Volunteers;
using RM_CMS.Data.DTO.Volunteers;

namespace RM_CMS.BLL.Volunteers
{
    public interface IVolunteerService
    {
    }

    public class VolunteerService : IVolunteerService
    {
        private readonly IVolunteerRepository _repository;

        public VolunteerService(IVolunteerRepository repository)
        {
            _repository = repository;
        }
    }
}
