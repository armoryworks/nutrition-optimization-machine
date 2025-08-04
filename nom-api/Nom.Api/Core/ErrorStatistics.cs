// File: Nom.Api/_Abstractions/_Core/IErrorHandler.cs

using System;
using System.Threading.Tasks;
using System.Collections.Generic; // Added for Dictionary

namespace Nom.Api.Core
{

    /// <summary>
    /// Error statistics
    /// </summary>
    public class ErrorStatistics
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
        public Dictionary<ErrorSeverityEnum, long> ErrorDistributionBySeverity { get; set; } = new();

        /// <summary>
        /// Gets or sets the error distribution by source
        /// </summary>
        public Dictionary<string, long> ErrorDistributionBySource { get; set; } = new();
    }
}