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
        private readonly ICatalogCleanupService _cleanup;
        private readonly ILineRelinkService _relink;
        private readonly ILineUnfoldService _unfold;
        private readonly IPackageLineService _packages;
        private readonly IIngredientLinkService _links;
        private readonly ICompoundLineService _compound;
        private readonly IScrapedRecipeRepairService _repair;
        private readonly IContainerSizeService? _containerSizes;
        private readonly IBrandedLiftService? _brandedLift;

        public FoodCatalogController(IFoodCatalogReviewService review, IFoodCatalogAuditService audit,
            ICatalogCleanupService cleanup, ILineRelinkService relink, ILineUnfoldService unfold,
            IPackageLineService packages, IIngredientLinkService links, ICompoundLineService compound,
            IScrapedRecipeRepairService repair, IContainerSizeService? containerSizes = null, IBrandedLiftService? brandedLift = null)
        {
            _brandedLift = brandedLift;
            _repair = repair;
            _containerSizes = containerSizes;
            _review = review;
            _audit = audit;
            _cleanup = cleanup;
            _relink = relink;
            _unfold = unfold;
            _packages = packages;
            _links = links;
            _compound = compound;
        }

        /// <summary>What the import-residue cleanup would do (deterministic name rules, no writes).</summary>
        [HttpGet("cleanup/preview")]
        public async Task<ActionResult<CatalogCleanupPreview>> CleanupPreview([FromQuery] int sampleSize = 50)
        {
            return Ok(await _cleanup.PreviewAsync(sampleSize));
        }

        /// <summary>Apply up to maxActions residue fixes (merge into canonical row or rename in place).</summary>
        [HttpPost("cleanup/apply")]
        public async Task<ActionResult<CatalogCleanupResult>> CleanupApply([FromQuery] int maxActions = 500)
        {
            return Ok(await _cleanup.ApplyAsync(maxActions));
        }

        /// <summary>Replaces quantity-prefixed aliases, fills NameNormalized and resets stale triangulation marks; a dry run unless apply=true.</summary>
        [HttpPost("cleanup/normalize")]
        public async Task<ActionResult<CatalogNormalizeResult>> CleanupNormalize([FromQuery] bool apply = false)
        {
            return Ok(await _cleanup.NormalizeAsync(apply));
        }

        /// <summary>Which scraped recipe lines would move to the curated food their wording names (no writes).</summary>
        [HttpGet("relink/preview")]
        public async Task<ActionResult<LineRelinkPreview>> RelinkPreview([FromQuery] int sampleSize = 50)
        {
            return Ok(await _relink.PreviewAsync(sampleSize));
        }

        /// <summary>Move up to maxLines lines to the curated food their wording names; each move is written to the audit log.</summary>
        [HttpPost("relink/apply")]
        public async Task<ActionResult<LineRelinkResult>> RelinkApply([FromQuery] int maxLines = 5000)
        {
            return Ok(await _relink.ApplyAsync(maxLines));
        }

        /// <summary>Move every recipe line of one curated food to another, except lines containing one of the keep words; each move is written to the audit log.</summary>
        [HttpPost("relink/food")]
        public async Task<ActionResult<LineRelinkResult>> RelinkFood([FromQuery, Required] long fromId, [FromQuery, Required] long toId, [FromQuery] string? keep = null)
        {
            var result = await _relink.MoveFoodAsync(fromId, toId, keep);
            return result == null ? BadRequest(new { message = "Both must be live curated foods, and the target must have nutrition." }) : Ok(result);
        }

        /// <summary>Move the scraped lines of each reviewed wording to the curated food named for it; a dry run unless apply is set.</summary>
        [HttpPost("relink/wordings")]
        public async Task<ActionResult<LineRelinkResult>> RelinkWordings([FromBody, Required] List<LineRelinkWordingMove> moves, [FromQuery] bool apply = false)
        {
            return Ok(await _relink.MoveWordingsAsync(moves, apply));
        }

        /// <summary>Retire a duplicate food that has no recipe lines left, leaving its name as an alias of the main entry.</summary>
        [HttpPost("relink/retire")]
        public async Task<ActionResult<FoodRetireResult>> RelinkRetire([FromQuery, Required] long fromId, [FromQuery, Required] long toId, [FromQuery] bool takeName = false)
        {
            var result = await _relink.RetireAsync(fromId, toId, takeName);
            return result == null ? BadRequest(new { message = "Both must be live curated foods, and the target must have nutrition." }) : Ok(result);
        }

        /// <summary>Give a curated food a new name, moving a retired holder of the name out of the way; journaled for relink/undo.</summary>
        [HttpPost("relink/rename")]
        public async Task<ActionResult<FoodRenameResult>> RelinkRename([FromQuery, Required] long id, [FromQuery, Required] string name)
        {
            var result = await _relink.RenameAsync(id, name);
            return result == null ? BadRequest(new { message = "The food must be a live curated food, and the name must be new and not empty." }) : Ok(result);
        }

        /// <summary>Move the scraped lines of uncurated ingredients named with a verified wording (of one batch, when given) to the wording's food and retire the emptied ingredients; a dry run unless apply is set, and each move is written to the audit log.</summary>
        [HttpPost("relink/verified")]
        public async Task<ActionResult<VerifiedWordingLinkResult>> RelinkVerified([FromQuery] bool apply = false, [FromQuery] int maxLines = 20000, [FromQuery] int sampleSize = 50, [FromQuery] string? batch = null)
        {
            return Ok(await _relink.LinkVerifiedWordingsAsync(apply, maxLines, sampleSize, batch));
        }

        /// <summary>Give each source line of up to maxRows joined rows ("a + b") a row of its own on the same food; a dry run unless apply is set, and each row taken apart is written to the audit log.</summary>
        [HttpPost("relink/unfold")]
        public async Task<ActionResult<LineUnfoldResult>> RelinkUnfold([FromQuery] bool apply = false, [FromQuery] int maxRows = 5000, [FromQuery] int sampleSize = 50)
        {
            return Ok(await _unfold.UnfoldAsync(apply, maxRows, sampleSize));
        }

        /// <summary>Which scraped lines naming more than one food the given rules (all when none are given) would split or move to their first alternative, and which are left alone and why (no writes).</summary>
        [HttpGet("compound/preview")]
        public async Task<ActionResult<CompoundLinePreview>> CompoundPreview([FromQuery] List<string>? rules = null, [FromQuery] int sampleSize = 50)
        {
            var chosen = RuleList(rules);
            return chosen.Any(r => !Nom.Orch.Services.Support.CompoundLineSplitter.Rules.Contains(r))
                ? BadRequest(new { message = "Unknown rule.", rules = Nom.Orch.Services.Support.CompoundLineSplitter.Rules })
                : Ok(await _compound.PreviewAsync(chosen, sampleSize));
        }

        /// <summary>Apply the given rules to up to maxLines scraped lines naming more than one food; each line changed is written to the audit log and put back by relink/undo.</summary>
        [HttpPost("compound/apply")]
        public async Task<ActionResult<CompoundLineResult>> CompoundApply([FromQuery, Required] List<string> rules, [FromQuery] int maxLines = 5000)
        {
            var chosen = RuleList(rules);
            return chosen.Count == 0 || chosen.Any(r => !Nom.Orch.Services.Support.CompoundLineSplitter.Rules.Contains(r))
                ? BadRequest(new { message = "Name one or more known rules.", rules = Nom.Orch.Services.Support.CompoundLineSplitter.Rules })
                : Ok(await _compound.ApplyAsync(chosen, maxLines));
        }

        private static List<string> RuleList(IEnumerable<string>? rules) =>
            (rules ?? Enumerable.Empty<string>())
                .SelectMany(r => (r ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Select(r => r.ToLowerInvariant())
                .Distinct()
                .ToList();

        /// <summary>Put back the lines moved, the foods retired, the proposals withdrawn, the rows unfolded and the compound lines split or moved at or after the given UTC time; a dry run (rolled back) unless apply=true.</summary>
        [HttpPost("relink/undo")]
        public async Task<ActionResult<LineRelinkUndoResult>> RelinkUndo([FromQuery, Required] DateTime sinceUtc, [FromQuery] bool apply = false)
        {
            return Ok(await _relink.UndoAsync(DateTime.SpecifyKind(sinceUtc, DateTimeKind.Utc), apply));
        }

        /// <summary>The container sizes with their hold-out figures and status, paused and active first, and the container-sizes lane's mode.</summary>
        [HttpGet("container-sizes")]
        public async Task<ActionResult<ContainerSizeStatusModel>> ContainerSizes([FromQuery] int keys = 50)
        {
            if (_containerSizes == null) return NotFound();
            return Ok(await _containerSizes.StatusAsync(keys));
        }

        /// <summary>Score every container size against the stated sizes it would replace and set each key active, shadow or paused; a dry run unless apply is set, and each change is written to the audit log.</summary>
        [HttpPost("container-sizes/score")]
        public async Task<ActionResult<ContainerSizeScoreReport>> ContainerSizesScore([FromQuery] bool apply = false)
        {
            if (_containerSizes == null) return NotFound();
            return Ok(await _containerSizes.ScoreAsync(apply, GetCurrentPersonIdRequired()));
        }

        /// <summary>Take back every container size written, retired or rescored at or after the given UTC time, newest first; a size changed since is skipped. A dry run unless apply=true.</summary>
        [HttpPost("container-sizes/undo")]
        public async Task<ActionResult<ContainerSizeUndoReport>> ContainerSizesUndo([FromQuery, Required] DateTime sinceUtc, [FromQuery] bool apply = false)
        {
            if (_containerSizes == null) return NotFound();
            return Ok(await _containerSizes.UndoSinceAsync(DateTime.SpecifyKind(sinceUtc, DateTimeKind.Utc), apply, GetCurrentPersonIdRequired()));
        }

        /// <summary>Judge every branded food with evidence by its agreeing sources and curate a pending one two or more sources agree on; a dry run unless apply is set, and each change is written to the audit log under the run's stamp.</summary>
        [HttpPost("branded-lift")]
        public async Task<ActionResult<BrandedLiftReport>> BrandedLift([FromQuery] bool apply = false)
        {
            if (_brandedLift == null) return NotFound();
            return Ok(await _brandedLift.RunAsync(apply, GetCurrentPersonIdRequired()));
        }

        /// <summary>Take back what the branded lift run with the given stamp wrote, where each food still holds it; the undo has its own stamp. A dry run unless apply=true.</summary>
        [HttpPost("branded-lift/undo")]
        public async Task<ActionResult<BrandedLiftUndoReport>> BrandedLiftUndo([FromQuery, Required] DateTime stamp, [FromQuery] bool apply = false)
        {
            if (_brandedLift == null) return NotFound();
            return Ok(await _brandedLift.UndoAsync(DateTime.SpecifyKind(stamp, DateTimeKind.Utc), apply, GetCurrentPersonIdRequired()));
        }

        /// <summary>Which scraped recipe lines counted in pieces, cans or packages would take the size or the package their wording states (no writes).</summary>
        [HttpGet("reread/preview")]
        public async Task<ActionResult<PackageLinePreview>> RereadPreview([FromQuery] int sampleSize = 50)
        {
            return Ok(await _packages.PreviewAsync(sampleSize));
        }

        /// <summary>Give up to maxLines can and package lines the amount their wording states; each change is written to the audit log.</summary>
        [HttpPost("reread/apply")]
        public async Task<ActionResult<PackageLineResult>> RereadApply([FromQuery] int maxLines = 5000)
        {
            return Ok(await _packages.ApplyAsync(maxLines));
        }

        /// <summary>Put back the quantity and measurement of the lines re-read at or after the given UTC time; a dry run unless apply=true.</summary>
        [HttpPost("reread/undo")]
        public async Task<ActionResult<PackageLineUndoResult>> RereadUndo([FromQuery, Required] DateTime sinceUtc, [FromQuery] bool apply = false)
        {
            return Ok(await _packages.UndoAsync(DateTime.SpecifyKind(sinceUtc, DateTimeKind.Utc), apply));
        }

        /// <summary>Give back the amount each line had before a start-up repair at or after the given UTC time changed it; a dry run unless apply=true.</summary>
        [HttpPost("startup-fills/undo")]
        public async Task<ActionResult<StartupFillUndoResult>> StartupFillsUndo([FromQuery, Required] DateTime sinceUtc, [FromQuery] bool apply = false)
        {
            return Ok(await _repair.UndoFillsAsync(DateTime.SpecifyKind(sinceUtc, DateTimeKind.Utc), apply));
        }

        /// <summary>Marks the aliases with no context that a deterministic rule gives to the food they are on, so imports may follow them; a dry run unless apply=true, and each mark is written to the audit log.</summary>
        [HttpPost("aliases/classify")]
        public async Task<ActionResult<AliasClassifyResult>> ClassifyAliases([FromQuery] bool apply = false, [FromQuery] int sampleSize = 50)
        {
            return Ok(await _links.ClassifyAliasesAsync(apply, GetCurrentPersonIdRequired(), sampleSize));
        }

        /// <summary>Take the context back off the aliases marked at or after the given UTC time.</summary>
        [HttpPost("aliases/classify/undo")]
        public async Task<ActionResult<AliasClassifyUndoResult>> ClassifyAliasesUndo([FromQuery, Required] DateTime sinceUtc)
        {
            return Ok(await _links.UndoAliasClassificationAsync(DateTime.SpecifyKind(sinceUtc, DateTimeKind.Utc), GetCurrentPersonIdRequired()));
        }

        /// <summary>Paged catalog for the review screen.</summary>
        [HttpGet]
        public async Task<ActionResult<FoodCatalogPageModel>> GetPage(
            [FromQuery] string? source, [FromQuery] long? status, [FromQuery] long? foodGroupId,
            [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
        {
            return Ok(await _review.GetPageAsync(source, status, foodGroupId, search, page, pageSize));
        }

        /// <summary>Deterministic quality audit — no model, no network. Pages by id: afterId gives the next page, and Remaining says how many matching foods this page did not reach.</summary>
        [HttpGet("audit")]
        public async Task<ActionResult<FoodCatalogAuditResult>> Audit(
            [FromQuery] string? source, [FromQuery] int limit = 5000, [FromQuery] long afterId = 0)
        {
            return Ok(await _audit.AuditAsync(source, limit, afterId));
        }

        /// <summary>Catalog as CSV, for an external reviewer to read.</summary>
        [HttpGet("export")]
        public async Task<IActionResult> Export(
            [FromQuery] string? source, [FromQuery] long? status, [FromQuery] long? foodGroupId,
            [FromQuery] string? search, [FromQuery] int limit = 5000)
        {
            var csv = await _review.ExportCsvAsync(source, status, foodGroupId, search, limit);
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
            var (updated, unchanged) = await _review.SetCurationStatusAsync(request.IngredientIds, request.CurationStatusId, GetCurrentPersonIdRequired());
            return Ok(new { updated, unchanged });
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

        /// <summary>Pending USDA links grouped by target food. source: ai, deterministic, admin or all; verified: true, false or omitted.</summary>
        [HttpGet("proposals/link-groups")]
        public async Task<ActionResult<List<FoodProposalGroupModel>>> GetLinkGroups(
            [FromQuery] string? source = null, [FromQuery] bool? verified = null, [FromQuery] int limit = 200)
        {
            return Ok(await _review.GetLinkGroupsAsync(source, verified, limit));
        }

        /// <summary>Uncurated wordings ranked by the recipes each alone keeps from the auto-approve sweep, with the latest link proposal for each; read-only.</summary>
        [HttpGet("proposals/link-impact")]
        public async Task<ActionResult<IReadOnlyList<LinkImpactRow>>> GetLinkImpact([FromQuery] int limit = 100)
        {
            return Ok(await _links.TopBlockersAsync(limit));
        }

        /// <summary>Applies the pending links that point at one food, up to 500 per call; Remaining says whether to call again.</summary>
        [HttpPost("proposals/link-groups/{targetIngredientId:long}/apply")]
        public async Task<ActionResult<FoodProposalGroupApplyResult>> ApplyLinkGroup(
            [Required] long targetIngredientId, [FromQuery] string? source = null, [FromQuery] bool? verified = null, [FromQuery] int max = 500)
        {
            return Ok(await _review.ApplyLinkGroupAsync(targetIngredientId, source, verified, max, GetCurrentPersonIdRequired()));
        }

        /// <summary>Applies the listed proposals in order; any that no longer apply are skipped.</summary>
        [HttpPost("proposals/apply-batch")]
        public async Task<ActionResult<FoodProposalBatchResult>> ApplyProposals([FromBody] FoodProposalBatchApplyModel request)
        {
            if (request.Ids.Count == 0 || request.Ids.Count > 1000)
                return BadRequest(new { message = "Send between 1 and 1000 proposal ids." });

            return Ok(await _review.ApplyProposalsAsync(request.Ids, GetCurrentPersonIdRequired()));
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
