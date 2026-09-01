namespace RM_CMS.Utilities
{
    public class ApiResponse<T>
    {
        public ResponseType ResponseType { get; set; }
        public string Message { get; set; }
        public T Data { get; set; }

        /// <summary>
        /// Optional machine-readable reason, when the caller needs to branch on WHICH
        /// warning this is rather than just show it.
        ///
        /// Message is for people and is expected to change; this is not. Without it a
        /// client can only pattern-match on prose — which is how the visitor screen
        /// ended up treating every warning as a duplicate, and offering "save anyway
        /// as a separate person" in response to an unknown age band.
        /// </summary>
        public string? Code { get; set; }

        public ApiResponse(ResponseType responseType, string message, T data, string? code = null)
        {
            ResponseType = responseType;
            Message = message;
            Data = data;
            Code = code;
        }
    }

    /// <summary>
    /// Codes a client is allowed to branch on. Kept small on purpose: a code exists
    /// only where behaviour differs, never as a duplicate of the message.
    /// </summary>
    public static class ResponseCodes
    {
        /// <summary>
        /// Somebody already holds one of the contact details supplied. The caller may
        /// offer to record a separate person, or to open the existing record.
        /// </summary>
        public const string DuplicateContact = "duplicate_contact";
    }

    public enum ResponseType
    {
        Success,
        Warning,
        Error
    }
}
