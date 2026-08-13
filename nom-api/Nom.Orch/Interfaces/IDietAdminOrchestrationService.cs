// File: nom-api/Nom.Orch/Interfaces/IDietAdminOrchestrationService.cs

using Nom.Orch.Models.Plan;

namespace Nom.Orch.Interfaces
{
    /// <summary>Admin management of restriction categories and their filter criteria.</summary>
    public interface IDietAdminOrchestrationService
    {
        Task<List<RestrictionGroupModel>> GetGroupsAsync();
        Task<RestrictionCategoryModel?> CreateCategoryAsync(CreateRestrictionCategoryRequest request);
        Task<RestrictionCategoryModel?> UpdateCategoryAsync(long id, UpdateRestrictionCategoryRequest request);
        /// <returns>null = not found; false = in use (409); true = deleted.</returns>
        Task<bool?> DeleteCategoryAsync(long id);
        Task<List<RestrictionCriterionModel>> GetCriteriaAsync(long categoryId);
        Task<RestrictionCriterionModel?> AddCriterionAsync(long categoryId, SaveRestrictionCriterionRequest request);
        Task<bool> DeleteCriterionAsync(long criterionId);
    }
}
