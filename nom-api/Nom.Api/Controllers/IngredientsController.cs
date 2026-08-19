// File: Nom.Api/Controllers/IngredientsController.cs

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;
using System;
using System.Threading.Tasks;
using System.Collections.Generic; // Added for List<object>

namespace Nom.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class IngredientsController : BaseApiController
    {
        private readonly IRecipeOrchestrationService _recipeOrch;

        public IngredientsController(IRecipeOrchestrationService recipeOrch)
        {
            _recipeOrch = recipeOrch;
        }

        [HttpGet("my")]
        public async Task<IActionResult> GetMyIngredients()
        {
            var personId = GetCurrentPersonId();
            if (!personId.HasValue)
            {
                return Unauthorized("User not authenticated");
            }

            var ingredients = await _recipeOrch.GetMyIngredientsAsync(personId.Value);
            return Ok(ingredients);
        }

        [HttpGet("search")]
        public async Task<IActionResult> SearchIngredients([FromQuery] string q)
        {
            if (string.IsNullOrWhiteSpace(q))
            {
                return BadRequest("Search query is required");
            }

            var ingredients = await _recipeOrch.SearchIngredientsAsync(q);
            return Ok(ingredients);
        }

        [HttpGet("{id:long}")]
        public async Task<IActionResult> GetIngredient(long id)
        {
            var ingredient = await _recipeOrch.GetIngredientForEditAsync(id);
            if (ingredient == null)
            {
                return NotFound();
            }
            return Ok(ingredient);
        }

        [HttpPost]
        public async Task<IActionResult> CreateIngredient([FromBody] CreateIngredientRequest request)
        {
            try
            {
                var newIngredient = await _recipeOrch.CreateIngredientAsync(request);
                return CreatedAtAction(nameof(GetIngredient), new { id = newIngredient.Id }, newIngredient);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Only the ingredient's author or a curator may edit it — an FDC-imported catalog
        /// row must not be rewritable by any signed-in user.
        /// </summary>
        [HttpPut("{id:long}")]
        public async Task<IActionResult> UpdateIngredient(long id, [FromBody] UpdateIngredientRequest request)
        {
            if (id != request.Id)
            {
                return BadRequest("ID mismatch between route and request body.");
            }

            var existing = await _recipeOrch.GetIngredientForEditAsync(id);
            if (existing == null) return NotFound();

            var personId = GetCurrentPersonId();
            var isCurator = User.HasClaim("CanManageCuration", "true");
            if (!isCurator && (personId == null || existing.AuthorId != personId.Value))
                return Forbid();

            try
            {
                await _recipeOrch.UpdateIngredientAsync(request);
                return NoContent();
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}