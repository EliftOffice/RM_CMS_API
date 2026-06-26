namespace RM_CMS.Utilities
{
    public class ApiResponse<T>
    {
        public ResponseType ResponseType { get; set; }
        public string Message { get; set; }
        public T Data { get; set; }

        public ApiResponse(ResponseType responseType, string message, T data)
        {
            ResponseType = responseType;
            Message = message;
            Data = data;
        }
    }

    public enum ResponseType
    {
        Success,
        Warning,
        Error
    }
}
