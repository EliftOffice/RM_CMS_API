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
       
        public Task<ApiResponse<AssignedVolunteerDTO>> SaveAndAssignPeople(CreatePersonDto createDto);
      
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

        public async Task<ApiResponse<AssignedVolunteerDTO>> SaveAndAssignPeople(CreatePersonDto createDto)
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
                    {
                        return new ApiResponse<AssignedVolunteerDTO>(ResponseType.Error, $"People Saved Succefully But Error assigning volunteer: {res.Message}", new AssignedVolunteerDTO());
                    }


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

        public async Task<ApiResponse<People>> SaveNewVisitorAsync(CreatePersonDto dto)
        {
            try
            {
                // 1. Basic Validation
                if (string.IsNullOrWhiteSpace(dto.FirstName))
                    return new ApiResponse<People>(ResponseType.Error, "First name is required", null);

                if (string.IsNullOrWhiteSpace(dto.LastName))
                    return new ApiResponse<People>(ResponseType.Error, "Last name is required", null);

                if (string.IsNullOrWhiteSpace(dto.Phone))
                    return new ApiResponse<People>(ResponseType.Error, "Phone is required", null);

                // 2. Phone format validation (10 digits)
                if (!System.Text.RegularExpressions.Regex.IsMatch(dto.Phone, @"^\d{10}$"))
                    return new ApiResponse<People>(ResponseType.Error, "Invalid phone number", null);

                // 3. Conditional Ref Validation
                if (dto.ConnectionSource == "Friend_Family_Invite")
                {
                    if (string.IsNullOrWhiteSpace(dto.RefName) || string.IsNullOrWhiteSpace(dto.RefPhone))
                    {
                        return new ApiResponse<People>(ResponseType.Error, "Reference details are required", null);
                    }

                    if (!System.Text.RegularExpressions.Regex.IsMatch(dto.RefPhone, @"^\d{10}$"))
                    {
                        return new ApiResponse<People>(ResponseType.Error, "Invalid reference phone number", null);
                    }
                }

                // 4. Check duplicates (based on phone or email)
                var existing = await _peoplesDAL.FindByEmailOrPhoneAsync(dto.Email, dto.Phone);

                if (existing.ResponseType == ResponseType.Success && existing.Data != null)
                {
                    await _peoplesDAL.IncrementVisitCountAsync(existing.Data.PersonId);
                    return existing;
                }

                // 5. Create new person
                var newPerson = new People
                {
                    FirstName = dto.FirstName,
                    LastName = dto.LastName,
                    Email = dto.Email,
                    Phone = dto.Phone,
                    AgeRange = dto.AgeRange,
                    ConnectionSource = dto.ConnectionSource,
                    InterestedIn = null,
                    PrayerRequests = dto.PrayerRequests,
                    SpecificNeeds = null,
                    VisitType = "First-Time Visitor",
                    FirstVisitDate = DateTime.UtcNow,
                    LastVisitDate = DateTime.UtcNow,
                    VisitCount = 1,
                    FollowUpStatus = "NEW",
                    FollowUpPriority = "Normal",
                    Campus= "Ongole",
                    HouseholdType = dto.HouseholdType,
                    RefName=dto.RefName,
                    refPhone=dto.RefPhone,
                    Address= dto.Address

                };

                return await _peoplesDAL.CreatePersonAsync(newPerson);
            }
            catch (Exception ex)
            {
                return new ApiResponse<People>(
                    ResponseType.Error,
                    $"Error saving visitor: {ex.Message}",
                    null
                );
            }
        }

       
    }
}