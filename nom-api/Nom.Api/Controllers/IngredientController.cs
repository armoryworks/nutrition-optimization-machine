// File: Nom.Api/Controllers/IngredientsController.cs

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;
using System;
using System.Threading.Tasks;

namespace Nom.Api.Controllers
{
    [Authorize]
    public class IngredientsController : BaseApiController
    {
        private readonly ILogger<IngredientsController> _logger;
        private readonly IRecipeOrchestrationService _recipeOrch;

        public IngredientsController(ILogger<IngredientsController> logger, IRecipeOrchestrationService recipeOrch)
        {
            _logger = logger;
            _recipeOrch = recipeOrch;
        }

        [HttpGet("{id:long}")]
        public async Task<IActionResult> GetIngredient(long id)
        {
            try
            {
                var ingredient = await _recipeOrch.GetIngredientForEditAsync(id);
                if (ingredient == null)
                {
                    return NotFound();
                }
                return Ok(ingredient);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving ingredient {IngredientId}", id);
                return StatusCode(500, "An unexpected error occurred while retrieving the ingredient.");
            }
        }

        [HttpPost]
        public async Task<IActionResult> CreateIngredient([FromBody] CreateIngredientRequest request)
        {
            try
            {
                var newIngredient = await _recipeOrch.CreateIngredientAsync(request);
                return CreatedAtAction(nameof(GetIngredient), new { id = newIngredient.Id }, newIngredient);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating new ingredient for user {PersonId}", GetCurrentPersonId());
                return StatusCode(500, "An unexpected error occurred while creating the ingredient.");
            }
        }

        [HttpPut("{id:long}")]
        public async Task<IActionResult> UpdateIngredient(long id, [FromBody] UpdateIngredientRequest request)
        {
            try
            {
                if (id != request.Id)
                {
                    return BadRequest("ID mismatch between route and request body.");
                }
                await _recipeOrch.UpdateIngredientAsync(request);
                return NoContent(); // Success
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating ingredient {IngredientId}", id);
                return StatusCode(500, "An unexpected error occurred while updating the ingredient.");
            }
        }
    }
}