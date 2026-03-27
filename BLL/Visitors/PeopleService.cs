using RM_CMS.Data.Models;
using RM_CMS.Data.DTO;
using RM_CMS.DAL.Visitors;

namespace RM_CMS.BLL.Visitors
{
    public interface IPeopleService
    {
        Task<IEnumerable<PeopleResponseDto>> GetAllAsync();
        Task<PeopleResponseDto?> GetByIdAsync(string personId);
        Task<IEnumerable<PeopleResponseDto>> GetByStatusAsync(string followUpStatus);
        Task<IEnumerable<PeopleResponseDto>> GetByAssignedVolunteerAsync(string volunteerId);
        Task<IEnumerable<PeopleResponseDto>> GetByPriorityAsync(string priority);
        Task<PeopleResponseDto?> CreateAsync(CreatePeopleDto dto);
        Task<bool> UpdateAsync(string personId, UpdatePeopleDto dto);
        Task<bool> DeleteAsync(string personId);
        Task<int> GetTotalCountAsync();
        Task<(IEnumerable<PeopleResponseDto> Data, int TotalCount)> GetPaginatedAsync(int pageNumber, int pageSize);
        Task<bool> UpdateFollowUpStatusAsync(string personId, string status);
        Task<bool> AssignVolunteerAsync(string personId, string volunteerId);
        Task<bool> UpdateContactAsync(string personId, DateTime lastContactDate, DateTime? nextActionDate);
        string GeneratePersonId();
    }

    public class PeopleService : IPeopleService
    {
        private readonly IPeopleRepository _repository;

        public PeopleService(IPeopleRepository repository)
        {
            _repository = repository;
        }

        public async Task<IEnumerable<PeopleResponseDto>> GetAllAsync()
        {
            var people = await _repository.GetAllAsync();
            return MapToResponseDtos(people);
        }

        public async Task<PeopleResponseDto?> GetByIdAsync(string personId)
        {
            var person = await _repository.GetByIdAsync(personId);
            return person == null ? null : MapToResponseDto(person);
        }

        public async Task<IEnumerable<PeopleResponseDto>> GetByStatusAsync(string followUpStatus)
        {
            var people = await _repository.GetByStatusAsync(followUpStatus);
            return MapToResponseDtos(people);
        }

        public async Task<IEnumerable<PeopleResponseDto>> GetByAssignedVolunteerAsync(string volunteerId)
        {
            var people = await _repository.GetByAssignedVolunteerAsync(volunteerId);
            return MapToResponseDtos(people);
        }

        public async Task<IEnumerable<PeopleResponseDto>> GetByPriorityAsync(string priority)
        {
            var people = await _repository.GetByPriorityAsync(priority);
            return MapToResponseDtos(people);
        }

        public async Task<PeopleResponseDto?> CreateAsync(CreatePeopleDto dto)
        {
            var personId = GeneratePersonId();
            var now = DateTime.UtcNow;

            var people = new People
            {
                PersonId = personId,
                FirstName = dto.FirstName,
                LastName = dto.LastName,
                Email = dto.Email,
                Phone = dto.Phone,
                AgeRange = dto.AgeRange,
                HouseholdType = dto.HouseholdType,
                ZipCode = dto.ZipCode,
                VisitType = dto.VisitType,
                FirstVisitDate = dto.FirstVisitDate,
                LastVisitDate = dto.LastVisitDate,
                VisitCount = 1,
                ConnectionSource = dto.ConnectionSource,
                Campus = dto.Campus,
                FollowUpStatus = dto.FollowUpStatus ?? "New",
                FollowUpPriority = dto.FollowUpPriority ?? "Normal",
                AssignedVolunteer = dto.AssignedVolunteer,
                AssignedDate = !string.IsNullOrEmpty(dto.AssignedVolunteer) ? now : null,
                InterestedIn = dto.InterestedIn,
                PrayerRequests = dto.PrayerRequests,
                SpecificNeeds = dto.SpecificNeeds,
                CreatedAt = now,
                UpdatedAt = now,
                CreatedBy = dto.CreatedBy
            };

            var success = await _repository.CreateAsync(people);
            return success ? MapToResponseDto(people) : null;
        }

        public async Task<bool> UpdateAsync(string personId, UpdatePeopleDto dto)
        {
            var existing = await _repository.GetByIdAsync(personId);
            if (existing == null) return false;

            existing.FirstName = dto.FirstName ?? existing.FirstName;
            existing.LastName = dto.LastName ?? existing.LastName;
            existing.Email = dto.Email ?? existing.Email;
            existing.Phone = dto.Phone ?? existing.Phone;
            existing.AgeRange = dto.AgeRange ?? existing.AgeRange;
            existing.HouseholdType = dto.HouseholdType ?? existing.HouseholdType;
            existing.ZipCode = dto.ZipCode ?? existing.ZipCode;
            existing.VisitType = dto.VisitType ?? existing.VisitType;
            existing.LastVisitDate = dto.LastVisitDate ?? existing.LastVisitDate;
            existing.ConnectionSource = dto.ConnectionSource ?? existing.ConnectionSource;
            existing.Campus = dto.Campus ?? existing.Campus;
            existing.FollowUpStatus = dto.FollowUpStatus ?? existing.FollowUpStatus;
            existing.FollowUpPriority = dto.FollowUpPriority ?? existing.FollowUpPriority;
            existing.AssignedVolunteer = dto.AssignedVolunteer ?? existing.AssignedVolunteer;
            existing.LastContactDate = dto.LastContactDate ?? existing.LastContactDate;
            existing.NextActionDate = dto.NextActionDate ?? existing.NextActionDate;
            existing.InterestedIn = dto.InterestedIn ?? existing.InterestedIn;
            existing.PrayerRequests = dto.PrayerRequests ?? existing.PrayerRequests;
            existing.SpecificNeeds = dto.SpecificNeeds ?? existing.SpecificNeeds;
            existing.UpdatedAt = DateTime.UtcNow;

            return await _repository.UpdateAsync(existing);
        }

        public async Task<bool> DeleteAsync(string personId)
        {
            return await _repository.DeleteAsync(personId);
        }

        public async Task<int> GetTotalCountAsync()
        {
            return await _repository.GetTotalCountAsync();
        }

        public async Task<(IEnumerable<PeopleResponseDto> Data, int TotalCount)> GetPaginatedAsync(int pageNumber, int pageSize)
        {
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1 || pageSize > 100) pageSize = 10;

            var data = await _repository.GetPaginatedAsync(pageNumber, pageSize);
            var totalCount = await _repository.GetTotalCountAsync();

            return (MapToResponseDtos(data), totalCount);
        }

        public async Task<bool> UpdateFollowUpStatusAsync(string personId, string status)
        {
            return await _repository.UpdateFollowUpStatusAsync(personId, status);
        }

        public async Task<bool> AssignVolunteerAsync(string personId, string volunteerId)
        {
            return await _repository.UpdateAssignmentAsync(personId, volunteerId, DateTime.UtcNow);
        }

        public async Task<bool> UpdateContactAsync(string personId, DateTime lastContactDate, DateTime? nextActionDate)
        {
            return await _repository.UpdateLastContactAsync(personId, lastContactDate, nextActionDate);
        }

        public string GeneratePersonId()
        {
            return $"P{DateTime.UtcNow:yyyyMMddHHmmss}{Random.Shared.Next(1000, 9999)}";
        }

        private PeopleResponseDto MapToResponseDto(People people)
        {
            return new PeopleResponseDto
            {
                PersonId = people.PersonId,
                FirstName = people.FirstName,
                LastName = people.LastName,
                Email = people.Email,
                Phone = people.Phone,
                AgeRange = people.AgeRange,
                HouseholdType = people.HouseholdType,
                ZipCode = people.ZipCode,
                VisitType = people.VisitType,
                FirstVisitDate = people.FirstVisitDate,
                LastVisitDate = people.LastVisitDate,
                VisitCount = people.VisitCount,
                ConnectionSource = people.ConnectionSource,
                Campus = people.Campus,
                FollowUpStatus = people.FollowUpStatus,
                FollowUpPriority = people.FollowUpPriority,
                AssignedVolunteer = people.AssignedVolunteer,
                AssignedDate = people.AssignedDate,
                LastContactDate = people.LastContactDate,
                NextActionDate = people.NextActionDate,
                InterestedIn = people.InterestedIn,
                PrayerRequests = people.PrayerRequests,
                SpecificNeeds = people.SpecificNeeds,
                CreatedAt = people.CreatedAt,
                UpdatedAt = people.UpdatedAt
            };
        }

        private IEnumerable<PeopleResponseDto> MapToResponseDtos(IEnumerable<People> people)
        {
            return people.Select(MapToResponseDto).ToList();
        }
    }
}
