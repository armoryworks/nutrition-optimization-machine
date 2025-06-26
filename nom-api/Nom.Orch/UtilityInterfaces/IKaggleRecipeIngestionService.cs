// Nom.Orch/UtilityInterfaces/IKaggleRecipeIngestionService.cs
using System;
using System.Threading.Tasks;
using Nom.Data.Audit; // For ImportJobEntity
using Nom.Orch.Models.Recipe; // For RecipeImportRequest, RecipeImportResponse
using Nom.Orch.Models.Audit; // For ImportJobStatusResponse

namespace Nom.Orch.UtilityInterfaces
{
    /// <summary>
    /// Defines the contract for a service responsible for ingesting (reading and processing)
    /// Kaggle-specific recipe data from a CSV file into the database.
    /// This service handles initiating the import (creating the job),
    /// and executing the detailed CSV reading, parsing, and progress tracking,
    /// as well as providing status updates.
    /// </summary>
    public interface IKaggleRecipeIngestionService
    {
        /// <summary>
        /// Initiates the asynchronous import of recipes from a specified Kaggle CSV file.
        /// A new ImportJobEntity record is created and tracked, and the detailed ingestion
        /// process is launched in a background task.
        /// </summary>
        /// <param name="request">The import request containing the source file path.</param>
        /// <returns>A response indicating the initiation status and a ProcessId for tracking.</returns>
        Task<RecipeImportResponse> StartRecipeImportAsync(RecipeImportRequest request);

        /// <summary>
        /// Executes the detailed ingestion process for Kaggle recipe data from a specified CSV file.
        /// This method is intended to be called internally by StartRecipeImportAsync,
        /// within a dedicated background task scope.
        /// It updates the associated ImportJobEntity's progress throughout its execution.
        /// </summary>
        /// <param name="jobProcessId">The unique identifier of the ImportJobEntity associated with this ingestion.</param>
        /// <param name="filePath">The full path to the Kaggle CSV file.</param>
        /// <returns>True if the detailed ingestion process completed without unhandled exceptions.
        /// The ImportJobEntity record will reflect the final status regardless.</returns>
        Task<bool> ExecuteIngestionProcessAsync(Guid jobProcessId, string filePath);

        /// <summary>
        /// Retrieves the current status of a specific import job.
        /// </summary>
        /// <param name="processId">The unique ID of the import job to query.</param>
        /// <returns>An <see cref="ImportJobStatusResponse"/> containing the job's current status and metrics,
        /// or null if a job with the specified ID is not found.</returns>
        Task<ImportJobStatusResponse?> GetImportStatusAsync(Guid processId);
    }
}
