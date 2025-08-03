using Nom.Orch.Models.Recipe;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// Service interface for recipe suggestion functionality
    /// </summary>
    public interface IRecipeSuggestionService
    {
        /// <summary>
        /// Get recipe suggestions based on available ingredients and tools
        /// </summary>
        Task<RecipeSuggestionResponseModel> GetRecipeSuggestionsAsync(RecipeSuggestionQueryModel query, List<long>? ingredientIds = null, List<long>? toolIds = null);

        /// <summary>
        /// Generate AI-powered recipe suggestions
        /// </summary>
        Task<AIRecipeSuggestionResponseModel> GenerateAIRecipeSuggestionsAsync(AIRecipeSuggestionRequestModel request);

        /// <summary>
        /// Get recipe recommendations based on user behavior
        /// </summary>
        Task<List<RecipeRecommendationModel>> GetRecipeRecommendationsAsync(long userId);

        /// <summary>
        /// Discover recipes based on various criteria
        /// </summary>
        Task<RecipeSuggestionResponseModel> DiscoverRecipesAsync(RecipeDiscoveryRequestModel request);

        /// <summary>
        /// Get similar recipes to a given recipe
        /// </summary>
        Task<List<RecipeSimilarityModel>> GetSimilarRecipesAsync(long recipeId, int limit = 10);

        /// <summary>
        /// Get trending recipes
        /// </summary>
        Task<List<RecipeTrendingModel>> GetTrendingRecipesAsync(int limit = 10);

        /// <summary>
        /// Get seasonal recipe suggestions
        /// </summary>
        Task<List<SeasonalRecipeModel>> GetSeasonalRecipesAsync(string? season = null);

        /// <summary>
        /// Get recipe suggestions for a specific meal type
        /// </summary>
        Task<RecipeSuggestionResponseModel> GetMealTypeSuggestionsAsync(string mealType, RecipeSuggestionQueryModel query);

        /// <summary>
        /// Get recipe suggestions based on dietary restrictions
        /// </summary>
        Task<RecipeSuggestionResponseModel> GetDietarySuggestionsAsync(List<string> dietaryRestrictions, RecipeSuggestionQueryModel query);

        /// <summary>
        /// Get recipe suggestions based on cuisine preferences
        /// </summary>
        Task<RecipeSuggestionResponseModel> GetCuisineSuggestionsAsync(List<string> cuisines, RecipeSuggestionQueryModel query);

        /// <summary>
        /// Get recipe suggestions based on available time
        /// </summary>
        Task<RecipeSuggestionResponseModel> GetQuickRecipeSuggestionsAsync(int maxTimeMinutes, RecipeSuggestionQueryModel query);

        /// <summary>
        /// Get recipe suggestions based on budget constraints
        /// </summary>
        Task<RecipeSuggestionResponseModel> GetBudgetRecipeSuggestionsAsync(decimal maxBudget, RecipeSuggestionQueryModel query);

        /// <summary>
        /// Get recipe suggestions for beginners
        /// </summary>
        Task<RecipeSuggestionResponseModel> GetBeginnerRecipeSuggestionsAsync(RecipeSuggestionQueryModel query);

        /// <summary>
        /// Get recipe suggestions for advanced cooks
        /// </summary>
        Task<RecipeSuggestionResponseModel> GetAdvancedRecipeSuggestionsAsync(RecipeSuggestionQueryModel query);

        /// <summary>
        /// Get recipe suggestions based on nutritional preferences
        /// </summary>
        Task<RecipeSuggestionResponseModel> GetNutritionalSuggestionsAsync(Dictionary<string, object> nutritionalPreferences, RecipeSuggestionQueryModel query);

        /// <summary>
        /// Get recipe suggestions based on cooking method preferences
        /// </summary>
        Task<RecipeSuggestionResponseModel> GetCookingMethodSuggestionsAsync(List<string> cookingMethods, RecipeSuggestionQueryModel query);

        /// <summary>
        /// Get recipe suggestions based on serving size
        /// </summary>
        Task<RecipeSuggestionResponseModel> GetServingSizeSuggestionsAsync(int servingSize, RecipeSuggestionQueryModel query);

        /// <summary>
        /// Get recipe suggestions based on available equipment
        /// </summary>
        Task<RecipeSuggestionResponseModel> GetEquipmentBasedSuggestionsAsync(List<string> availableEquipment, RecipeSuggestionQueryModel query);

        /// <summary>
        /// Get recipe suggestions based on seasonal ingredients
        /// </summary>
        Task<RecipeSuggestionResponseModel> GetSeasonalIngredientSuggestionsAsync(List<string> seasonalIngredients, RecipeSuggestionQueryModel query);

        /// <summary>
        /// Get recipe suggestions based on user ratings and reviews
        /// </summary>
        Task<RecipeSuggestionResponseModel> GetRatedRecipeSuggestionsAsync(decimal minRating, RecipeSuggestionQueryModel query);

        /// <summary>
        /// Get recipe suggestions based on popularity
        /// </summary>
        Task<RecipeSuggestionResponseModel> GetPopularRecipeSuggestionsAsync(RecipeSuggestionQueryModel query);

        /// <summary>
        /// Get recipe suggestions based on recent activity
        /// </summary>
        Task<RecipeSuggestionResponseModel> GetRecentRecipeSuggestionsAsync(RecipeSuggestionQueryModel query);

        /// <summary>
        /// Get recipe suggestions based on user favorites
        /// </summary>
        Task<RecipeSuggestionResponseModel> GetFavoriteBasedSuggestionsAsync(long userId, RecipeSuggestionQueryModel query);

        /// <summary>
        /// Get recipe suggestions based on cooking history
        /// </summary>
        Task<RecipeSuggestionResponseModel> GetHistoryBasedSuggestionsAsync(long userId, RecipeSuggestionQueryModel query);

        /// <summary>
        /// Get recipe suggestion analytics
        /// </summary>
        Task<RecipeSuggestionAnalyticsModel> GetSuggestionAnalyticsAsync();

        /// <summary>
        /// Update recipe suggestion preferences
        /// </summary>
        Task<bool> UpdateSuggestionPreferencesAsync(long userId, Dictionary<string, object> preferences);

        /// <summary>
        /// Get recipe suggestion preferences
        /// </summary>
        Task<Dictionary<string, object>> GetSuggestionPreferencesAsync(long userId);
    }
} 