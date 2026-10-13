using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nom.Orch.Services.Support;

namespace Nom.Orch.Interfaces
{
    /// <summary>The candidate a second opinion names for an ingredient (null = none of them).</summary>
    public sealed record LinkVerdict(long IngredientId, FoodCandidate? Choice);

    /// <summary>
    /// A second, independently worded opinion on which shortlisted USDA food an ingredient name
    /// refers to. It is never shown the first pick and chooses only among the candidates given —
    /// it never names a food or supplies a value. Throws when the model is unreachable or answers
    /// in a shape that can't be read.
    /// </summary>
    public interface IIngredientLinkVerifier
    {
        bool IsConfigured { get; }
        string ModelName { get; }
        Task<IReadOnlyList<LinkVerdict>> ChooseAsync(IReadOnlyList<LinkQuestion> questions, CancellationToken cancellationToken = default);
    }
}
