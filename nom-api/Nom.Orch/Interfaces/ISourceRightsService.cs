using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nom.Orch.Models.Recipe;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// The rights of a web source as an engine property (docs/architecture/source-rights.md): the class
    /// and AI reservation derived by rule from the stored source-check report, the written permissions and
    /// facts-only approvals an admin records, the stamps the source's recipes carry, and the recipe steps
    /// that follow from a facts-only class (titles, credit lines, prose retention). Every change is
    /// journaled and can be undone; every step has a dry run. Nothing here deletes a recipe: the recipes already
    /// taken from a forbidden source are removed by <see cref="IRecipeRemovalService"/>.
    /// </summary>
    public interface ISourceRightsService
    {
        /// <summary>Derives every web source's class and AI flag from its last stored report (or one source's, or only those derived under another rule or report); a dry run unless apply.</summary>
        Task<SourceRightsRunModel> DeriveAsync(bool apply, long? sourceId = null, bool staleOnly = false, CancellationToken cancellationToken = default);

        /// <summary>Records the publisher's written permission (who, when, the text); journaled, it lifts permission-needed or forbidden for the source.</summary>
        Task<SourceActionResult> RecordPermissionAsync(long sourceId, SourcePermissionRequestModel request, long personId);

        /// <summary>
        /// Records a rights class by hand for a host (a source's, a candidate's, or one NOM has no row for) with the site's own
        /// deciding words, their page and the date read; a dry run unless apply. Journaled and undone from the journal; the
        /// source lane never overwrites it, and a forbidden or permission-needed host is never fetched or checked.
        /// </summary>
        Task<SourceRightsByHandResultModel> RecordByHandAsync(SourceRightsByHandRequestModel request, bool apply, long personId, CancellationToken cancellationToken = default);

        /// <summary>The classes recorded by hand that stand now.</summary>
        Task<List<SourceRightsRecordModel>> RecordsAsync(CancellationToken cancellationToken = default);

        /// <summary>Approves (or withdraws) facts-only use of a facts-only source, so it may be fetched for facts and a link; journaled.</summary>
        Task<SourceActionResult> SetFactsOnlyUseAsync(long sourceId, bool approve, long personId, string? reason);

        /// <summary>Reduces distinctive titles of facts-only recipes to the dish name by rule; a dry run unless apply.</summary>
        Task<SourceRecipeStepModel> TitlesAsync(bool apply, long? sourceId = null, CancellationToken cancellationToken = default);

        /// <summary>Stores third-party credit lines found in the stored source text of recipes that have none; a dry run unless apply.</summary>
        Task<SourceRecipeStepModel> CreditsAsync(bool apply, long? sourceId = null, CancellationToken cancellationToken = default);

        /// <summary>Reduces the stored source documents of facts-only recipes whose rewrite stands or was given up to what a rebuild needs; a dry run unless apply.</summary>
        Task<SourceRecipeStepModel> RetentionAsync(bool apply, CancellationToken cancellationToken = default);

        /// <summary>Deletes for good the documents a reduction held past the undo window; a dry run unless apply.</summary>
        Task<SourceRecipeStepModel> PurgeRetentionAsync(bool apply, CancellationToken cancellationToken = default);

        /// <summary>Undoes one run of a recipe step by its stamp (<c>titles</c>, <c>credits</c>, <c>retention</c>, <c>facts-only</c>); a dry run unless apply.</summary>
        Task<SourceRecipeStepModel> UndoStepAsync(string step, DateTime stamp, bool apply, CancellationToken cancellationToken = default);

        /// <summary>The dry run for the owner: recipes already taken from each forbidden or permission-needed source with no permission recorded, once per host (a bare and a www row of one site count its recipes once).</summary>
        Task<List<SourceTakenModel>> TakenAsync(CancellationToken cancellationToken = default);

        Task<SourceRightsStatusModel> StatusAsync(CancellationToken cancellationToken = default);
    }
}
