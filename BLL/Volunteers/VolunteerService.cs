using RM_CMS.Data.Models;
using RM_CMS.Data.DTO;
using RM_CMS.DAL.Volunteers;

namespace RM_CMS.BLL.Volunteers
{
    public interface IVolunteerService
    {
        Task<IEnumerable<VolunteerResponseDto>> GetAllAsync();
        Task<VolunteerResponseDto?> GetByIdAsync(string volunteerId);
        Task<IEnumerable<VolunteerResponseDto>> GetByStatusAsync(string status);
        Task<IEnumerable<VolunteerResponseDto>> GetByTeamLeadAsync(string teamLeadId);
        Task<IEnumerable<VolunteerResponseDto>> GetByCapacityBandAsync(string capacityBand);
        Task<VolunteerResponseDto?> CreateAsync(CreateVolunteerDto dto);
        Task<bool> UpdateAsync(string volunteerId, UpdateVolunteerDto dto);
        Task<bool> DeleteAsync(string volunteerId);
        Task<int> GetTotalCountAsync();
        Task<(IEnumerable<VolunteerResponseDto> Data, int TotalCount)> GetPaginatedAsync(int pageNumber, int pageSize);
        Task<bool> UpdateStatusAsync(string volunteerId, string status);
        Task<bool> UpdateCapacityAsync(string volunteerId, string capacityBand, int min, int max);
        Task<bool> UpdatePerformanceAsync(string volunteerId, int completed, int assigned);
        Task<bool> UpdateCheckInAsync(string volunteerId, DateTime? lastCheckIn, DateTime? nextCheckIn);
        Task<int> GetActiveVolunteerCountAsync();
        Task<IEnumerable<VolunteerResponseDto>> GetWithLowCompletionRateAsync(decimal threshold);
        string GenerateVolunteerId();
    }

    public class VolunteerService : IVolunteerService
    {
        private readonly IVolunteerRepository _repository;

        public VolunteerService(IVolunteerRepository repository)
        {
            _repository = repository;
        }

        public async Task<IEnumerable<VolunteerResponseDto>> GetAllAsync()
        {
            var volunteers = await _repository.GetAllAsync();
            return MapToResponseDtos(volunteers);
        }

        public async Task<VolunteerResponseDto?> GetByIdAsync(string volunteerId)
        {
            var volunteer = await _repository.GetByIdAsync(volunteerId);
            return volunteer == null ? null : MapToResponseDto(volunteer);
        }

        public async Task<IEnumerable<VolunteerResponseDto>> GetByStatusAsync(string status)
        {
            var volunteers = await _repository.GetByStatusAsync(status);
            return MapToResponseDtos(volunteers);
        }

        public async Task<IEnumerable<VolunteerResponseDto>> GetByTeamLeadAsync(string teamLeadId)
        {
            var volunteers = await _repository.GetByTeamLeadAsync(teamLeadId);
            return MapToResponseDtos(volunteers);
        }

        public async Task<IEnumerable<VolunteerResponseDto>> GetByCapacityBandAsync(string capacityBand)
        {
            var volunteers = await _repository.GetByCapacityBandAsync(capacityBand);
            return MapToResponseDtos(volunteers);
        }

        public async Task<VolunteerResponseDto?> CreateAsync(CreateVolunteerDto dto)
        {
            var volunteerId = GenerateVolunteerId();
            var now = DateTime.UtcNow;

            var volunteer = new Volunteer
            {
                VolunteerId = volunteerId,
                FirstName = dto.FirstName,
                LastName = dto.LastName,
                Email = dto.Email,
                Phone = dto.Phone,
                Status = dto.Status ?? "Active",
                Level = dto.Level ?? "Level 0",
                StartDate = dto.StartDate,
                EndDate = dto.EndDate,
                CapacityBand = dto.CapacityBand ?? "Balanced",
                CapacityMin = dto.CapacityMin,
                CapacityMax = dto.CapacityMax,
                CurrentAssignments = 0,
                TotalCompleted = 0,
                TotalAssigned = 0,
                CompletionRate = 0,
                AvgResponseTime = 0,
                TeamLead = dto.TeamLead,
                Campus = null,
                Level0Complete = dto.Level0Complete,
                CrisisTrained = dto.CrisisTrained,
                ConfidentialitySigned = dto.ConfidentialitySigned,
                BackgroundCheck = dto.BackgroundCheck,
                BoundaryViolations = 0,
                CreatedAt = now,
                UpdatedAt = now
            };

            var success = await _repository.CreateAsync(volunteer);
            return success ? MapToResponseDto(volunteer) : null;
        }

        public async Task<bool> UpdateAsync(string volunteerId, UpdateVolunteerDto dto)
        {
            var existing = await _repository.GetByIdAsync(volunteerId);
            if (existing == null) return false;

            existing.FirstName = dto.FirstName ?? existing.FirstName;
            existing.LastName = dto.LastName ?? existing.LastName;
            existing.Email = dto.Email ?? existing.Email;
            existing.Phone = dto.Phone ?? existing.Phone;
            existing.Status = dto.Status ?? existing.Status;
            existing.Level = dto.Level ?? existing.Level;
            existing.EndDate = dto.EndDate ?? existing.EndDate;
            existing.CapacityBand = dto.CapacityBand ?? existing.CapacityBand;
            if (dto.CapacityMin.HasValue) existing.CapacityMin = dto.CapacityMin.Value;
            if (dto.CapacityMax.HasValue) existing.CapacityMax = dto.CapacityMax.Value;
            if (dto.CurrentAssignments.HasValue) existing.CurrentAssignments = dto.CurrentAssignments.Value;
            if (dto.CompletionRate.HasValue) existing.CompletionRate = dto.CompletionRate.Value;
            existing.EmotionalTone = dto.EmotionalTone ?? existing.EmotionalTone;
            if (dto.VnpsScore.HasValue) existing.VnpsScore = dto.VnpsScore.Value;
            existing.BurnoutRisk = dto.BurnoutRisk ?? existing.BurnoutRisk;
            existing.NextCheckIn = dto.NextCheckIn ?? existing.NextCheckIn;
            existing.Campus = dto.Campus ?? existing.Campus;
            existing.UpdatedAt = DateTime.UtcNow;

            return await _repository.UpdateAsync(existing);
        }

        public async Task<bool> DeleteAsync(string volunteerId)
        {
            return await _repository.DeleteAsync(volunteerId);
        }

        public async Task<int> GetTotalCountAsync()
        {
            return await _repository.GetTotalCountAsync();
        }

        public async Task<(IEnumerable<VolunteerResponseDto> Data, int TotalCount)> GetPaginatedAsync(int pageNumber, int pageSize)
        {
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1 || pageSize > 100) pageSize = 10;

            var data = await _repository.GetPaginatedAsync(pageNumber, pageSize);
            var totalCount = await _repository.GetTotalCountAsync();

            return (MapToResponseDtos(data), totalCount);
        }

        public async Task<bool> UpdateStatusAsync(string volunteerId, string status)
        {
            return await _repository.UpdateStatusAsync(volunteerId, status);
        }

        public async Task<bool> UpdateCapacityAsync(string volunteerId, string capacityBand, int min, int max)
        {
            return await _repository.UpdateCapacityAsync(volunteerId, capacityBand, min, max);
        }

        public async Task<bool> UpdatePerformanceAsync(string volunteerId, int completed, int assigned)
        {
            return await _repository.UpdatePerformanceAsync(volunteerId, completed, assigned);
        }

        public async Task<bool> UpdateCheckInAsync(string volunteerId, DateTime? lastCheckIn, DateTime? nextCheckIn)
        {
            return await _repository.UpdateCheckInAsync(volunteerId, lastCheckIn, nextCheckIn);
        }

        public async Task<int> GetActiveVolunteerCountAsync()
        {
            return await _repository.GetActiveVolunteerCountAsync();
        }

        public async Task<IEnumerable<VolunteerResponseDto>> GetWithLowCompletionRateAsync(decimal threshold)
        {
            var volunteers = await _repository.GetWithLowCompletionRateAsync(threshold);
            return MapToResponseDtos(volunteers);
        }

        public string GenerateVolunteerId()
        {
            return $"V{DateTime.UtcNow:yyyyMMddHHmmss}{Random.Shared.Next(1000, 9999)}";
        }

        private VolunteerResponseDto MapToResponseDto(Volunteer volunteer)
        {
            return new VolunteerResponseDto
            {
                VolunteerId = volunteer.VolunteerId,
                FirstName = volunteer.FirstName,
                LastName = volunteer.LastName,
                Email = volunteer.Email,
                Phone = volunteer.Phone,
                Status = volunteer.Status,
                Level = volunteer.Level,
                StartDate = volunteer.StartDate,
                EndDate = volunteer.EndDate,
                CapacityBand = volunteer.CapacityBand,
                CapacityMin = volunteer.CapacityMin,
                CapacityMax = volunteer.CapacityMax,
                CurrentAssignments = volunteer.CurrentAssignments,
                TotalCompleted = volunteer.TotalCompleted,
                TotalAssigned = volunteer.TotalAssigned,
                CompletionRate = volunteer.CompletionRate,
                AvgResponseTime = volunteer.AvgResponseTime,
                LastCheckIn = volunteer.LastCheckIn,
                NextCheckIn = volunteer.NextCheckIn,
                EmotionalTone = volunteer.EmotionalTone,
                VnpsScore = volunteer.VnpsScore,
                BurnoutRisk = volunteer.BurnoutRisk,
                TeamLead = volunteer.TeamLead,
                Campus = volunteer.Campus,
                Level0Complete = volunteer.Level0Complete,
                CrisisTrained = volunteer.CrisisTrained,
                ConfidentialitySigned = volunteer.ConfidentialitySigned,
                BackgroundCheck = volunteer.BackgroundCheck,
                BoundaryViolations = volunteer.BoundaryViolations,
                LastViolationDate = volunteer.LastViolationDate,
                CreatedAt = volunteer.CreatedAt,
                UpdatedAt = volunteer.UpdatedAt
            };
        }

        private IEnumerable<VolunteerResponseDto> MapToResponseDtos(IEnumerable<Volunteer> volunteers)
        {
            return volunteers.Select(MapToResponseDto).ToList();
        }
    }
}
