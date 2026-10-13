using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nom.Orch.Services.Support
{
    /// <summary>A USDA food that a catalog ingredient might be.</summary>
    public sealed record FoodCandidate(long IngredientId, string FdcId, string Name, string DataType);

    /// <summary>
    /// Lexical shortlisting of USDA foods for a free-text ingredient name ("unsalted butter" →
    /// "Butter, without salt"). USDA descriptions lead with the food ("Butter, ...") and qualify
    /// after commas, so the leading segment carries most of the identity. An exact match on that
    /// segment with a single owner is unambiguous; anything else needs a reviewer to choose.
    /// </summary>
    public sealed class FoodNameMatcher
    {
        private static readonly Regex NonWord = new(@"[^a-z0-9 ]+", RegexOptions.Compiled);
        private static readonly HashSet<string> Noise = new(StringComparer.Ordinal)
        {
            "freshly", "chopped", "minced", "diced", "sliced", "large", "small", "medium", "organic",
            "of", "and", "or", "the", "a", "an", "to", "for", "taste", "optional", "finely", "roughly",
            "plain", "pure", "good", "quality",
        };

        private static readonly HashSet<string> ClassPrefixes = new(StringComparer.Ordinal)
        {
            "spice", "oil", "leavening", "agent", "seasoning", "condiment", "sweetener", "nut", "seed", "sauce",
            "vinegar", "salad", "dressing", "cereal", "beverage", "alcoholic", "fish", "crustacean", "mollusk",
        };

        private static readonly HashSet<string> IdentityClasses = new(StringComparer.Ordinal)
        {
            "oil", "sauce", "vinegar", "salad", "dressing", "condiment", "sweetener",
        };

        private static readonly HashSet<string> NeutralQualifiers = new(StringComparer.Ordinal)
        {
            "raw", "fresh", "whole", "ground", "dried", "table", "prepared",
        };

        private static readonly HashSet<string> Disqualifiers = new(StringComparer.Ordinal)
        {
            "non", "imitation", "substitute", "artificial", "dietetic", "reduced", "low", "free", "mix", "flavored", "restaurant",
        };

        private readonly List<(FoodCandidate Food, HashSet<string> Head, HashSet<string> Lead, HashSet<string> All)> _foods;
        private readonly Dictionary<string, List<int>> _byToken = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<int>> _byHeadKey = new(StringComparer.Ordinal);

        public FoodNameMatcher(IEnumerable<FoodCandidate> foods)
        {
            _foods = foods.Select(f =>
            {
                var segments = f.Name.Split(',');
                var first = Tokens(segments[0]);
                var second = segments.Length > 1 ? Tokens(segments[1]) : new HashSet<string>(StringComparer.Ordinal);
                var head = first;
                if (first.Count > 0 && first.All(ClassPrefixes.Contains) && second.Count > 0)
                {
                    head = new HashSet<string>(second, StringComparer.Ordinal);
                    head.UnionWith(first.Where(IdentityClasses.Contains));
                }
                var lead = new HashSet<string>(first, StringComparer.Ordinal);
                lead.UnionWith(second);
                return (f, head, lead, Tokens(f.Name));
            }).ToList();

            for (var i = 0; i < _foods.Count; i++)
            {
                foreach (var t in _foods[i].All)
                {
                    if (!_byToken.TryGetValue(t, out var list)) _byToken[t] = list = new List<int>();
                    list.Add(i);
                }
                var key = Key(_foods[i].Head);
                if (key.Length == 0) continue;
                if (!_byHeadKey.TryGetValue(key, out var owners)) _byHeadKey[key] = owners = new List<int>();
                owners.Add(i);
            }
        }

        /// <summary>The single USDA food whose leading segment is exactly this name, when only one is.</summary>
        public FoodCandidate? Exact(string name)
        {
            var key = Key(Tokens(name));
            if (key.Length == 0 || !_byHeadKey.TryGetValue(key, out var owners) || owners.Count != 1) return null;
            var (food, _, lead, all) = _foods[owners[0]];
            return Qualifies(Tokens(name), lead, all) ? food : null;
        }

        private static bool Qualifies(HashSet<string> tokens, HashSet<string> lead, HashSet<string> all) =>
            lead.All(t => tokens.Contains(t) || NeutralQualifiers.Contains(t) || (ClassPrefixes.Contains(t) && !IdentityClasses.Contains(t)))
            && all.All(t => !Disqualifiers.Contains(t) || tokens.Contains(t));

        /// <summary>
        /// The single USDA food that contains every word of the name and whose identifying words all
        /// appear in the name ("black pepper" → "Spices, pepper, black"), when only one does.
        /// </summary>
        public FoodCandidate? Covering(string name)
        {
            var tokens = Tokens(name);
            if (tokens.Count == 0) return null;

            FoodCandidate? only = null;
            foreach (var t in tokens)
            {
                if (!_byToken.TryGetValue(t, out var hits)) return null;
            }
            var first = tokens.First();
            foreach (var i in _byToken[first])
            {
                var (food, head, lead, all) = _foods[i];
                if (head.Count == 0 || !tokens.IsSubsetOf(all) || !head.IsSubsetOf(tokens) || !Qualifies(tokens, lead, all)) continue;
                if (only != null && only.IngredientId != food.IngredientId) return null;
                only = food;
            }
            return only;
        }

        /// <summary>Up to <paramref name="max"/> plausible USDA foods, best first; empty when nothing shares the name's words.</summary>
        public IReadOnlyList<FoodCandidate> Shortlist(string name, int max = 8)
        {
            var tokens = Tokens(name);
            if (tokens.Count == 0) return Array.Empty<FoodCandidate>();

            var scores = new Dictionary<int, double>();
            foreach (var t in tokens)
            {
                if (!_byToken.TryGetValue(t, out var hits)) continue;
                foreach (var i in hits)
                {
                    var (_, head, lead, all) = _foods[i];
                    if (!head.Overlaps(tokens) && !lead.Overlaps(tokens)) continue;
                    var shared = all.Count(tokens.Contains);
                    var headShared = head.Count(tokens.Contains);
                    var covers = shared == tokens.Count ? 1.0 : 0.0;
                    var score = covers + (2.0 * headShared + shared) / (head.Count + tokens.Count + 0.25 * all.Count);
                    if (!scores.TryGetValue(i, out var best) || score > best) scores[i] = score;
                }
            }

            return scores
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => _foods[kv.Key].Food.Name.Length)
                .Take(max)
                .Select(kv => _foods[kv.Key].Food)
                .ToList();
        }

        private static string Key(HashSet<string> tokens) => string.Join(' ', tokens.OrderBy(t => t, StringComparer.Ordinal));

        private static readonly Dictionary<string, string> Synonyms = new(StringComparer.Ordinal)
        {
            ["unsalted"] = "without salt",
            ["confectioners"] = "powdered",
            ["confectioner"] = "powdered",
            ["icing"] = "powdered",
            ["scallion"] = "green onion",
            ["scallions"] = "green onion",
            ["cilantro"] = "coriander leaves",
            ["garbanzo"] = "chickpea",
            ["aubergine"] = "eggplant",
            ["courgette"] = "zucchini",
        };

        public static HashSet<string> Tokens(string text)
        {
            var words = NonWord.Replace(text.ToLowerInvariant().Replace('-', ' '), " ")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .SelectMany(w => Synonyms.TryGetValue(w, out var mapped) ? mapped.Split(' ') : new[] { w });
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var w in words)
            {
                if (Noise.Contains(w) || w.Any(char.IsDigit)) continue;
                set.Add(Singular(w));
            }
            return set;
        }

        private static string Singular(string w)
        {
            if (w.Length <= 3 || w.EndsWith("ss")) return w;
            if (w.EndsWith("ies") && w.Length > 4) return w[..^3] + "y";
            if (w.EndsWith("oes") || w.EndsWith("ches") || w.EndsWith("shes")) return w[..^2];
            return w.EndsWith('s') ? w[..^1] : w;
        }
    }
}
