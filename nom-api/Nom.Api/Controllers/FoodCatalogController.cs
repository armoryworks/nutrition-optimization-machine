using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using System.ComponentModel.DataAnnotations;

namespace Nom.Api.Controllers
{
    /// <summary>
    /// Admin review of the imported food catalog (FDC and authored), the deterministic quality
    /// audit, and the proposal pipeline that external reviewers feed. Curation-gated throughout.
    /// </summary>
    [Authorize(Policy = "CanManageCuration")]
    [ApiController]
    [Route("api/[controller]")]
    public class FoodCatalogController : BaseApiController
    {
        private readonly IFoodCatalogReviewService _review;
        private readonly IFoodCatalogAuditService _audit;

        public FoodCatalogController(IFoodCatalogReviewService review, IFoodCatalogAuditService audit)
        {
            _review = review;
            _audit = audit;
        }

        /// <summary>Paged catalog for the review screen.</summary>
        [HttpGet]
        public async Task<ActionResult<FoodCatalogPageModel>> GetPage(
            [FromQuery] string? source, [FromQuery] long? status, [FromQuery] long? foodGroupId,
            [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
        {
            return Ok(await _review.GetPageAsync(source, status, foodGroupId, search, page, pageSize));
        }

        /// <summary>Deterministic quality audit — no model, no network.</summary>
        [HttpGet("audit")]
        public async Task<ActionResult<FoodCatalogAuditResult>> Audit(
            [FromQuery] string? source, [FromQuery] int limit = 5000)
        {
            return Ok(await _audit.AuditAsync(source, limit));
        }

        /// <summary>Catalog as CSV, for an external reviewer to read.</summary>
        [HttpGet("export")]
        public async Task<IActionResult> Export(
            [FromQuery] string? source, [FromQuery] long? status, [FromQuery] int limit = 5000)
        {
            var csv = await _review.ExportCsvAsync(source, status, limit);
            return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", "food-catalog.csv");
        }

        [HttpPut("{id:long}")]
        public async Task<ActionResult<FoodCatalogItemModel>> Update(long id, [FromBody] FoodCatalogUpdateModel model)
        {
            var updated = await _review.UpdateAsync(id, model);
            return updated == null ? NotFound() : Ok(updated);
        }

        /// <summary>Bulk promote/demote reviewed rows (e.g. to Curated so planning can use them).</summary>
        [HttpPost("curation-status")]
        public async Task<IActionResult> SetCurationStatus([FromBody] SetCurationStatusRequest request)
        {
            var count = await _review.SetCurationStatusAsync(request.IngredientIds, request.CurationStatusId);
            return Ok(new { updated = count });
        }

        /// <summary>
        /// Ingests a reviewer's proposal CSV. Rows proposing a nutrient value without an
        /// authoritative source are rejected — automated reviewers may not author numbers.
        /// </summary>
        [HttpPost("proposals")]
        public async Task<ActionResult<FoodProposalIngestResult>> IngestProposals(
            [FromBody] IngestProposalsRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Csv))
                return BadRequest(new { message = "CSV content is required." });

            var batch = string.IsNullOrWhiteSpace(request.Batch)
                ? $"batch-{DateTime.UtcNow:yyyyMMdd-HHmmss}"
                : request.Batch.Trim();

            return Ok(await _review.IngestProposalsCsvAsync(request.Csv, batch));
        }

        [HttpGet("proposals")]
        public async Task<ActionResult<List<FoodProposalModel>>> GetProposals(
            [FromQuery] string? batch, [FromQuery] string? status = "Pending", [FromQuery] int limit = 200)
        {
            return Ok(await _review.GetProposalsAsync(batch, status, limit));
        }

        [HttpPost("proposals/{id:long}/apply")]
        public async Task<IActionResult> ApplyProposal([Required] long id)
        {
            var ok = await _review.ApplyProposalAsync(id, GetCurrentPersonIdRequired());
            return ok ? Ok() : BadRequest(new { message = "Proposal could not be applied." });
        }

        [HttpPost("proposals/{id:long}/reject")]
        public async Task<IActionResult> RejectProposal([Required] long id)
        {
            var ok = await _review.RejectProposalAsync(id, GetCurrentPersonIdRequired());
            return ok ? Ok() : NotFound();
        }
    }

    public class SetCurationStatusRequest
    {
        public List<long> IngredientIds { get; set; } = new();
        public long CurationStatusId { get; set; }
    }

    public class IngestProposalsRequest
    {
        public string Csv { get; set; } = string.Empty;
        public string? Batch { get; set; }
    }
}
