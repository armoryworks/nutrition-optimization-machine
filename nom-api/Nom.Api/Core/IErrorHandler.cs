// File: Nom.Api/_Abstractions/_Core/IErrorHandler.cs

using System;
using System.Threading.Tasks;
using System.Collections.Generic; // Added for Dictionary

namespace Nom.Api.Core
{
    /// <summary>
    /// Interface for error handling operations
    /// </summary>
    public interface IErrorHandler
    {
        /// <summary>
        /// Handles an exception
        /// </summary>
        /// <param name="exception">The exception to handle</param>
        /// <param name="context">Optional context information</param>
        /// <returns>Task representing the async operation</returns>
        Task HandleExceptionAsync(Exception exception, ErrorContext? context = null);

        /// <summary>
        /// Handles an exception with a specific error type
        /// </summary>
        /// <typeparam name="TException">The type of exception</typeparam>
        /// <param name="exception">The exception to handle</param>
        /// <param name="context">Optional context information</param>
        /// <returns>Task representing the async operation</returns>
        Task HandleExceptionAsync<TException>(TException exception, ErrorContext? context = null) where TException : Exception;

        /// <summary>
        /// Executes an action with error handling
        /// </summary>
        /// <param name="action">The action to execute</param>
        /// <param name="context">Optional context information</param>
        /// <returns>Task representing the async operation</returns>
        Task ExecuteWithErrorHandlingAsync(Func<Task> action, ErrorContext? context = null);

        /// <summary>
        /// Executes an action with error handling and returns a result
        /// </summary>
        /// <typeparam name="T">The type of the result</typeparam>
        /// <param name="action">The action to execute</param>
        /// <param name="context">Optional context information</param>
        /// <returns>The result of the action or default value on error</returns>
        Task<T> ExecuteWithErrorHandlingAsync<T>(Func<Task<T>> action, ErrorContext? context = null);

        /// <summary>
        /// Executes an action with error handling and returns a result with fallback
        /// </summary>
        /// <typeparam name="T">The type of the result</typeparam>
        /// <param name="action">The action to execute</param>
        /// <param name="fallback">The fallback value on error</param>
        /// <param name="context">Optional context information</param>
        /// <returns>The result of the action or fallback value on error</returns>
        Task<T> ExecuteWithErrorHandlingAsync<T>(Func<Task<T>> action, T fallback, ErrorContext? context = null);

        /// <summary>
        /// Logs an error
        /// </summary>
        /// <param name="message">The error message</param>
        /// <param name="exception">The exception</param>
        /// <param name="context">Optional context information</param>
        /// <returns>Task representing the async operation</returns>
        Task LogErrorAsync(string message, Exception? exception = null, ErrorContext? context = null);

        /// <summary>
        /// Logs a warning
        /// </summary>
        /// <param name="message">The warning message</param>
        /// <param name="context">Optional context information</param>
        /// <returns>Task representing the async operation</returns>
        Task LogWarningAsync(string message, ErrorContext? context = null);

        /// <summary>
        /// Logs information
        /// </summary>
        /// <param name="message">The information message</param>
        /// <param name="context">Optional context information</param>
        /// <returns>Task representing the async operation</returns>
        Task LogInformationAsync(string message, ErrorContext? context = null);

        /// <summary>
        /// Creates a standardized error response
        /// </summary>
        /// <param name="exception">The exception</param>
        /// <param name="context">Optional context information</param>
        /// <returns>The standardized error response</returns>
        ErrorResponse CreateErrorResponse(Exception exception, ErrorContext? context = null);

        /// <summary>
        /// Creates a standardized error response with custom message
        /// </summary>
        /// <param name="message">The error message</param>
        /// <param name="exception">The exception</param>
        /// <param name="context">Optional context information</param>
        /// <returns>The standardized error response</returns>
        ErrorResponse CreateErrorResponse(string message, Exception? exception = null, ErrorContext? context = null);

        /// <summary>
        /// Gets error statistics
        /// </summary>
        /// <returns>Error statistics</returns>
        Task<ErrorStatistics> GetErrorStatisticsAsync();

        /// <summary>
        /// Clears error statistics
        /// </summary>
        /// <returns>Task representing the async operation</returns>
        Task ClearErrorStatisticsAsync();
    }
}