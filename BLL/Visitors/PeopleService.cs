using RM_CMS.Data.Models;
using RM_CMS.DAL.Visitors;
using RM_CMS.Data.DTO.Visitors;

namespace RM_CMS.BLL.Visitors
{
    public interface IPeopleService
    {
        Task<RM_CMS.Data.ApiResponse<IEnumerable<PeopleResponseDto>>> GetAllAsync();
        Task<RM_CMS.Data.ApiResponse<PeopleResponseDto>> GetByIdAsync(string personId);
        Task<RM_CMS.Data.ApiResponse<IEnumerable<PeopleResponseDto>>> GetByStatusAsync(string followUpStatus);
        Task<RM_CMS.Data.ApiResponse<IEnumerable<PeopleResponseDto>>> GetByAssignedVolunteerAsync(string volunteerId);
        Task<RM_CMS.Data.ApiResponse<IEnumerable<PeopleResponseDto>>> GetByPriorityAsync(string priority);
        Task<RM_CMS.Data.ApiResponse<PeopleResponseDto>> CreateAsync(CreatePeopleDto dto);
        Task<RM_CMS.Data.ApiResponse<bool>> UpdateAsync(string personId, UpdatePeopleDto dto);
        Task<RM_CMS.Data.ApiResponse<bool>> DeleteAsync(string personId);
        Task<RM_CMS.Data.ApiResponse<int>> GetTotalCountAsync();
        Task<RM_CMS.Data.ApiResponse<RM_CMS.Data.PaginatedResult<PeopleResponseDto>>> GetPaginatedAsync(int pageNumber, int pageSize);
        Task<RM_CMS.Data.ApiResponse<bool>> UpdateFollowUpStatusAsync(string personId, string status);
        Task<RM_CMS.Data.ApiResponse<bool>> AssignVolunteerAsync(string personId, string volunteerId);
        Task<RM_CMS.Data.ApiResponse<bool>> UpdateContactAsync(string personId, DateTime lastContactDate, DateTime? nextActionDate);
        Task<RM_CMS.Data.ApiResponse<string>> GeneratePersonId();
    }

    public class PeopleService : IPeopleService
    {
        private readonly IPeopleRepository _repository;

        public PeopleService(IPeopleRepository repository)
        {
            _repository = repository;
        }

        public async Task<RM_CMS.Data.ApiResponse<IEnumerable<PeopleResponseDto>>> GetAllAsync()
        {
            try
            {
                var people = await _repository.GetAllAsync();
                return RM_CMS.Data.ApiResponse<IEnumerable<PeopleResponseDto>>.Success(MapToResponseDtos(people));
            }
            catch (Exception ex)
            {
                return RM_CMS.Data.ApiResponse<IEnumerable<PeopleResponseDto>>.Error("Failed to get people", ex.Message);
            }
        }

        public async Task<RM_CMS.Data.ApiResponse<PeopleResponseDto>> GetByIdAsync(string personId)
        {
            try
            {
                var person = await _repository.GetByIdAsync(personId);
                if (person == null) return RM_CMS.Data.ApiResponse<PeopleResponseDto>.Error("Person not found", null, 404);
                return RM_CMS.Data.ApiResponse<PeopleResponseDto>.Success(MapToResponseDto(person));
            }
            catch (Exception ex)
            {
                return RM_CMS.Data.ApiResponse<PeopleResponseDto>.Error("Failed to get person", ex.Message);
            }
        }

        public async Task<RM_CMS.Data.ApiResponse<IEnumerable<PeopleResponseDto>>> GetByStatusAsync(string followUpStatus)
        {
            try
            {
                var people = await _repository.GetByStatusAsync(followUpStatus);
                return RM_CMS.Data.ApiResponse<IEnumerable<PeopleResponseDto>>.Success(MapToResponseDtos(people));
            }
            catch (Exception ex)
            {
                return RM_CMS.Data.ApiResponse<IEnumerable<PeopleResponseDto>>.Error("Failed to get people by status", ex.Message);
            }
        }

        public async Task<RM_CMS.Data.ApiResponse<IEnumerable<PeopleResponseDto>>> GetByAssignedVolunteerAsync(string volunteerId)
        {
            try
            {
                var people = await _repository.GetByAssignedVolunteerAsync(volunteerId);
                return RM_CMS.Data.ApiResponse<IEnumerable<PeopleResponseDto>>.Success(MapToResponseDtos(people));
            }
            catch (Exception ex)
            {
                return RM_CMS.Data.ApiResponse<IEnumerable<PeopleResponseDto>>.Error("Failed to get people by volunteer", ex.Message);
            }
        }

        public async Task<RM_CMS.Data.ApiResponse<IEnumerable<PeopleResponseDto>>> GetByPriorityAsync(string priority)
        {
            try
            {
                var people = await _repository.GetByPriorityAsync(priority);
                return RM_CMS.Data.ApiResponse<IEnumerable<PeopleResponseDto>>.Success(MapToResponseDtos(people));
            }
            catch (Exception ex)
            {
                return RM_CMS.Data.ApiResponse<IEnumerable<PeopleResponseDto>>.Error("Failed to get people by priority", ex.Message);
            }
        }

        public async Task<RM_CMS.Data.ApiResponse<PeopleResponseDto>> CreateAsync(CreatePeopleDto dto)
        {
            try
            {
                var genId = await GeneratePersonId();
                var personId = genId.Data!;
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
                if (!success) return RM_CMS.Data.ApiResponse<PeopleResponseDto>.Error("Failed to create person");
                return RM_CMS.Data.ApiResponse<PeopleResponseDto>.Success(MapToResponseDto(people), "Person created", 201);
            }
            catch (Exception ex)
            {
                return RM_CMS.Data.ApiResponse<PeopleResponseDto>.Error("Failed to create person", ex.Message);
            }
        }

        public async Task<RM_CMS.Data.ApiResponse<bool>> UpdateAsync(string personId, UpdatePeopleDto dto)
        {
            try
            {
                var existing = await _repository.GetByIdAsync(personId);
                if (existing == null) return RM_CMS.Data.ApiResponse<bool>.Error("Person not found", null, 404);

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

                var updated = await _repository.UpdateAsync(existing);
                return updated ? RM_CMS.Data.ApiResponse<bool>.Success(true, "Person updated") : RM_CMS.Data.ApiResponse<bool>.Error("Failed to update person");
            }
            catch (Exception ex)
            {
                return RM_CMS.Data.ApiResponse<bool>.Error("Failed to update person", ex.Message);
            }
        }

        public async Task<RM_CMS.Data.ApiResponse<bool>> DeleteAsync(string personId)
        {
            try
            {
                var deleted = await _repository.DeleteAsync(personId);
                return deleted ? RM_CMS.Data.ApiResponse<bool>.Success(true, "Person deleted") : RM_CMS.Data.ApiResponse<bool>.Error("Person not found", null, 404);
            }
            catch (Exception ex)
            {
                return RM_CMS.Data.ApiResponse<bool>.Error("Failed to delete person", ex.Message);
            }
        }

        public async Task<RM_CMS.Data.ApiResponse<int>> GetTotalCountAsync()
        {
            try
            {
                var count = await _repository.GetTotalCountAsync();
                return RM_CMS.Data.ApiResponse<int>.Success(count);
            }
            catch (Exception ex)
            {
                return RM_CMS.Data.ApiResponse<int>.Error("Failed to get total count", ex.Message);
            }
        }

        public async Task<RM_CMS.Data.ApiResponse<RM_CMS.Data.PaginatedResult<PeopleResponseDto>>> GetPaginatedAsync(int pageNumber, int pageSize)
        {
            try
            {
                if (pageNumber < 1) pageNumber = 1;
                if (pageSize < 1 || pageSize > 100) pageSize = 10;

                var data = await _repository.GetPaginatedAsync(pageNumber, pageSize);
                var totalCount = await _repository.GetTotalCountAsync();

                var result = new RM_CMS.Data.PaginatedResult<PeopleResponseDto>
                {
                    Data = MapToResponseDtos(data),
                    TotalCount = totalCount
                };

                return RM_CMS.Data.ApiResponse<RM_CMS.Data.PaginatedResult<PeopleResponseDto>>.Success(result);
            }
            catch (Exception ex)
            {
                return RM_CMS.Data.ApiResponse<RM_CMS.Data.PaginatedResult<PeopleResponseDto>>.Error("Failed to get paginated people", ex.Message);
            }
        }

        public async Task<RM_CMS.Data.ApiResponse<bool>> UpdateFollowUpStatusAsync(string personId, string status)
        {
            try
            {
                var updated = await _repository.UpdateFollowUpStatusAsync(personId, status);
                return updated ? RM_CMS.Data.ApiResponse<bool>.Success(true, "Status updated") : RM_CMS.Data.ApiResponse<bool>.Error("Person not found", null, 404);
            }
            catch (Exception ex)
            {
                return RM_CMS.Data.ApiResponse<bool>.Error("Failed to update status", ex.Message);
            }
        }

        public async Task<RM_CMS.Data.ApiResponse<bool>> AssignVolunteerAsync(string personId, string volunteerId)
        {
            try
            {
                var updated = await _repository.UpdateAssignmentAsync(personId, volunteerId, DateTime.UtcNow);
                return updated ? RM_CMS.Data.ApiResponse<bool>.Success(true, "Volunteer assigned") : RM_CMS.Data.ApiResponse<bool>.Error("Person not found", null, 404);
            }
            catch (Exception ex)
            {
                return RM_CMS.Data.ApiResponse<bool>.Error("Failed to assign volunteer", ex.Message);
            }
        }

        public async Task<RM_CMS.Data.ApiResponse<bool>> UpdateContactAsync(string personId, DateTime lastContactDate, DateTime? nextActionDate)
        {
            try
            {
                var updated = await _repository.UpdateLastContactAsync(personId, lastContactDate, nextActionDate);
                return updated ? RM_CMS.Data.ApiResponse<bool>.Success(true, "Contact updated") : RM_CMS.Data.ApiResponse<bool>.Error("Person not found", null, 404);
            }
            catch (Exception ex)
            {
                return RM_CMS.Data.ApiResponse<bool>.Error("Failed to update contact", ex.Message);
            }
        }

        public Task<RM_CMS.Data.ApiResponse<string>> GeneratePersonId()
        {
            try
            {
                var id = $"P{DateTime.UtcNow:yyyyMMddHHmmss}{Random.Shared.Next(1000, 9999)}";
                return Task.FromResult(RM_CMS.Data.ApiResponse<string>.Success(id));
            }
            catch (Exception ex)
            {
                return Task.FromResult(RM_CMS.Data.ApiResponse<string>.Error("Failed to generate person id", ex.Message));
            }
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
