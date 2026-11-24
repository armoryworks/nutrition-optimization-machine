using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Plan;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// How the diet gate's build job reads a recipe's own lines (<see cref="DietGateStoreBuilder"/>): for every pattern
    /// criterion of every restriction type (a name pattern read by <see cref="DietPatternRule"/>, keyed by its pattern and
    /// the type its exceptions are scoped to, or a word pattern) whether a live line's own text names it
    /// (<see cref="DietCriteriaMatcher.LineNames"/>), and which lines read as a drink
    /// (<see cref="DietCriteriaMatcher.LineCounts"/>). A line is read for a rule only when it holds the rule's first word
    /// (a line with a character that a match without case may read as an ASCII letter is read for every rule), so the
    /// reading is the matcher's own. The line-move guard reads a moved line the same way (<see cref="LineMoveDietGuard"/>).
    /// </summary>
    public static class DietLineRules
    {
        /// <summary>One live recipe line as the reading takes it.</summary>
        public sealed record Line(long RecipeId, long IngredientId, int SortOrder, string? RawLine);

        /// <summary>One rule a line is read for: its key, the first word a line must hold (lower case; null when every line is read) and the reading.</summary>
        public sealed class Rule
        {
            internal Rule(string key, string? seed, Func<string, bool> names)
            {
                Key = key;
                Seed = seed;
                Names = names;
            }

            public string Key { get; }

            public string? Seed { get; }

            internal Func<string, bool> Names { get; }
        }

        private static readonly Regex DrinkWord = DietCriteriaMatcher.WordRegex(AlcoholLineContext.DrinkNamePattern);

        /// <summary>The key a criterion's rule is kept under: a name pattern by its pattern and the type its exceptions are scoped to, a word pattern by itself; null for a nutrient criterion.</summary>
        public static string? KeyOf(DietDataCriterion criterion) =>
            criterion.Rule is { } rule ? "like:" + rule.Pattern + "\n" + rule.TypeName
            : criterion.WordPattern != null ? "word:" + criterion.WordPattern
            : null;

        /// <summary>The distinct rules of the pattern criteria.</summary>
        public static List<Rule> Rules(IEnumerable<DietDataCriterion> criteria)
        {
            var result = new Dictionary<string, Rule>(StringComparer.Ordinal);
            foreach (var c in criteria)
            {
                if (KeyOf(c) is not { } key || result.ContainsKey(key)) continue;
                if (c.Rule is { } rule) result[key] = new Rule(key, Seed(rule), rule.IsLineMatch);
                else
                {
                    var criterion = c;
                    result[key] = new Rule(key, null, text => DietCriteriaMatcher.LineNames(criterion, text));
                }
            }
            return result.Values.ToList();
        }

        /// <summary>The rules of every pattern criterion of every restriction type in the database.</summary>
        public static async Task<List<Rule>> RulesAsync(ApplicationDbContext db)
        {
            var typeIds = await db.Set<RestrictionCriterionEntity>().AsNoTracking()
                .Where(c => c.IngredientPattern != null)
                .Select(c => c.RestrictionTypeId)
                .Distinct()
                .ToListAsync();
            return Rules(await DietCriteriaMatcher.CriteriaAsync(db, typeIds));
        }

        internal static string? Seed(DietPatternRule rule)
        {
            var seed = rule.Substring
                ? rule.Pattern.Split('%', '_', '\\').OrderByDescending(p => p.Length).FirstOrDefault()?.ToLowerInvariant()
                : rule.Words.Count > 0 ? rule.Words[0] : null;
            return string.IsNullOrEmpty(seed) || !IsAscii(seed) ? null : seed;
        }

        private static bool IsAscii(string text)
        {
            foreach (var ch in text)
                if (ch > '\u007f') return false;
            return true;
        }

        /// <summary>Whether the text holds a character beyond ASCII that a match without case may read as an ASCII letter (the long s, the Kelvin sign, the Turkish dotted and dotless i, or any whose case maps into ASCII); such a line is read for every rule.</summary>
        internal static bool FoldsToAscii(string text)
        {
            foreach (var ch in text)
            {
                if (ch <= '\u007f') continue;
                if (ch is 'ſ' or 'K' or 'İ' or 'ı' || char.ToLowerInvariant(ch) <= '\u007f' || char.ToUpperInvariant(ch) <= '\u007f') return true;
            }
            return false;
        }

        internal sealed class Matcher
        {
            private readonly (string Seed, Rule[] Rules)[] _seeded;
            private readonly Rule[] _unseeded;
            private readonly Rule[] _all;

            public Matcher(IReadOnlyList<Rule> rules)
            {
                _all = rules.ToArray();
                _seeded = rules.Where(r => r.Seed != null).GroupBy(r => r.Seed!, StringComparer.Ordinal).Select(g => (g.Key, g.ToArray())).ToArray();
                _unseeded = rules.Where(r => r.Seed == null).ToArray();
            }

            public (string[] Rules, DietCriteriaMatcher.RecipeLine[] Drinks) Read(IReadOnlyList<Line> recipe, bool drinks)
            {
                var matched = new HashSet<string>(StringComparer.Ordinal);
                List<DietCriteriaMatcher.RecipeLine>? drinkLines = null;
                foreach (var line in recipe)
                {
                    var text = line.RawLine;
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    if (_all.Length > 0)
                    {
                        if (FoldsToAscii(text))
                        {
                            foreach (var rule in _all)
                                if (!matched.Contains(rule.Key) && rule.Names(text)) matched.Add(rule.Key);
                        }
                        else
                        {
                            var lower = DietPatternRule.Fold(text).ToLowerInvariant();
                            foreach (var (seed, rules) in _seeded)
                            {
                                if (!lower.Contains(seed, StringComparison.Ordinal)) continue;
                                foreach (var rule in rules)
                                    if (!matched.Contains(rule.Key) && rule.Names(text)) matched.Add(rule.Key);
                            }
                            foreach (var rule in _unseeded)
                                if (!matched.Contains(rule.Key) && rule.Names(text)) matched.Add(rule.Key);
                        }
                    }
                    if (drinks && DrinkWord.IsMatch(text) && DietCriteriaMatcher.LineCounts(text))
                        (drinkLines ??= new()).Add(new DietCriteriaMatcher.RecipeLine(line.RecipeId, line.IngredientId, text));
                }
                return (matched.ToArray(), drinkLines?.ToArray() ?? Array.Empty<DietCriteriaMatcher.RecipeLine>());
            }
        }
    }
}
