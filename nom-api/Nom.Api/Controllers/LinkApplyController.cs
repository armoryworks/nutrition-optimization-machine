using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using System.ComponentModel.DataAnnotations;

namespace Nom.Api.Controllers
{
    /// <summary>
    /// The rails on applied USDA links: taking back applied links from the audit log, and the
    /// hold rules that keep a name match or model pick for a person. Curation-gated, beside the
    /// food catalog's own proposal routes.
    /// </summary>
    [Authorize(Policy = "CanManageCuration")]
    [ApiController]
    [Route("api/FoodCatalog/proposals")]
    public class LinkApplyController : BaseApiController
    {
        private readonly ILinkApplyUndoService _undo;
        private readonly ILinkAutoApplyService _autoApply;

        public LinkApplyController(ILinkApplyUndoService undo, ILinkAutoApplyService autoApply)
        {
            _undo = undo;
            _autoApply = autoApply;
        }

        /// <summary>Take back, newest first, up to max links applied at or after the given UTC time, optionally only those whose source starts with source or that the lanes applied; a dry run unless apply=true.</summary>
        [HttpPost("undo-applied")]
        public async Task<ActionResult<LinkApplyUndoResult>> UndoApplied(
            [FromQuery, Required] DateTime sinceUtc, [FromQuery] string? source = null, [FromQuery] bool systemOnly = false,
            [FromQuery] bool apply = false, [FromQuery] int max = 500, [FromQuery] int sampleSize = 50)
        {
            return Ok(await _undo.UndoAsync(DateTime.SpecifyKind(sinceUtc, DateTimeKind.Utc), source, systemOnly, apply, max, sampleSize));
        }

        /// <summary>What the hold rules make of every pending name match and model pick (no writes).</summary>
        [HttpGet("link-holds")]
        public async Task<ActionResult<LinkHoldReport>> LinkHolds([FromQuery] int sampleSize = 50)
        {
            return Ok(await _autoApply.HoldReportAsync(sampleSize, HttpContext.RequestAborted));
        }
    }
}
