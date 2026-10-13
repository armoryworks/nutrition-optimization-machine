using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Nom.Orch.Interfaces
{
    /// <summary>A recipe as the tagging model sees it.</summary>
    public sealed record ToolTagCandidate(long RecipeId, string Name, string StepText);

    /// <summary>Equipment the model named that isn't a known tool key.</summary>
    public sealed record ProposedTool(string Name, string? Category);

    /// <summary>What the model says one recipe needs.</summary>
    public sealed record ToolTagResult(long RecipeId, IReadOnlyList<string> KnownToolKeys, IReadOnlyList<ProposedTool> Other);

    /// <summary>
    /// Classifies the equipment recipes need against the kitchen-tool vocabulary and names
    /// uncommon equipment outside it. Throws when the model is unavailable or answers badly,
    /// so the caller can leave those recipes untagged and retry later.
    /// </summary>
    public interface IRecipeToolTagger
    {
        bool IsConfigured { get; }
        string ModelName { get; }

        /// <param name="knownTools">key → display name for every tool the model may pick.</param>
        Task<IReadOnlyList<ToolTagResult>> TagAsync(
            IReadOnlyList<ToolTagCandidate> recipes,
            IReadOnlyDictionary<string, string> knownTools,
            CancellationToken cancellationToken = default);
    }
}
