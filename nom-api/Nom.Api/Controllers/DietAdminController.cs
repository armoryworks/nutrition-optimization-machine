// File: Nom.Api/Controllers/DietAdminController.cs

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Plan;

namespace Nom.Api.Controllers
{
    /// <summary>
    /// Admin management of diet/restriction categories (reference rows in the
    /// diet groups) and the filter criteria that give them teeth.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "CanManageCuration")]
    public class DietAdminController : BaseApiController
    {
        private readonly IDietAdminOrchestrationService _dietAdminService;
        private readonly Nom.Data.ApplicationDbContext _db;

        public DietAdminController(IDietAdminOrchestrationService dietAdminService, Nom.Data.ApplicationDbContext db)
        {
            _dietAdminService = dietAdminService;
            _db = db;
        }

        [HttpGet("gate-counts")]
        public async Task<ActionResult<Nom.Orch.Services.Support.DietStoredCountsReport>> GetGateCounts()
            => Ok(await Nom.Orch.Services.Support.DietGateStoreReports.CountsAsync(_db));

        [HttpGet("gate-build")]
        public async Task<ActionResult<List<Nom.Orch.Services.Support.DietGateBuildReport>>> GetGateBuild()
            => Ok(await Nom.Orch.Services.Support.DietGateStoreReports.BuildsAsync(_db));

        [HttpGet("second-reader")]
        public async Task<ActionResult<Nom.Orch.Services.DietSecondProgress>> GetSecondReader([FromServices] Nom.Orch.Services.DietSecondReaderLaneService lane, CancellationToken cancellationToken)
            => Ok(await lane.ProgressAsync(cancellationToken));

        /// <summary>A page of the second reader's stored readings (recipe id, model, prompt and text signatures, the answer per type), by recipe id after <paramref name="after"/>; currentOnly (default) gives only readable readings under the current prompt.</summary>
        [HttpGet("second-reader/readings")]
        public async Task<ActionResult<Nom.Orch.Services.DietReadingExportPage>> ExportReadings([FromServices] Nom.Orch.Services.DietSecondReaderLaneService lane,
            [FromQuery] long after = 0, [FromQuery] int recipes = 1000, [FromQuery] bool currentOnly = true, CancellationToken cancellationToken = default)
            => Ok(await lane.ExportAsync(after, recipes, currentOnly, cancellationToken));

        /// <summary>Takes readings made elsewhere where the model, the prompt signature and the recipe's text signature match exactly; a dry run unless apply=true, and every reading taken is written to the audit log under the returned stamp.</summary>
        [HttpPost("second-reader/readings/import")]
        public async Task<ActionResult<Nom.Orch.Services.DietReadingImportReport>> ImportReadings([FromServices] Nom.Orch.Services.DietSecondReaderLaneService lane,
            [FromBody] List<Nom.Orch.Services.DietReadingImportItem> items, [FromQuery] bool apply = false, CancellationToken cancellationToken = default)
        {
            if (items == null || items.Count == 0 || items.Count > Nom.Orch.Services.DietSecondReaderLaneService.MaxImport)
                return BadRequest($"Send between 1 and {Nom.Orch.Services.DietSecondReaderLaneService.MaxImport} readings.");
            return Ok(await lane.ImportAsync(items, apply, GetCurrentPersonIdRequired(), cancellationToken));
        }

        /// <summary>Takes back the readings an import with the given stamp took; a reading changed since is skipped. A dry run unless apply=true.</summary>
        [HttpPost("second-reader/readings/import/undo")]
        public async Task<ActionResult<Nom.Orch.Services.DietReadingUndoReport>> UndoImportReadings([FromServices] Nom.Orch.Services.DietSecondReaderLaneService lane,
            [FromQuery, System.ComponentModel.DataAnnotations.Required] DateTime stamp, [FromQuery] bool apply = false, CancellationToken cancellationToken = default)
            => Ok(await lane.UndoImportAsync(Utc(stamp), apply, GetCurrentPersonIdRequired(), cancellationToken));

        /// <summary>Every judged hold: a recipe people judged to hold a food a restriction type keeps out, flagged by the gate as "Judged to contain".</summary>
        [HttpGet("judged-holds")]
        public async Task<ActionResult<DietJudgedHoldListModel>> GetJudgedHolds([FromServices] IDietJudgedHoldService holds, CancellationToken cancellationToken)
            => Ok(await holds.ListAsync(cancellationToken));

        /// <summary>Loads the judged holds ops/diet-judged-holds.py sends; a dry run unless apply=true, and every hold added is written to the audit log under the returned stamp.</summary>
        [HttpPost("judged-holds")]
        public async Task<ActionResult<DietJudgedHoldLoadReport>> LoadJudgedHolds([FromServices] IDietJudgedHoldService holds, [FromBody] List<DietJudgedHoldInput> rows,
            [FromQuery] bool apply = false, CancellationToken cancellationToken = default)
        {
            if (rows == null || rows.Count == 0 || rows.Count > 5000) return BadRequest("Send between 1 and 5000 rows.");
            return Ok(await holds.LoadAsync(rows, apply, GetCurrentPersonIdRequired(), cancellationToken));
        }

        /// <summary>Takes back the holds the load with the given stamp added; a row removed or changed since is skipped. A dry run unless apply=true.</summary>
        [HttpPost("judged-holds/undo")]
        public async Task<ActionResult<DietJudgedHoldUndoReport>> UndoJudgedHolds([FromServices] IDietJudgedHoldService holds,
            [FromQuery, System.ComponentModel.DataAnnotations.Required] DateTime stamp, [FromQuery] bool apply = false, CancellationToken cancellationToken = default)
            => Ok(await holds.UndoAsync(Utc(stamp), apply, GetCurrentPersonIdRequired(), cancellationToken));

        private static DateTime Utc(DateTime stamp) => DateTime.SpecifyKind(stamp.Kind == DateTimeKind.Local ? stamp.ToUniversalTime() : stamp, DateTimeKind.Utc);

        [HttpGet("groups")]
        public async Task<ActionResult<List<RestrictionGroupModel>>> GetGroups()
            => Ok(await _dietAdminService.GetGroupsAsync());

        [HttpPost("categories")]
        public async Task<ActionResult<RestrictionCategoryModel>> CreateCategory([FromBody] CreateRestrictionCategoryRequest request)
        {
            var created = await _dietAdminService.CreateCategoryAsync(request);
            if (created == null)
                return BadRequest(new { message = "Unknown or non-diet group" });
            return Ok(created);
        }

        [HttpPut("categories/{id}")]
        public async Task<ActionResult<RestrictionCategoryModel>> UpdateCategory(long id, [FromBody] UpdateRestrictionCategoryRequest request)
        {
            var updated = await _dietAdminService.UpdateCategoryAsync(id, request);
            if (updated == null)
                return NotFound(new { message = "Category not found" });
            return Ok(updated);
        }

        [HttpDelete("categories/{id}")]
        public async Task<ActionResult> DeleteCategory(long id)
        {
            var result = await _dietAdminService.DeleteCategoryAsync(id);
            if (result == null)
                return NotFound(new { message = "Category not found" });
            if (result == false)
                return Conflict(new { message = "Category is referenced by existing restrictions" });
            return NoContent();
        }

        [HttpGet("categories/{id}/criteria")]
        public async Task<ActionResult<List<RestrictionCriterionModel>>> GetCriteria(long id)
            => Ok(await _dietAdminService.GetCriteriaAsync(id));

        [HttpPost("categories/{id}/criteria")]
        public async Task<ActionResult<RestrictionCriterionModel>> AddCriterion(long id, [FromBody] SaveRestrictionCriterionRequest request)
        {
            var created = await _dietAdminService.AddCriterionAsync(id, request);
            if (created == null)
                return BadRequest(new { message = "Category not found, or criterion needs an ingredient, pattern, or nutrient" });
            return Ok(created);
        }

        [HttpDelete("criteria/{criterionId}")]
        public async Task<ActionResult> DeleteCriterion(long criterionId)
        {
            var removed = await _dietAdminService.DeleteCriterionAsync(criterionId);
            return removed ? NoContent() : NotFound(new { message = "Criterion not found" });
        }
    }
}
