using RM_CMS.BLL.Volunteers;
using RM_CMS.DAL.Peoples;
using RM_CMS.DAL.Volunteers;
using RM_CMS.Data.DTO;
using RM_CMS.Data.DTO.Peoples;
using RM_CMS.Data.DTO.Volunteers;
using RM_CMS.Data.Models;
using RM_CMS.Utilities;

namespace RM_CMS.BLL.Peoples
{
    public interface IPeoplesBLL
    {
        public Task<ApiResponse<People>> SaveNewVisitorAsync(CreatePeopleDto createDto);
        public Task<ApiResponse<AssignedVolunteerDTO>> SaveAndAssignePeople(CreatePeopleDto createDto);
        Task<ApiResponse<People>> GetPersonByIdAsync(string personId);
        Task<ApiResponse<List<People>>> GetPeopleByFilterAsync(PeoplesFilterDTO filter);
    }
    public class PeoplesBLL : IPeoplesBLL
    {
        private readonly IPeoplesDAL _peoplesDAL;
        private readonly IVolunteersBLL _volunteersBLL;


        public PeoplesBLL(IPeoplesDAL peoplesDAL, IVolunteersBLL volunteersBLL)
        {
            _peoplesDAL = peoplesDAL;
            _volunteersBLL = volunteersBLL;

        }
        public async Task<ApiResponse<AssignedVolunteerDTO>> SaveAndAssignePeople(CreatePeopleDto createDto)
        {
            try
            {
                var result = await SaveNewVisitorAsync(createDto);
                if (result.ResponseType != ResponseType.Success)
                    return new ApiResponse<AssignedVolunteerDTO>(ResponseType.Error, $"Error saving visitor: {result.Message}", new AssignedVolunteerDTO());
                else
                {
                    var res = await _volunteersBLL.AssignToVolunteerAsync(result.Data.PersonId);
                    if (res.ResponseType != ResponseType.Success)
                        return new ApiResponse<AssignedVolunteerDTO>(ResponseType.Error, $"Error assigning volunteer: {res.Message}", new AssignedVolunteerDTO());

                    else
                    {
                        res.Data.people_id = result.Data.PersonId;
                        res.Data.people_name = result.Data.FirstName + " " + result.Data.LastName;
                        return new ApiResponse<AssignedVolunteerDTO>(ResponseType.Success, $"Visitor saved and assigned to {res.Data.last_name + " " + res.Data.first_name}", res.Data);

                    }

                }
            }
            catch (Exception ex)
            {
                return new ApiResponse<AssignedVolunteerDTO>(ResponseType.Error, $"Error retrieving person: {ex.Message}", new AssignedVolunteerDTO());
            }
        }

        public async Task<ApiResponse<People>> SaveNewVisitorAsync(CreatePeopleDto createDto)
        {
            try
            {
                // 1. Validate
                if (string.IsNullOrWhiteSpace(createDto.first_name))
                {
                    return new ApiResponse<People>(ResponseType.Error, "Missing required fields: first_name", new People());
                }

                if (string.IsNullOrWhiteSpace(createDto.last_name))
                {
                    return new ApiResponse<People>(ResponseType.Error, "Missing required fields:  last_name", new People());
                }

                if (string.IsNullOrWhiteSpace(createDto.email) && string.IsNullOrWhiteSpace(createDto.phone))
                {
                    return new ApiResponse<People>(ResponseType.Error, "Must provide at least email or phone", new People());
                }

                // 2. Check duplicates
                var response = await _peoplesDAL.FindByEmailOrPhoneAsync(createDto.email, createDto.phone);


                if (response.ResponseType == ResponseType.Success)
                {

                    await _peoplesDAL.IncrementVisitCountAsync(response.Data.PersonId);
                    return response;

                }


                else
                {
                    var newPerson = new People
                    {
                        FirstName = createDto.first_name,
                        LastName = createDto.last_name,
                        Email = createDto.email,
                        Phone = createDto.phone,
                        AgeRange = createDto.age_range,
                        HouseholdType = createDto.household_type,
                        ZipCode = createDto.zip_code,
                        VisitType = string.IsNullOrWhiteSpace(createDto.visit_type) ? "First-Time Visitor" : createDto.visit_type,
                        FirstVisitDate = DateTime.UtcNow,
                        LastVisitDate = DateTime.UtcNow,
                        VisitCount = 1,
                        ConnectionSource = createDto.connection_source,
                        Campus = createDto.campus,
                        FollowUpStatus = string.IsNullOrWhiteSpace(createDto.follow_up_status) ? "NEW" : createDto.follow_up_status,
                        FollowUpPriority = string.IsNullOrWhiteSpace(createDto.follow_up_priority) ? DeterminePriority(createDto) : createDto.follow_up_priority,
                        InterestedIn = createDto.interested_in,
                        PrayerRequests = createDto.prayer_requests,
                        SpecificNeeds = createDto.specific_needs,

                    };

                    return await _peoplesDAL.CreatePersonAsync(newPerson);

                }

            }
            catch (Exception ex)
            {
                return new ApiResponse<People>(ResponseType.Error, $"Error saving visitor: {ex.Message}", new People());
            }
        }

        private string DeterminePriority(CreatePeopleDto dto)
        {
            if (!string.IsNullOrWhiteSpace(dto.specific_needs) || !string.IsNullOrWhiteSpace(dto.prayer_requests))
            {
                return "High";
            }

            if (!string.IsNullOrWhiteSpace(dto.interested_in) && dto.interested_in.Contains("Counseling", StringComparison.OrdinalIgnoreCase))
            {
                return "Urgent";
            }

            return "Normal";
        }

        public async Task<ApiResponse<People>> GetPersonByIdAsync(string personId)
        {
            try
            {
                var result = await _peoplesDAL.GetPersonByIdAsync(personId);
                if (result.ResponseType != ResponseType.Success)
                    return new ApiResponse<People>(ResponseType.Error, $"Error retrieving person: {result.Message}", new People());
                else
                    return result;
            }
            catch (Exception ex)
            {
                return new ApiResponse<People>(ResponseType.Error, $"Error retrieving person: {ex.Message}", new People());
            }
        }
        public async Task<ApiResponse<List<People>>> GetPeopleByFilterAsync(PeoplesFilterDTO filter)
        {
            try
            {
                var result = await _peoplesDAL.GetPeopleByFilterAsync(filter);

                if (result.ResponseType != ResponseType.Success)
                {
                    return new ApiResponse<List<People>>(
                        ResponseType.Error,
                        result.Message,
                        new List<People>()
                    );
                }

                // ✅ Convert IEnumerable → List
                var peopleList = result.Data?.ToList() ?? new List<People>();

                return new ApiResponse<List<People>>(
                    ResponseType.Success,
                    result.Message,
                    peopleList
                );
            }
            catch (Exception ex)
            {
                return new ApiResponse<List<People>>(
                    ResponseType.Error,
                    $"Error retrieving people: {ex.Message}",
                    new List<People>()
                );
            }
        }
    }
}