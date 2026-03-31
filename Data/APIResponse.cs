using System;

namespace RM_CMS.Data
{
    public class ApiResponse<T>
    {
        public ResponseType Type { get; set; }
        public string Message { get; set; } = string.Empty;
        public T? Data { get; set; }
        public object? Errors { get; set; }
        public int StatusCode { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        // ✅ Success
        public static ApiResponse<T> Success(T data, string message = "Request successful", int statusCode = 200)
        {
            return new ApiResponse<T>
            {
                Type = ResponseType.Success,
                Message = message,
                Data = data,
                StatusCode = statusCode
            };
        }

        // ⚠️ Warning (Partial success / business warning)
        public static ApiResponse<T> Warning(T? data, string message = "Warning", int statusCode = 200)
        {
            return new ApiResponse<T>
            {
                Type = ResponseType.Warning,
                Message = message,
                Data = data,
                StatusCode = statusCode
            };
        }

        // ❌ Error
        public static ApiResponse<T> Error(string message = "Request failed", object? errors = null, int statusCode = 500)
        {
            return new ApiResponse<T>
            {
                Type = ResponseType.Error,
                Message = message,
                Errors = errors,
                StatusCode = statusCode
            };
        }
    }

    public enum ResponseType
    {
        Success,
        Warning,
        Error
    }
}
