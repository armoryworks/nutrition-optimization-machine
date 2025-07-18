// File: Nom.Data/Privacy/DataProcessingLogEntity.cs

using System;
using System.ComponentModel.DataAnnotations;

namespace Nom.Data.Privacy
{
    /// <summary>
    /// Creates an audit trail of all actions performed on a user's personal data.
    /// Inherits from BasePrivacyEntity to link it to a person and include audit fields.
    /// </summary>
    public class DataProcessingLogEntity : BasePrivacyEntity
    {
        /// <summary>
        /// The type of action performed (e.g., "Read", "Update", "Delete", "Export").
        /// </summary>
        [Required]
        public string ActionType { get; set; } = string.Empty;

        /// <summary>
        /// The ID of the user or system process that performed the action.
        /// </summary>
        public long ActorId { get; set; }

        /// <summary>
        /// The timestamp when the action occurred.
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// JSON-formatted string containing details about the action (e.g., changed fields).
        /// </summary>
        public string Details { get; set; } = string.Empty;

        /// <summary>
        /// The IP address from which the action was initiated.
        /// </summary>
        public string IpAddress { get; set; } = string.Empty;

        /// <summary>
        /// The user agent string of the client that initiated the action.
        /// </summary>
        public string UserAgent { get; set; } = string.Empty;
    }
}
