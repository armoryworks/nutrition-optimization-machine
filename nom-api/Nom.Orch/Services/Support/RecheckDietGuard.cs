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
    /// food's companion patterns included), or, for a diet of choice, its contents are known by the cited table
    /// (<see cref="StatementLabelTerms"/>: "chicken thighs" is chicken, and "large chicken thighs" with its size words read
    /// off) and none of them is a word the type restricts, or the wording reads as a plain drink (<see cref="ReadsAsDrink"/>:
    /// its own name keeps the drink's flags, staging checkpoint 6, R16), or the type is a diet of choice whose whole
    /// restriction a free-of marker of the new name's first item states it lacks (<see cref="MarkedFree"/>: "vegan butter"
    /// for Vegan and Dairy-Free). Otherwise the lines are not left on a food the rules prove wrong: they go back to a wording
    /// food marked of unknown contents (<see cref="UnknownContentsContext"/>), which every severe type holds as unknown
    /// (staging checkpoint 7, S15: 9 of 20 holds kept only the wrong food's flags, shaoxing wine on red wine vinegar).
    /// </summary>
    public sealed class RecheckDietGuard
    {
        /// <summary>The reason a held take-back carries, before the types it names.</summary>
        public const string HeldReason = "diet flags kept";

        /// <summary>The guard's version, part of the re-check's signature: raise it when what it holds changes (recheck-diet/2: a plain drink and a wording with size words, staging checkpoint 6, R16; recheck-diet/3: a free-of marker for the diets of choice it covers; recheck-diet/4: a take-back it would hold goes to a wording food of unknown contents instead, and a severe flag the gate reads from a food's data and not its name is never lowered, staging checkpoint 7, S11 and S15).</summary>
        public const string Version = "recheck-diet/4";

        /// <summary>
        /// The context of the alias that marks a wording food's contents not known (the alias is the food's own name): the diet
        /// gate decides it unknown for every restriction set its name does not flag, so a set that holds unknowns (a severe
        /// type, or a member at severity 4 or 5) holds every recipe on it (staging checkpoint 7, S15).
        /// </summary>
        public const string UnknownContentsContext = "deterministic:recheck:contents-unknown";

        /// <summary>The reason a take-back to a wording food of unknown contents carries, before the types it names.</summary>
        public const string UnknownReason = "contents unknown";

        /// <summary>The lowest severity of a severe type (allergy, medical, intolerance); a type all of whose criteria sit below it is a diet of choice.</summary>
        public const int SevereFrom = 4;

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
        /// <paramref name="wording"/> (by that name or a line's own text), unless the type is no severe type (every criterion
        /// below <see cref="SevereFrom"/>), the wording is <see cref="Known"/> and nothing the table says it is, is made of or
        /// may be made of is a word the type restricts (a dish name never accounts for an allergen the food it leaves named,
        /// staging checkpoint 7, S11: "spanish rice" off the USDA mix made with margarine lowered Soy Allergy): the gate reads a recipe
        /// line by its food's name and composite companions only, so contents it cannot read from the name ("textured
        /// vegetable protein" is soy) never keep a flag; nor is a diet of choice the wording's free-of marker covers
        /// (<see cref="MarkedFree"/>).
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

        /// <summary>
        /// The diets of choice whose flag a free-of marker in the first item of <paramref name="wording"/> accounts for
        /// (<see cref="LinkRecheckRules.FreeMarkedTypes"/>): every type whose criteria all sit below <see cref="SevereFrom"/>
        /// and whose every pattern is a pattern of a type the marker names, so the wording states the food lacks all the
        /// type restricts ("vegan butter": Vegan, Dairy-Free; "gluten-free flour": Gluten-Free; "meatless sausage":
        /// Vegetarian). An allergy, Celiac Disease and every other severe type is never among them, nor a diet that also
        /// restricts what the marker does not name (Grain-Free under "gluten-free", Sugar-Free under "vegan").
        /// </summary>
        public IReadOnlyList<string> MarkedFree(string? wording)
        {
            var named = LinkRecheckRules.FreeMarkedTypes(wording);
            if (named.Count == 0) return Array.Empty<string>();
            return _marked.GetOrAdd(string.Join("\n", named.OrderBy(n => n, StringComparer.OrdinalIgnoreCase)), _ => Covered(named));
        }

        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, IReadOnlyList<string>> _marked = new(StringComparer.OrdinalIgnoreCase);

        private IReadOnlyList<string> Covered(IReadOnlyList<string> named)
        {
            var marker = _types.Where(t => named.Contains(t.Type, StringComparer.OrdinalIgnoreCase)).SelectMany(t => t.Criteria).ToList();
            var lacks = marker.Select(PatternOf).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
            bool Lacks(DietDataCriterion c) => PatternOf(c) is { } p
                && (lacks.Contains(p) || !DietCriteriaMatcher.IsWordPattern(p) && marker.Any(m => DietCriteriaMatcher.LineNames(m, "1 cup " + p.Trim('%'))));
            return _types.Where(t => t.Criteria.Count > 0 && t.Criteria.All(c => c.Severity < SevereFrom) && t.Criteria.All(Lacks))
                .Select(t => t.Type)
                .ToList();
        }

        private static string? PatternOf(DietDataCriterion criterion) => (criterion.LikePattern ?? criterion.WordPattern)?.Trim();

        private IReadOnlyList<string> Lowering(GateFood food, string name, string? description, IEnumerable<string?> lines)
        {
            if (ReadsAsDrink(name, food.Name + " " + food.Description)) return Array.Empty<string>();
            var texts = lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
            var terms = Terms(name);
            var marked = MarkedFree(name);
            var lowered = new List<string>();
            foreach (var (type, criteria) in _types)
            {
                bool Names(string? text) => criteria.Any(c => DietCriteriaMatcher.LineNames(c, text));
                bool Describes(string? text) => criteria.Any(c => DietCriteriaMatcher.LineNames(c, text) && (c.Rule == null || c.Rule.IsDescriptionMatch(text)));
                var before = Names(food.Name) || Describes(food.Description) || texts.Any(Names);
                if (!before || Names(name) || Describes(description) || texts.Any(Names)) continue;
                if (terms != null && criteria.All(c => c.Severity < SevereFrom) && !terms.Any(t => new[] { t.Term }.Concat(t.Contains).Concat(t.May).Any(w => Names("1 cup " + w)))) continue;
                if (marked.Contains(type, StringComparer.OrdinalIgnoreCase)) continue;
                lowered.Add(type);
            }
            return lowered;
        }

        /// <summary>
        /// Whether the guard reads <paramref name="type"/> on <paramref name="food"/> where the lines sit, by its name, its USDA
        /// description or a line's own text; a type the gate counts for the food that this does not read comes from the food's
        /// data (a read statement, a nutrient, a category), which no wording's words can account for.
        /// </summary>
        public bool Reads(GateFood food, string type, IEnumerable<string?> lines)
        {
            var criteria = _types.Where(t => string.Equals(t.Type, type, StringComparison.OrdinalIgnoreCase)).SelectMany(t => t.Criteria).ToList();
            bool Names(string? text) => criteria.Any(c => DietCriteriaMatcher.LineNames(c, text));
            bool Describes(string? text) => criteria.Any(c => DietCriteriaMatcher.LineNames(c, text) && (c.Rule == null || c.Rule.IsDescriptionMatch(text)));
            return Names(food.Name) || Describes(food.Description) || lines.Any(Names);
        }

        /// <summary>The reason a take-back to a wording food of unknown contents carries, naming the types it would lower (<see cref="UnknownContentsContext"/>).</summary>
        public static string ReasonUnknown(IReadOnlyCollection<string> types) =>
            UnknownReason + " (it would lower " + Types(types) + "; the severe types hold it as unknown)";

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
