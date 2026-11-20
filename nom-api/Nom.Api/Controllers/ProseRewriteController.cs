using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services.Support;
using System.ComponentModel.DataAnnotations;

namespace Nom.Api.Controllers
{
    /// <summary>
    /// Admin re-check of the scraped recipes whose prose is shown, with the withholding of the ones
    /// that fail, the step-marker cleanup, and the stock-phrase index of the prose check.
    /// Curation-gated throughout.
    /// </summary>
    [Authorize(Policy = "CanManageCuration")]
    public class ProseRewriteController : BaseApiController
    {
        private readonly IProseRecheckService _recheck;
        private readonly IProseStockService _stock;
        private readonly IProseMarkerService _markers;

        public ProseRewriteController(IProseRecheckService recheck, IProseStockService stock, IProseMarkerService markers)
        {
            _recheck = recheck;
            _stock = stock;
            _markers = markers;
        }

        /// <summary>Checks every shown scraped recipe against its stored source with the prose check of the given version (only the current one runs) and, with apply=true, withholds the prose of up to maxRecipes failing ones, approved first; a dry run unless apply=true, and each one withheld is written to the audit log.</summary>
        [HttpPost("recheck")]
        public async Task<ActionResult<ProseRecheckResult>> Recheck([FromQuery] int version = ProseCheck.Version, [FromQuery] bool apply = false, [FromQuery] int maxRecipes = Nom.Orch.Services.ProseRecheckService.WithholdBatch, [FromQuery] int sampleSize = 50, CancellationToken cancellationToken = default)
        {
            if (version != ProseCheck.Version)
                return BadRequest($"Only prose check version {ProseCheck.Version} runs; version {version} is not available.");
            return Ok(await _recheck.RecheckAsync(apply, maxRecipes, sampleSize, cancellationToken: cancellationToken));
        }

        /// <summary>Release the recipes withheld (or, from an earlier re-check, put back into quarantine) at or after the given UTC time, unless their prose has been replaced since; a dry run unless apply=true.</summary>
        [HttpPost("recheck/undo")]
        public async Task<ActionResult<ProseRecheckUndoResult>> RecheckUndo([FromQuery, Required] DateTime sinceUtc, [FromQuery] bool apply = false)
        {
            return Ok(await _recheck.UndoAsync(DateTime.SpecifyKind(sinceUtc, DateTimeKind.Utc), apply));
        }

        /// <summary>Take the step markers out of the steps of up to maxRecipes scraped recipes; a dry run unless apply=true, and each step changed is written to the audit log with its old text.</summary>
        [HttpPost("markers")]
        public async Task<ActionResult<ProseMarkerStripResult>> StripMarkers([FromQuery] bool apply = false, [FromQuery] int maxRecipes = 1000, [FromQuery] int sampleSize = 20, CancellationToken cancellationToken = default)
        {
            return Ok(await _markers.StripAsync(apply, maxRecipes, sampleSize, cancellationToken));
        }

        /// <summary>Put back the step text the marker cleanup changed at or after the given UTC time, unless the step has changed since; a dry run unless apply=true.</summary>
        [HttpPost("markers/undo")]
        public async Task<ActionResult<ProseMarkerUndoResult>> UndoMarkers([FromQuery, Required] DateTime sinceUtc, [FromQuery] bool apply = false, CancellationToken cancellationToken = default)
        {
            return Ok(await _markers.UndoAsync(DateTime.SpecifyKind(sinceUtc, DateTimeKind.Utc), apply, cancellationToken));
        }

        /// <summary>Rebuild the stock-phrase index of the prose check from the stored source documents under the next version; a dry run unless apply=true, an unchanged index is not written, and a write is journaled.</summary>
        [HttpPost("stock/rebuild")]
        public async Task<ActionResult<ProseStockRebuildResult>> RebuildStock([FromQuery] bool apply = false, CancellationToken cancellationToken = default)
        {
            return Ok(await _stock.RebuildAsync(apply, cancellationToken));
        }

        /// <summary>Restore an earlier version of the stock-phrase index by writing its phrases again under the next version; a dry run unless apply=true, an unknown or current version is not written, and a write is journaled.</summary>
        [HttpPost("stock/restore")]
        public async Task<ActionResult<ProseStockRestoreResult>> RestoreStock([FromQuery] int version, [FromQuery] bool apply = false, CancellationToken cancellationToken = default)
        {
            return Ok(await _stock.RestoreAsync(version, apply, cancellationToken));
        }
    }
}
