using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services.Support;
using System.ComponentModel.DataAnnotations;

namespace Nom.Api.Controllers
{
    /// <summary>
    /// Admin corrections of what the model lanes wrote on recipe lines, under the food catalog's
    /// routes: line labels the deterministic guard refuses, and piece counts filled from a quote
    /// that states no count. Each, and each undo, is a dry run unless apply=true.
    /// </summary>
    [Authorize(Policy = "CanManageCuration")]
    [ApiController]
    [Route("api/FoodCatalog")]
    public class RecipeLineCorrectionController : BaseApiController
    {
        private readonly IRecipeLineLabelReviewService _labels;
        private readonly IAiRepairRevertService _quantities;

        public RecipeLineCorrectionController(IRecipeLineLabelReviewService labels, IAiRepairRevertService quantities)
        {
            _labels = labels;
            _quantities = quantities;
        }

        /// <summary>Which lines labelled as not an ingredient state an amount or name a curated food, and with shortLines=true also the short lines on a curated food; a dry run unless apply=true, and each change is written to the audit log.</summary>
        [HttpPost("labels/review")]
        public async Task<ActionResult<LineLabelReviewResult>> ReviewLabels([FromQuery] bool apply = false, [FromQuery] bool shortLines = false, [FromQuery] int maxLines = 5000, [FromQuery] int sampleSize = 50)
        {
            return Ok(await _labels.ReviewAsync(apply, shortLines, maxLines, sampleSize));
        }

        /// <summary>Give back their earlier label to the lines corrected at or after the given UTC time; a dry run unless apply=true.</summary>
        [HttpPost("labels/undo")]
        public async Task<ActionResult<LineCorrectionUndoResult>> UndoLabels([FromQuery, Required] DateTime sinceUtc, [FromQuery] bool apply = false)
        {
            return Ok(await _labels.UndoAsync(DateTime.SpecifyKind(sinceUtc, DateTimeKind.Utc), apply));
        }

        /// <summary>Which model-filled piece counts state no count and would get back their earlier quantity, limited to the given reasons (all when none are given); a dry run unless apply=true, and each change is written to the audit log.</summary>
        [HttpPost("quantities/revert")]
        public async Task<ActionResult<QuantityRevertResult>> RevertQuantities([FromQuery] bool apply = false, [FromQuery] int maxLines = 5000, [FromQuery] int sampleSize = 50, [FromQuery] List<string>? reasons = null)
        {
            var chosen = (reasons ?? new List<string>())
                .SelectMany(r => (r ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Select(r => r.ToLowerInvariant())
                .Distinct()
                .ToList();
            if (chosen.Any(r => !RecipeRepairGrounding.RefusalReasons.ContainsKey(r)))
                return BadRequest(new { message = "Unknown reason.", reasons = RecipeRepairGrounding.RefusalReasons });
            return Ok(await _quantities.RevertAsync(apply, maxLines, sampleSize, chosen));
        }

        /// <summary>Give back the filled quantity to the lines reverted at or after the given UTC time; a dry run unless apply=true.</summary>
        [HttpPost("quantities/undo")]
        public async Task<ActionResult<LineCorrectionUndoResult>> UndoQuantityRevert([FromQuery, Required] DateTime sinceUtc, [FromQuery] bool apply = false)
        {
            return Ok(await _quantities.UndoAsync(DateTime.SpecifyKind(sinceUtc, DateTimeKind.Utc), apply));
        }
    }
}
