using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nom.Api.Filters;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;

namespace Nom.Api.Controllers
{
    /// <summary>
    /// API controller for recipe suggestion functionality
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    // Quarantined (audit): no UI, doc or E2E consumer; enable per tenant with Features:RecipeSuggestions=true.
    [FeatureGate("RecipeSuggestions")]
    [ApiExplorerSettings(IgnoreApi = true)]
    [Authorize]
    public class RecipeSuggestionController : BaseApiController
    {
        private readonly IRecipeSuggestionService _recipeSuggestionService;
        private readonly ILogger<RecipeSuggestionController> _logger;

        public RecipeSuggestionController(
            IRecipeSuggestionService recipeSuggestionService,
            ILogger<RecipeSuggestionController> logger)
        {
            _recipeSuggestionService = recipeSuggestionService;
            _logger = logger;
        }

        /// <summary>
        /// Get recipe suggestions based on available ingredients and tools
        /// </summary>
        [HttpGet("suggestions")]
        public async Task<ActionResult<RecipeSuggestionResponseModel>> GetRecipeSuggestions(
            [FromQuery] RecipeSuggestionQueryModel query,
            [FromQuery] List<long>? ingredientIds = null,
            [FromQuery] List<long>? toolIds = null)
        {
            _logger.LogInformation("Getting recipe suggestions for ingredients: {IngredientIds}, tools: {ToolIds}",
                ingredientIds?.Count ?? 0, toolIds?.Count ?? 0);

            var result = await _recipeSuggestionService.GetRecipeSuggestionsAsync(query, ingredientIds, toolIds);
            return Ok(result);
        }

        /// <summary>
        /// Generate keyword-based recipe suggestions (matches recipes by description keywords).
        /// Note: This is not AI/ML-powered; it uses keyword extraction and database matching.
        /// </summary>
        [HttpPost("keyword-suggestions")]
        [HttpPost("ai-suggestions")] // Backwards-compatible alias
        public async Task<ActionResult<AIRecipeSuggestionResponseModel>> GenerateKeywordSuggestions(
            [FromBody] AIRecipeSuggestionRequestModel request)
        {
            _logger.LogInformation("Generating keyword-based recipe suggestions for: {Description}", request.Description);

            var result = await _recipeSuggestionService.GenerateAIRecipeSuggestionsAsync(request);
            return Ok(result);
        }

        /// <summary>
        /// Get recipe recommendations based on user behavior
        /// </summary>
        [HttpGet("recommendations")]
        public async Task<ActionResult<List<RecipeRecommendationModel>>> GetRecipeRecommendations()
        {
            var userId = GetCurrentPersonIdRequired();
            _logger.LogInformation("Getting recipe recommendations for user: {UserId}", userId);

            var result = await _recipeSuggestionService.GetRecipeRecommendationsAsync(userId);
            return Ok(result);
        }

        /// <summary>
        /// Discover recipes based on various criteria
        /// </summary>
        [HttpPost("discover")]
        public async Task<ActionResult<RecipeSuggestionResponseModel>> DiscoverRecipes(
            [FromBody] RecipeDiscoveryRequestModel request)
        {
            _logger.LogInformation("Discovering recipes with criteria");

            var result = await _recipeSuggestionService.DiscoverRecipesAsync(request);
            return Ok(result);
        }

        /// <summary>
        /// Get similar recipes to a given recipe
        /// </summary>
        [HttpGet("similar/{recipeId}")]
        public async Task<ActionResult<List<RecipeSimilarityModel>>> GetSimilarRecipes(
            long recipeId,
            [FromQuery] int limit = 10)
        {
            _logger.LogInformation("Getting similar recipes for recipe: {RecipeId}", recipeId);

            var result = await _recipeSuggestionService.GetSimilarRecipesAsync(recipeId, limit);
            return Ok(result);
        }

        /// <summary>
        /// Get trending recipes
        /// </summary>
        [HttpGet("trending")]
        public async Task<ActionResult<List<RecipeTrendingModel>>> GetTrendingRecipes(
            [FromQuery] int limit = 10)
        {
            _logger.LogInformation("Getting trending recipes with limit: {Limit}", limit);

            var result = await _recipeSuggestionService.GetTrendingRecipesAsync(limit);
            return Ok(result);
        }

        /// <summary>
        /// Get seasonal recipe suggestions
        /// </summary>
        [HttpGet("seasonal")]
        public async Task<ActionResult<List<SeasonalRecipeModel>>> GetSeasonalRecipes(
            [FromQuery] string? season = null)
        {
            _logger.LogInformation("Getting seasonal recipes for season: {Season}", season ?? "current");

            var result = await _recipeSuggestionService.GetSeasonalRecipesAsync(season);
            return Ok(result);
        }

        /// <summary>
        /// Get recipe suggestions for a specific meal type
        /// </summary>
        [HttpGet("meal-type/{mealType}")]
        public async Task<ActionResult<RecipeSuggestionResponseModel>> GetMealTypeSuggestions(
            string mealType,
            [FromQuery] RecipeSuggestionQueryModel query)
        {
            _logger.LogInformation("Getting {MealType} suggestions", mealType);

            var result = await _recipeSuggestionService.GetMealTypeSuggestionsAsync(mealType, query);
            return Ok(result);
        }

        /// <summary>
        /// Get recipe suggestions based on dietary restrictions
        /// </summary>
        [HttpPost("dietary")]
        public async Task<ActionResult<RecipeSuggestionResponseModel>> GetDietarySuggestions(
            [FromBody] List<string> dietaryRestrictions,
            [FromQuery] RecipeSuggestionQueryModel query)
        {
            _logger.LogInformation("Getting dietary suggestions for: {Restrictions}", string.Join(", ", dietaryRestrictions));

            var result = await _recipeSuggestionService.GetDietarySuggestionsAsync(dietaryRestrictions, query);
            return Ok(result);
        }

        /// <summary>
        /// Get recipe suggestions based on cuisine preferences
        /// </summary>
        [HttpPost("cuisine")]
        public async Task<ActionResult<RecipeSuggestionResponseModel>> GetCuisineSuggestions(
            [FromBody] List<string> cuisines,
            [FromQuery] RecipeSuggestionQueryModel query)
        {
            _logger.LogInformation("Getting cuisine suggestions for: {Cuisines}", string.Join(", ", cuisines));

            var result = await _recipeSuggestionService.GetCuisineSuggestionsAsync(cuisines, query);
            return Ok(result);
        }

        /// <summary>
        /// Get quick recipe suggestions based on available time
        /// </summary>
        [HttpGet("quick")]
        public async Task<ActionResult<RecipeSuggestionResponseModel>> GetQuickRecipeSuggestions(
            [FromQuery] int maxTimeMinutes,
            [FromQuery] RecipeSuggestionQueryModel query)
        {
            _logger.LogInformation("Getting quick recipe suggestions for max time: {MaxTime} minutes", maxTimeMinutes);

            var result = await _recipeSuggestionService.GetQuickRecipeSuggestionsAsync(maxTimeMinutes, query);
            return Ok(result);
        }

        /// <summary>
        /// Get budget recipe suggestions
        /// </summary>
        [HttpGet("budget")]
        public async Task<ActionResult<RecipeSuggestionResponseModel>> GetBudgetRecipeSuggestions(
            [FromQuery] decimal maxBudget,
            [FromQuery] RecipeSuggestionQueryModel query)
        {
            _logger.LogInformation("Getting budget recipe suggestions for max budget: {MaxBudget}", maxBudget);

            var result = await _recipeSuggestionService.GetBudgetRecipeSuggestionsAsync(maxBudget, query);
            return Ok(result);
        }

        /// <summary>
        /// Get beginner recipe suggestions
        /// </summary>
        [HttpGet("beginner")]
        public async Task<ActionResult<RecipeSuggestionResponseModel>> GetBeginnerRecipeSuggestions(
            [FromQuery] RecipeSuggestionQueryModel query)
        {
            _logger.LogInformation("Getting beginner recipe suggestions");

            var result = await _recipeSuggestionService.GetBeginnerRecipeSuggestionsAsync(query);
            return Ok(result);
        }

        /// <summary>
        /// Get advanced recipe suggestions
        /// </summary>
        [HttpGet("advanced")]
        public async Task<ActionResult<RecipeSuggestionResponseModel>> GetAdvancedRecipeSuggestions(
            [FromQuery] RecipeSuggestionQueryModel query)
        {
            _logger.LogInformation("Getting advanced recipe suggestions");

            var result = await _recipeSuggestionService.GetAdvancedRecipeSuggestionsAsync(query);
            return Ok(result);
        }

        /// <summary>
        /// Get recipe suggestion analytics
        /// </summary>
        [HttpGet("analytics")]
        public async Task<ActionResult<RecipeSuggestionAnalyticsModel>> GetSuggestionAnalytics()
        {
            _logger.LogInformation("Getting recipe suggestion analytics");

            var result = await _recipeSuggestionService.GetSuggestionAnalyticsAsync();
            return Ok(result);
        }

        /// <summary>
        /// Update recipe suggestion preferences
        /// </summary>
        [HttpPut("preferences")]
        public async Task<ActionResult<bool>> UpdateSuggestionPreferences(
            [FromBody] Dictionary<string, object> preferences)
        {
            var userId = GetCurrentPersonIdRequired();
            _logger.LogInformation("Updating suggestion preferences for user: {UserId}", userId);

            var result = await _recipeSuggestionService.UpdateSuggestionPreferencesAsync(userId, preferences);
            return Ok(result);
        }

        /// <summary>
        /// Get recipe suggestion preferences
        /// </summary>
        [HttpGet("preferences")]
        public async Task<ActionResult<Dictionary<string, object>>> GetSuggestionPreferences()
        {
            var userId = GetCurrentPersonIdRequired();
            _logger.LogInformation("Getting suggestion preferences for user: {UserId}", userId);

            var result = await _recipeSuggestionService.GetSuggestionPreferencesAsync(userId);
            return Ok(result);
        }
    }
}