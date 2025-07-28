// File: Nom.Api/Controllers/RecipesController.cs

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;
using System;
using System.Threading.Tasks;

namespace Nom.Api.Controllers
{
    [Authorize]
    public class RecipeController : BaseApiController
    {
        private readonly IRecipeOrchestrationService _recipeOrchestrationService;
        private readonly ILogger<RecipeController> _logger;

        public RecipeController(IRecipeOrchestrationService recipeOrchestrationService, ILogger<RecipeController> logger)
        {
            _recipeOrchestrationService = recipeOrchestrationService;
            _logger = logger;
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
        [HttpGet("ingredients/{id:long}")]
        public async Task<IActionResult> GetIngredientDetails(long id)
        {
            var result = await _recipeOrchestrationService.GetIngredientDetailsAsync(id);
            if (result == null)
            {
                return NotFound();
            }
            return Ok(result);
        }

        /// <summary>
        /// Creates a new recipe.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CreateRecipe([FromBody] CreateRecipeRequest request)
        {
            try
            {
                var authorPersonId = GetCurrentPersonId();
                var newRecipeId = await _recipeOrchestrationService.CreateRecipeAsync(request, authorPersonId);
                return CreatedAtAction(nameof(GetRecipe), new { id = newRecipeId }, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while creating a recipe for user {PersonId}", GetCurrentPersonId());
                return StatusCode(500, "An unexpected error occurred while creating the recipe.");
            }
        }

        [HttpPut("{id:long}")]
        public async Task<IActionResult> UpdateRecipe(long id, [FromBody] UpdateRecipeRequest request)
        {
            if (id != request.Id)
            {
                return BadRequest("ID mismatch between route and request body.");
            }

            try
            {
                var authorPersonId = GetCurrentPersonId();
                await _recipeOrchestrationService.UpdateRecipeAsync(request, authorPersonId);
                return NoContent(); // Success
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid(ex.Message);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while updating recipe {RecipeId}", id);
                return StatusCode(500, "An unexpected error occurred while updating the recipe.");
            }
        }

        /// <summary>
        /// Creates a new version of an existing curated recipe.
        /// </summary>
        /// <param name="id">The ID of the parent recipe.</param>
        [HttpPost("{id:long}/version")]
        public async Task<IActionResult> CreateNewVersion(long id)
        {
            try
            {
                var authorPersonId = GetCurrentPersonId();
                var newVersionId = await _recipeOrchestrationService.CreateNewRecipeVersionAsync(id, authorPersonId);
                return CreatedAtAction(nameof(GetRecipe), new { id = newVersionId }, null);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Invalid operation while creating a new version for recipe {RecipeId}", id);
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while creating a new version for recipe {RecipeId}", id);
                return StatusCode(500, "An unexpected error occurred while creating the new version.");
            }
        }

        [HttpGet("my-recipes")]
        public async Task<IActionResult> GetMyRecipes()
        {
            try
            {
                var authorPersonId = GetCurrentPersonId();
                var recipes = await _recipeOrchestrationService.GetAuthorRecipesAsync(authorPersonId);
                return Ok(recipes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while fetching recipes for user {PersonId}", GetCurrentPersonId());
                return StatusCode(500, "An unexpected error occurred while fetching recipes.");
            }
        }

        [HttpGet("my-ingredients")]
        public async Task<IActionResult> GetMyIngredients()
        {
            try
            {
                var authorPersonId = GetCurrentPersonId();
                var ingredients = await _recipeOrchestrationService.GetAuthorIngredientsAsync(authorPersonId);
                return Ok(ingredients);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while fetching ingredients for user {PersonId}", GetCurrentPersonId());
                return StatusCode(500, "An unexpected error occurred while fetching ingredients.");
            }
        }

        // Placeholder for a GetRecipe endpoint that would be referenced by CreatedAtAction
        [HttpGet("{id:long}")]
        public async Task<IActionResult> GetRecipe(long id)
        {
            await Task.CompletedTask;
            return Ok(new { Id = id, Message = "Endpoint not fully implemented." });
        }
    }
}