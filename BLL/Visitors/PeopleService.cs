using RM_CMS.Data.Models;
using RM_CMS.DAL.Visitors;
using RM_CMS.DAL.Volunteers;
using RM_CMS.Data.DTO.Visitors;
using RM_CMS.Utilities;

namespace RM_CMS.BLL.Visitors
{
    public interface IPeopleService
    {
        Task<ApiResponse<object>> AssignPersonToVolunteerAsync(string personId);
    }

    public class PeopleService : IPeopleService
    {
        private readonly IPeopleRepository _peopleRepository;
        private readonly IVolunteerRepository _volunteerRepository;

        public PeopleService(IPeopleRepository peopleRepository, IVolunteerRepository volunteerRepository)
        {
            _peopleRepository = peopleRepository;
            _volunteerRepository = volunteerRepository;
        }

        public async Task<ApiResponse<object>> AssignPersonToVolunteerAsync(string personId)
        {
            try
            {
                // 1. Get person details
                var getPersonResponse = await _peopleRepository.GetPersonByIdAsync(personId);

                if (getPersonResponse.ResponseType == ResponseType.Error || getPersonResponse.Data == null)
                {
                    return new ApiResponse<object>(
                        ResponseType.Error,
                        getPersonResponse.Message,
                        null
                    );
                }

                var person = getPersonResponse.Data;

                // 2. Check if person is already assigned
                if (!string.IsNullOrEmpty(person.AssignedVolunteer))
                {
                    return new ApiResponse<object>(
                        ResponseType.Warning,
                        "Person is already assigned to a volunteer",
                        new { person.PersonId, person.AssignedVolunteer }
                    );
                }

                // 3. Find available volunteer for the campus
                if (string.IsNullOrEmpty(person.Campus))
                {
                    return new ApiResponse<object>(
                        ResponseType.Error,
                        "Person does not have a campus assigned",
                        null
                    );
                }

                var getVolunteerResponse = await _volunteerRepository.GetAvailableVolunteerAsync(person.Campus);

                if (getVolunteerResponse.ResponseType == ResponseType.Warning || getVolunteerResponse.Data == null)
                {
                    return new ApiResponse<object>(
                        ResponseType.Warning,
                        "No available volunteers with capacity for assignment",
                        new { person.PersonId, campus = person.Campus }
                    );
                }

                var selectedVolunteer = getVolunteerResponse.Data;

                // 4. Calculate next action date (48 hours from now)
                var nextActionDate = DateTime.UtcNow.AddHours(48);

                // 5. Update person assignment
                var updatePersonResponse = await _peopleRepository.UpdatePersonAssignmentAsync(
                    personId,
                    selectedVolunteer.VolunteerId,
                    nextActionDate
                );

                if (updatePersonResponse.ResponseType == ResponseType.Error || !updatePersonResponse.Data)
                {
                    return new ApiResponse<object>(
                        ResponseType.Error,
                        updatePersonResponse.Message,
                        null
                    );
                }

                // 6. Update volunteer current assignments
                var updateVolunteerResponse = await _volunteerRepository.UpdateCurrentAssignmentsAsync(
                    selectedVolunteer.VolunteerId
                );

                if (updateVolunteerResponse.ResponseType == ResponseType.Error || !updateVolunteerResponse.Data)
                {
                    return new ApiResponse<object>(
                        ResponseType.Error,
                        updateVolunteerResponse.Message,
                        null
                    );
                }

                // 7. Return success response with assignment details
                var assignmentDetails = new
                {
                    person.PersonId,
                    PersonName = $"{person.FirstName} {person.LastName}",
                    person.Email,
                    person.Phone,
                    person.Campus,
                    AssignedVolunteerId = selectedVolunteer.VolunteerId,
                    VolunteerName = $"{selectedVolunteer.FirstName} {selectedVolunteer.LastName}",
                    VolunteerEmail = selectedVolunteer.Email,
                    VolunteerPhone = selectedVolunteer.Phone,
                    VolunteerCapacityMax = selectedVolunteer.CapacityMax,
                    VolunteerCurrentAssignments = selectedVolunteer.CurrentAssignments + 1,
                    AssignedDate = DateTime.UtcNow,
                    NextActionDate = nextActionDate,
                    Status = "ASSIGNED"
                };

                return new ApiResponse<object>(
                    ResponseType.Success,
                    "Person successfully assigned to volunteer",
                    assignmentDetails
                );
            }
            catch (Exception ex)
            {
                return new ApiResponse<object>(
                    ResponseType.Error,
                    $"Error during assignment: {ex.Message}",
                    null
                );
            }
        }
    }
}
