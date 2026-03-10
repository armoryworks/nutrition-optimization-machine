using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nom.Api.Controllers
{
    [ApiController]
    [Route("api/recipe-advanced-search")]
    [Authorize]
    public class RecipeAdvancedSearchController : BaseApiController
    {
        private readonly IRecipeSearchOrchestrationService _searchService;

        public RecipeAdvancedSearchController(IRecipeSearchOrchestrationService searchService)
        {
            _searchService = searchService;
        }

        /// <summary>
        /// Perform fuzzy search for recipes
        /// </summary>
        [HttpGet("fuzzy")]
        public async Task<ActionResult<RecipeSearchResponseModel>> FuzzySearch([FromQuery] string query, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            var result = await _searchService.FuzzySearchAsync(query, page, pageSize);
            return Ok(result);
        }

        /// <summary>
        /// Perform advanced search with multiple filters
        /// </summary>
        [HttpPost("advanced")]
        public async Task<ActionResult<RecipeSearchResponseModel>> AdvancedSearch([FromBody] RecipeAdvancedSearchModel searchModel)
        {
            var result = await _searchService.AdvancedSearchAsync(searchModel);
            return Ok(result);
        }

        /// <summary>
        /// Get recipe suggestions based on various criteria
        /// </summary>
        [HttpPost("suggestions")]
        public async Task<ActionResult<RecipeSuggestionResponseModel>> GetSuggestions([FromBody] RecipeSuggestionModel suggestionModel)
        {
            var result = await _searchService.SuggestRecipesAsync(suggestionModel);
            return Ok(result);
        }

        /// <summary>
        /// Search recipes by categories
        /// </summary>
        [HttpGet("by-categories")]
        public async Task<ActionResult<RecipeSearchResponseModel>> SearchByCategories([FromQuery] List<long> categoryIds, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            var result = await _searchService.SearchByCategoriesAsync(categoryIds, page, pageSize);
            return Ok(result);
        }

        /// <summary>
        /// Search recipes by tags
        /// </summary>
        [HttpGet("by-tags")]
        public async Task<ActionResult<RecipeSearchResponseModel>> SearchByTags([FromQuery] List<long> tagIds, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            var result = await _searchService.SearchByTagsAsync(tagIds, page, pageSize);
            return Ok(result);
        }

        /// <summary>
        /// Search recipes by tools
        /// </summary>
        [HttpGet("by-tools")]
        public async Task<ActionResult<RecipeSearchResponseModel>> SearchByTools([FromQuery] List<long> toolIds, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            var result = await _searchService.SearchByToolsAsync(toolIds, page, pageSize);
            return Ok(result);
        }

        /// <summary>
        /// Get popular recipes
        /// </summary>
        [HttpGet("popular")]
        public async Task<ActionResult<RecipeSearchResponseModel>> GetPopularRecipes([FromQuery] int count = 10)
        {
            var result = await _searchService.GetPopularRecipesAsync(count);
            return Ok(result);
        }

        /// <summary>
        /// Get recent recipes
        /// </summary>
        [HttpGet("recent")]
        public async Task<ActionResult<RecipeSearchResponseModel>> GetRecentRecipes([FromQuery] int count = 10)
        {
            var result = await _searchService.GetRecentRecipesAsync(count);
            return Ok(result);
        }

        /// <summary>
        /// Search recipes by ingredients
        /// </summary>
        [HttpGet("by-ingredients")]
        public async Task<ActionResult<RecipeSearchResponseModel>> SearchByIngredients([FromQuery] List<long> ingredientIds, [FromQuery] int count = 20)
        {
            var result = await _searchService.GetRecipesByIngredientsAsync(ingredientIds, count);
            return Ok(result);
        }
    }
}
