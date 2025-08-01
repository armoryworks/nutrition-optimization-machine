using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
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
    public class RecipeAdvancedController : BaseApiController
    {
        private readonly IRecipeAdvancedOrchestrationService _recipeAdvancedOrchestrationService;
        private readonly ILogger<RecipeAdvancedController> _logger;

        public RecipeAdvancedController(
            IRecipeAdvancedOrchestrationService recipeAdvancedOrchestrationService,
            ILogger<RecipeAdvancedController> logger)
        {
            _recipeAdvancedOrchestrationService = recipeAdvancedOrchestrationService;
            _logger = logger;
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

            try
            {
                var response = await _recipeAdvancedOrchestrationService.CreateCommentAsync(model);
                return CreatedAtAction(nameof(GetRecipeComments), new { recipeId = model.RecipeId }, response);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Recipe not found for comment creation");
                return NotFound(new { Message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in CreateComment");
                return StatusCode(StatusCodes.Status500InternalServerError, new { Message = "An internal error occurred." });
            }
        }

        [HttpGet("recipes/{recipeId}/comments")]
        [ProducesResponseType(typeof(List<RecipeCommentResponseModel>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetRecipeComments(long recipeId)
        {
            try
            {
                var comments = await _recipeAdvancedOrchestrationService.GetRecipeCommentsAsync(recipeId);
                return Ok(comments);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in GetRecipeComments");
                return StatusCode(StatusCodes.Status500InternalServerError, new { Message = "An internal error occurred." });
            }
        }

        [HttpDelete("comments/{commentId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteComment(long commentId)
        {
            try
            {
                var success = await _recipeAdvancedOrchestrationService.DeleteCommentAsync(commentId);
                if (!success)
                {
                    return NotFound(new { Message = "Comment not found or not authorized to delete." });
                }
                return Ok(new { Message = "Comment deleted successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in DeleteComment");
                return StatusCode(StatusCodes.Status500InternalServerError, new { Message = "An internal error occurred." });
            }
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

            try
            {
                var response = await _recipeAdvancedOrchestrationService.CreateRatingAsync(model);
                return CreatedAtAction(nameof(GetUserRating), new { recipeId = model.RecipeId }, response);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Recipe not found for rating creation");
                return NotFound(new { Message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "User already rated this recipe");
                return Conflict(new { Message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in CreateRating");
                return StatusCode(StatusCodes.Status500InternalServerError, new { Message = "An internal error occurred." });
            }
        }

        [HttpGet("recipes/{recipeId}/ratings/user")]
        [ProducesResponseType(typeof(RecipeRatingResponseModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetUserRating(long recipeId)
        {
            try
            {
                var rating = await _recipeAdvancedOrchestrationService.GetUserRatingAsync(recipeId);
                if (rating == null)
                {
                    return NotFound(new { Message = "User has not rated this recipe." });
                }
                return Ok(rating);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in GetUserRating");
                return StatusCode(StatusCodes.Status500InternalServerError, new { Message = "An internal error occurred." });
            }
        }

        [HttpGet("recipes/{recipeId}/ratings/average")]
        [ProducesResponseType(typeof(decimal), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetRecipeAverageRating(long recipeId)
        {
            try
            {
                var averageRating = await _recipeAdvancedOrchestrationService.GetRecipeAverageRatingAsync(recipeId);
                return Ok(new { AverageRating = averageRating });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in GetRecipeAverageRating");
                return StatusCode(StatusCodes.Status500InternalServerError, new { Message = "An internal error occurred." });
            }
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

            try
            {
                var success = await _recipeAdvancedOrchestrationService.UpdateRatingAsync(ratingId, model);
                if (!success)
                {
                    return NotFound(new { Message = "Rating not found or not authorized to update." });
                }
                return Ok(new { Message = "Rating updated successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in UpdateRating");
                return StatusCode(StatusCodes.Status500InternalServerError, new { Message = "An internal error occurred." });
            }
        }

        [HttpDelete("ratings/{ratingId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteRating(long ratingId)
        {
            try
            {
                var success = await _recipeAdvancedOrchestrationService.DeleteRatingAsync(ratingId);
                if (!success)
                {
                    return NotFound(new { Message = "Rating not found or not authorized to delete." });
                }
                return Ok(new { Message = "Rating deleted successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in DeleteRating");
                return StatusCode(StatusCodes.Status500InternalServerError, new { Message = "An internal error occurred." });
            }
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

            try
            {
                var response = await _recipeAdvancedOrchestrationService.CreateShareTokenAsync(model);
                return CreatedAtAction(nameof(GetRecipeShareTokens), new { recipeId = model.RecipeId }, response);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Recipe not found for share token creation");
                return NotFound(new { Message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in CreateShareToken");
                return StatusCode(StatusCodes.Status500InternalServerError, new { Message = "An internal error occurred." });
            }
        }

        [HttpGet("recipes/{recipeId}/share-tokens")]
        [ProducesResponseType(typeof(List<RecipeShareTokenResponseModel>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetRecipeShareTokens(long recipeId)
        {
            try
            {
                var shareTokens = await _recipeAdvancedOrchestrationService.GetRecipeShareTokensAsync(recipeId);
                return Ok(shareTokens);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in GetRecipeShareTokens");
                return StatusCode(StatusCodes.Status500InternalServerError, new { Message = "An internal error occurred." });
            }
        }

        [HttpDelete("share-tokens/{shareTokenId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteShareToken(long shareTokenId)
        {
            try
            {
                var success = await _recipeAdvancedOrchestrationService.DeleteShareTokenAsync(shareTokenId);
                if (!success)
                {
                    return NotFound(new { Message = "Share token not found or not authorized to delete." });
                }
                return Ok(new { Message = "Share token deleted successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in DeleteShareToken");
                return StatusCode(StatusCodes.Status500InternalServerError, new { Message = "An internal error occurred." });
            }
        }

        [HttpGet("share-tokens/{shareToken}/recipe")]
        [ProducesResponseType(typeof(RecipeShareTokenResponseModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [AllowAnonymous]
        public async Task<IActionResult> GetRecipeByShareToken(string shareToken)
        {
            try
            {
                var response = await _recipeAdvancedOrchestrationService.GetRecipeByShareTokenAsync(shareToken);
                if (response == null)
                {
                    return NotFound(new { Message = "Recipe not found or share token is not public." });
                }
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in GetRecipeByShareToken");
                return StatusCode(StatusCodes.Status500InternalServerError, new { Message = "An internal error occurred." });
            }
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

            try
            {
                var response = await _recipeAdvancedOrchestrationService.CreateTimelineEventAsync(model);
                return CreatedAtAction(nameof(GetRecipeTimelineEvents), new { recipeId = model.RecipeId }, response);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Recipe not found for timeline event creation");
                return NotFound(new { Message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in CreateTimelineEvent");
                return StatusCode(StatusCodes.Status500InternalServerError, new { Message = "An internal error occurred." });
            }
        }

        [HttpGet("recipes/{recipeId}/timeline-events")]
        [ProducesResponseType(typeof(List<RecipeTimelineEventResponseModel>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetRecipeTimelineEvents(long recipeId)
        {
            try
            {
                var events = await _recipeAdvancedOrchestrationService.GetRecipeTimelineEventsAsync(recipeId);
                return Ok(events);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in GetRecipeTimelineEvents");
                return StatusCode(StatusCodes.Status500InternalServerError, new { Message = "An internal error occurred." });
            }
        }

        [HttpDelete("timeline-events/{eventId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteTimelineEvent(long eventId)
        {
            try
            {
                var success = await _recipeAdvancedOrchestrationService.DeleteTimelineEventAsync(eventId);
                if (!success)
                {
                    return NotFound(new { Message = "Timeline event not found or not authorized to delete." });
                }
                return Ok(new { Message = "Timeline event deleted successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in DeleteTimelineEvent");
                return StatusCode(StatusCodes.Status500InternalServerError, new { Message = "An internal error occurred." });
            }
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

            try
            {
                var response = await _recipeAdvancedOrchestrationService.CreateNoteAsync(model);
                return CreatedAtAction(nameof(GetRecipeNotes), new { recipeId = model.RecipeId }, response);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Recipe not found for note creation");
                return NotFound(new { Message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in CreateNote");
                return StatusCode(StatusCodes.Status500InternalServerError, new { Message = "An internal error occurred." });
            }
        }

        [HttpGet("recipes/{recipeId}/notes")]
        [ProducesResponseType(typeof(List<RecipeNoteResponseModel>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetRecipeNotes(long recipeId)
        {
            try
            {
                var notes = await _recipeAdvancedOrchestrationService.GetRecipeNotesAsync(recipeId);
                return Ok(notes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in GetRecipeNotes");
                return StatusCode(StatusCodes.Status500InternalServerError, new { Message = "An internal error occurred." });
            }
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

            try
            {
                var success = await _recipeAdvancedOrchestrationService.UpdateNoteAsync(noteId, model);
                if (!success)
                {
                    return NotFound(new { Message = "Note not found or not authorized to update." });
                }
                return Ok(new { Message = "Note updated successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in UpdateNote");
                return StatusCode(StatusCodes.Status500InternalServerError, new { Message = "An internal error occurred." });
            }
        }

        [HttpDelete("notes/{noteId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteNote(long noteId)
        {
            try
            {
                var success = await _recipeAdvancedOrchestrationService.DeleteNoteAsync(noteId);
                if (!success)
                {
                    return NotFound(new { Message = "Note not found or not authorized to delete." });
                }
                return Ok(new { Message = "Note deleted successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in DeleteNote");
                return StatusCode(StatusCodes.Status500InternalServerError, new { Message = "An internal error occurred." });
            }
        }

        // Recipe Actions
        [HttpPost("recipes/{recipeId}/mark-as-made")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> MarkRecipeAsMade(long recipeId)
        {
            try
            {
                var success = await _recipeAdvancedOrchestrationService.MarkRecipeAsMadeAsync(recipeId);
                if (!success)
                {
                    return NotFound(new { Message = "Recipe not found." });
                }
                return Ok(new { Message = "Recipe marked as made successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in MarkRecipeAsMade");
                return StatusCode(StatusCodes.Status500InternalServerError, new { Message = "An internal error occurred." });
            }
        }

        [HttpGet("recipes/{recipeId}/last-made")]
        [ProducesResponseType(typeof(DateTime?), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetRecipeLastMade(long recipeId)
        {
            try
            {
                var lastMade = await _recipeAdvancedOrchestrationService.GetRecipeLastMadeAsync(recipeId);
                return Ok(new { LastMade = lastMade });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in GetRecipeLastMade");
                return StatusCode(StatusCodes.Status500InternalServerError, new { Message = "An internal error occurred." });
            }
        }
    }
} 