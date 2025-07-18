// File: Nom.Orch/Models/Privacy/DataDeletionRequest.cs

using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Privacy
{
    /// <summary>
    /// Represents a user's request for their data to be deleted.
    /// </summary>
    public class DataDeletionRequest
    {
        /// <summary>
        /// A confirmation flag to prevent accidental deletion.
        /// </summary>
        [Required]
        [Compare(nameof(Confirm), ErrorMessage = "Confirmation must be true to proceed with deletion.")]
        public bool Confirm { get; set; }
    }
}
