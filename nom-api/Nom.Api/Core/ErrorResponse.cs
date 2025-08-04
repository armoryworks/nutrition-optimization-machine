namespace Nom.Api.Core
{
    /// <summary>
    /// Standardized error response
    /// </summary>
    public class ErrorResponse
    {
        /// <summary>
        /// Gets or sets the error ID
        /// </summary>
        public string ErrorId { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Gets or sets the error message
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the error type
        /// </summary>
        public string ErrorType { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the error code
        /// </summary>
        public string? ErrorCode { get; set; }

        /// <summary>
        /// Gets or sets the HTTP status code
        /// </summary>
        public int StatusCode { get; set; } = 500;

        /// <summary>
        /// Gets or sets the timestamp
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Gets or sets the request ID
        /// </summary>
        public string? RequestId { get; set; }

        /// <summary>
        /// Gets or sets the correlation ID
        /// </summary>
        public string? CorrelationId { get; set; }

        /// <summary>
        /// Gets or sets the user-friendly message
        /// </summary>
        public string? UserMessage { get; set; }

        /// <summary>
        /// Gets or sets the technical details
        /// </summary>
        public string? TechnicalDetails { get; set; }

        /// <summary>
        /// Gets or sets the stack trace
        /// </summary>
        public string? StackTrace { get; set; }

        /// <summary>
        /// Gets or sets additional properties
        /// </summary>
        public Dictionary<string, object>? Properties { get; set; }

        /// <summary>
        /// Gets or sets the severity level
        /// </summary>
        public ErrorSeverityEnum Severity { get; set; } = ErrorSeverityEnum.Error;

        /// <summary>
        /// Gets or sets whether this is a user-facing error
        /// </summary>
        public bool IsUserFacing { get; set; } = false;

        /// <summary>
        /// Gets or sets the retry information
        /// </summary>
        public RetryInformation? RetryInformation { get; set; }
    }
}