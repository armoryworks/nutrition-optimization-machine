using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;
using Nom.Orch.Services;

namespace Nom.Api.Controllers
{
    /// <summary>
    /// Rebuilds recipes in place from their stored source with the import's own line builder, sets
    /// the lost order of their lines, retires second copies and takes any of it back. Nothing is
    /// fetched from any site. Curation-gated; each call is a dry run unless apply=true, and every
    /// write is journaled with the rows before and after.
    /// </summary>
    [Authorize(Policy = "CanManageCuration")]
    public class RecipeReprocessController : BaseApiController
    {
        private readonly IRecipeReprocessService _reprocess;
        private readonly IReprocessDetectorService _detector;

        public RecipeReprocessController(IRecipeReprocessService reprocess, IReprocessDetectorService detector)
        {
            _reprocess = reprocess;
            _detector = detector;
        }

        /// <summary>
        /// Run the detector over at most maxRecipes recipes with a stored source after afterRecipeId and
        /// show the candidates by reason and tier, with samples. No model is called and no pause switch
        /// is waited on. A dry run reads every such recipe and writes nothing; with apply=true it reads
        /// those the current line rules have not read and stores their reasons as the lane's sweep does,
        /// writing nothing to recipes. Page with the answer's lastRecipeId.
        /// </summary>
        [HttpPost("detect")]
        public async Task<ActionResult<ReprocessDetectReport>> Detect(
            [FromQuery] long afterRecipeId = 0, [FromQuery] int maxRecipes = RecipeReprocessService.MaxRecipesPerCall, [FromQuery] bool apply = false, [FromQuery] int sampleSize = 20,
            CancellationToken cancellationToken = default)
        {
            if (maxRecipes < 1 || maxRecipes > RecipeReprocessService.MaxRecipesPerCall)
            {
                return BadRequest(new { message = $"maxRecipes must be 1 to {RecipeReprocessService.MaxRecipesPerCall}" });
            }

            return Ok(await _detector.DetectAsync(afterRecipeId, maxRecipes, apply, Math.Clamp(sampleSize, 0, 200), cancellationToken));
        }

        /// <summary>Build the given recipes' lines again from their stored source and show each line before and after; with orderOnly=true only set their order; a dry run unless apply=true.</summary>
        [HttpPost("rebuild")]
        public async Task<ActionResult<RecipeReprocessResult>> Rebuild([FromBody] RecipeReprocessRequestModel request, [FromQuery] bool apply = false, [FromQuery] bool orderOnly = false)
        {
            if (request.RecipeIds.Count == 0)
            {
                return BadRequest(new { message = "recipeIds is empty" });
            }

            if (request.RecipeIds.Count > RecipeReprocessService.MaxRecipesPerCall)
            {
                return BadRequest(new { message = $"at most {RecipeReprocessService.MaxRecipesPerCall} recipes per call" });
            }

            return Ok(await _reprocess.RebuildAsync(request, apply, orderOnly));
        }

        /// <summary>Retire the second copies of recipes imported twice, among the given recipes or all; a dry run unless apply=true.</summary>
        [HttpPost("duplicates")]
        public async Task<ActionResult<RecipeDuplicateResult>> Duplicates([FromBody] RecipeDuplicateRequestModel request, [FromQuery] bool apply = false)
        {
            return Ok(await _reprocess.RetireDuplicatesAsync(request, apply));
        }

        /// <summary>Take back the reprocess writes made at or after the given UTC time, or of one batch, newest first, limited to one kind when given; a dry run unless apply=true.</summary>
        [HttpPost("undo")]
        public async Task<ActionResult<RecipeReprocessUndoResult>> Undo([FromQuery] DateTime? sinceUtc, [FromQuery] string? batchId, [FromQuery] string? kind = null, [FromQuery] bool apply = false)
        {
            if (sinceUtc == null && string.IsNullOrWhiteSpace(batchId))
            {
                return BadRequest(new { message = "Give sinceUtc or batchId." });
            }

            if (kind != null && kind is not (RecipeReprocessKind.Rebuild or RecipeReprocessKind.Order or RecipeReprocessKind.RuleReject or RecipeReprocessKind.DuplicateRetire))
            {
                return BadRequest(new { message = "Unknown kind.", kinds = new[] { RecipeReprocessKind.Rebuild, RecipeReprocessKind.Order, RecipeReprocessKind.RuleReject, RecipeReprocessKind.DuplicateRetire } });
            }

            var since = sinceUtc == null ? (DateTime?)null : DateTime.SpecifyKind(sinceUtc.Value, DateTimeKind.Utc);
            return Ok(await _reprocess.UndoAsync(since, batchId, kind, apply));
        }
    }
}
