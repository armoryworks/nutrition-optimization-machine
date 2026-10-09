using System.Threading.Tasks;
using Nom.Orch.Models.Curation;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// Admin cleanup of import residue in the ingredient catalog: rows whose
    /// name still carries quantity/unit/prep noise ("1 can black beans") are
    /// merged into their canonical row or renamed in place, with recipe links
    /// re-pointed. Deterministic name rules only — no nutrition is authored.
    /// </summary>
    public interface ICatalogCleanupService
    {
        Task<CatalogCleanupPreview> PreviewAsync(int sampleSize = 50);
        Task<CatalogCleanupResult> ApplyAsync(int maxActions = 500);

        /// <summary>
        /// Moves every recipe link from <paramref name="sourceId"/> to <paramref name="targetId"/>
        /// (folding duplicates into the target's raw line) and soft-deletes the source. Refuses and
        /// returns false when anything other than recipes references the source.
        /// </summary>
        Task<bool> MergeIntoAsync(long sourceId, long targetId, long? personId, bool ignoreProposals = false);
    }
}
