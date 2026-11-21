using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services;
using System.ComponentModel.DataAnnotations;

namespace Nom.Api.Controllers
{
    /// <summary>
    /// Admin loading of the reviewed wording table (db/verified-wordings.tsv), beside the rest of
    /// the food catalog tools. Curation-gated throughout.
    /// </summary>
    [Authorize(Policy = "CanManageCuration")]
    [ApiController]
    [Route("api/FoodCatalog/wordings")]
    public class VerifiedWordingController : BaseApiController
    {
        public const string EmptyBatch =
            "The batch holds no wording row, so nothing would be loaded. db/verified-wordings.tsv in the repository is the header only; "
            + "send the reviewed batch's own file (its text as tsv, or its rows).";

        private readonly IVerifiedWordingService _wordings;

        public VerifiedWordingController(IVerifiedWordingService wordings)
        {
            _wordings = wordings;
        }

        /// <summary>Load one reviewed batch, given as the TSV file's text or as rows; a dry run unless apply is set, and each row written is journaled.</summary>
        [HttpPost("load")]
        public async Task<ActionResult<VerifiedWordingLoadResult>> Load(
            [FromBody, Required] VerifiedWordingLoadRequest request, [FromQuery, Required] string batch,
            [FromQuery] bool apply = false, [FromQuery] bool replace = false, [FromQuery] bool overrideRejections = false)
        {
            if (string.IsNullOrWhiteSpace(batch) || batch.Trim().Length > 128)
                return BadRequest(new { message = "A batch name of 1 to 128 characters is required." });

            var problems = new List<VerifiedWordingProblem>();
            var rows = request.Rows ?? new List<VerifiedWordingRow>();
            if (!string.IsNullOrEmpty(request.Tsv)) rows.AddRange(VerifiedWordingService.ParseTsv(request.Tsv, problems));
            if (rows.Count == 0 && problems.Count == 0)
                return BadRequest(new { message = EmptyBatch });

            var result = await _wordings.LoadAsync(rows, batch, apply, replace, overrideRejections);
            result.Rows += problems.Count;
            result.Rejected += problems.Count;
            result.Problems.InsertRange(0, problems);
            return Ok(result);
        }

        /// <summary>Take every row of a batch out of the table, with withLines its lines and import landings first; a dry run unless apply is set.</summary>
        [HttpPost("unload")]
        public async Task<ActionResult<VerifiedWordingUnloadResult>> Unload([FromQuery, Required] string batch, [FromQuery] bool apply = false, [FromQuery] bool withLines = false)
        {
            return Ok(await _wordings.UnloadAsync(batch, apply, withLines));
        }

        /// <summary>Retract one wording row with its lines, sources, proposals, picks and import landings, and remember the pair as rejected; a dry run unless apply is set.</summary>
        [HttpPost("{id:long}/retract")]
        public async Task<ActionResult<WordingRetractResult>> Retract(long id, [FromQuery, Required] string reason, [FromQuery] bool apply = false, [FromQuery] string? evidenceKey = null)
        {
            if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 1023)
                return BadRequest(new { message = "A reason of 1 to 1023 characters is required." });

            var result = await _wordings.RetractAsync(id, reason, apply, evidenceKey);
            return result == null ? NotFound() : Ok(result);
        }

        /// <summary>The live table as TSV, for diffing against db/verified-wordings.tsv.</summary>
        [HttpGet("export")]
        public async Task<IActionResult> Export()
        {
            var tsv = await _wordings.ExportTsvAsync();
            return File(System.Text.Encoding.UTF8.GetBytes(tsv), "text/tab-separated-values", "verified-wordings.tsv");
        }
    }
}
