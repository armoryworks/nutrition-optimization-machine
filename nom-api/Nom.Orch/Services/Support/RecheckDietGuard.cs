using System;
using System.Collections.Generic;
using System.Linq;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// Keeps the link re-check from lowering a recipe's diet flags when it takes lines back to a food of their own wording
    /// (staging checkpoint 4, P19: the prepared set moved "cheese tortellini", "zwieback" and "frozen potstickers" off USDA
    /// foods whose descriptions named pasta, wonton and pork, onto new foods whose names carry no such word, so Celiac
    /// Disease and Wheat Allergy stopped flagging 111 and 128 recipes). A take-back or a move may lower the flag of a
    /// restriction type only when the new food's name or USDA description still names what the type restricts (a composite
    /// food's companion patterns included), or its contents are known by the cited table (<see cref="StatementLabelTerms"/>:
    /// "chicken thighs" is chicken, and "large chicken thighs" with its size words read off) and none of them is a word the
    /// type restricts, or the wording reads as a plain drink (<see cref="ReadsAsDrink"/>: its own name keeps the drink's
    /// flags, staging checkpoint 6, R16). Otherwise the lines stay where they are, on the safe side.
    /// </summary>
    public sealed class RecheckDietGuard
    {
        /// <summary>The reason a held take-back carries, before the types it names.</summary>
        public const string HeldReason = "diet flags kept";

        /// <summary>The guard's version, part of the re-check's signature: raise it when what it holds changes (recheck-diet/2: a plain drink and a wording with size words, staging checkpoint 6, R16).</summary>
        public const string Version = "recheck-diet/2";

        private readonly IReadOnlyList<(string Type, IReadOnlyList<DietDataCriterion> Criteria)> _types;

        private RecheckDietGuard(IReadOnlyList<(string Type, IReadOnlyList<DietDataCriterion> Criteria)> types) => _types = types;

        /// <summary>The guard over the default restriction types (<see cref="DefaultRestrictionCriteria.ByName"/>, companions and composite foods included) and the stored types' own pattern criteria, read together by type name.</summary>
        public static RecheckDietGuard For(IEnumerable<DietDataCriterion>? stored = null)
        {
            var byType = new Dictionary<string, List<DietDataCriterion>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (type, (severity, patterns)) in DefaultRestrictionCriteria.ByName)
                byType[type] = patterns.Select(p => DietCriteriaMatcher.IsWordPattern(p)
                    ? new DietDataCriterion(0, 0, p, null, null, null, severity, null, null, type)
                    : new DietDataCriterion(0, 0, null, null, null, null, severity, null, p, type)).ToList();
            foreach (var c in stored ?? Array.Empty<DietDataCriterion>())
            {
                if (c.TypeName == null || (c.LikePattern == null && c.WordPattern == null)) continue;
                if (!byType.TryGetValue(c.TypeName, out var list)) byType[c.TypeName] = list = new List<DietDataCriterion>();
                list.Add(c);
            }
            return new RecheckDietGuard(byType.Select(kv => (kv.Key, (IReadOnlyList<DietDataCriterion>)kv.Value)).OrderBy(t => t.Key, StringComparer.Ordinal).ToList());
        }

        /// <summary>Whether a wording's contents are known by the cited label table: the wording, or the wording with its qualifiers and forms read off, or with its size words read off ("large chicken thighs"), is a term of it.</summary>
        public static bool Known(string wording) => Terms(wording) != null;

        /// <summary>
        /// Whether a wording or a line reads as a plain drink and nothing else (<see cref="AlcoholLineContext.Read"/>: a
        /// spirit, a wine, a fortified wine, a beer or a cider with a USDA food of its sense, an unclear wine, or a list of
        /// such drinks), so its own words say what it is: "brandy", "rum", "Madeira or sherry", "dry red or white wine"
        /// (staging checkpoint 6, R16). The new food is read by the gate by its own name (a drink name counts for the
        /// drink's types), so a take-back or a move of such a line drops only the flags a wrong food gave it: the cream and
        /// coffee of a coffee cream liqueur, the vinegar of a red wine vinegar. A liqueur, a vermouth or a country wine is
        /// flavoured with what its name may not say ("Grand Marnier" is orange, "Kahlúa" coffee), so its flags are kept;
        /// a vinegar, an extract, a measure, a dish named after a drink and a soft drink are not drinks here. Given the
        /// name of the food the lines leave (<paramref name="offName"/>), a drink that shares a
        /// word with it other than a word every drink of its kind may carry ("cherry brandy" off cherries) may owe its
        /// flags to that word, so it is not read as a drink alone.
        /// </summary>
        public static bool ReadsAsDrink(string? text, string? offName = null)
        {
            if (string.IsNullOrWhiteSpace(text) || !DrinkAlone(AlcoholLineContext.Read(text))) return false;
            return string.IsNullOrWhiteSpace(offName) || !ContentWords(text).Overlaps(ContentWords(offName));
        }

        private static readonly HashSet<string> DrinkKindWords = new(StringComparer.Ordinal)
        {
            "liqueur", "liquor", "wine", "brandy", "cordial", "spirit", "schnapps", "red", "white", "rose", "rosé", "dry", "sweet",
            "dark", "light", "golden", "pale", "good", "best", "old", "fine", "cooking", "and", "the", "with", "glass", "gill",
            "cup", "pint", "quart", "bottle", "tablespoon", "teaspoon", "tbsp", "tsp", "ounce",
        };

        private static HashSet<string> ContentWords(string text) =>
            System.Text.RegularExpressions.Regex.Matches(text.ToLowerInvariant(), @"\p{L}{3,}")
                .Select(m => m.Value.Length > 4 && m.Value.EndsWith('s') ? m.Value[..^1] : m.Value)
                .Where(w => !DrinkKindWords.Contains(w))
                .ToHashSet(StringComparer.Ordinal);

        private static readonly HashSet<string> FlavouredSenses = new(StringComparer.Ordinal) { "coffee-liqueur", "cream-liqueur", "creme-de-menthe" };

        private static bool DrinkAlone(AlcoholReading reading) => reading.Kind switch
        {
            AlcoholSenseKind.Drink => !FlavouredSenses.Contains(reading.Sense),
            AlcoholSenseKind.Unclear => true,
            AlcoholSenseKind.List => reading.Parts is { Count: > 0 } parts && parts.Any(p => p.Kind != AlcoholSenseKind.None)
                && parts.All(p => p.Kind == AlcoholSenseKind.None ? false : DrinkAlone(p)),
            _ => false,
        };

        private static readonly System.Text.RegularExpressions.Regex SizeWords = new(
            @"^(?:(?:extra[\s-]+|very\s+)?(?:large|small|medium|big|jumbo|little)\s+)+", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        private static IReadOnlyList<LabelTerm>? Terms(string wording)
        {
            var terms = StatementLabelTerms.Match(wording);
            if (terms != null) return terms;
            var unsized = SizeWords.Replace(wording.Trim(), string.Empty);
            return unsized.Length > 0 && unsized.Length < wording.Trim().Length ? StatementLabelTerms.Match(unsized) : null;
        }

        /// <summary>
        /// The restriction types that read the lines' recipe as restricted while they sit on <paramref name="food"/> (by its
        /// name, its USDA description in either order, or a line's own text) and would not once the lines sit on a food named
        /// <paramref name="wording"/> (by that name or a line's own text), unless the wording is <see cref="Known"/> and
        /// nothing the table says it is, is made of or may be made of is a word the type restricts: the gate reads a recipe
        /// line by its food's name and composite companions only, so contents it cannot read from the name ("textured
        /// vegetable protein" is soy) never keep a flag.
        /// </summary>
        public IReadOnlyList<string> Lowered(GateFood food, string wording, IEnumerable<string?> lines) =>
            Lowering(food, wording, null, lines);

        /// <summary>
        /// The restriction types a move of the lines from <paramref name="food"/> to the catalog food <paramref name="target"/>
        /// would lower, read as <see cref="Lowered(GateFood, string, IEnumerable{string})"/> with the target's name and its
        /// USDA description in place of the wording; none for a USDA food with its description, whose record the gate reads
        /// as the food's own statement of what it is ("brandy" off a coffee cream liqueur onto the distilled spirit).
        /// </summary>
        public IReadOnlyList<string> Lowered(GateFood food, GateFood target, IEnumerable<string?> lines) =>
            !string.IsNullOrWhiteSpace(target.FdcId) && !string.IsNullOrWhiteSpace(target.Description) ? Array.Empty<string>() : Lowering(food, target.Name, target.Description, lines);

        private IReadOnlyList<string> Lowering(GateFood food, string name, string? description, IEnumerable<string?> lines)
        {
            if (ReadsAsDrink(name, food.Name + " " + food.Description)) return Array.Empty<string>();
            var texts = lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
            var terms = Terms(name);
            var lowered = new List<string>();
            foreach (var (type, criteria) in _types)
            {
                bool Names(string? text) => criteria.Any(c => DietCriteriaMatcher.LineNames(c, text));
                bool Describes(string? text) => criteria.Any(c => DietCriteriaMatcher.LineNames(c, text) && (c.Rule == null || c.Rule.IsDescriptionMatch(text)));
                var before = Names(food.Name) || Describes(food.Description) || texts.Any(Names);
                if (!before || Names(name) || Describes(description) || texts.Any(Names)) continue;
                if (terms != null && !terms.Any(t => new[] { t.Term }.Concat(t.Contains).Concat(t.May).Any(w => Names("1 cup " + w)))) continue;
                lowered.Add(type);
            }
            return lowered;
        }

        /// <summary>The reason a take-back held by the guard carries, naming the types whose flags it keeps.</summary>
        public static string Reason(IReadOnlyCollection<string> types) =>
            HeldReason + " (contents of the wording not known; it would lower " + Types(types) + ")";

        /// <summary>The reason a move held by the guard carries, naming the target and the types whose flags it keeps.</summary>
        public static string Reason(IReadOnlyCollection<string> types, GateFood target) =>
            HeldReason + " (a move to " + target.Name + " would lower " + Types(types) + ")";

        private static string Types(IReadOnlyCollection<string> types) =>
            string.Join(", ", types.Take(5)) + (types.Count > 5 ? $" and {types.Count - 5} more" : string.Empty);
    }
}
