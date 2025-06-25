// Nom.Orch/Models/Recipe/RecipeImportResponse.cs

namespace Nom.Orch.Models.Recipe // Corrected namespace: Nom.Orch.Models.Recipe
{
    /// <summary>
    /// Represents the response body for a recipe import operation.
    /// </summary>
    public class RecipeImportResponse
    {
        /// <summary>
        /// Indicates if the import operation was successfully initiated.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// A message providing details about the import operation (e.g., "Import started successfully").
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// An optional ID to track the asynchronous import process.
        /// This can be used to query the status later via another API endpoint.
        /// </summary>
        public Guid? ProcessId { get; set; }
    }
}
