using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Nom.Orch.Interfaces
{
    /// <summary>A catalog ingredient that recipes use but that isn't linked to a USDA food yet.</summary>
    public sealed record LinkSource(long IngredientId, string Name, int Uses);

    public sealed record IngredientLinkBatchResult(int Seen, int ExactProposals, int AiProposals, int NoMatch);

    /// <summary>
    /// Proposes links from catalog ingredients (mostly names minted by recipe imports) to USDA
    /// foods, most-used first. Unambiguous name matches are proposed deterministically; the rest
    /// go to the local model to choose among shortlisted USDA rows. Every link is an admin-reviewed
    /// FoodCatalogProposal (field "fdc_link"); nothing changes until one is approved.
    /// </summary>
    public interface IIngredientLinkService
    {
        Task<IReadOnlyList<LinkSource>> NextSourcesAsync(int count, CancellationToken cancellationToken = default);

        /// <summary>Null when the model is needed but unreachable; those sources stay unproposed.</summary>
        Task<IngredientLinkBatchResult?> ProposeAsync(IReadOnlyList<LinkSource> sources, CancellationToken cancellationToken = default);

        /// <summary>Re-points pending model-chosen links for bare staple names to their standard USDA entry; returns how many changed.</summary>
        Task<int> ApplyStapleDefaultsToPendingAsync(CancellationToken cancellationToken = default);
    }
}
