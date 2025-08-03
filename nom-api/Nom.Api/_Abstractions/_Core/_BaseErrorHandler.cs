// File: Nom.Api/_Abstractions/_Core/_BaseErrorHandler.cs

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Nom.Api._Abstractions._Core
{
    /// <summary>
    /// Base error handler implementation
    /// </summary>
    public abstract class _BaseErrorHandler : _IErrorHandler
    {
        protected readonly ILogger<_BaseErrorHandler> _logger;
        protected readonly _ErrorHandlerOptions _options;
        protected readonly ConcurrentDictionary<string, long> _errorTypeCounts;
        protected readonly ConcurrentDictionary<_ErrorSeverity, long> _severityCounts;
        protected readonly ConcurrentDictionary<string, long> _sourceCounts;
        protected readonly _ErrorStatistics _statistics;

        protected _BaseErrorHandler(ILogger<_BaseErrorHandler> logger, IOptions<_ErrorHandlerOptions> options)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _options = options?.Value ?? new _ErrorHandlerOptions();
            _errorTypeCounts = new ConcurrentDictionary<string, long>();
            _severityCounts = new ConcurrentDictionary<_ErrorSeverity, long>();
            _sourceCounts = new ConcurrentDictionary<string, long>();
            _statistics = new _ErrorStatistics();
        }

        public virtual async Task HandleExceptionAsync(Exception exception, _ErrorContext? context = null)
        {
            if (exception == null)
            {
                _logger.LogWarning("Attempted to handle null exception");
                return;
            }

            var startTime = DateTime.UtcNow;
            var errorType = exception.GetType().Name;
            var source = context?.Source ?? "Unknown";

            try
            {
                // Update statistics
                _errorTypeCounts.AddOrUpdate(errorType, 1, (key, value) => value + 1);
                _severityCounts.AddOrUpdate(context?.Severity ?? _ErrorSeverity.Error, 1, (key, value) => value + 1);
                _sourceCounts.AddOrUpdate(source, 1, (key, value) => value + 1);

                _statistics.TotalErrors++;
                _statistics.LastErrorTime = DateTime.UtcNow;

                // Log the error
                await LogErrorAsync(exception.Message, exception, context);

                // Create error response
                var errorResponse = CreateErrorResponse(exception, context);

                // Handle based on severity
                await HandleErrorBySeverityAsync(errorResponse, context);

                // Update processing time
                var processingTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
                _statistics.AverageErrorProcessingTimeMs = (_statistics.AverageErrorProcessingTimeMs + processingTime) / 2;

                // Notify administrators if needed
                if (context?.NotifyAdministrators == true)
                {
                    await NotifyAdministratorsAsync(errorResponse, context);
                }

                // Log to external systems if needed
                if (context?.LogToExternalSystems != false)
                {
                    await LogToExternalSystemsAsync(errorResponse, context);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while handling exception: {OriginalException}", exception.Message);
            }
        }

        public virtual async Task HandleExceptionAsync<TException>(TException exception, _ErrorContext? context = null) where TException : Exception
        {
            await HandleExceptionAsync((Exception)exception, context);
        }

        public virtual async Task ExecuteWithErrorHandlingAsync(Func<Task> action, _ErrorContext? context = null)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync(ex, context);
                throw;
            }
        }

        public virtual async Task<T> ExecuteWithErrorHandlingAsync<T>(Func<Task<T>> action, _ErrorContext? context = null)
        {
            try
            {
                return await action();
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync(ex, context);
                return default(T)!;
            }
        }

        public virtual async Task<T> ExecuteWithErrorHandlingAsync<T>(Func<Task<T>> action, T fallback, _ErrorContext? context = null)
        {
            try
            {
                return await action();
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync(ex, context);
                return fallback;
            }
        }

        public virtual async Task LogErrorAsync(string message, Exception? exception = null, _ErrorContext? context = null)
        {
            try
            {
                var logLevel = GetLogLevel(context?.Severity ?? _ErrorSeverity.Error);
                var logMessage = FormatLogMessage(message, context);

                if (exception != null)
                {
                    _logger.Log(logLevel, exception, logMessage);
                }
                else
                {
                    _logger.Log(logLevel, logMessage);
                }

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while logging error message: {OriginalMessage}", message);
            }
        }

        public virtual async Task LogWarningAsync(string message, _ErrorContext? context = null)
        {
            try
            {
                _statistics.TotalWarnings++;
                var logMessage = FormatLogMessage(message, context);
                _logger.LogWarning(logMessage);
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while logging warning message: {OriginalMessage}", message);
            }
        }

        public virtual async Task LogInformationAsync(string message, _ErrorContext? context = null)
        {
            try
            {
                var logMessage = FormatLogMessage(message, context);
                _logger.LogInformation(logMessage);
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while logging information message: {OriginalMessage}", message);
            }
        }

        public virtual _ErrorResponse CreateErrorResponse(Exception exception, _ErrorContext? context = null)
        {
            var errorResponse = new _ErrorResponse
            {
                Message = exception.Message,
                ErrorType = exception.GetType().Name,
                StatusCode = GetStatusCode(exception),
                Timestamp = DateTime.UtcNow,
                RequestId = context?.RequestId,
                CorrelationId = context?.CorrelationId,
                Severity = context?.Severity ?? _ErrorSeverity.Error,
                IsUserFacing = _options.IsUserFacing(exception),
                RetryInformation = GetRetryInformation(exception)
            };

            if (context?.IncludeStackTrace == true)
            {
                errorResponse.StackTrace = exception.StackTrace;
            }

            if (context?.Properties != null)
            {
                errorResponse.Properties = new Dictionary<string, object>(context.Properties);
            }

            return errorResponse;
        }

        public virtual _ErrorResponse CreateErrorResponse(string message, Exception? exception = null, _ErrorContext? context = null)
        {
            var errorResponse = new _ErrorResponse
            {
                Message = message,
                ErrorType = exception?.GetType().Name ?? "Unknown",
                StatusCode = exception != null ? GetStatusCode(exception) : 500,
                Timestamp = DateTime.UtcNow,
                RequestId = context?.RequestId,
                CorrelationId = context?.CorrelationId,
                Severity = context?.Severity ?? _ErrorSeverity.Error,
                IsUserFacing = context?.Severity != _ErrorSeverity.Critical && context?.Severity != _ErrorSeverity.Fatal,
                RetryInformation = exception != null ? GetRetryInformation(exception) : null
            };

            if (exception != null && context?.IncludeStackTrace == true)
            {
                errorResponse.StackTrace = exception.StackTrace;
            }

            if (context?.Properties != null)
            {
                errorResponse.Properties = new Dictionary<string, object>(context.Properties);
            }

            return errorResponse;
        }

        public virtual async Task<_ErrorStatistics> GetErrorStatisticsAsync()
        {
            return await Task.FromResult(new _ErrorStatistics
            {
                TotalErrors = _statistics.TotalErrors,
                TotalWarnings = _statistics.TotalWarnings,
                TotalCriticalErrors = _statistics.TotalCriticalErrors,
                TotalFatalErrors = _statistics.TotalFatalErrors,
                ErrorRatePerMinute = CalculateErrorRatePerMinute(),
                AverageErrorProcessingTimeMs = _statistics.AverageErrorProcessingTimeMs,
                LastErrorTime = _statistics.LastErrorTime,
                MostCommonErrorTypes = new Dictionary<string, long>(_errorTypeCounts),
                ErrorDistributionBySeverity = new Dictionary<_ErrorSeverity, long>(_severityCounts),
                ErrorDistributionBySource = new Dictionary<string, long>(_sourceCounts)
            });
        }

        public virtual async Task ClearErrorStatisticsAsync()
        {
            _errorTypeCounts.Clear();
            _severityCounts.Clear();
            _sourceCounts.Clear();
            _statistics.TotalErrors = 0;
            _statistics.TotalWarnings = 0;
            _statistics.TotalCriticalErrors = 0;
            _statistics.TotalFatalErrors = 0;
            _statistics.AverageErrorProcessingTimeMs = 0;
            _statistics.LastErrorTime = null;
            await Task.CompletedTask;
        }

        /// <summary>
        /// Handles error based on severity
        /// </summary>
        /// <param name="errorResponse">The error response</param>
        /// <param name="context">The error context</param>
        protected virtual async Task HandleErrorBySeverityAsync(_ErrorResponse errorResponse, _ErrorContext? context)
        {
            switch (errorResponse.Severity)
            {
                case _ErrorSeverity.Critical:
                    _statistics.TotalCriticalErrors++;
                    await HandleCriticalErrorAsync(errorResponse, context);
                    break;
                case _ErrorSeverity.Fatal:
                    _statistics.TotalFatalErrors++;
                    await HandleFatalErrorAsync(errorResponse, context);
                    break;
                default:
                    await HandleStandardErrorAsync(errorResponse, context);
                    break;
            }
        }

        /// <summary>
        /// Handles critical errors
        /// </summary>
        /// <param name="errorResponse">The error response</param>
        /// <param name="context">The error context</param>
        protected virtual async Task HandleCriticalErrorAsync(_ErrorResponse errorResponse, _ErrorContext? context)
        {
            _logger.LogCritical("Critical error occurred: {ErrorId}", errorResponse.ErrorId);
            await Task.CompletedTask;
        }

        /// <summary>
        /// Handles fatal errors
        /// </summary>
        /// <param name="errorResponse">The error response</param>
        /// <param name="context">The error context</param>
        protected virtual async Task HandleFatalErrorAsync(_ErrorResponse errorResponse, _ErrorContext? context)
        {
            _logger.LogCritical("Fatal error occurred: {ErrorId}", errorResponse.ErrorId);
            await Task.CompletedTask;
        }

        /// <summary>
        /// Handles standard errors
        /// </summary>
        /// <param name="errorResponse">The error response</param>
        /// <param name="context">The error context</param>
        protected virtual async Task HandleStandardErrorAsync(_ErrorResponse errorResponse, _ErrorContext? context)
        {
            await Task.CompletedTask;
        }

        /// <summary>
        /// Notifies administrators
        /// </summary>
        /// <param name="errorResponse">The error response</param>
        /// <param name="context">The error context</param>
        protected virtual async Task NotifyAdministratorsAsync(_ErrorResponse errorResponse, _ErrorContext? context)
        {
            // Implementation for administrator notification
            await Task.CompletedTask;
        }

        /// <summary>
        /// Logs to external systems
        /// </summary>
        /// <param name="errorResponse">The error response</param>
        /// <param name="context">The error context</param>
        protected virtual async Task LogToExternalSystemsAsync(_ErrorResponse errorResponse, _ErrorContext? context)
        {
            // Implementation for external system logging
            await Task.CompletedTask;
        }

        /// <summary>
        /// Gets the log level for a severity
        /// </summary>
        /// <param name="severity">The error severity</param>
        /// <returns>The log level</returns>
        protected virtual LogLevel GetLogLevel(_ErrorSeverity severity)
        {
            return severity switch
            {
                _ErrorSeverity.Information => LogLevel.Information,
                _ErrorSeverity.Warning => LogLevel.Warning,
                _ErrorSeverity.Error => LogLevel.Error,
                _ErrorSeverity.Critical => LogLevel.Critical,
                _ErrorSeverity.Fatal => LogLevel.Critical,
                _ => LogLevel.Error
            };
        }

        /// <summary>
        /// Gets the HTTP status code for an exception
        /// </summary>
        /// <param name="exception">The exception</param>
        /// <returns>The HTTP status code</returns>
        protected virtual int GetStatusCode(Exception exception)
        {
            return exception switch
            {
                ArgumentException => 400,
                ArgumentNullException => 400,
                ArgumentOutOfRangeException => 400,
                InvalidOperationException => 400,
                NotSupportedException => 501,
                NotImplementedException => 501,
                UnauthorizedAccessException => 401,
                _ => 500
            };
        }

        /// <summary>
        /// Gets retry information for an exception
        /// </summary>
        /// <param name="exception">The exception</param>
        /// <returns>The retry information</returns>
        protected virtual _RetryInformation? GetRetryInformation(Exception exception)
        {
            return new _RetryInformation
            {
                IsRetryAllowed = _options.IsRetryAllowed(exception),
                MaxRetryAttempts = _options.MaxRetryAttempts,
                RetryDelayMs = _options.RetryDelayMs
            };
        }

        /// <summary>
        /// Formats a log message
        /// </summary>
        /// <param name="message">The message</param>
        /// <param name="context">The context</param>
        /// <returns>The formatted message</returns>
        protected virtual string FormatLogMessage(string message, _ErrorContext? context)
        {
            var parts = new List<string> { message };

            if (!string.IsNullOrEmpty(context?.OperationName))
            {
                parts.Add($"Operation: {context.OperationName}");
            }

            if (!string.IsNullOrEmpty(context?.UserId))
            {
                parts.Add($"User: {context.UserId}");
            }

            if (!string.IsNullOrEmpty(context?.RequestId))
            {
                parts.Add($"Request: {context.RequestId}");
            }

            if (!string.IsNullOrEmpty(context?.CorrelationId))
            {
                parts.Add($"Correlation: {context.CorrelationId}");
            }

            return string.Join(" | ", parts);
        }

        /// <summary>
        /// Calculates the error rate per minute
        /// </summary>
        /// <returns>The error rate per minute</returns>
        protected virtual double CalculateErrorRatePerMinute()
        {
            // Simple calculation - in a real implementation, you might want to track timestamps
            return _statistics.TotalErrors / Math.Max(1, (DateTime.UtcNow - DateTime.UtcNow.AddHours(-1)).TotalMinutes);
        }
    }

    /// <summary>
    /// Error handler options
    /// </summary>
    public class _ErrorHandlerOptions
    {
        /// <summary>
        /// Gets or sets whether to enable error handling
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Gets or sets whether to enable error logging
        /// </summary>
        public bool EnableLogging { get; set; } = true;

        /// <summary>
        /// Gets or sets whether to enable error statistics
        /// </summary>
        public bool EnableStatistics { get; set; } = true;

        /// <summary>
        /// Gets or sets the maximum retry attempts
        /// </summary>
        public int MaxRetryAttempts { get; set; } = 3;

        /// <summary>
        /// Gets or sets the retry delay in milliseconds
        /// </summary>
        public int RetryDelayMs { get; set; } = 1000;

        /// <summary>
        /// Gets or sets the list of retryable exception types
        /// </summary>
        public List<Type> RetryableExceptionTypes { get; set; } = new()
        {
            typeof(TimeoutException),
            typeof(System.Net.Sockets.SocketException),
            typeof(System.Net.WebException)
        };

        /// <summary>
        /// Gets or sets the list of user-facing exception types
        /// </summary>
        public List<Type> UserFacingExceptionTypes { get; set; } = new()
        {
            typeof(ArgumentException),
            typeof(ArgumentNullException),
            typeof(ArgumentOutOfRangeException),
            typeof(InvalidOperationException)
        };

        /// <summary>
        /// Determines if an exception is retryable
        /// </summary>
        /// <param name="exception">The exception</param>
        /// <returns>True if the exception is retryable</returns>
        public bool IsRetryAllowed(Exception exception)
        {
            return RetryableExceptionTypes.Any(type => type.IsInstanceOfType(exception));
        }

        /// <summary>
        /// Determines if an exception is user-facing
        /// </summary>
        /// <param name="exception">The exception</param>
        /// <returns>True if the exception is user-facing</returns>
        public bool IsUserFacing(Exception exception)
        {
            return UserFacingExceptionTypes.Any(type => type.IsInstanceOfType(exception));
        }
    }
} 