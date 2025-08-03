using Nom.Orch.Models.Recipe;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// Service interface for recipe bulk operations functionality
    /// </summary>
    public interface IRecipeBulkOperationsService
    {
        /// <summary>
        /// Export recipes to file
        /// </summary>
        Task<RecipeBulkOperationResponseModel> ExportRecipesAsync(RecipeBulkExportModel request);

        /// <summary>
        /// Import recipes from file
        /// </summary>
        Task<RecipeBulkOperationResponseModel> ImportRecipesAsync(RecipeBulkImportModel request);

        /// <summary>
        /// Assign categories to recipes
        /// </summary>
        Task<RecipeBulkOperationResponseModel> AssignCategoriesAsync(RecipeBulkAssignCategoriesModel request);

        /// <summary>
        /// Assign tags to recipes
        /// </summary>
        Task<RecipeBulkOperationResponseModel> AssignTagsAsync(RecipeBulkAssignTagsModel request);

        /// <summary>
        /// Update settings for recipes
        /// </summary>
        Task<RecipeBulkOperationResponseModel> UpdateSettingsAsync(RecipeBulkUpdateSettingsModel request);

        /// <summary>
        /// Delete recipes
        /// </summary>
        Task<RecipeBulkOperationResponseModel> DeleteRecipesAsync(RecipeBulkDeleteModel request);

        /// <summary>
        /// Get bulk operation progress
        /// </summary>
        Task<RecipeBulkOperationProgressModel?> GetOperationProgressAsync(long operationId);

        /// <summary>
        /// Get all export files for the current user
        /// </summary>
        Task<List<RecipeExportFileModel>> GetExportFilesAsync();

        /// <summary>
        /// Get export file by ID
        /// </summary>
        Task<RecipeExportFileModel?> GetExportFileAsync(long exportId);

        /// <summary>
        /// Delete export file
        /// </summary>
        Task<bool> DeleteExportFileAsync(long exportId);

        /// <summary>
        /// Clean up expired export files
        /// </summary>
        Task<int> CleanupExpiredExportsAsync();
    }
} 