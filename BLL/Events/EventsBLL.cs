using RM_CMS.DAL.Events;
using RM_CMS.Data.DTO.Events;
using RM_CMS.Utilities;

namespace RM_CMS.BLL.Events
{
    public interface IEventsBLL
    {
        Task<ApiResponse<List<EventDto>>> GetAdminEventsAsync();
        Task<ApiResponse<EventDto>> GetEventByIdAsync(long eventId);
        Task<ApiResponse<long>> CreateEventAsync(AdminEventRequest request);
        Task<ApiResponse<bool>> UpdateEventAsync(long id, AdminEventRequest request);
        Task<ApiResponse<bool>> UpdateEventStatusAsync(long id, bool isActive);
        Task<ApiResponse<List<EventDto>>> GetActiveEventsAsync(long? userId);
    }

    public class EventsBLL : IEventsBLL
    {
        private readonly IEventsDAL _eventsDAL;

        public EventsBLL(IEventsDAL eventsDAL)
        {
            _eventsDAL = eventsDAL;
        }

        public async Task<ApiResponse<List<EventDto>>> GetAdminEventsAsync()
        {
            try
            {
                return await _eventsDAL.GetAdminEventsAsync();
            }
            catch (Exception ex)
            {
                return new ApiResponse<List<EventDto>>(ResponseType.Error, $"Error retrieving events: {ex.Message}", new List<EventDto>());
            }
        }

        public async Task<ApiResponse<EventDto>> GetEventByIdAsync(long eventId)
        {
            try
            {
                if (eventId <= 0)
                {
                    return new ApiResponse<EventDto>(ResponseType.Warning, "Invalid event ID", null);
                }

                return await _eventsDAL.GetByIdAsync(eventId);
            }
            catch (Exception ex)
            {
                return new ApiResponse<EventDto>(ResponseType.Error, $"Error retrieving event: {ex.Message}", null);
            }
        }

        public async Task<ApiResponse<long>> CreateEventAsync(AdminEventRequest request)
        {
            try
            {
                if (request == null)
                {
                    return new ApiResponse<long>(ResponseType.Warning, "Invalid event data", 0);
                }

                if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.VenueName))
                {
                    return new ApiResponse<long>(ResponseType.Warning, "Title and venue name are required", 0);
                }

                if (request.Radius <= 0)
                {
                    return new ApiResponse<long>(ResponseType.Warning, "Radius must be greater than zero", 0);
                }

                if (request.EndTime <= request.StartTime)
                {
                    return new ApiResponse<long>(ResponseType.Warning, "End time must be greater than start time", 0);
                }

                return await _eventsDAL.CreateAsync(request);
            }
            catch (Exception ex)
            {
                return new ApiResponse<long>(ResponseType.Error, $"Error creating event: {ex.Message}", 0);
            }
        }

        public async Task<ApiResponse<bool>> UpdateEventAsync(long id, AdminEventRequest request)
        {
            try
            {
                if (id <= 0)
                {
                    return new ApiResponse<bool>(ResponseType.Warning, "Invalid event ID", false);
                }

                if (request == null)
                {
                    return new ApiResponse<bool>(ResponseType.Warning, "Invalid event data", false);
                }

                if (request.EndTime <= request.StartTime)
                {
                    return new ApiResponse<bool>(ResponseType.Warning, "End time must be greater than start time", false);
                }

                return await _eventsDAL.UpdateAsync(id, request);
            }
            catch (Exception ex)
            {
                return new ApiResponse<bool>(ResponseType.Error, $"Error updating event: {ex.Message}", false);
            }
        }

        public async Task<ApiResponse<bool>> UpdateEventStatusAsync(long id, bool isActive)
        {
            try
            {
                if (id <= 0)
                {
                    return new ApiResponse<bool>(ResponseType.Warning, "Invalid event ID", false);
                }

                return await _eventsDAL.UpdateStatusAsync(id, isActive);
            }
            catch (Exception ex)
            {
                return new ApiResponse<bool>(ResponseType.Error, $"Error updating event status: {ex.Message}", false);
            }
        }

        public async Task<ApiResponse<List<EventDto>>> GetActiveEventsAsync(long? userId)
        {
            try
            {
                return await _eventsDAL.GetActiveEventsAsync(userId);
            }
            catch (Exception ex)
            {
                return new ApiResponse<List<EventDto>>(ResponseType.Error, $"Error retrieving active events: {ex.Message}", new List<EventDto>());
            }
        }
    }
}
