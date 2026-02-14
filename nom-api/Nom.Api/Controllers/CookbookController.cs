using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Cookbook;
using Nom.Orch.Models.Recipe;
using System.ComponentModel.DataAnnotations;

namespace Nom.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class CookbookController : BaseApiController
    {
        private readonly ICookbookOrchestrationService _cookbookService;
        private readonly ILogger<CookbookController> _logger;

        public CookbookController(
            ICookbookOrchestrationService cookbookService,
            ILogger<CookbookController> logger)
        {
            _cookbookService = cookbookService;
            _logger = logger;
        }

        [HttpGet]
        [ProducesResponseType(typeof(List<CookbookResponseModel>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetCookbooks([FromQuery, Required] long householdId)
        {
            try
            {
                var result = await _cookbookService.GetCookbooksAsync(householdId);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting cookbooks for household {HouseholdId}", householdId);
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        [HttpGet("{id}")]
        [ProducesResponseType(typeof(CookbookResponseModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetCookbook(long id)
        {
            try
            {
                var result = await _cookbookService.GetCookbookAsync(id);
                if (result == null) return NotFound();
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting cookbook {CookbookId}", id);
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        [HttpPost]
        [ProducesResponseType(typeof(long), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> CreateCookbook([FromBody] CookbookCreateModel model)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            try
            {
                var id = await _cookbookService.CreateCookbookAsync(model);
                return Ok(id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating cookbook");
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        [HttpPut("{id}")]
        [ProducesResponseType(typeof(CookbookResponseModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> UpdateCookbook(long id, [FromBody] CookbookUpdateModel model)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            try
            {
                var result = await _cookbookService.UpdateCookbookAsync(id, model);
                if (result == null) return NotFound();
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating cookbook {CookbookId}", id);
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        [HttpDelete("{id}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteCookbook(long id)
        {
            try
            {
                var success = await _cookbookService.DeleteCookbookAsync(id);
                if (!success) return NotFound();
                return Ok(new { Message = "Cookbook deleted successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting cookbook {CookbookId}", id);
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        [HttpPost("{id}/recipe/{recipeId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> AddRecipe(long id, long recipeId)
        {
            try
            {
                var added = await _cookbookService.AddRecipeToCookbookAsync(id, recipeId);
                if (!added) return Conflict(new { Message = "Recipe already in cookbook." });
                return Ok(new { Message = "Recipe added to cookbook." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding recipe {RecipeId} to cookbook {CookbookId}", recipeId, id);
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        [HttpDelete("{id}/recipe/{recipeId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> RemoveRecipe(long id, long recipeId)
        {
            try
            {
                var removed = await _cookbookService.RemoveRecipeFromCookbookAsync(id, recipeId);
                if (!removed) return NotFound();
                return Ok(new { Message = "Recipe removed from cookbook." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing recipe {RecipeId} from cookbook {CookbookId}", recipeId, id);
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        [HttpGet("{id}/recipes")]
        [ProducesResponseType(typeof(List<RecipeResponseModel>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetCookbookRecipes(long id)
        {
            try
            {
                var result = await _cookbookService.GetCookbookRecipesAsync(id);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting recipes for cookbook {CookbookId}", id);
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }
    }
}
