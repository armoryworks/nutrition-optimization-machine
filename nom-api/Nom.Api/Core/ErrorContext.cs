// File: Nom.Api/_Abstractions/_Core/IErrorHandler.cs

using System;
using System.Threading.Tasks;
using System.Collections.Generic; // Added for Dictionary

namespace Nom.Api.Core
{
    /// <summary>
    /// Error context information
    /// </summary>
    public class ErrorContext
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
        public ErrorSeverityEnum Severity { get; set; } = ErrorSeverityEnum.Error;

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

}