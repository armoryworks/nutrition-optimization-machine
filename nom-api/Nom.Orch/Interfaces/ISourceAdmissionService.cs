using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nom.Orch.Models.Recipe;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// The NOM side of recipe sources (docs/architecture/recipe-sources.md): discovery and source
    /// checks through the scraper's budgeted jobs, the single verdict of each check, the one-click
    /// approval of clear candidates, the re-judging of approved sources, probation, the weekly
    /// re-check, the journaled admin actions and their undo, and the list the harvester reads.
    /// No source is ever approved here except by an admin.
    /// </summary>
    public interface ISourceAdmissionService
    {
        /// <summary>One discovery run: a job with the least recently discovered seeds, polled to its end; a lost or failed job is a failed run, stored with what it found.</summary>
        Task<SourceDiscoveryRunModel> RunDiscoveryAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Runs a terms-only check an admin asked for, if one waits; otherwise checks the next due domain (approved sources
        /// first, then requests, then candidates) under the daily cap. Sources and candidates classed forbidden or
        /// permission-needed are never due. Null when none is due, the cap is reached or the scraper is busy.
        /// </summary>
        Task<SourceCheckModel?> RunNextCheckAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Queues a terms-only check (the scraper's sampleSize 0 mode: robots.txt, the homepage and the terms pages, no
        /// recipe page) of an approved web source that is forbidden or needs permission, run by the source lane's next tick;
        /// journaled. Refused while the scraper's terms-only mode is not enabled.
        /// </summary>
        Task<SourceActionResult> RequestTermsCheckAsync(long sourceId, long personId, string? reason);

        /// <summary>Ends the probations that are due: passed, or failed and harvesting paused (journaled, shown).</summary>
        Task<int> WatchAsync(CancellationToken cancellationToken = default);

        Task<SourceReviewModel> ReviewAsync(CancellationToken cancellationToken = default);

        Task<SourceAdmissionStatusModel> StatusAsync(CancellationToken cancellationToken = default);

        /// <summary>The one-click approval: only a candidate whose last check is clear, conclusive, under the current rules and recent enough. The source starts in probation.</summary>
        Task<SourceActionResult> ApproveCandidateAsync(long candidateId, long personId, string? notes);

        Task<SourceActionResult> PauseHarvestAsync(long sourceId, long personId, string? reason);

        Task<SourceActionResult> ResumeHarvestAsync(long sourceId, long personId, string? reason);

        /// <summary>Withholds every recipe from the source's domain; a dry run unless apply.</summary>
        Task<SourceWithholdReport> WithholdRecipesAsync(long sourceId, long personId, string? reason, bool apply);

        /// <summary>Releases the recipes the source's withholding holds; a dry run unless apply.</summary>
        Task<SourceWithholdReport> ReleaseRecipesAsync(long sourceId, long personId, bool apply);

        /// <summary>Records a hand source (PDFs, scanned books) as approved with the licence facts an admin entered; it is never fetched.</summary>
        Task<SourceActionResult> RecordHandSourceAsync(HandSourceRequestModel request, long personId);

        /// <summary>Approved web sources whose harvesting is not paused and whose recipes are not withheld.</summary>
        Task<List<HarvestableSourceModel>> HarvestableAsync(CancellationToken cancellationToken = default);

        Task<List<SourceJournalEntryModel>> JournalAsync(int limit, CancellationToken cancellationToken = default);

        /// <summary>Undoes one journal row of an undoable field while the field still holds the row's new state.</summary>
        Task<SourceActionResult> UndoAsync(long entryId, long personId);
    }
}
