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

        public CookbookController(
            ICookbookOrchestrationService cookbookService)
        {
            _cookbookService = cookbookService;
        }

        [HttpGet]
        [ProducesResponseType(typeof(List<CookbookResponseModel>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetCookbooks([FromQuery, Required] long householdId)
        {
            if (!IsHouseholdMember(householdId))
                return Forbid();

            var result = await _cookbookService.GetCookbooksAsync(householdId);
            return Ok(result);
        }

        /// <summary>The household's cookbooks that contain the given recipe.</summary>
        [HttpGet("for-recipe/{recipeId:long}")]
        [ProducesResponseType(typeof(List<CookbookResponseModel>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetCookbooksForRecipe(long recipeId, [FromQuery, Required] long householdId)
        {
            if (!IsHouseholdMember(householdId))
                return Forbid();

            return Ok(await _cookbookService.GetCookbooksForRecipeAsync(householdId, recipeId));
        }

        [HttpGet("{id}")]
        [ProducesResponseType(typeof(CookbookResponseModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetCookbook(long id)
        {
            var result = await _cookbookService.GetCookbookAsync(id);
            if (result == null) return NotFound();

            if (!IsHouseholdMember(result.HouseholdId))
                return Forbid();

            return Ok(result);
        }

        [HttpPost]
        [ProducesResponseType(typeof(long), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> CreateCookbook([FromBody] CookbookCreateModel model)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            if (!IsHouseholdMember(model.HouseholdId))
                return Forbid();

            var id = await _cookbookService.CreateCookbookAsync(model);
            return CreatedAtAction(nameof(GetCookbook), new { id }, id);
        }

        [HttpPut("{id}")]
        [ProducesResponseType(typeof(CookbookResponseModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> UpdateCookbook(long id, [FromBody] CookbookUpdateModel model)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var existing = await _cookbookService.GetCookbookAsync(id);
            if (existing == null) return NotFound();

            if (!IsHouseholdMember(existing.HouseholdId))
                return Forbid();

            var result = await _cookbookService.UpdateCookbookAsync(id, model);
            return Ok(result);
        }

        [HttpDelete("{id}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteCookbook(long id)
        {
            var existing = await _cookbookService.GetCookbookAsync(id);
            if (existing == null) return NotFound();

            if (!IsHouseholdMember(existing.HouseholdId))
                return Forbid();

            await _cookbookService.DeleteCookbookAsync(id);
            return Ok(new { Message = "Cookbook deleted successfully." });
        }

        [HttpPost("{id}/recipe/{recipeId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> AddRecipe(long id, long recipeId)
        {
            var existing = await _cookbookService.GetCookbookAsync(id);
            if (existing == null) return NotFound();

            if (!IsHouseholdMember(existing.HouseholdId))
                return Forbid();

            var added = await _cookbookService.AddRecipeToCookbookAsync(id, recipeId);
            if (!added) return Conflict(new { Message = "Recipe already in cookbook." });
            return Ok(new { Message = "Recipe added to cookbook." });
        }

        [HttpDelete("{id}/recipe/{recipeId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> RemoveRecipe(long id, long recipeId)
        {
            var existing = await _cookbookService.GetCookbookAsync(id);
            if (existing == null) return NotFound();

            if (!IsHouseholdMember(existing.HouseholdId))
                return Forbid();

            var removed = await _cookbookService.RemoveRecipeFromCookbookAsync(id, recipeId);
            if (!removed) return NotFound();
            return Ok(new { Message = "Recipe removed from cookbook." });
        }

        [HttpGet("{id}/recipes")]
        [ProducesResponseType(typeof(List<RecipeResponseModel>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetCookbookRecipes(long id)
        {
            var existing = await _cookbookService.GetCookbookAsync(id);
            if (existing == null) return NotFound();

            if (!IsHouseholdMember(existing.HouseholdId))
                return Forbid();

            var result = await _cookbookService.GetCookbookRecipesAsync(id);
            return Ok(result);
        }
    }
}
