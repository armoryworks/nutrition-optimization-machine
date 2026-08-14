using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Nom.Orch.Interfaces
{
    /// <summary>One recipe's inputs for dish-group suggestion.</summary>
    public record DishGroupCandidate(long RecipeId, string Name, List<string> IngredientNames);

    /// <summary>
    /// Suggests a canonical dish name ("chocolate chip cookies") for recipes.
    /// The default implementation is heuristic (name normalization); an
    /// AI-backed implementation replaces it when a local model is configured.
    /// A null suggestion means "leave unclassified".
    /// </summary>
    public interface IDishGroupSuggester
    {
        Task<List<string?>> SuggestAsync(List<DishGroupCandidate> candidates, CancellationToken cancellationToken = default);
    }
}
