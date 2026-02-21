using Nom.Orch.Models.Recipe;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nom.Orch.Interfaces
{
    public interface IRecipeSearchOrchestrationService
    {
        Task<RecipeSearchResponseModel> SearchRecipesAsync(RecipeSearchModel searchModel);
        Task<List<string>> GetSearchSuggestionsAsync(string query);
        Task<RecipeSearchResponseModel> GetPopularRecipesAsync(int count = 10);
        Task<RecipeSearchResponseModel> GetRecentRecipesAsync(int count = 10);
        Task<RecipeSearchResponseModel> GetRandomRecipesAsync(int count = 1, long? householdId = null, int? minCalories = null, int? maxCalories = null, long? recipeTypeId = null);
        Task<RecipeSearchResponseModel> GetRecipesByIngredientsAsync(List<long> ingredientIds, int count = 20);

        // Advanced search features (from Mealie)
        Task<RecipeSearchResponseModel> FuzzySearchAsync(string query, int page = 1, int pageSize = 20);
        Task<RecipeSearchResponseModel> AdvancedSearchAsync(RecipeAdvancedSearchModel searchModel);
        Task<RecipeSuggestionResponseModel> SuggestRecipesAsync(RecipeSuggestionModel suggestionModel);
        Task<RecipeSearchResponseModel> SearchByCategoriesAsync(List<long> categoryIds, int page = 1, int pageSize = 20);
        Task<RecipeSearchResponseModel> SearchByTagsAsync(List<long> tagIds, int page = 1, int pageSize = 20);
        Task<RecipeSearchResponseModel> SearchByToolsAsync(List<long> toolIds, int page = 1, int pageSize = 20);
    }
} 