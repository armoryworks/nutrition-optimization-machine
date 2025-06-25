// Nom.Orch/Interfaces/IRecipeOrchestrationService.cs
using Nom.Orch.Models.Recipe; // Still references models from Nom.Orch.Models.Recipe
using System.Threading.Tasks;

namespace Nom.Orch.Interfaces // Corrected namespace
{
    /// <summary>
    /// Defines the contract for services that orchestrate recipe-related operations,
    /// including importing recipe data from external sources.
    /// </summary>
    public interface IRecipeOrchestrationService
    {
        /// <summary>
        /// Initiates the asynchronous import of recipes from a specified file path.
        /// </summary>
        /// <param name="request">The import request containing the source file path.</param>
        /// <returns>A response indicating the success of the initiation and an optional process ID.</returns>
        Task<RecipeImportResponse> StartRecipeImportAsync(RecipeImportRequest request);

        /// <summary>
        /// Retrieves the current status of a recipe import process.
        /// (This method will be implemented later, as part of viewing import status/logs)
        /// </summary>
        /// <param name="processId">The ID of the import process to query.</param>
        /// <returns>A response containing the import status.</returns>
        // Task<RecipeImportStatusResponse> GetImportStatusAsync(Guid processId); // Future task
    }
}
