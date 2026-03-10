// File: Nom.Data/Privacy/PrivacyRequestEntity.cs

using System;

namespace Nom.Data.Privacy
{
    /// <summary>
    /// Tracks user requests related to their data rights under GDPR (e.g., access, erasure).
    /// Inherits from BasePrivacyEntity to link it to a person and include audit fields.
    /// </summary>
    public class PrivacyRequestEntity : BasePrivacyEntity
    {
        /// <summary>
        /// The type of GDPR request (e.g., "DataExport", "DataDeletion", "DataRectification").
        /// </summary>
        public string RequestType { get; set; } = string.Empty;

        /// <summary>
        /// The current status of the request (e.g., "Pending", "InProgress", "Completed", "Failed").
        /// </summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>
        /// The timestamp when the user submitted the request.
        /// </summary>
        public DateTime RequestTimestamp { get; set; }

        /// <summary>
        /// The timestamp when the request was completed. Nullable if not yet completed.
        /// </summary>
        public DateTime? CompletionTimestamp { get; set; }

        /// <summary>
        /// JSON-formatted string containing any specific details or parameters for the request.
        /// </summary>
        public string RequestDetails { get; set; } = string.Empty;
    }
}
