using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nom.Api.Filters;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nom.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    // Quarantined (audit): no UI, doc or E2E consumer; enable per tenant with Features:RecipeAdvanced=true.
    [FeatureGate("RecipeAdvanced")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public class RecipeAdvancedController : BaseApiController
    {
        private readonly IRecipeAdvancedOrchestrationService _recipeAdvancedOrchestrationService;

        public RecipeAdvancedController(
            IRecipeAdvancedOrchestrationService recipeAdvancedOrchestrationService)
        {
            _recipeAdvancedOrchestrationService = recipeAdvancedOrchestrationService;
        }

        // Comments
        [HttpPost("comments")]
        [ProducesResponseType(typeof(RecipeCommentResponseModel), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> CreateComment([FromBody] RecipeCommentCreateModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var response = await _recipeAdvancedOrchestrationService.CreateCommentAsync(model);
            return CreatedAtAction(nameof(GetRecipeComments), new { recipeId = model.RecipeId }, response);
        }

        [HttpGet("recipes/{recipeId}/comments")]
        [ProducesResponseType(typeof(List<RecipeCommentResponseModel>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetRecipeComments(long recipeId)
        {
            var comments = await _recipeAdvancedOrchestrationService.GetRecipeCommentsAsync(recipeId);
            return Ok(comments);
        }

        [HttpDelete("comments/{commentId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteComment(long commentId)
        {
            var success = await _recipeAdvancedOrchestrationService.DeleteCommentAsync(commentId);
            if (!success)
            {
                return NotFound(new { Message = "Comment not found or not authorized to delete." });
            }
            return Ok(new { Message = "Comment deleted successfully." });
        }

        // Ratings
        [HttpPost("ratings")]
        [ProducesResponseType(typeof(RecipeRatingResponseModel), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> CreateRating([FromBody] RecipeRatingCreateModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var response = await _recipeAdvancedOrchestrationService.CreateRatingAsync(model);
            return CreatedAtAction(nameof(GetUserRating), new { recipeId = model.RecipeId }, response);
        }

        [HttpGet("recipes/{recipeId}/ratings/user")]
        [ProducesResponseType(typeof(RecipeRatingResponseModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetUserRating(long recipeId)
        {
            var rating = await _recipeAdvancedOrchestrationService.GetUserRatingAsync(recipeId);
            if (rating == null)
            {
                return NotFound(new { Message = "User has not rated this recipe." });
            }
            return Ok(rating);
        }

        [HttpGet("recipes/{recipeId}/ratings/average")]
        [ProducesResponseType(typeof(decimal), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetRecipeAverageRating(long recipeId)
        {
            var averageRating = await _recipeAdvancedOrchestrationService.GetRecipeAverageRatingAsync(recipeId);
            return Ok(new { AverageRating = averageRating });
        }

        [HttpPut("ratings/{ratingId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> UpdateRating(long ratingId, [FromBody] RecipeRatingCreateModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var success = await _recipeAdvancedOrchestrationService.UpdateRatingAsync(ratingId, model);
            if (!success)
            {
                return NotFound(new { Message = "Rating not found or not authorized to update." });
            }
            return Ok(new { Message = "Rating updated successfully." });
        }

        [HttpDelete("ratings/{ratingId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteRating(long ratingId)
        {
            var success = await _recipeAdvancedOrchestrationService.DeleteRatingAsync(ratingId);
            if (!success)
            {
                return NotFound(new { Message = "Rating not found or not authorized to delete." });
            }
            return Ok(new { Message = "Rating deleted successfully." });
        }

        // Share Tokens
        [HttpPost("share-tokens")]
        [ProducesResponseType(typeof(RecipeShareTokenResponseModel), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> CreateShareToken([FromBody] RecipeShareTokenCreateModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var response = await _recipeAdvancedOrchestrationService.CreateShareTokenAsync(model);
            return CreatedAtAction(nameof(GetRecipeShareTokens), new { recipeId = model.RecipeId }, response);
        }

        [HttpGet("recipes/{recipeId}/share-tokens")]
        [ProducesResponseType(typeof(List<RecipeShareTokenResponseModel>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetRecipeShareTokens(long recipeId)
        {
            var shareTokens = await _recipeAdvancedOrchestrationService.GetRecipeShareTokensAsync(recipeId);
            return Ok(shareTokens);
        }

        [HttpDelete("share-tokens/{shareTokenId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteShareToken(long shareTokenId)
        {
            var success = await _recipeAdvancedOrchestrationService.DeleteShareTokenAsync(shareTokenId);
            if (!success)
            {
                return NotFound(new { Message = "Share token not found or not authorized to delete." });
            }
            return Ok(new { Message = "Share token deleted successfully." });
        }

        [HttpGet("share-tokens/{shareToken}/recipe")]
        [ProducesResponseType(typeof(RecipeShareTokenResponseModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [AllowAnonymous]
        public async Task<IActionResult> GetRecipeByShareToken(string shareToken)
        {
            var response = await _recipeAdvancedOrchestrationService.GetRecipeByShareTokenAsync(shareToken);
            if (response == null)
            {
                return NotFound(new { Message = "Recipe not found or share token is not public." });
            }
            return Ok(response);
        }

        // Timeline Events
        [HttpPost("timeline-events")]
        [ProducesResponseType(typeof(RecipeTimelineEventResponseModel), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> CreateTimelineEvent([FromBody] RecipeTimelineEventCreateModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var response = await _recipeAdvancedOrchestrationService.CreateTimelineEventAsync(model);
            return CreatedAtAction(nameof(GetRecipeTimelineEvents), new { recipeId = model.RecipeId }, response);
        }

        [HttpGet("recipes/{recipeId}/timeline-events")]
        [ProducesResponseType(typeof(List<RecipeTimelineEventResponseModel>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetRecipeTimelineEvents(long recipeId)
        {
            var events = await _recipeAdvancedOrchestrationService.GetRecipeTimelineEventsAsync(recipeId);
            return Ok(events);
        }

        [HttpDelete("timeline-events/{eventId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteTimelineEvent(long eventId)
        {
            var success = await _recipeAdvancedOrchestrationService.DeleteTimelineEventAsync(eventId);
            if (!success)
            {
                return NotFound(new { Message = "Timeline event not found or not authorized to delete." });
            }
            return Ok(new { Message = "Timeline event deleted successfully." });
        }

        // Notes
        [HttpPost("notes")]
        [ProducesResponseType(typeof(RecipeNoteResponseModel), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> CreateNote([FromBody] RecipeNoteCreateModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var response = await _recipeAdvancedOrchestrationService.CreateNoteAsync(model);
            return CreatedAtAction(nameof(GetRecipeNotes), new { recipeId = model.RecipeId }, response);
        }

        [HttpGet("recipes/{recipeId}/notes")]
        [ProducesResponseType(typeof(List<RecipeNoteResponseModel>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetRecipeNotes(long recipeId)
        {
            var notes = await _recipeAdvancedOrchestrationService.GetRecipeNotesAsync(recipeId);
            return Ok(notes);
        }

        [HttpPut("notes/{noteId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> UpdateNote(long noteId, [FromBody] RecipeNoteCreateModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var success = await _recipeAdvancedOrchestrationService.UpdateNoteAsync(noteId, model);
            if (!success)
            {
                return NotFound(new { Message = "Note not found or not authorized to update." });
            }
            return Ok(new { Message = "Note updated successfully." });
        }

        [HttpDelete("notes/{noteId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteNote(long noteId)
        {
            var success = await _recipeAdvancedOrchestrationService.DeleteNoteAsync(noteId);
            if (!success)
            {
                return NotFound(new { Message = "Note not found or not authorized to delete." });
            }
            return Ok(new { Message = "Note deleted successfully." });
        }

        // Recipe Actions
        [HttpPost("recipes/{recipeId}/mark-as-made")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> MarkRecipeAsMade(long recipeId)
        {
            var success = await _recipeAdvancedOrchestrationService.MarkRecipeAsMadeAsync(recipeId);
            if (!success)
            {
                return NotFound(new { Message = "Recipe not found." });
            }
            return Ok(new { Message = "Recipe marked as made successfully." });
        }

        [HttpGet("recipes/{recipeId}/last-made")]
        [ProducesResponseType(typeof(DateTime?), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetRecipeLastMade(long recipeId)
        {
            var lastMade = await _recipeAdvancedOrchestrationService.GetRecipeLastMadeAsync(recipeId);
            return Ok(new { LastMade = lastMade });
        }
    }
}