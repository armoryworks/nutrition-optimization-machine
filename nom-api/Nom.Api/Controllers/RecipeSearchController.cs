using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;
using System.ComponentModel.DataAnnotations;

namespace Nom.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class RecipeSearchController : BaseApiController
    {
        private readonly IRecipeSearchOrchestrationService _searchOrchestrationService;

        public RecipeSearchController(
            IRecipeSearchOrchestrationService searchOrchestrationService)
        {
            _searchOrchestrationService = searchOrchestrationService;
        }

        /// <summary>
        /// Search recipes. Anonymous users can only search public, approved recipes.
        /// </summary>
        [AllowAnonymous]
        [HttpPost("search")]
        [ProducesResponseType(typeof(RecipeSearchResponseModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> SearchRecipes([FromBody] RecipeSearchModel searchModel)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            // Anonymous callers may only ever see public, approved recipes — force the
            // filter server-side rather than trusting the request body to opt in.
            if (!(User.Identity?.IsAuthenticated ?? false))
            {
                searchModel.IsPublic = true;
                searchModel.IsApproved = true;
            }

            // Visibility filtering keys off the requester, never the body.
            searchModel.RequesterPersonId = GetCurrentPersonId();

            var results = await _searchOrchestrationService.SearchRecipesAsync(searchModel);
            return Ok(results);
        }

        /// <summary>
        /// Get search suggestions for autocomplete. Anonymous access allowed.
        /// </summary>
        [AllowAnonymous]
        [HttpGet("suggestions")]
        [ProducesResponseType(typeof(List<string>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetSearchSuggestions([Required] string query)
        {
            var suggestions = await _searchOrchestrationService.GetSearchSuggestionsAsync(query);
            return Ok(suggestions);
        }

        /// <summary>
        /// Get popular public recipes. Anonymous access allowed.
        /// </summary>
        [AllowAnonymous]
        [HttpGet("popular")]
        [ProducesResponseType(typeof(RecipeSearchResponseModel), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetPopularRecipes([Range(1, 50)] int count = 10)
        {
            var results = await _searchOrchestrationService.GetPopularRecipesAsync(count);
            return Ok(results);
        }

        /// <summary>
        /// Get recently added public recipes. Anonymous access allowed.
        /// </summary>
        [AllowAnonymous]
        [HttpGet("recent")]
        [ProducesResponseType(typeof(RecipeSearchResponseModel), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetRecentRecipes([Range(1, 50)] int count = 10)
        {
            var results = await _searchOrchestrationService.GetRecentRecipesAsync(count);
            return Ok(results);
        }

        /// <summary>
        /// Get random approved recipes, optionally filtered by household restrictions and calorie range.
        /// </summary>
        [HttpGet("random")]
        [ProducesResponseType(typeof(RecipeSearchResponseModel), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetRandomRecipes([Range(1, 50)] int count = 1, long? householdId = null, int? minCalories = null, int? maxCalories = null, long? recipeTypeId = null)
        {
            // Filtering by a household reveals its restriction shape — members only.
            if (householdId.HasValue && !IsHouseholdMember(householdId.Value)) return Forbid();
            var results = await _searchOrchestrationService.GetRandomRecipesAsync(count, householdId, minCalories, maxCalories, recipeTypeId);
            return Ok(results);
        }

        [HttpPost("by-ingredients")]
        [ProducesResponseType(typeof(RecipeSearchResponseModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> GetRecipesByIngredients([FromBody] List<long> ingredientIds, [Range(1, 50)] int count = 20)
        {
            if (ingredientIds == null || !ingredientIds.Any())
            {
                return BadRequest("At least one ingredient ID is required.");
            }

            var results = await _searchOrchestrationService.GetRecipesByIngredientsAsync(ingredientIds, count);
            return Ok(results);
        }
    }
}