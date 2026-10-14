using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;

namespace Nom.Api.Controllers
{
    /// <summary>
    /// Admin view of the AI kitchen-tool lane: tagging progress and the queue of uncommon
    /// equipment it noticed, which admins approve into the tool catalog or reject.
    /// </summary>
    [Authorize(Policy = "CanManageCuration")]
    public class KitchenToolsController : BaseApiController
    {
        private readonly IKitchenToolTaggingService _tagging;

        public KitchenToolsController(IKitchenToolTaggingService tagging)
        {
            _tagging = tagging;
        }

        [HttpGet("status")]
        public async Task<ActionResult<KitchenToolTaggingStatusModel>> GetStatus()
        {
            return Ok(await _tagging.GetStatusAsync());
        }

        [HttpGet("suggestions")]
        public async Task<ActionResult<List<KitchenToolSuggestionModel>>> GetSuggestions([FromQuery] string status = KitchenToolSuggestionStatus.Pending)
        {
            if (status is not (KitchenToolSuggestionStatus.Pending or KitchenToolSuggestionStatus.Approved or KitchenToolSuggestionStatus.Rejected))
                return BadRequest(new { error = $"Unknown status '{status}'." });

            return Ok(await _tagging.GetSuggestionsAsync(status));
        }

        /// <summary>Adds the suggested equipment to the tool catalog and links it to every recipe it was seen in.</summary>
        [HttpPost("suggestions/{id:long}/approve")]
        public async Task<ActionResult<KitchenToolSuggestionApproveResultModel>> Approve(long id, [FromBody] KitchenToolSuggestionApproveModel? request)
        {
            var result = await _tagging.ApproveAsync(id, request ?? new KitchenToolSuggestionApproveModel(), GetCurrentPersonId());
            return result == null ? NotFound() : Ok(result);
        }

        [HttpPost("suggestions/{id:long}/reject")]
        public async Task<IActionResult> Reject(long id)
        {
            return await _tagging.RejectAsync(id) ? NoContent() : NotFound();
        }

        /// <summary>Groups a pending suggestion under another pending one, as another name for the same equipment.</summary>
        [HttpPost("suggestions/{id:long}/merge")]
        public async Task<IActionResult> MergeSuggestion(long id, [FromBody] KitchenToolSuggestionMergeModel request)
        {
            return await _tagging.MergeSuggestionAsync(id, request.IntoSuggestionId) ? NoContent() : NotFound();
        }

        /// <summary>Every tool a suggestion can be marked as another name for.</summary>
        [HttpGet("tools")]
        public async Task<ActionResult<List<KitchenToolOptionModel>>> GetTools()
        {
            return Ok(await _tagging.GetToolsAsync());
        }

        /// <summary>Retires an approved tool into another, keeping its recipes, household answers and names.</summary>
        [HttpPost("tools/{id:long}/merge")]
        public async Task<ActionResult<KitchenToolMergeResultModel>> MergeTool(long id, [FromBody] KitchenToolMergeModel request)
        {
            var result = await _tagging.MergeToolAsync(id, request.IntoToolId);
            return result == null ? NotFound() : Ok(result);
        }

        [HttpPost("tools/{id:long}/rename")]
        public async Task<IActionResult> RenameTool(long id, [FromBody] KitchenToolRenameModel request)
        {
            return await _tagging.RenameToolAsync(id, request.Name) ? NoContent() : NotFound();
        }

        /// <summary>Tags the next batch of untagged recipes now, without waiting for the background lane.</summary>
        [HttpPost("tag-batch")]
        public async Task<ActionResult<KitchenToolTaggingBatchResult>> TagBatch([FromQuery] int size = 6)
        {
            var result = await _tagging.TagNextBatchAsync(Math.Clamp(size, 1, 20), HttpContext.RequestAborted);
            return result == null
                ? StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "The tagging model is not configured or not responding." })
                : Ok(result);
        }
    }
}
