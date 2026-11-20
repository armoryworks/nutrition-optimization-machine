using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using System.ComponentModel.DataAnnotations;

namespace Nom.Api.Controllers
{
    /// <summary>
    /// Admin re-check of the scraped recipes the prose-rewrite lane has taken out of the
    /// source-prose quarantine. Curation-gated throughout.
    /// </summary>
    [Authorize(Policy = "CanManageCuration")]
    public class ProseRewriteController : BaseApiController
    {
        private readonly IProseRecheckService _recheck;

        public ProseRewriteController(IProseRecheckService recheck)
        {
            _recheck = recheck;
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
    }
}
