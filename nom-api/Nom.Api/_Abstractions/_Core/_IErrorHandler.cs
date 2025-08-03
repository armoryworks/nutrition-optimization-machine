// File: Nom.Api/_Abstractions/_Core/_IErrorHandler.cs

using System;
using System.Threading.Tasks;
using System.Collections.Generic; // Added for Dictionary

namespace Nom.Api._Abstractions._Core
{
    /// <summary>
    /// Interface for error handling operations
    /// </summary>
    public interface _IErrorHandler
    {
        /// <summary>
        /// Handles an exception
        /// </summary>
        /// <param name="exception">The exception to handle</param>
        /// <param name="context">Optional context information</param>
        /// <returns>Task representing the async operation</returns>
        Task HandleExceptionAsync(Exception exception, _ErrorContext? context = null);

        /// <summary>
        /// Handles an exception with a specific error type
        /// </summary>
        /// <typeparam name="TException">The type of exception</typeparam>
        /// <param name="exception">The exception to handle</param>
        /// <param name="context">Optional context information</param>
        /// <returns>Task representing the async operation</returns>
        Task HandleExceptionAsync<TException>(TException exception, _ErrorContext? context = null) where TException : Exception;

        /// <summary>
        /// Executes an action with error handling
        /// </summary>
        /// <param name="action">The action to execute</param>
        /// <param name="context">Optional context information</param>
        /// <returns>Task representing the async operation</returns>
        Task ExecuteWithErrorHandlingAsync(Func<Task> action, _ErrorContext? context = null);

        /// <summary>
        /// Executes an action with error handling and returns a result
        /// </summary>
        /// <typeparam name="T">The type of the result</typeparam>
        /// <param name="action">The action to execute</param>
        /// <param name="context">Optional context information</param>
        /// <returns>The result of the action or default value on error</returns>
        Task<T> ExecuteWithErrorHandlingAsync<T>(Func<Task<T>> action, _ErrorContext? context = null);

        /// <summary>
        /// Executes an action with error handling and returns a result with fallback
        /// </summary>
        /// <typeparam name="T">The type of the result</typeparam>
        /// <param name="action">The action to execute</param>
        /// <param name="fallback">The fallback value on error</param>
        /// <param name="context">Optional context information</param>
        /// <returns>The result of the action or fallback value on error</returns>
        Task<T> ExecuteWithErrorHandlingAsync<T>(Func<Task<T>> action, T fallback, _ErrorContext? context = null);

        /// <summary>
        /// Logs an error
        /// </summary>
        /// <param name="message">The error message</param>
        /// <param name="exception">The exception</param>
        /// <param name="context">Optional context information</param>
        /// <returns>Task representing the async operation</returns>
        Task LogErrorAsync(string message, Exception? exception = null, _ErrorContext? context = null);

        /// <summary>
        /// Logs a warning
        /// </summary>
        /// <param name="message">The warning message</param>
        /// <param name="context">Optional context information</param>
        /// <returns>Task representing the async operation</returns>
        Task LogWarningAsync(string message, _ErrorContext? context = null);

        /// <summary>
        /// Logs information
        /// </summary>
        /// <param name="message">The information message</param>
        /// <param name="context">Optional context information</param>
        /// <returns>Task representing the async operation</returns>
        Task LogInformationAsync(string message, _ErrorContext? context = null);

        /// <summary>
        /// Creates a standardized error response
        /// </summary>
        /// <param name="exception">The exception</param>
        /// <param name="context">Optional context information</param>
        /// <returns>The standardized error response</returns>
        _ErrorResponse CreateErrorResponse(Exception exception, _ErrorContext? context = null);

        /// <summary>
        /// Creates a standardized error response with custom message
        /// </summary>
        /// <param name="message">The error message</param>
        /// <param name="exception">The exception</param>
        /// <param name="context">Optional context information</param>
        /// <returns>The standardized error response</returns>
        _ErrorResponse CreateErrorResponse(string message, Exception? exception = null, _ErrorContext? context = null);

        /// <summary>
        /// Gets error statistics
        /// </summary>
        /// <returns>Error statistics</returns>
        Task<_ErrorStatistics> GetErrorStatisticsAsync();

        /// <summary>
        /// Clears error statistics
        /// </summary>
        /// <returns>Task representing the async operation</returns>
        Task ClearErrorStatisticsAsync();
    }

    /// <summary>
    /// Error context information
    /// </summary>
    public class _ErrorContext
    {
        /// <summary>
        /// Gets or sets the operation name
        /// </summary>
        public string? OperationName { get; set; }

        /// <summary>
        /// Gets or sets the user ID
        /// </summary>
        public string? UserId { get; set; }

        /// <summary>
        /// Gets or sets the request ID
        /// </summary>
        public string? RequestId { get; set; }

        /// <summary>
        /// Gets or sets the correlation ID
        /// </summary>
        public string? CorrelationId { get; set; }

        /// <summary>
        /// Gets or sets the source
        /// </summary>
        public string? Source { get; set; }

        /// <summary>
        /// Gets or sets additional properties
        /// </summary>
        public Dictionary<string, object>? Properties { get; set; }

        /// <summary>
        /// Gets or sets the severity level
        /// </summary>
        public _ErrorSeverity Severity { get; set; } = _ErrorSeverity.Error;

        /// <summary>
        /// Gets or sets whether to include stack trace
        /// </summary>
        public bool IncludeStackTrace { get; set; } = true;

        /// <summary>
        /// Gets or sets whether to notify administrators
        /// </summary>
        public bool NotifyAdministrators { get; set; } = false;

        /// <summary>
        /// Gets or sets whether to log to external systems
        /// </summary>
        public bool LogToExternalSystems { get; set; } = true;
    }

    /// <summary>
    /// Error severity levels
    /// </summary>
    public enum _ErrorSeverity
    {
        /// <summary>
        /// Information level
        /// </summary>
        Information,

        /// <summary>
        /// Warning level
        /// </summary>
        Warning,

        /// <summary>
        /// Error level
        /// </summary>
        Error,

        /// <summary>
        /// Critical level
        /// </summary>
        Critical,

        /// <summary>
        /// Fatal level
        /// </summary>
        Fatal
    }

    /// <summary>
    /// Standardized error response
    /// </summary>
    public class _ErrorResponse
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
        public _ErrorSeverity Severity { get; set; } = _ErrorSeverity.Error;

        /// <summary>
        /// Gets or sets whether this is a user-facing error
        /// </summary>
        public bool IsUserFacing { get; set; } = false;

        /// <summary>
        /// Gets or sets the retry information
        /// </summary>
        public _RetryInformation? RetryInformation { get; set; }
    }

    /// <summary>
    /// Retry information
    /// </summary>
    public class _RetryInformation
    {
        /// <summary>
        /// Gets or sets whether retry is allowed
        /// </summary>
        public bool IsRetryAllowed { get; set; } = false;

        /// <summary>
        /// Gets or sets the maximum retry attempts
        /// </summary>
        public int MaxRetryAttempts { get; set; } = 3;

        /// <summary>
        /// Gets or sets the retry delay in milliseconds
        /// </summary>
        public int RetryDelayMs { get; set; } = 1000;

        /// <summary>
        /// Gets or sets the current retry attempt
        /// </summary>
        public int CurrentRetryAttempt { get; set; } = 0;

        /// <summary>
        /// Gets or sets the retry after timestamp
        /// </summary>
        public DateTime? RetryAfter { get; set; }
    }

    /// <summary>
    /// Error statistics
    /// </summary>
    public class _ErrorStatistics
    {
        /// <summary>
        /// Gets or sets the total number of errors
        /// </summary>
        public long TotalErrors { get; set; }

        /// <summary>
        /// Gets or sets the total number of warnings
        /// </summary>
        public long TotalWarnings { get; set; }

        /// <summary>
        /// Gets or sets the total number of critical errors
        /// </summary>
        public long TotalCriticalErrors { get; set; }

        /// <summary>
        /// Gets or sets the total number of fatal errors
        /// </summary>
        public long TotalFatalErrors { get; set; }

        /// <summary>
        /// Gets or sets the error rate per minute
        /// </summary>
        public double ErrorRatePerMinute { get; set; }

        /// <summary>
        /// Gets or sets the average error processing time in milliseconds
        /// </summary>
        public double AverageErrorProcessingTimeMs { get; set; }

        /// <summary>
        /// Gets or sets the last error time
        /// </summary>
        public DateTime? LastErrorTime { get; set; }

        /// <summary>
        /// Gets or sets the most common error types
        /// </summary>
        public Dictionary<string, long> MostCommonErrorTypes { get; set; } = new();

        /// <summary>
        /// Gets or sets the error distribution by severity
        /// </summary>
        public Dictionary<_ErrorSeverity, long> ErrorDistributionBySeverity { get; set; } = new();

        /// <summary>
        /// Gets or sets the error distribution by source
        /// </summary>
        public Dictionary<string, long> ErrorDistributionBySource { get; set; } = new();
    }
} 