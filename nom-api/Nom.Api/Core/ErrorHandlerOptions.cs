// File: Nom.Api/_Abstractions/_Core/BaseErrorHandler.cs

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Nom.Api.Core
{

    /// <summary>
    /// Error handler options
    /// </summary>
    public class ErrorHandlerOptions
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