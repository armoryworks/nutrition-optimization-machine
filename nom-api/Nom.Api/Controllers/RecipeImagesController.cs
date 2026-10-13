using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;

namespace Nom.Api.Controllers
{
    /// <summary>
    /// Openly licensed photos for published recipes. The external image finder
    /// (ops/recipe-image-finder.py, run with a curation admin's API token) reads the recipes
    /// that need one and submits verified candidates; admins review the borderline ones.
    /// This API never fetches a third-party URL.
    /// </summary>
    [Authorize(Policy = "CanManageCuration")]
    public class RecipeImagesController : BaseApiController
    {
        private static readonly string[] ListableStatuses =
        {
            RecipeImageCandidateStatus.Pending, RecipeImageCandidateStatus.Attached, RecipeImageCandidateStatus.Approved,
            RecipeImageCandidateStatus.Rejected, RecipeImageCandidateStatus.Superseded, RecipeImageCandidateStatus.Removed,
        };

        private readonly IRecipeImageService _images;

        public RecipeImagesController(IRecipeImageService images)
        {
            _images = images;
        }

        /// <summary>Published recipes with no image and nothing waiting in the review queue, most-rated first.</summary>
        [HttpGet("missing")]
        public async Task<ActionResult<List<RecipeImageWorkItemModel>>> GetMissing([FromQuery] int limit = 50)
        {
            return Ok(await _images.GetMissingAsync(limit));
        }

        /// <summary>Stores the finder's candidates for one recipe; "attached" ones go live at once.</summary>
        [HttpPost("candidates")]
        [RequestSizeLimit(60_000_000)]
        public async Task<ActionResult<RecipeImageSubmissionResultModel>> Submit([FromBody] RecipeImageSubmissionModel submission)
        {
            if (submission.Candidates.Count == 0)
                return BadRequest(new { message = "Send at least one candidate." });

            var result = await _images.SubmitAsync(submission, GetCurrentPersonId());
            return result == null ? NotFound(new { message = "Recipe not found." }) : Ok(result);
        }

        [HttpGet("candidates")]
        public async Task<ActionResult<List<RecipeImageCandidateModel>>> GetCandidates(
            [FromQuery] string status = RecipeImageCandidateStatus.Pending, [FromQuery] int limit = 100)
        {
            if (!ListableStatuses.Contains(status))
                return BadRequest(new { message = $"Unknown status '{status}'." });

            return Ok(await _images.GetCandidatesAsync(status, limit));
        }

        /// <summary>The rehosted photo of a candidate, for the review screen.</summary>
        [HttpGet("candidates/{id:long}/image")]
        public async Task<IActionResult> GetCandidateImage(long id)
        {
            var image = await _images.GetCandidateImageAsync(id);
            return image == null ? NotFound() : File(image.Value.Data, image.Value.ContentType);
        }

        [HttpPost("candidates/{id:long}/approve")]
        public async Task<IActionResult> Approve(long id)
        {
            return ToResponse(await _images.ApproveAsync(id, GetCurrentPersonId()));
        }

        [HttpPost("candidates/{id:long}/reject")]
        public async Task<IActionResult> Reject(long id)
        {
            return ToResponse(await _images.RejectAsync(id, GetCurrentPersonId()));
        }

        /// <summary>Takes an attached photo back off its recipe, e.g. after a licence complaint.</summary>
        [HttpPost("candidates/{id:long}/remove")]
        public async Task<IActionResult> Remove(long id)
        {
            return ToResponse(await _images.RemoveAsync(id, GetCurrentPersonId()));
        }

        /// <summary>In-use Unsplash photos whose download event the finder still has to report to Unsplash.</summary>
        [HttpGet("download-tracking")]
        public async Task<ActionResult<List<RecipeImageDownloadTrackingModel>>> GetDownloadTracking()
        {
            return Ok(await _images.GetPendingDownloadTrackingAsync());
        }

        [HttpPost("candidates/{id:long}/download-tracked")]
        public async Task<IActionResult> MarkDownloadTracked(long id)
        {
            return await _images.MarkDownloadTrackedAsync(id) ? NoContent() : NotFound();
        }

        private IActionResult ToResponse(RecipeImageReviewResult result) => result switch
        {
            RecipeImageReviewResult.Ok => NoContent(),
            RecipeImageReviewResult.NotFound => NotFound(),
            RecipeImageReviewResult.RecipeHasImage => Conflict(new { message = "The recipe already has an image." }),
            _ => Conflict(new { message = "The candidate is not in a state that allows this." }),
        };
    }
}
