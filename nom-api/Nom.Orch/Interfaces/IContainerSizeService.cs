using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nom.Orch.Models.Curation;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// The container size lane (nutrient.IngredientContainerSize): writing an import run's sizes
    /// with a journal, the hold-out scorer that sets each key active, shadow or paused and runs the
    /// lane on the lane frame (lane container-sizes), the undo, and the Pipeline block.
    /// See docs/architecture/food-catalog-ingestion.md, "Container sizes".
    /// </summary>
    public interface IContainerSizeService
    {
        /// <summary>Writes the proposed sizes: a new key is added in shadow, a changed one replaces the old row, and with <paramref name="retireMissing"/> a key no source gives any more is retired, except a row whose source kind is in <paramref name="unreadSources"/> (a source the run did not read). Every change is journaled under <paramref name="stamp"/>.</summary>
        Task<ContainerSizeWriteReport> WriteAsync(IReadOnlyList<ContainerSizeProposal> proposals, bool dryRun, DateTime stamp, bool retireMissing, long personId, IReadOnlyCollection<string>? unreadSources = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Scores every size against the hold-out of stated sizes, sets each key's status and, with
        /// <paramref name="apply"/>, journals the changes, records the decisions on the lane frame and
        /// evaluates the lane. With <paramref name="proposals"/> it scores those sizes instead of the
        /// stored ones and writes nothing (an import's dry run).
        /// </summary>
        Task<ContainerSizeScoreReport> ScoreAsync(bool apply, long personId, IReadOnlyList<ContainerSizeProposal>? proposals = null, CancellationToken cancellationToken = default);

        /// <summary>Takes back every journaled write, retire and status change since <paramref name="sinceUtc"/>, newest first; a row changed since is skipped. <paramref name="statusOnly"/> limits it to the scorer's status changes that made a key active, so a key the scorer paused or demoted stays so.</summary>
        Task<ContainerSizeUndoReport> UndoSinceAsync(DateTime sinceUtc, bool apply, long personId, bool statusOnly = false, CancellationToken cancellationToken = default);

        Task<ContainerSizeStatusModel> StatusAsync(int keys = 50, CancellationToken cancellationToken = default);
    }
}
