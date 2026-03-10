// File: Nom.Data/Privacy/UserConsentEntity.cs

using System;

namespace Nom.Data.Privacy
{
    /// <summary>
    /// Stores a record of a specific consent a user has given for data processing.
    /// Inherits from BasePrivacyEntity to link it to a person and include audit fields.
    /// </summary>
    public class UserConsentEntity : BasePrivacyEntity
    {
        /// <summary>
        /// The type of consent granted (e.g., "Analytics", "Marketing").
        /// </summary>
        public string ConsentType { get; set; } = string.Empty;

        /// <summary>
        /// Indicates whether the consent is currently active.
        /// </summary>
        public bool IsConsented { get; set; }

        /// <summary>
        /// The timestamp when the consent status was last updated.
        /// </summary>
        public DateTime ConsentTimestamp { get; set; }

        /// <summary>
        /// The version of the privacy policy or terms to which the user consented.
        /// </summary>
        public string ConsentVersion { get; set; } = string.Empty;

        /// <summary>
        /// The legal basis for processing under which the consent was obtained (e.g., "Consent", "Legitimate Interest").
        /// </summary>
        public string LegalBasis { get; set; } = string.Empty;
    }
}
