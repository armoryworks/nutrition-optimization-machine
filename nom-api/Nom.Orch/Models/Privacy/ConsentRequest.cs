// File: Nom.Orch/Models/Privacy/ConsentRequest.cs

using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Privacy
{
    /// <summary>
    /// Represents a single user consent preference submitted to the API.
    /// </summary>
    public class ConsentRequest
    {
        /// <summary>
        /// The reference ID for the type of consent (e.g., Analytics, Marketing).
        /// Corresponds to an ID in the reference.Reference table.
        /// </summary>
        [Required]
        public long ConsentTypeRefId { get; set; }

        /// <summary>
        /// Indicates whether the user has granted or revoked this consent.
        /// </summary>
        [Required]
        public bool IsConsented { get; set; }
    }
}
