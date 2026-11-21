using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;

namespace Nom.Api.Controllers
{
    /// <summary>
    /// Admin management of the scraping-source whitelist. Scraping is
    /// deny-by-default; approving a domain here is the admin accepting
    /// responsibility for the legality and quality of importing from it.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "CanManageCuration")]
    public class ScrapingSourceController : BaseApiController
    {
        private readonly IScrapingSourceService _scrapingSources;
        private readonly ISourceAdmissionService _admission;
        private readonly ISourceRightsService _rights;
        private readonly ICurrentUserService _currentUser;

        public ScrapingSourceController(
            IScrapingSourceService scrapingSources,
            ISourceAdmissionService admission,
            ISourceRightsService rights,
            ICurrentUserService currentUser)
        {
            _scrapingSources = scrapingSources;
            _admission = admission;
            _rights = rights;
            _currentUser = currentUser;
        }

        /// <summary>List scraping sources, optionally filtered by status (Pending/Approved/Rejected).</summary>
        [HttpGet]
        public async Task<ActionResult<List<ScrapingSourceModel>>> List([FromQuery] ScrapingSourceStatusEnum? status)
        {
            return Ok(await _scrapingSources.ListAsync(status));
        }

        [HttpPost("{id:long}/reject")]
        public async Task<ActionResult<ScrapingSourceModel>> Reject(long id, [FromBody] ScrapingSourceReviewRequestModel? request)
        {
            var result = await _scrapingSources.RejectAsync(id, _currentUser.RequiredPersonId, request?.Notes);
            return result == null ? NotFound() : Ok(result);
        }

        /// <summary>
        /// The admin sources page: the approval queue (clear candidates only, with their facts), the
        /// candidates that are not clear with their reasons, the approved sources each with its
        /// re-check beside it, hand sources, discovery runs, failed checks and the journal.
        /// </summary>
        [HttpGet("review")]
        public async Task<ActionResult<SourceReviewModel>> Review(CancellationToken cancellationToken)
        {
            return Ok(await _admission.ReviewAsync(cancellationToken));
        }

        /// <summary>The one-click approval of a candidate whose last check is clear; 409 with the reason otherwise.</summary>
        [HttpPost("candidates/{id:long}/approve")]
        public async Task<ActionResult<ScrapingSourceModel>> ApproveCandidate(long id, [FromBody] ScrapingSourceReviewRequestModel? request)
        {
            return Answer(await _admission.ApproveCandidateAsync(id, _currentUser.RequiredPersonId, request?.Notes));
        }

        /// <summary>Pauses harvesting of a source (journaled; undone by resume or by undoing the entry).</summary>
        [HttpPost("{id:long}/pause-harvest")]
        public async Task<ActionResult<ScrapingSourceModel>> PauseHarvest(long id, [FromBody] SourceReasonRequestModel? request)
        {
            return Answer(await _admission.PauseHarvestAsync(id, _currentUser.RequiredPersonId, request?.Reason));
        }

        [HttpPost("{id:long}/resume-harvest")]
        public async Task<ActionResult<ScrapingSourceModel>> ResumeHarvest(long id, [FromBody] SourceReasonRequestModel? request)
        {
            return Answer(await _admission.ResumeHarvestAsync(id, _currentUser.RequiredPersonId, request?.Reason));
        }

        /// <summary>Withholds every recipe from the source's domain from all shared pools; a dry run (a count) unless apply=true.</summary>
        [HttpPost("{id:long}/withhold-recipes")]
        public async Task<ActionResult<SourceWithholdReport>> WithholdRecipes(long id, [FromBody] SourceReasonRequestModel? request, [FromQuery] bool apply = false)
        {
            return Answer(await _admission.WithholdRecipesAsync(id, _currentUser.RequiredPersonId, request?.Reason, apply));
        }

        /// <summary>Releases the recipes the source's withholding holds; a dry run unless apply=true.</summary>
        [HttpPost("{id:long}/release-recipes")]
        public async Task<ActionResult<SourceWithholdReport>> ReleaseRecipes(long id, [FromQuery] bool apply = false)
        {
            return Answer(await _admission.ReleaseRecipesAsync(id, _currentUser.RequiredPersonId, apply));
        }

        /// <summary>Records a hand source (PDFs, scanned books) with the licence facts an admin read on the source.</summary>
        [HttpPost("hand")]
        public async Task<ActionResult<ScrapingSourceModel>> RecordHand([FromBody] HandSourceRequestModel request)
        {
            return Answer(await _admission.RecordHandSourceAsync(request, _currentUser.RequiredPersonId));
        }

        /// <summary>The domains the external harvester may harvest now: approved web sources, not paused, recipes not withheld; with the probation limit and crawl-delay.</summary>
        [HttpGet("harvestable")]
        public async Task<ActionResult<List<HarvestableSourceModel>>> Harvestable(CancellationToken cancellationToken)
        {
            return Ok(await _admission.HarvestableAsync(cancellationToken));
        }

        [HttpGet("journal")]
        public async Task<ActionResult<List<SourceJournalEntryModel>>> Journal([FromQuery] int limit = 100, CancellationToken cancellationToken = default)
        {
            return Ok(await _admission.JournalAsync(limit, cancellationToken));
        }

        /// <summary>Undoes one journal entry (approval, harvest pause or resume, withholding or release) while the source still holds its new state.</summary>
        [HttpPost("journal/{entryId:long}/undo")]
        public async Task<ActionResult<ScrapingSourceModel>> Undo(long entryId)
        {
            return Answer(await _admission.UndoAsync(entryId, _currentUser.RequiredPersonId));
        }

        /// <summary>Derives every web source's rights class and AI flag from its stored report (or one source's); a dry run unless apply=true.</summary>
        [HttpPost("rights/derive")]
        public async Task<ActionResult<SourceRightsRunModel>> DeriveRights([FromQuery] bool apply = false, [FromQuery] long? sourceId = null, CancellationToken cancellationToken = default)
        {
            return Ok(await _rights.DeriveAsync(apply, sourceId, staleOnly: false, cancellationToken));
        }

        /// <summary>Records the publisher's written permission (who, when, the text); it lifts permission-needed or forbidden and is undone from the journal.</summary>
        [HttpPost("{id:long}/permission")]
        public async Task<ActionResult<ScrapingSourceModel>> RecordPermission(long id, [FromBody] SourcePermissionRequestModel request)
        {
            return Answer(await _rights.RecordPermissionAsync(id, request, _currentUser.RequiredPersonId));
        }

        /// <summary>Approves (approve=true) or withdraws facts-only use of a facts-only source; journaled.</summary>
        [HttpPost("{id:long}/facts-only-use")]
        public async Task<ActionResult<ScrapingSourceModel>> FactsOnlyUse(long id, [FromBody] SourceReasonRequestModel? request, [FromQuery] bool approve = true)
        {
            return Answer(await _rights.SetFactsOnlyUseAsync(id, approve, _currentUser.RequiredPersonId, request?.Reason));
        }

        /// <summary>Reduces distinctive titles of facts-only recipes to the dish name; a dry run unless apply=true.</summary>
        [HttpPost("rights/titles")]
        public async Task<ActionResult<SourceRecipeStepModel>> Titles([FromQuery] bool apply = false, [FromQuery] long? sourceId = null, CancellationToken cancellationToken = default)
        {
            return Ok(await _rights.TitlesAsync(apply, sourceId, cancellationToken));
        }

        /// <summary>Stores the third-party credit lines found in recipes' stored source text; a dry run unless apply=true.</summary>
        [HttpPost("rights/credits")]
        public async Task<ActionResult<SourceRecipeStepModel>> Credits([FromQuery] bool apply = false, [FromQuery] long? sourceId = null, CancellationToken cancellationToken = default)
        {
            return Ok(await _rights.CreditsAsync(apply, sourceId, cancellationToken));
        }

        /// <summary>Reduces facts-only recipes' stored source prose to what a rebuild needs; a dry run unless apply=true.</summary>
        [HttpPost("rights/retention")]
        public async Task<ActionResult<SourceRecipeStepModel>> Retention([FromQuery] bool apply = false, CancellationToken cancellationToken = default)
        {
            return Ok(await _rights.RetentionAsync(apply, cancellationToken));
        }

        /// <summary>Deletes for good the source documents a reduction held past the undo window; a dry run unless apply=true.</summary>
        [HttpPost("rights/retention/purge")]
        public async Task<ActionResult<SourceRecipeStepModel>> PurgeRetention([FromQuery] bool apply = false, CancellationToken cancellationToken = default)
        {
            return Ok(await _rights.PurgeRetentionAsync(apply, cancellationToken));
        }

        /// <summary>Undoes one run of titles, credits, retention or facts-only withholding by its stamp; a dry run unless apply=true.</summary>
        [HttpPost("rights/undo")]
        public async Task<ActionResult<SourceRecipeStepModel>> UndoStep([FromQuery] string step, [FromQuery] DateTime stamp, [FromQuery] bool apply = false, CancellationToken cancellationToken = default)
        {
            var result = await _rights.UndoStepAsync(step, stamp.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(stamp, DateTimeKind.Utc) : stamp.ToUniversalTime(), apply, cancellationToken);
            return result.Refused != null ? BadRequest(new { error = result.Refused }) : Ok(result);
        }

        /// <summary>The recipes already taken from each forbidden or permission-needed source with no permission recorded (a count for the owner's decision; nothing is changed).</summary>
        [HttpGet("rights/taken")]
        public async Task<ActionResult<List<SourceTakenModel>>> Taken(CancellationToken cancellationToken)
        {
            return Ok(await _rights.TakenAsync(cancellationToken));
        }

        [HttpGet("rights/status")]
        public async Task<ActionResult<SourceRightsStatusModel>> RightsStatus(CancellationToken cancellationToken)
        {
            return Ok(await _rights.StatusAsync(cancellationToken));
        }

        private ActionResult<ScrapingSourceModel> Answer(SourceActionResult result) =>
            result.Ok ? Ok(result.Source) : result.NotFound ? NotFound(new { error = result.Error }) : Conflict(new { error = result.Error });

        private ActionResult<SourceWithholdReport> Answer(SourceWithholdReport report) =>
            report.NotFound ? NotFound(new { error = report.Error }) : report.Error != null ? Conflict(new { error = report.Error }) : Ok(report);
    }
}
