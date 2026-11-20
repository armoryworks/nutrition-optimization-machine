using System.Threading;
using System.Threading.Tasks;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services.Support;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// The stock-phrase index of the prose check: the five-word phrases at least
    /// <see cref="ProseStockIndex.StockSites"/> recipe sites use, built deterministically from the
    /// stored source documents and versioned, so the check can leave stock wording out of its copy
    /// measures and a change of the index can be told from a change of the check.
    /// </summary>
    public interface IProseStockService
    {
        /// <summary>
        /// Reads every stored source document, counts the sites that use each phrase of its
        /// description and method, and with <paramref name="apply"/> replaces the index with the
        /// phrases of <see cref="ProseStockIndex.StockSites"/> or more sites under the next version,
        /// journaled as one <c>ProseStockRebuild</c> entry. An index that would not change is not
        /// written. Without <paramref name="apply"/> nothing is written and the result is what the
        /// same call would do.
        /// </summary>
        Task<ProseStockRebuildResult> RebuildAsync(bool apply = false, CancellationToken cancellationToken = default);

        /// <summary>The current index, or <see cref="ProseStockIndex.Empty"/> when none has been built.</summary>
        Task<ProseStockIndex> LoadAsync(CancellationToken cancellationToken = default);
    }
}
