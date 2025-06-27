// Nom.Orch/Models/Recipe/RecipeImportFromFileRequestModel.cs
using Microsoft.AspNetCore.Http; // Required for IFormFile
using System.ComponentModel.DataAnnotations; // Required for [Required]

namespace Nom.Orch.Models.Recipe
{
    /// <summary>
    /// Represents a request model for importing recipes from an uploaded file,
    /// suitable for multipart/form-data API endpoints.
    /// </summary>
    public class RecipeImportFromFileRequestModel
    {
        /// <summary>
        /// The CSV file containing recipe data to be imported.
        /// </summary>
        [Required(ErrorMessage = "A file must be provided for import.")]
        public IFormFile File { get; set; } = default!;

        /// <summary>
        /// A descriptive name for the import job (e.g., "Kaggle Recipes Batch 1").
        /// </summary>
        [Required(ErrorMessage = "Job name is required.")]
        [MaxLength(255, ErrorMessage = "Job name cannot exceed 255 characters.")]
        public string JobName { get; set; } = string.Empty;
    }
}
