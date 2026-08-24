using Nom.Orch.Models.Cookbook;
using Nom.Orch.Models.Recipe;

namespace Nom.Orch.Interfaces
{
    public interface ICookbookOrchestrationService
    {
        Task<List<CookbookResponseModel>> GetCookbooksAsync(long householdId);
        Task<List<CookbookResponseModel>> GetCookbooksForRecipeAsync(long householdId, long recipeId);
        Task<CookbookResponseModel?> GetCookbookAsync(long id);
        Task<long> CreateCookbookAsync(CookbookCreateModel model);
        Task<CookbookResponseModel?> UpdateCookbookAsync(long id, CookbookUpdateModel model);
        Task<bool> DeleteCookbookAsync(long id);
        Task<bool> AddRecipeToCookbookAsync(long cookbookId, long recipeId);
        Task<bool> RemoveRecipeFromCookbookAsync(long cookbookId, long recipeId);
        Task<List<RecipeResponseModel>> GetCookbookRecipesAsync(long cookbookId);
    }
}
