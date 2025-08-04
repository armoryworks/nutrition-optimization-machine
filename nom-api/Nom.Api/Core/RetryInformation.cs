// File: Nom.Api/_Abstractions/_Core/IErrorHandler.cs

using System;
using System.Threading.Tasks;
using System.Collections.Generic; // Added for Dictionary

namespace Nom.Api.Core
{

    /// <summary>
    /// Retry information
    /// </summary>
    public class RetryInformation
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

}