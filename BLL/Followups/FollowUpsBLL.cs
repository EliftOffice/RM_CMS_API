using RM_CMS.DAL.Followups;
using RM_CMS.Data.DTO.Followups;
using RM_CMS.Data.Models;
using RM_CMS.DAL.Visitors;
using RM_CMS.DAL.Volunteers;
using Microsoft.Extensions.Logging;
using System.Linq;

namespace RM_CMS.BLL.Followups
{
    public interface IFollowUpService
    {
        Task<RM_CMS.Data.ApiResponse<FollowUpResponseDto>> CreateAsync(CreateFollowUpDto dto);
        Task<RM_CMS.Data.ApiResponse<IEnumerable<FollowUpResponseDto>>> GetByPersonAsync(string personId);
        Task<RM_CMS.Data.ApiResponse<IEnumerable<FollowUpResponseDto>>> GetByVolunteerAsync(string volunteerId);
        Task<RM_CMS.Data.ApiResponse<bool>> AssignVolunteerAsync(string personId);
    }

    public class FollowUpsBLL : IFollowUpService
    {
        private readonly IFollowUpRepository _followUpRepository;
        private readonly IPeopleRepository _peopleRepository;
        private readonly IVolunteerRepository _volunteerRepository;
        private readonly ILogger<FollowUpsBLL> _logger;

        public FollowUpsBLL(IFollowUpRepository followUpRepository, IPeopleRepository peopleRepository, IVolunteerRepository volunteerRepository, ILogger<FollowUpsBLL> logger)
        {
            _followUpRepository = followUpRepository;
            _peopleRepository = peopleRepository;
            _volunteerRepository = volunteerRepository;
            _logger = logger;
        }

        public async Task<RM_CMS.Data.ApiResponse<FollowUpResponseDto>> CreateAsync(CreateFollowUpDto dto)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dto.PersonId) || string.IsNullOrWhiteSpace(dto.VolunteerId) || string.IsNullOrWhiteSpace(dto.ContactStatus))
                {
                    return RM_CMS.Data.ApiResponse<FollowUpResponseDto>.Error("Missing required follow-up fields", null, 400);
                }

                // Determine attempt number
                var previousCount = await _followUpRepository.CountByPersonAsync(dto.PersonId);
                var attemptNumber = previousCount + 1;

                // Generate follow up id
                var year = DateTime.UtcNow.Year;
                var maxSeq = await _followUpRepository.GetMaxSequenceForYearAsync(year);
                var nextSeq = maxSeq + 1;
                var followUpId = $"FU{year}{nextSeq.ToString().PadLeft(4, '0')}";

                var followUp = new FollowUp
                {
                    FollowUpId = followUpId,
                    PersonId = dto.PersonId,
                    VolunteerId = dto.VolunteerId,
                    AttemptNumber = attemptNumber,
                    AttemptDate = DateTime.UtcNow,
                    ContactMethod = dto.ContactMethod,
                    ContactStatus = dto.ContactStatus,
                    ResponseType = dto.ResponseType,
                    CallDurationMin = dto.CallDurationMin,
                    Notes = dto.Notes,
                    WeekNumber = System.Globalization.CultureInfo.InvariantCulture.Calendar.GetWeekOfYear(DateTime.UtcNow, System.Globalization.CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday),
                    EscalationAppropriate = "Not-Assessed",
                    CreatedAt = DateTime.UtcNow
                };

                var created = await _followUpRepository.CreateAsync(followUp);
                if (!created) return RM_CMS.Data.ApiResponse<FollowUpResponseDto>.Error("Failed to create follow-up record");

                // Update person's last contact date and next action date if needed
                await _peopleRepository.UpdateLastContactAsync(dto.PersonId, DateTime.UtcNow, null);

                var response = new FollowUpResponseDto
                {
                    FollowUpId = followUp.FollowUpId,
                    PersonId = followUp.PersonId,
                    VolunteerId = followUp.VolunteerId,
                    AttemptNumber = followUp.AttemptNumber,
                    AttemptDate = followUp.AttemptDate,
                    ContactMethod = followUp.ContactMethod,
                    ContactStatus = followUp.ContactStatus,
                    ResponseType = followUp.ResponseType,
                    CallDurationMin = followUp.CallDurationMin,
                    Notes = followUp.Notes,
                    EscalationAppropriate = followUp.EscalationAppropriate,
                    CreatedAt = followUp.CreatedAt
                };

                return RM_CMS.Data.ApiResponse<FollowUpResponseDto>.Success(response, "Follow-up created", 201);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating follow-up for person {PersonId}", dto.PersonId);
                return RM_CMS.Data.ApiResponse<FollowUpResponseDto>.Error("An error occurred creating follow-up", ex.Message);
            }
        }

        public async Task<RM_CMS.Data.ApiResponse<IEnumerable<FollowUpResponseDto>>> GetByPersonAsync(string personId)
        {
            try
            {
                var items = await _followUpRepository.GetByPersonAsync(personId);
                var list = items.Select(i => new FollowUpResponseDto
                {
                    FollowUpId = i.FollowUpId,
                    PersonId = i.PersonId,
                    VolunteerId = i.VolunteerId,
                    AttemptNumber = i.AttemptNumber,
                    AttemptDate = i.AttemptDate,
                    ContactMethod = i.ContactMethod,
                    ContactStatus = i.ContactStatus,
                    ResponseType = i.ResponseType,
                    CallDurationMin = i.CallDurationMin,
                    Notes = i.Notes,
                    EscalationAppropriate = i.EscalationAppropriate,
                    CreatedAt = i.CreatedAt
                });

                return RM_CMS.Data.ApiResponse<IEnumerable<FollowUpResponseDto>>.Success(list);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching follow-ups for person {PersonId}", personId);
                return RM_CMS.Data.ApiResponse<IEnumerable<FollowUpResponseDto>>.Error("An error occurred fetching follow-ups", ex.Message);
            }
        }

        public async Task<RM_CMS.Data.ApiResponse<IEnumerable<FollowUpResponseDto>>> GetByVolunteerAsync(string volunteerId)
        {
            try
            {
                var items = await _followUpRepository.GetByVolunteerAsync(volunteerId);
                var list = items.Select(i => new FollowUpResponseDto
                {
                    FollowUpId = i.FollowUpId,
                    PersonId = i.PersonId,
                    VolunteerId = i.VolunteerId,
                    AttemptNumber = i.AttemptNumber,
                    AttemptDate = i.AttemptDate,
                    ContactMethod = i.ContactMethod,
                    ContactStatus = i.ContactStatus,
                    ResponseType = i.ResponseType,
                    CallDurationMin = i.CallDurationMin,
                    Notes = i.Notes,
                    EscalationAppropriate = i.EscalationAppropriate,
                    CreatedAt = i.CreatedAt
                });

                return RM_CMS.Data.ApiResponse<IEnumerable<FollowUpResponseDto>>.Success(list);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching follow-ups for volunteer {VolunteerId}", volunteerId);
                return RM_CMS.Data.ApiResponse<IEnumerable<FollowUpResponseDto>>.Error("An error occurred fetching follow-ups", ex.Message);
            }
        }

        public async Task<RM_CMS.Data.ApiResponse<bool>> AssignVolunteerAsync(string personId)
        {
            try
            {
                // 1. Get person details
                var person = await _peopleRepository.GetByIdAsync(personId);
                if (person == null)
                {
                    _logger.LogWarning("AssignVolunteer: person {PersonId} not found", personId);
                    return RM_CMS.Data.ApiResponse<bool>.Error("Person not found", null, 404);
                }

                // 2. Get available volunteers (capacity check)
                var campus = person.Campus; // nullable or string
                var available = (await _volunteerRepository.GetAvailableVolunteersAsync(campus)).ToList();

                if (!available.Any())
                {
                    // No capacity - alert Team Lead
                    _logger.LogWarning("No available volunteers for assignment for person {PersonId}", personId);
                    await _peopleRepository.UpdateFollowUpStatusAsync(personId, "NEW");
                    return RM_CMS.Data.ApiResponse<bool>.Error("No available volunteers", null, 409);
                }

                // 3. Select volunteer (least loaded first, then random for fairness)
                var selected = available.First();

                // 4. Create assignment
                var assignedDate = DateTime.UtcNow;
                var updated = await _peopleRepository.UpdateAssignmentAsync(personId, selected.VolunteerId, assignedDate);
                if (!updated)
                {
                    _logger.LogError("Failed to update assignment for person {PersonId}", personId);
                    return RM_CMS.Data.ApiResponse<bool>.Error("Failed to update person assignment");
                }

                // 5. Update volunteer workload
                var inc = await _volunteerRepository.IncrementCurrentAssignmentsAsync(selected.VolunteerId);
                if (!inc)
                {
                    _logger.LogWarning("Failed to increment assignments for volunteer {VolunteerId}", selected.VolunteerId);
                }

                // 6. Notify volunteer (placeholder)
                _logger.LogInformation("Assigned volunteer {VolunteerId} to person {PersonId}", selected.VolunteerId, personId);

                return RM_CMS.Data.ApiResponse<bool>.Success(true, "Volunteer assigned");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error assigning volunteer to person {PersonId}", personId);
                return RM_CMS.Data.ApiResponse<bool>.Error("An error occurred assigning volunteer", ex.Message);
            }
        }
    }
}
