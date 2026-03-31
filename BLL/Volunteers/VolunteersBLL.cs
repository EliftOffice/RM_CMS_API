using RM_CMS.Data.Models;
using RM_CMS.Data.DTO;
using RM_CMS.DAL.Volunteers;
using RM_CMS.Data.DTO.Volunteers;

namespace RM_CMS.BLL.Volunteers
{
    public interface IVolunteerService
    {
        Task<RM_CMS.Data.ApiResponse<IEnumerable<VolunteerResponseDto>>> GetAllAsync();
        Task<RM_CMS.Data.ApiResponse<VolunteerResponseDto>> GetByIdAsync(string volunteerId);
        Task<RM_CMS.Data.ApiResponse<IEnumerable<VolunteerResponseDto>>> GetByStatusAsync(string status);
        Task<RM_CMS.Data.ApiResponse<IEnumerable<VolunteerResponseDto>>> GetByTeamLeadAsync(string teamLeadId);
        Task<RM_CMS.Data.ApiResponse<IEnumerable<VolunteerResponseDto>>> GetByCapacityBandAsync(string capacityBand);
        Task<RM_CMS.Data.ApiResponse<VolunteerResponseDto>> CreateAsync(CreateVolunteerDto dto);
        Task<RM_CMS.Data.ApiResponse<bool>> UpdateAsync(string volunteerId, UpdateVolunteerDto dto);
        Task<RM_CMS.Data.ApiResponse<bool>> DeleteAsync(string volunteerId);
        Task<RM_CMS.Data.ApiResponse<int>> GetTotalCountAsync();
        Task<RM_CMS.Data.ApiResponse<RM_CMS.Data.PaginatedResult<VolunteerResponseDto>>> GetPaginatedAsync(int pageNumber, int pageSize);
        Task<RM_CMS.Data.ApiResponse<bool>> UpdateStatusAsync(string volunteerId, string status);
        Task<RM_CMS.Data.ApiResponse<bool>> UpdateCapacityAsync(string volunteerId, string capacityBand, int min, int max);
        Task<RM_CMS.Data.ApiResponse<bool>> UpdatePerformanceAsync(string volunteerId, int completed, int assigned);
        Task<RM_CMS.Data.ApiResponse<bool>> UpdateCheckInAsync(string volunteerId, DateTime? lastCheckIn, DateTime? nextCheckIn);
        Task<RM_CMS.Data.ApiResponse<int>> GetActiveVolunteerCountAsync();
        Task<RM_CMS.Data.ApiResponse<IEnumerable<VolunteerResponseDto>>> GetWithLowCompletionRateAsync(decimal threshold);
        Task<RM_CMS.Data.ApiResponse<string>> GenerateVolunteerId();
    }

    public class VolunteersBLL : IVolunteerService
    {
        private readonly IVolunteerRepository _repository;

        public VolunteersBLL(IVolunteerRepository repository)
        {
            _repository = repository;
        }

        public async Task<RM_CMS.Data.ApiResponse<IEnumerable<VolunteerResponseDto>>> GetAllAsync()
        {
            try
            {
                var volunteers = await _repository.GetAllAsync();
                return RM_CMS.Data.ApiResponse<IEnumerable<VolunteerResponseDto>>.Success(MapToResponseDtos(volunteers));
            }
            catch (Exception ex)
            {
                return RM_CMS.Data.ApiResponse<IEnumerable<VolunteerResponseDto>>.Error("Failed to get volunteers", ex.Message);
            }
        }

        public async Task<RM_CMS.Data.ApiResponse<VolunteerResponseDto>> GetByIdAsync(string volunteerId)
        {
            try
            {
                var volunteer = await _repository.GetByIdAsync(volunteerId);
                if (volunteer == null) return RM_CMS.Data.ApiResponse<VolunteerResponseDto>.Error("Volunteer not found", null, 404);
                return RM_CMS.Data.ApiResponse<VolunteerResponseDto>.Success(MapToResponseDto(volunteer));
            }
            catch (Exception ex)
            {
                return RM_CMS.Data.ApiResponse<VolunteerResponseDto>.Error("Failed to get volunteer", ex.Message);
            }
        }

        public async Task<RM_CMS.Data.ApiResponse<IEnumerable<VolunteerResponseDto>>> GetByStatusAsync(string status)
        {
            try
            {
                var volunteers = await _repository.GetByStatusAsync(status);
                return RM_CMS.Data.ApiResponse<IEnumerable<VolunteerResponseDto>>.Success(MapToResponseDtos(volunteers));
            }
            catch (Exception ex)
            {
                return RM_CMS.Data.ApiResponse<IEnumerable<VolunteerResponseDto>>.Error("Failed to get volunteers by status", ex.Message);
            }
        }

        public async Task<RM_CMS.Data.ApiResponse<IEnumerable<VolunteerResponseDto>>> GetByTeamLeadAsync(string teamLeadId)
        {
            try
            {
                var volunteers = await _repository.GetByTeamLeadAsync(teamLeadId);
                return RM_CMS.Data.ApiResponse<IEnumerable<VolunteerResponseDto>>.Success(MapToResponseDtos(volunteers));
            }
            catch (Exception ex)
            {
                return RM_CMS.Data.ApiResponse<IEnumerable<VolunteerResponseDto>>.Error("Failed to get volunteers by team lead", ex.Message);
            }
        }

        public async Task<RM_CMS.Data.ApiResponse<IEnumerable<VolunteerResponseDto>>> GetByCapacityBandAsync(string capacityBand)
        {
            try
            {
                var volunteers = await _repository.GetByCapacityBandAsync(capacityBand);
                return RM_CMS.Data.ApiResponse<IEnumerable<VolunteerResponseDto>>.Success(MapToResponseDtos(volunteers));
            }
            catch (Exception ex)
            {
                return RM_CMS.Data.ApiResponse<IEnumerable<VolunteerResponseDto>>.Error("Failed to get volunteers by capacity band", ex.Message);
            }
        }

        public async Task<RM_CMS.Data.ApiResponse<VolunteerResponseDto>> CreateAsync(CreateVolunteerDto dto)
        {
            try
            {
                var volunteerId = (await GenerateVolunteerId()).Data!;
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
                if (!success) return RM_CMS.Data.ApiResponse<VolunteerResponseDto>.Error("Failed to create volunteer");
                return RM_CMS.Data.ApiResponse<VolunteerResponseDto>.Success(MapToResponseDto(volunteer), "Volunteer created", 201);
            }
            catch (Exception ex)
            {
                return RM_CMS.Data.ApiResponse<VolunteerResponseDto>.Error("Failed to create volunteer", ex.Message);
            }
        }

        public async Task<RM_CMS.Data.ApiResponse<bool>> UpdateAsync(string volunteerId, UpdateVolunteerDto dto)
        {
            try
            {
                var existing = await _repository.GetByIdAsync(volunteerId);
                if (existing == null) return RM_CMS.Data.ApiResponse<bool>.Error("Volunteer not found", null, 404);

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

                var updated = await _repository.UpdateAsync(existing);
                return updated ? RM_CMS.Data.ApiResponse<bool>.Success(true, "Volunteer updated") : RM_CMS.Data.ApiResponse<bool>.Error("Failed to update volunteer");
            }
            catch (Exception ex)
            {
                return RM_CMS.Data.ApiResponse<bool>.Error("Failed to update volunteer", ex.Message);
            }
        }

        public async Task<RM_CMS.Data.ApiResponse<bool>> DeleteAsync(string volunteerId)
        {
            try
            {
                var deleted = await _repository.DeleteAsync(volunteerId);
                return deleted ? RM_CMS.Data.ApiResponse<bool>.Success(true, "Volunteer deleted") : RM_CMS.Data.ApiResponse<bool>.Error("Volunteer not found", null, 404);
            }
            catch (Exception ex)
            {
                return RM_CMS.Data.ApiResponse<bool>.Error("Failed to delete volunteer", ex.Message);
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

        public async Task<RM_CMS.Data.ApiResponse<RM_CMS.Data.PaginatedResult<VolunteerResponseDto>>> GetPaginatedAsync(int pageNumber, int pageSize)
        {
            try
            {
                if (pageNumber < 1) pageNumber = 1;
                if (pageSize < 1 || pageSize > 100) pageSize = 10;

                var data = await _repository.GetPaginatedAsync(pageNumber, pageSize);
                var totalCount = await _repository.GetTotalCountAsync();

                var result = new RM_CMS.Data.PaginatedResult<VolunteerResponseDto>
                {
                    Data = MapToResponseDtos(data),
                    TotalCount = totalCount
                };

                return RM_CMS.Data.ApiResponse<RM_CMS.Data.PaginatedResult<VolunteerResponseDto>>.Success(result);
            }
            catch (Exception ex)
            {
                return RM_CMS.Data.ApiResponse<RM_CMS.Data.PaginatedResult<VolunteerResponseDto>>.Error("Failed to get paginated volunteers", ex.Message);
            }
        }

        public async Task<RM_CMS.Data.ApiResponse<bool>> UpdateStatusAsync(string volunteerId, string status)
        {
            try
            {
                var updated = await _repository.UpdateStatusAsync(volunteerId, status);
                return updated ? RM_CMS.Data.ApiResponse<bool>.Success(true, "Status updated") : RM_CMS.Data.ApiResponse<bool>.Error("Volunteer not found", null, 404);
            }
            catch (Exception ex)
            {
                return RM_CMS.Data.ApiResponse<bool>.Error("Failed to update status", ex.Message);
            }
        }

        public async Task<RM_CMS.Data.ApiResponse<bool>> UpdateCapacityAsync(string volunteerId, string capacityBand, int min, int max)
        {
            try
            {
                var updated = await _repository.UpdateCapacityAsync(volunteerId, capacityBand, min, max);
                return updated ? RM_CMS.Data.ApiResponse<bool>.Success(true, "Capacity updated") : RM_CMS.Data.ApiResponse<bool>.Error("Volunteer not found", null, 404);
            }
            catch (Exception ex)
            {
                return RM_CMS.Data.ApiResponse<bool>.Error("Failed to update capacity", ex.Message);
            }
        }

        public async Task<RM_CMS.Data.ApiResponse<bool>> UpdatePerformanceAsync(string volunteerId, int completed, int assigned)
        {
            try
            {
                var updated = await _repository.UpdatePerformanceAsync(volunteerId, completed, assigned);
                return updated ? RM_CMS.Data.ApiResponse<bool>.Success(true, "Performance updated") : RM_CMS.Data.ApiResponse<bool>.Error("Volunteer not found", null, 404);
            }
            catch (Exception ex)
            {
                return RM_CMS.Data.ApiResponse<bool>.Error("Failed to update performance", ex.Message);
            }
        }

        public async Task<RM_CMS.Data.ApiResponse<bool>> UpdateCheckInAsync(string volunteerId, DateTime? lastCheckIn, DateTime? nextCheckIn)
        {
            try
            {
                var updated = await _repository.UpdateCheckInAsync(volunteerId, lastCheckIn, nextCheckIn);
                return updated ? RM_CMS.Data.ApiResponse<bool>.Success(true, "Check-in updated") : RM_CMS.Data.ApiResponse<bool>.Error("Volunteer not found", null, 404);
            }
            catch (Exception ex)
            {
                return RM_CMS.Data.ApiResponse<bool>.Error("Failed to update check-in", ex.Message);
            }
        }

        public async Task<RM_CMS.Data.ApiResponse<int>> GetActiveVolunteerCountAsync()
        {
            try
            {
                var count = await _repository.GetActiveVolunteerCountAsync();
                return RM_CMS.Data.ApiResponse<int>.Success(count);
            }
            catch (Exception ex)
            {
                return RM_CMS.Data.ApiResponse<int>.Error("Failed to get active volunteer count", ex.Message);
            }
        }

        public async Task<RM_CMS.Data.ApiResponse<IEnumerable<VolunteerResponseDto>>> GetWithLowCompletionRateAsync(decimal threshold)
        {
            try
            {
                var volunteers = await _repository.GetWithLowCompletionRateAsync(threshold);
                return RM_CMS.Data.ApiResponse<IEnumerable<VolunteerResponseDto>>.Success(MapToResponseDtos(volunteers));
            }
            catch (Exception ex)
            {
                return RM_CMS.Data.ApiResponse<IEnumerable<VolunteerResponseDto>>.Error("Failed to get volunteers with low completion rate", ex.Message);
            }
        }

        public Task<RM_CMS.Data.ApiResponse<string>> GenerateVolunteerId()
        {
            try
            {
                var id = $"V{DateTime.UtcNow:yyyyMMddHHmmss}{Random.Shared.Next(1000, 9999)}";
                return Task.FromResult(RM_CMS.Data.ApiResponse<string>.Success(id));
            }
            catch (Exception ex)
            {
                return Task.FromResult(RM_CMS.Data.ApiResponse<string>.Error("Failed to generate volunteer id", ex.Message));
            }
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
