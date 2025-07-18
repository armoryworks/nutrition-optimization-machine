// File: Nom.Orch/Models/Privacy/UpdateConsentRequest.cs

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Privacy
{
    /// <summary>
    /// Represents a request to update one or more user consent settings.
    /// </summary>
    public class UpdateConsentRequest
    {
        /// <summary>
        /// A list of consent preferences to be updated.
        /// </summary>
        [Required]
        [MinLength(1, ErrorMessage = "At least one consent setting must be provided.")]
        public List<ConsentRequest> Consents { get; set; } = new List<ConsentRequest>();
    }
}
