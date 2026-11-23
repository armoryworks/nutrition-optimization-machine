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

        /// <summary>
        /// Take back the reprocess writes made at or after the given UTC time, or of one batch, newest first,
        /// limited to one kind when given; a dry run unless apply=true. The entries are taken in batches of
        /// batchSize (at most maxBatches when given); an apply commits each batch with a progress row, answers
        /// its runId, ends between batches when stopped or when the caller goes away, and goes on under
        /// runId (the filter of that run is used) from the last entry it reached.
        /// </summary>
        [HttpPost("undo")]
        public async Task<ActionResult<RecipeReprocessUndoResult>> Undo(
            [FromQuery] DateTime? sinceUtc, [FromQuery] string? batchId, [FromQuery] string? kind = null, [FromQuery] bool apply = false,
            [FromQuery] string? runId = null, [FromQuery] int batchSize = RecipeReprocessService.UndoBatchSize, [FromQuery] int? maxBatches = null,
            CancellationToken cancellationToken = default)
        {
            if (batchSize < 1 || batchSize > RecipeReprocessService.MaxUndoBatchSize || maxBatches < 1)
            {
                return BadRequest(new { message = $"batchSize must be 1 to {RecipeReprocessService.MaxUndoBatchSize} and maxBatches at least 1" });
            }

            if (!string.IsNullOrWhiteSpace(runId))
            {
                var continued = await _reprocess.UndoAsync(new RecipeReprocessUndoRequest { RunId = runId, Apply = apply, BatchSize = batchSize, MaxBatches = maxBatches }, cancellationToken);
                return continued.Refused != null ? Conflict(continued) : Ok(continued);
            }

            if (sinceUtc == null && string.IsNullOrWhiteSpace(batchId))
            {
                return BadRequest(new { message = "Give sinceUtc, batchId or runId." });
            }

            if (kind != null && kind is not (RecipeReprocessKind.Rebuild or RecipeReprocessKind.Order or RecipeReprocessKind.RuleReject or RecipeReprocessKind.DuplicateRetire))
            {
                return BadRequest(new { message = "Unknown kind.", kinds = new[] { RecipeReprocessKind.Rebuild, RecipeReprocessKind.Order, RecipeReprocessKind.RuleReject, RecipeReprocessKind.DuplicateRetire } });
            }

            var since = sinceUtc == null ? (DateTime?)null : DateTime.SpecifyKind(sinceUtc.Value, DateTimeKind.Utc);
            var result = await _reprocess.UndoAsync(
                new RecipeReprocessUndoRequest { SinceUtc = since, BatchId = batchId, Kind = kind, Apply = apply, BatchSize = batchSize, MaxBatches = maxBatches },
                cancellationToken);
            return result.Refused != null ? Conflict(result) : Ok(result);
        }

        /// <summary>How far an undo run has come (the newest operator run, not the lane's own, when runId is not given): batches, entries reached and remaining, counts, working time and rate, and state.</summary>
        [HttpGet("undo/progress")]
        public async Task<ActionResult<RecipeReprocessUndoProgress>> UndoProgress([FromQuery] string? runId = null)
        {
            var progress = await _reprocess.UndoProgressAsync(runId);
            return progress == null ? NotFound(new { message = "No such undo run." }) : Ok(progress);
        }

        /// <summary>Ask an undo run (the newest operator run, not the lane's own, when runId is not given) to stop after the batch it is on; it can go on later under its runId.</summary>
        [HttpPost("undo/stop")]
        public async Task<ActionResult<RecipeReprocessUndoProgress>> StopUndo([FromQuery] string? runId = null)
        {
            var progress = await _reprocess.StopUndoAsync(runId);
            return progress == null ? NotFound(new { message = "No such undo run." }) : Ok(progress);
        }
    }
}
