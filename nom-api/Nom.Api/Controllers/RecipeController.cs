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
        private readonly IRecipeEnhancementService _enhancementService;

        public RecipeController(
            IRecipeOrchestrationService recipeService,
            IRecipeEnhancementService enhancementService)
        {
            _recipeService = recipeService;
            _enhancementService = enhancementService;
        }

        private bool CanManageCuration() => User.HasClaim("CanManageCuration", "true");

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
            var recipe = await _recipeService.GetRecipeAsync(id, GetCurrentPersonId());
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

        // Variation + diet endpoints

        /// <summary>Save the caller's default variation (ingredient swaps) for this recipe.</summary>
        [HttpPut("{id}/variation")]
        public async Task<ActionResult<List<RecipeVariationItemModel>>> SaveVariation(long id, [FromBody] List<SaveVariationItemRequest> items)
        {
            var personId = GetCurrentPersonId();
            if (!personId.HasValue)
                return Unauthorized("User not authenticated");
            if (items == null || items.Count == 0)
                return BadRequest(new { message = "At least one substitution is required (DELETE to clear)" });

            var saved = await _recipeService.SaveVariationAsync(id, personId.Value, items);
            if (saved == null)
                return BadRequest(new { message = "Substitution not available for this recipe" });
            return Ok(saved);
        }

        /// <summary>Clear the caller's default variation for this recipe.</summary>
        [HttpDelete("{id}/variation")]
        public async Task<ActionResult> DeleteVariation(long id)
        {
            var personId = GetCurrentPersonId();
            if (!personId.HasValue)
                return Unauthorized("User not authenticated");
            var removed = await _recipeService.DeleteVariationAsync(id, personId.Value);
            return removed ? NoContent() : NotFound(new { message = "No variation saved" });
        }

        /// <summary>The caller's dietary restrictions that this recipe's ingredients trip.</summary>
        [HttpGet("{id}/diet")]
        public async Task<ActionResult<List<RecipeDietMatchModel>>> GetDietMatches(long id)
        {
            var personId = GetCurrentPersonId();
            if (!personId.HasValue)
                return Unauthorized("User not authenticated");
            return Ok(await _recipeService.GetDietMatchesAsync(id, personId.Value));
        }

        // Recipe substitutions (with step effects) and augmentations.
        // Users see curated entries; curators also see machine-proposed ones.

        [HttpGet("{id}/substitutions")]
        public async Task<ActionResult<List<RecipeSubstitutionModel>>> GetSubstitutions(long id)
        {
            return Ok(await _enhancementService.GetSubstitutionsAsync(id, includeUncurated: CanManageCuration()));
        }

        [HttpGet("{id}/augmentations")]
        public async Task<ActionResult<List<RecipeAugmentationModel>>> GetAugmentations(long id)
        {
            return Ok(await _enhancementService.GetAugmentationsAsync(id, includeUncurated: CanManageCuration()));
        }

        [HttpPost("{id}/substitutions")]
        [Authorize(Policy = "CanManageCuration")]
        public async Task<ActionResult<RecipeSubstitutionModel>> CreateSubstitution(long id, [FromBody] RecipeSubstitutionUpsertModel model)
        {
            var personId = GetCurrentPersonId();
            if (!personId.HasValue)
                return Unauthorized("User not authenticated");
            var result = await _enhancementService.UpsertSubstitutionAsync(id, null, model, personId.Value);
            return Ok(result);
        }

        [HttpPut("{id}/substitutions/{substitutionId:long}")]
        [Authorize(Policy = "CanManageCuration")]
        public async Task<ActionResult<RecipeSubstitutionModel>> UpdateSubstitution(long id, long substitutionId, [FromBody] RecipeSubstitutionUpsertModel model)
        {
            var personId = GetCurrentPersonId();
            if (!personId.HasValue)
                return Unauthorized("User not authenticated");
            var result = await _enhancementService.UpsertSubstitutionAsync(id, substitutionId, model, personId.Value);
            return Ok(result);
        }

        [HttpPost("{id}/substitutions/{substitutionId:long}/curate")]
        [Authorize(Policy = "CanManageCuration")]
        public async Task<IActionResult> CurateSubstitution(long id, long substitutionId)
        {
            var personId = GetCurrentPersonId();
            if (!personId.HasValue)
                return Unauthorized("User not authenticated");
            return await _enhancementService.CurateSubstitutionAsync(id, substitutionId, personId.Value)
                ? NoContent() : NotFound();
        }

        [HttpDelete("{id}/substitutions/{substitutionId:long}")]
        [Authorize(Policy = "CanManageCuration")]
        public async Task<IActionResult> DeleteSubstitution(long id, long substitutionId)
        {
            var personId = GetCurrentPersonId();
            if (!personId.HasValue)
                return Unauthorized("User not authenticated");
            return await _enhancementService.DeleteSubstitutionAsync(id, substitutionId, personId.Value)
                ? NoContent() : NotFound();
        }

        [HttpPost("{id}/augmentations")]
        [Authorize(Policy = "CanManageCuration")]
        public async Task<ActionResult<RecipeAugmentationModel>> CreateAugmentation(long id, [FromBody] RecipeAugmentationUpsertModel model)
        {
            var personId = GetCurrentPersonId();
            if (!personId.HasValue)
                return Unauthorized("User not authenticated");
            var result = await _enhancementService.UpsertAugmentationAsync(id, null, model, personId.Value);
            return Ok(result);
        }

        [HttpPut("{id}/augmentations/{augmentationId:long}")]
        [Authorize(Policy = "CanManageCuration")]
        public async Task<ActionResult<RecipeAugmentationModel>> UpdateAugmentation(long id, long augmentationId, [FromBody] RecipeAugmentationUpsertModel model)
        {
            var personId = GetCurrentPersonId();
            if (!personId.HasValue)
                return Unauthorized("User not authenticated");
            var result = await _enhancementService.UpsertAugmentationAsync(id, augmentationId, model, personId.Value);
            return Ok(result);
        }

        [HttpPost("{id}/augmentations/{augmentationId:long}/curate")]
        [Authorize(Policy = "CanManageCuration")]
        public async Task<IActionResult> CurateAugmentation(long id, long augmentationId)
        {
            var personId = GetCurrentPersonId();
            if (!personId.HasValue)
                return Unauthorized("User not authenticated");
            return await _enhancementService.CurateAugmentationAsync(id, augmentationId, personId.Value)
                ? NoContent() : NotFound();
        }

        [HttpDelete("{id}/augmentations/{augmentationId:long}")]
        [Authorize(Policy = "CanManageCuration")]
        public async Task<IActionResult> DeleteAugmentation(long id, long augmentationId)
        {
            var personId = GetCurrentPersonId();
            if (!personId.HasValue)
                return Unauthorized("User not authenticated");
            return await _enhancementService.DeleteAugmentationAsync(id, augmentationId, personId.Value)
                ? NoContent() : NotFound();
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