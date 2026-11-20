using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;

namespace Nom.Api.Controllers
{
    /// <summary>
    /// Stores the source records of already imported recipes (the staged import's files, posted by
    /// <c>ops/recipe-source-attach.py</c>) on those recipes. Nothing is fetched from any site.
    /// Curation-gated; a dry run unless apply=true, and every stored document is journaled.
    /// </summary>
    [Authorize(Policy = "CanManageCuration")]
    public class RecipeSourceController : BaseApiController
    {
        public const int MaxRecordsPerBatch = 500;

        private readonly IRecipeSourceService _sources;

        public RecipeSourceController(IRecipeSourceService sources)
        {
            _sources = sources;
        }

        /// <summary>Match each record to the one recipe imported from it (URL, else name and attribution, and at least 80 % of its lines) and, with apply=true, store it; newVersion=true stores a record on a recipe that already holds a different one of its format as the next version.</summary>
        [HttpPost("attach")]
        public async Task<ActionResult<RecipeSourceAttachResult>> Attach([FromBody] RecipeSourceAttachRequestModel request, [FromQuery] bool apply = false, [FromQuery] bool newVersion = false)
        {
            if (request.Records.Count == 0)
            {
                return BadRequest(new { message = "records is empty" });
            }

            if (request.Records.Count > MaxRecordsPerBatch)
            {
                return BadRequest(new { message = $"at most {MaxRecordsPerBatch} records per batch" });
            }

            return Ok(await _sources.AttachAsync(request, apply, newVersion));
        }

        /// <summary>Remove the documents attached at or after the given UTC time that are still there; a dry run unless apply=true, and each removal is journaled.</summary>
        [HttpPost("attach/undo")]
        public async Task<ActionResult<RecipeSourceUndoResult>> Undo([FromQuery, Required] DateTime sinceUtc, [FromQuery] bool apply = false)
        {
            return Ok(await _sources.UndoAsync(DateTime.SpecifyKind(sinceUtc, DateTimeKind.Utc), apply));
        }
    }
}
