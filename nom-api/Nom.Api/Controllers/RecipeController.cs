// File: nom-api/Nom.Api/Controllers/RecipeController.cs

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nom.Orch.Interfaces;
using System.Threading.Tasks;

namespace Nom.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class RecipeController : ControllerBase
    {
        private readonly IRecipeOrchestrationService _recipeOrchestrationService;

        public RecipeController(IRecipeOrchestrationService recipeOrchestrationService)
        {
            _recipeOrchestrationService = recipeOrchestrationService;
        }

        /// <summary>
        /// Searches for ingredients by a given search term.
        /// </summary>
        /// <param name="q">The query string to search for.</param>
        /// <returns>A list of matching ingredients.</returns>
        [HttpGet("ingredients/search")]
        public async Task<IActionResult> SearchIngredients([FromQuery] string q)
        {
            var result = await _recipeOrchestrationService.SearchIngredientsAsync(q);
            return Ok(result);
        }

        /// <summary>
        /// Gets the detailed nutritional information for a specific ingredient.
        /// </summary>
        /// <param name="id">The ID of the ingredient.</param>
        /// <returns>The ingredient details with its nutrient profile.</returns>
        [HttpGet("ingredients/{id}")]
        public async Task<IActionResult> GetIngredientDetails(long id)
        {
            var result = await _recipeOrchestrationService.GetIngredientDetailsAsync(id);
            if (result == null)
            {
                return NotFound();
            }
            return Ok(result);
        }
    }
}
