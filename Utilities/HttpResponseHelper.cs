using Microsoft.AspNetCore.Mvc;

namespace RM_CMS.Utilities
{
    /// <summary>
    /// Utility class for converting ApiResponse to appropriate ActionResult based on ResponseType
    /// </summary>
    public static class HttpResponseHelper
    {
        /// <summary>
        /// Converts an ApiResponse to an appropriate ActionResult based on ResponseType
        /// </summary>
        /// <typeparam name="T">The type of data in the ApiResponse</typeparam>
        /// <param name="response">The ApiResponse to convert</param>
        /// <returns>ActionResult with appropriate HTTP status code</returns>
        public static ActionResult<ApiResponse<T>> CreateHttpResponse<T>(ApiResponse<T> response)
        {
            if (response == null)
            {
                return new ObjectResult(new ApiResponse<T>(
                    ResponseType.Error,
                    "Response is null",
                    default
                ))
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
            }

            return response.ResponseType switch
            {
                ResponseType.Success => new OkObjectResult(response),
                ResponseType.Warning => new OkObjectResult(response),
                ResponseType.Error => new BadRequestObjectResult(response),
                _ => new ObjectResult(response) { StatusCode = StatusCodes.Status500InternalServerError }
            };
        }

      
    }
}