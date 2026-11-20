using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using System.ComponentModel.DataAnnotations;

namespace Nom.Api.Controllers
{
    /// <summary>
    /// Admin re-check of the scraped recipes the prose-rewrite lane has taken out of the
    /// source-prose quarantine, and the stock-phrase index of the prose check. Curation-gated
    /// throughout.
    /// </summary>
    [Authorize(Policy = "CanManageCuration")]
    public class ProseRewriteController : BaseApiController
    {
        private readonly IProseRecheckService _recheck;
        private readonly IProseStockService _stock;

        public ProseRewriteController(IProseRecheckService recheck, IProseStockService stock)
        {
            _recheck = recheck;
            _stock = stock;
        }

        /// <summary>Checks every cleared recipe against its stored source and, with apply=true, puts up to maxRecipes failing ones back into quarantine; a dry run unless apply=true, and each one put back is written to the audit log.</summary>
        [HttpPost("recheck")]
        public async Task<ActionResult<ProseRecheckResult>> Recheck([FromQuery] bool apply = false, [FromQuery] int maxRecipes = 50000, [FromQuery] int sampleSize = 50)
        {
            return Ok(await _recheck.RecheckAsync(apply, maxRecipes, sampleSize));
        }

        /// <summary>Take the recipes put back into quarantine at or after the given UTC time out of it again, unless the rewrite lane has cleared them with a rewrite since; a dry run unless apply=true.</summary>
        [HttpPost("recheck/undo")]
        public async Task<ActionResult<ProseRecheckUndoResult>> RecheckUndo([FromQuery, Required] DateTime sinceUtc, [FromQuery] bool apply = false)
        {
            return Ok(await _recheck.UndoAsync(DateTime.SpecifyKind(sinceUtc, DateTimeKind.Utc), apply));
        }

        /// <summary>Rebuild the stock-phrase index of the prose check from the stored source documents under the next version; a dry run unless apply=true, an unchanged index is not written, and a write is journaled.</summary>
        [HttpPost("stock/rebuild")]
        public async Task<ActionResult<ProseStockRebuildResult>> RebuildStock([FromQuery] bool apply = false, CancellationToken cancellationToken = default)
        {
            return Ok(await _stock.RebuildAsync(apply, cancellationToken));
        }
    }
}
