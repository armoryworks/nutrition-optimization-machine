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
        private readonly ILogger<RecipeSearchController> _logger;

        public RecipeSearchController(
            IRecipeSearchOrchestrationService searchOrchestrationService,
            ILogger<RecipeSearchController> logger)
        {
            _searchOrchestrationService = searchOrchestrationService;
            _logger = logger;
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

            try
            {
                var results = await _searchOrchestrationService.SearchRecipesAsync(searchModel);
                return Ok(results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in SearchRecipes.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        /// <summary>
        /// Get search suggestions for autocomplete. Anonymous access allowed.
        /// </summary>
        [AllowAnonymous]
        [HttpGet("suggestions")]
        [ProducesResponseType(typeof(List<string>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetSearchSuggestions([Required] string query)
        {
            try
            {
                var suggestions = await _searchOrchestrationService.GetSearchSuggestionsAsync(query);
                return Ok(suggestions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in GetSearchSuggestions.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        /// <summary>
        /// Get popular public recipes. Anonymous access allowed.
        /// </summary>
        [AllowAnonymous]
        [HttpGet("popular")]
        [ProducesResponseType(typeof(RecipeSearchResponseModel), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetPopularRecipes([Range(1, 50)] int count = 10)
        {
            try
            {
                var results = await _searchOrchestrationService.GetPopularRecipesAsync(count);
                return Ok(results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in GetPopularRecipes.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        /// <summary>
        /// Get recently added public recipes. Anonymous access allowed.
        /// </summary>
        [AllowAnonymous]
        [HttpGet("recent")]
        [ProducesResponseType(typeof(RecipeSearchResponseModel), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetRecentRecipes([Range(1, 50)] int count = 10)
        {
            try
            {
                var results = await _searchOrchestrationService.GetRecentRecipesAsync(count);
                return Ok(results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in GetRecentRecipes.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
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

            try
            {
                var results = await _searchOrchestrationService.GetRecipesByIngredientsAsync(ingredientIds, count);
                return Ok(results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in GetRecipesByIngredients.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }
    }
} 