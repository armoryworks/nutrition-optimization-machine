using System.Collections.Generic;
using System.Linq;
using Nom.Data.Reference;

namespace Nom.Orch.Services.Support
{
    /// <summary>Overall fit between a recipe's tool needs and a household's kitchen, best to worst.</summary>
    public enum KitchenToolFit
    {
        Ready = 0,
        Substitute = 1,
        Harder = 2,
        Missing = 3,
    }

    /// <summary>One tool a recipe needs and how the household covers it.</summary>
    public sealed record KitchenToolNeed(long ToolId, string ToolName, KitchenToolFit Fit, long? UsingToolId, string? Note);

    /// <summary>The fit verdict for a recipe plus a note for every tool that isn't simply on hand.</summary>
    public sealed record KitchenToolCheck(KitchenToolFit Fit, IReadOnlyList<KitchenToolNeed> Needs)
    {
        public IEnumerable<string> Notes => Needs.Where(n => n.Note != null).Select(n => n.Note!);
    }

    /// <summary>
    /// Decides whether a household can cook a recipe with what's in its kitchen. A needed tool
    /// is covered when owned, otherwise by the first owned alternative in catalog order; with no
    /// owned alternative the recipe is <see cref="KitchenToolFit.Missing"/> that tool.
    /// </summary>
    public static class KitchenToolEvaluator
    {
        /// <summary>
        /// Tools owned once the household's explicit answers are laid over the catalog defaults.
        /// </summary>
        public static IReadOnlySet<long> ResolveOwned(IReadOnlyDictionary<long, bool> explicitAnswers)
        {
            var owned = new HashSet<long>();
            foreach (var tool in KitchenToolCatalog.All)
            {
                var has = explicitAnswers.TryGetValue(tool.Id, out var answer) ? answer : tool.OwnedByDefault;
                if (has) owned.Add(tool.Id);
            }
            return owned;
        }

        /// <summary>
        /// Tools a recipe needs: its explicit tool list when it has one, otherwise what its name
        /// and steps mention.
        /// </summary>
        public static IReadOnlySet<long> RequiredTools(IEnumerable<long>? explicitToolIds, string? name, IEnumerable<string?> stepTexts)
        {
            var known = explicitToolIds?.Where(KitchenToolCatalog.ById.ContainsKey).ToHashSet();
            if (known is { Count: > 0 }) return known;
            return KitchenToolCatalog.Detect(name, stepTexts);
        }

        public static KitchenToolCheck Evaluate(IEnumerable<long> requiredToolIds, IReadOnlySet<long> owned)
        {
            var needs = new List<KitchenToolNeed>();
            foreach (var id in requiredToolIds.Distinct().OrderBy(i => i))
            {
                if (!KitchenToolCatalog.ById.TryGetValue(id, out var tool)) continue;

                if (owned.Contains(id))
                {
                    needs.Add(new KitchenToolNeed(id, tool.Name, KitchenToolFit.Ready, id, null));
                    continue;
                }

                var alt = tool.Alternatives.FirstOrDefault(a => owned.Contains(a.ToolId));
                if (alt != null)
                {
                    var fit = alt.Difficulty == KitchenToolSubstitution.Easy ? KitchenToolFit.Substitute : KitchenToolFit.Harder;
                    needs.Add(new KitchenToolNeed(id, tool.Name, fit, alt.ToolId, alt.Note));
                    continue;
                }

                var lower = tool.Name.ToLowerInvariant();
                var article = "aeiou".Contains(lower[0]) ? "an" : "a";
                needs.Add(new KitchenToolNeed(id, tool.Name, KitchenToolFit.Missing, null,
                    $"This will be hard without {article} {lower}."));
            }

            var overall = needs.Count == 0 ? KitchenToolFit.Ready : needs.Max(n => n.Fit);
            return new KitchenToolCheck(overall, needs);
        }
    }
}
