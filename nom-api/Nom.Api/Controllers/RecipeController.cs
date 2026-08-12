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
            var personId = GetCurrentPersonId();
            var recipes = await _recipeService.GetAllRecipesAsync(personId);
            return Ok(recipes);
        }

        [HttpGet("my")]
        public async Task<ActionResult<List<RecipeResponseModel>>> GetMyRecipes()
        {
            var personId = GetCurrentPersonId();
            if (!personId.HasValue)
            {
                return Unauthorized("User not authenticated");
            }

            var recipes = await _recipeService.GetMyRecipesAsync(personId.Value);
            return Ok(recipes);
        }

        [HttpPost]
        public async Task<ActionResult<RecipeCreateResponseModel>> CreateRecipe([FromBody] RecipeCreateModel request)
        {
            var currentPersonId = GetCurrentPersonId();
            if (!currentPersonId.HasValue)
            {
                return Unauthorized("User not authenticated");
            }

            var response = await _recipeService.CreateRecipeAsync(request, currentPersonId.Value);
            return CreatedAtAction(nameof(GetRecipe), new { id = response.Id }, response);
        }

        /// <summary>
        /// Get a single recipe by ID. Anonymous access allowed for public (Approved) recipes.
        /// Private recipes require authentication and ownership.
        /// </summary>
        [AllowAnonymous]
        [HttpGet("{id}")]
        public async Task<ActionResult<RecipeResponseModel>> GetRecipe(long id)
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

        [HttpPut("{id}")]
        public async Task<ActionResult<RecipeResponseModel>> UpdateRecipe(long id, [FromBody] UpdateRecipeRequest request)
        {
            var currentPersonId = GetCurrentPersonId();
            if (!currentPersonId.HasValue)
                return Unauthorized("User not authenticated");

            var existing = await _recipeService.GetRecipeAsync(id);
            if (existing == null)
                return NotFound(new { message = "Recipe not found" });

            if (existing.AuthorId != currentPersonId.Value)
                return Forbid("You can only edit your own recipes");

            var response = await _recipeService.UpdateRecipeAsync(id, request);
            return Ok(response);
        }

        [HttpDelete("{id}")]
        public async Task<ActionResult> DeleteRecipe(long id)
        {
            var currentPersonId = GetCurrentPersonId();
            if (!currentPersonId.HasValue)
                return Unauthorized("User not authenticated");

            var existing = await _recipeService.GetRecipeAsync(id);
            if (existing == null)
                return NotFound(new { message = "Recipe not found" });

            if (existing.AuthorId != currentPersonId.Value)
                return Forbid("You can only delete your own recipes");

            await _recipeService.DeleteRecipeAsync(id);
            return NoContent();
        }

        [HttpGet("dashboard/analytics")]
        [ProducesResponseType(typeof(RecipeDashboardAnalyticsModel), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetDashboardAnalytics()
        {
            var personId = GetCurrentPersonId();
            if (!personId.HasValue)
            {
                return Unauthorized("User not authenticated");
            }
            var analytics = await _recipeService.GetDashboardAnalyticsAsync(personId.Value);
            return Ok(analytics);
        }

        // Recipe Comments Endpoints
        [HttpPost("{id}/comments")]
        public async Task<ActionResult<RecipeCommentResponseModel>> AddComment(long id, [FromBody] RecipeCommentCreateModel request)
        {
            request.RecipeId = id;
            var response = await _recipeService.AddCommentAsync(request);
            return CreatedAtAction(nameof(GetComments), new { id }, response);
        }

        [HttpGet("{id}/comments")]
        public async Task<ActionResult<List<RecipeCommentResponseModel>>> GetComments(long id)
        {
            var comments = await _recipeService.GetCommentsAsync(id);
            return Ok(comments);
        }

        [HttpDelete("comments/{commentId}")]
        public async Task<ActionResult> DeleteComment(long commentId)
        {
            var success = await _recipeService.DeleteCommentAsync(commentId);
            if (!success)
            {
                return NotFound(new { message = "Comment not found" });
            }
            return NoContent();
        }

        // Recipe Ratings Endpoints
        [HttpPost("{id}/ratings")]
        public async Task<ActionResult<RecipeRatingResponseModel>> AddRating(long id, [FromBody] RecipeRatingCreateModel request)
        {
            request.RecipeId = id;
            var response = await _recipeService.AddRatingAsync(request);
            return CreatedAtAction(nameof(GetRatings), new { id }, response);
        }

        [HttpGet("{id}/ratings")]
        public async Task<ActionResult<List<RecipeRatingResponseModel>>> GetRatings(long id)
        {
            var ratings = await _recipeService.GetRatingsAsync(id);
            return Ok(ratings);
        }

        [HttpPut("ratings/{ratingId}")]
        public async Task<ActionResult<RecipeRatingResponseModel>> UpdateRating(long ratingId, [FromBody] RecipeRatingUpdateModel request)
        {
            var response = await _recipeService.UpdateRatingAsync(ratingId, request);
            if (response == null)
            {
                return NotFound(new { message = "Rating not found" });
            }
            return Ok(response);
        }

        [HttpDelete("ratings/{ratingId}")]
        public async Task<ActionResult> DeleteRating(long ratingId)
        {
            var success = await _recipeService.DeleteRatingAsync(ratingId);
            if (!success)
            {
                return NotFound(new { message = "Rating not found" });
            }
            return NoContent();
        }

        // Recipe Image/Asset Endpoints

        [HttpPost("{id}/image")]
        [RequestSizeLimit(10_485_760)] // 10MB
        public async Task<ActionResult<RecipeAssetResponseModel>> UploadImage(long id, IFormFile file)
        {
            var currentPersonId = GetCurrentPersonId();
            if (!currentPersonId.HasValue)
                return Unauthorized("User not authenticated");

            if (file == null || file.Length == 0)
                return BadRequest(new { message = "No file provided" });

            var allowedTypes = new[] { "image/jpeg", "image/png", "image/gif", "image/webp" };
            if (!allowedTypes.Contains(file.ContentType.ToLower()))
                return BadRequest(new { message = "Only JPEG, PNG, GIF, and WebP images are allowed" });

            using var ms = new System.IO.MemoryStream();
            await file.CopyToAsync(ms);
            var fileData = ms.ToArray();

            var result = await _recipeService.UploadImageAsync(id, currentPersonId.Value, file.FileName, file.ContentType, fileData);
            return CreatedAtAction(nameof(GetAssets), new { id }, result);
        }

        [AllowAnonymous]
        [HttpGet("{id}/image")]
        public async Task<IActionResult> GetImage(long id)
        {
            var result = await _recipeService.GetImageAsync(id, GetCurrentPersonId());
            if (result == null)
                return NotFound(new { message = "No image found for this recipe" });

            var (fileData, contentType) = result.Value;
            return File(fileData, contentType);
        }

        [HttpDelete("{id}/image/{assetId}")]
        public async Task<ActionResult> DeleteImage(long id, long assetId)
        {
            var currentPersonId = GetCurrentPersonId();
            if (!currentPersonId.HasValue)
                return Unauthorized("User not authenticated");

            var success = await _recipeService.DeleteImageAsync(id, assetId, currentPersonId.Value);
            if (!success)
                return NotFound(new { message = "Image not found" });

            return NoContent();
        }

        [HttpGet("{id}/assets")]
        public async Task<ActionResult<List<RecipeAssetResponseModel>>> GetAssets(long id)
        {
            var assets = await _recipeService.GetAssetsAsync(id);
            return Ok(assets);
        }
    }
}