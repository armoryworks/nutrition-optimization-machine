using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Nom.Orch.Interfaces
{
    /// <summary>A recipe wording and the one USDA description a pick links it to.</summary>
    public sealed record LinkPair(long IngredientId, string Wording, string Description);

    /// <summary>What a judge says of a pair. Only <see cref="Same"/> verifies; <see cref="Unsure"/> is an abstention.</summary>
    public enum LinkJudgement
    {
        Same,
        VarietyOrForm,
        DifferentFood,
        MoreThanOneFood,
        Unsure,
    }

    /// <summary>The judgement a second opinion gave one pair.</summary>
    public sealed record LinkVerdict(long IngredientId, LinkJudgement Judgement);

    /// <summary>
    /// A second opinion on ingredient → USDA links from a different model than the picker: it is
    /// shown the wording and the one USDA description the pick chose and says whether they are the
    /// same food, or abstains. It never names a food or supplies a value. A pair it does not answer
    /// readably is left out of the result. Throws when the model is unreachable or the whole answer
    /// can't be read.
    /// </summary>
    public interface IIngredientLinkVerifier
    {
        /// <summary>True only when both a model host and a judge model are configured; no model is assumed.</summary>
        bool IsConfigured { get; }
        string ModelName { get; }
        int PromptVersion { get; }
        Task<IReadOnlyList<LinkVerdict>> JudgeAsync(IReadOnlyList<LinkPair> pairs, CancellationToken cancellationToken = default);
    }
}
