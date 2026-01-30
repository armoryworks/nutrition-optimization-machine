// File: Nom.Api/Controllers/RecipeController.cs

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;
using System;

namespace Nom.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class RecipeController : BaseApiController
    {
        private readonly IRecipeOrchestrationService _recipeService;

        public RecipeController(IRecipeOrchestrationService recipeService)
        {
            _recipeService = recipeService;
        }

        [HttpGet]
        public async Task<ActionResult<List<RecipeResponseModel>>> GetRecipes()
        {
            try
            {
                var recipes = await _recipeService.GetAllRecipesAsync();
                return Ok(recipes);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to retrieve recipes", error = ex.Message });
            }
        }

        [HttpGet("my")]
        public async Task<ActionResult<List<RecipeResponseModel>>> GetMyRecipes()
        {
            try
            {
                var personId = GetCurrentPersonId();
                if (!personId.HasValue)
                {
                    return Unauthorized("User not authenticated");
                }

                var recipes = await _recipeService.GetMyRecipesAsync(personId.Value);
                return Ok(recipes);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid(ex.Message);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to retrieve your recipes", error = ex.Message });
            }
        }

        [HttpPost]
        public async Task<ActionResult<RecipeCreateResponseModel>> CreateRecipe([FromBody] RecipeCreateModel request)
        {
            try
            {
                var currentPersonId = GetCurrentPersonId();
                if (!currentPersonId.HasValue)
                {
                    return Unauthorized("User not authenticated");
                }

                var response = await _recipeService.CreateRecipeAsync(request, currentPersonId.Value);
                return CreatedAtAction(nameof(GetRecipe), new { id = response.Id }, response);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to create recipe", error = ex.Message });
            }
        }

        /// <summary>
        /// Get a single recipe by ID. Anonymous access allowed for public (Approved) recipes.
        /// Private recipes require authentication and ownership.
        /// </summary>
        [AllowAnonymous]
        [HttpGet("{id}")]
        public async Task<ActionResult<RecipeResponseModel>> GetRecipe(long id)
        {
            try
            {
                var recipe = await _recipeService.GetRecipeAsync(id);
                if (recipe == null)
                {
                    return NotFound(new { message = "Recipe not found" });
                }

                // Check if recipe is public (Approved)
                var isPublic = recipe.CurationStatus == "Approved";

                if (!isPublic)
                {
                    // Recipe is private - require authentication and ownership
                    var currentPersonId = GetCurrentPersonId();
                    if (!currentPersonId.HasValue)
                    {
                        return Unauthorized(new { message = "Authentication required to view this recipe" });
                    }

                    // Check if user is the author
                    if (recipe.AuthorId != currentPersonId.Value)
                    {
                        return Forbid("You do not have permission to view this recipe");
                    }
                }

                return Ok(recipe);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to retrieve recipe", error = ex.Message });
            }
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<RecipeResponseModel>> UpdateRecipe(long id, [FromBody] UpdateRecipeRequest request)
        {
            try
            {
                var response = await _recipeService.UpdateRecipeAsync(id, request);
                if (response == null)
                {
                    return NotFound(new { message = "Recipe not found" });
                }
                return Ok(response);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to update recipe", error = ex.Message });
            }
        }

        [HttpDelete("{id}")]
        public async Task<ActionResult> DeleteRecipe(long id)
        {
            try
            {
                var success = await _recipeService.DeleteRecipeAsync(id);
                if (!success)
                {
                    return NotFound(new { message = "Recipe not found" });
                }
                return NoContent();
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to delete recipe", error = ex.Message });
            }
        }

        // Recipe Comments Endpoints
        [HttpPost("{id}/comments")]
        public async Task<ActionResult<RecipeCommentResponseModel>> AddComment(long id, [FromBody] RecipeCommentCreateModel request)
        {
            try
            {
                request.RecipeId = id;
                var response = await _recipeService.AddCommentAsync(request);
                return Ok(response);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to add comment", error = ex.Message });
            }
        }

        [HttpGet("{id}/comments")]
        public async Task<ActionResult<List<RecipeCommentResponseModel>>> GetComments(long id)
        {
            try
            {
                var comments = await _recipeService.GetCommentsAsync(id);
                return Ok(comments);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to retrieve comments", error = ex.Message });
            }
        }

        [HttpDelete("comments/{commentId}")]
        public async Task<ActionResult> DeleteComment(long commentId)
        {
            try
            {
                var success = await _recipeService.DeleteCommentAsync(commentId);
                if (!success)
                {
                    return NotFound(new { message = "Comment not found" });
                }
                return NoContent();
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to delete comment", error = ex.Message });
            }
        }

        // Recipe Ratings Endpoints
        [HttpPost("{id}/ratings")]
        public async Task<ActionResult<RecipeRatingResponseModel>> AddRating(long id, [FromBody] RecipeRatingCreateModel request)
        {
            try
            {
                request.RecipeId = id;
                var response = await _recipeService.AddRatingAsync(request);
                return Ok(response);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to add rating", error = ex.Message });
            }
        }

        [HttpGet("{id}/ratings")]
        public async Task<ActionResult<List<RecipeRatingResponseModel>>> GetRatings(long id)
        {
            try
            {
                var ratings = await _recipeService.GetRatingsAsync(id);
                return Ok(ratings);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to retrieve ratings", error = ex.Message });
            }
        }

        [HttpPut("ratings/{ratingId}")]
        public async Task<ActionResult<RecipeRatingResponseModel>> UpdateRating(long ratingId, [FromBody] RecipeRatingUpdateModel request)
        {
            try
            {
                var response = await _recipeService.UpdateRatingAsync(ratingId, request);
                if (response == null)
                {
                    return NotFound(new { message = "Rating not found" });
                }
                return Ok(response);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to update rating", error = ex.Message });
            }
        }

        [HttpDelete("ratings/{ratingId}")]
        public async Task<ActionResult> DeleteRating(long ratingId)
        {
            try
            {
                var success = await _recipeService.DeleteRatingAsync(ratingId);
                if (!success)
                {
                    return NotFound(new { message = "Rating not found" });
                }
                return NoContent();
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to delete rating", error = ex.Message });
            }
        }
    }
}