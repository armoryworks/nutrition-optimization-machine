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
        /// (a link at a position the target already has a line at takes the recipe's next free
        /// position, so both lines keep their own text and amount) and soft-deletes the source.
        /// Refuses and returns false when anything other than recipes references the source, or when
        /// moving any of its lines would lower a recipe's diet flags (<see cref="Nom.Orch.Services.Support.LineMoveDietGuard"/>).
        /// </summary>
        Task<bool> MergeIntoAsync(long sourceId, long targetId, long? personId, bool ignoreProposals = false, long? keepProposalId = null);

        /// <summary>
        /// Whether moving any recipe line of <paramref name="sourceId"/> to <paramref name="targetId"/> would lower its
        /// recipe's diet flags (<see cref="Nom.Orch.Services.Support.LineMoveDietGuard"/>): the target lacks what the
        /// source's lines name, so it is not the same food.
        /// </summary>
        Task<bool> LowersDietAsync(long sourceId, long targetId);

        Task<CatalogNormalizeResult> NormalizeAsync(bool apply);
    }
}
