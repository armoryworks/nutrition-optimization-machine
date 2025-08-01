using Nom.Orch.Models.Recipe;

namespace Nom.Orch.Interfaces
{
    public interface IRecipeSearchOrchestrationService
    {
        Task<RecipeSearchResponseModel> SearchRecipesAsync(RecipeSearchModel searchModel);
        Task<List<string>> GetSearchSuggestionsAsync(string query);
        Task<RecipeSearchResponseModel> GetPopularRecipesAsync(int count = 10);
        Task<RecipeSearchResponseModel> GetRecentRecipesAsync(int count = 10);
        Task<RecipeSearchResponseModel> GetRecipesByIngredientsAsync(List<long> ingredientIds, int count = 20);
    }
} 