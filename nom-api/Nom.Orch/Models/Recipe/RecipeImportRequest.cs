// Nom.Orch/Models/Recipe/RecipeImportRequest.cs
using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Recipe // Corrected namespace: Nom.Orch.Models.Recipe
{
    /// <summary>
    /// Represents the request body for triggering a recipe import from a specified source file.
    /// </summary>
    public class RecipeImportRequest
    {
        /// <summary>
        /// The full path to the source file (e.g., CSV) on the server's file system.
        /// This path should be accessible by the application.
        /// </summary>
        [Required(ErrorMessage = "Source file path is required.")]
        [MinLength(5, ErrorMessage = "Source file path must be at least 5 characters long.")]
        // Further validation (e.g., file extension, existence) will be handled in the service layer.
        public string SourceFilePath { get; set; } = string.Empty;
    }
}
