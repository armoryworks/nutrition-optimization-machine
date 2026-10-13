using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nom.Orch.Services.Support;

namespace Nom.Orch.Interfaces
{
    /// <summary>An ingredient name and the USDA foods it might be.</summary>
    public sealed record LinkQuestion(long IngredientId, string Name, IReadOnlyList<FoodCandidate> Candidates);

    /// <summary>The chosen candidate (null = none of them) and how sure the matcher is.</summary>
    public sealed record LinkAnswer(long IngredientId, FoodCandidate? Choice, decimal Confidence);

    /// <summary>
    /// Picks which shortlisted USDA food an ingredient name refers to, or none. Chooses only among
    /// the candidates given — it never names a food or supplies a value. Throws when the model is
    /// unreachable or answers in a shape that can't be read.
    /// </summary>
    public interface IIngredientLinkMatcher
    {
        bool IsConfigured { get; }
        string ModelName { get; }
        Task<IReadOnlyList<LinkAnswer>> ChooseAsync(IReadOnlyList<LinkQuestion> questions, CancellationToken cancellationToken = default);
    }
}
