using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Nom.Orch.Services;

namespace Nom.Orch.Services.Support
{
    /// <summary>Where a name pattern's term may begin: anywhere a word or a compound part begins (<c>%egg%</c>), only where a word begins (<c>% oat%</c>), or only at the start of the text (<c>oat%</c>).</summary>
    public enum DietPatternStart { Open, Word, Text }

    /// <summary>Where a name pattern's term may end: anywhere (<c>%egg%</c>), at the end of a word (<c>%oat %</c>), or at the end of the text (<c>%ham</c>).</summary>
    public enum DietPatternEnd { Open, Word, Text }

    /// <summary>Which regular expression dialect a pattern is written for: .NET in memory, or PostgreSQL's advanced expressions (<c>~*</c>, <c>regexp_replace</c>) in a probe.</summary>
    public enum DietRegexDialect { DotNet, Postgres }

    /// <summary>A compound food that holds a pattern's term but is not the food the term names, with the reason it is not, and the restriction types it is read for (every type when null).</summary>
    public sealed record DietPatternException(string Term, string Phrase, string Reason, IReadOnlyList<string>? Types = null)
    {
        /// <summary>Whether the exception is read for a pattern of the named type: an exception scoped to types only for those types, so a pattern with no type reads none of them.</summary>
        public bool AppliesTo(string? typeName) =>
            Types == null || (typeName != null && Types.Contains(typeName.Trim(), StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The diet gate's reading of a name pattern with <c>%</c> (<c>%ham%</c>, <c>%egg%</c>) in a food's
    /// name, an alias or a recipe line (staging checkpoint 2 and the wave 13 report: crude ILIKE
    /// patterns hit "graham", "eggplant", "peppercorn", "licorice", "goat"). The pattern's term is read
    /// by words, not letters:
    /// - the term counts where it begins a word, whatever follows (plurals, stems and compounds:
    ///   "eggs", "anchovies", "buttermilk", "cornmeal");
    /// - inside a word it counts only as a whole part of a compound: at least three letters before it,
    ///   and after it nothing, an inflection (s, es, ed, ing) or at least three letters ("shortbread",
    ///   "popcorn", "horseradish"), so "goat" is not oat, "spear" not pear, "slicing" not icing,
    ///   "avocado" not cod and "chocolate" not cola;
    /// - a term followed by "less" or "free" ("eggless", "peanut-free", "flourless") says the food is
    ///   absent and does not count, but "free-range", "free range" and "free-run" describe the food
    ///   and do, and so does "free from" or "free of" something else ("bread free from crust", "fish
    ///   free of bones": staging checkpoint 4, P12), since "free" is then no label of the food itself;
    /// - a compound food that holds the term but is not its food is a named exception with a cited
    ///   reason (<see cref="Exceptions"/>): "graham" is not ham, "buckwheat" not wheat, "peanut butter"
    ///   not dairy, "coconut milk" not milk. A compound such as "coconut milk" stays restricted where a
    ///   type's own pattern names it (Coconut Allergy's <c>%coconut%</c>);
    /// - words of a term are matched across any run of spaces or punctuation ("brazil-nut"); words of
    ///   an exception only across spaces or hyphens, so a list ("rice, flour") is never a compound.
    /// A pattern that asks for a substring on purpose keeps the ILIKE reading: one that starts or ends
    /// with <c>%%</c> (<c>%%nut%%</c>, which ILIKE reads as <c>%nut%</c>), or one with <c>_</c> or a
    /// <c>%</c> inside its term. The rule only ever reads fewer texts than ILIKE did, beside the
    /// separators. The same expression, in either dialect, serves the in-memory reading and the
    /// whole-catalog probe: the exceptions are blanked out of the text first, then the term is sought.
    /// </summary>
    public sealed class DietPatternRule
    {
        /// <summary>The version of the rule and its exceptions, part of every cache key that holds a reading.</summary>
        public const string Version = "diet-patterns/5";

        /// <summary>The fewest letters that must stand before a term inside a word for the term to be a compound's part.</summary>
        public const int CompoundPart = 3;

        /// <summary>The endings a term inside a word may take and still end the word.</summary>
        public const string Inflection = "(?:s|es|ed|ing)";

        private static readonly ConcurrentDictionary<string, DietPatternRule> Parsed = new(StringComparer.Ordinal);

        private readonly Regex? _match;
        private readonly Regex? _blank;

        private DietPatternRule(string pattern, bool substring, string term, IReadOnlyList<string> words, DietPatternStart start, DietPatternEnd end, string? typeName = null)
        {
            Pattern = pattern;
            Substring = substring;
            Term = term;
            Words = words;
            Start = start;
            End = end;
            TypeName = typeName;
            Exceptions = substring ? Array.Empty<DietPatternException>() : All.Where(e => e.Term == term && e.AppliesTo(typeName)).ToList();
            if (substring) return;
            _match = new Regex(Expression(DietRegexDialect.DotNet), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (ExceptionExpression(DietRegexDialect.DotNet) is { } blank)
                _blank = new Regex(blank, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        /// <summary>The criterion's pattern as stored.</summary>
        public string Pattern { get; }

        /// <summary>Whether the pattern keeps the ILIKE substring reading.</summary>
        public bool Substring { get; }

        /// <summary>The pattern's term in lower case, its words joined by single spaces ("nut butter").</summary>
        public string Term { get; }

        /// <summary>The words of the term.</summary>
        public IReadOnlyList<string> Words { get; }

        /// <summary>Where the term may begin.</summary>
        public DietPatternStart Start { get; }

        /// <summary>Where the term may end.</summary>
        public DietPatternEnd End { get; }

        /// <summary>The named exceptions of the term, those scoped to other types left out.</summary>
        public IReadOnlyList<DietPatternException> Exceptions { get; }

        /// <summary>The restriction type the reading is for when an exception of its term is scoped to it; otherwise null.</summary>
        public string? TypeName { get; }

        /// <summary>The reading of one stored pattern, parsed once; for a pattern of a named type, the exceptions scoped to that type are read too.</summary>
        public static DietPatternRule For(string likePattern, string? typeName = null)
        {
            var pattern = likePattern.Trim();
            if (typeName == null || !ScopedTypes.Value.TryGetValue(typeName.Trim(), out var scope)) return Parsed.GetOrAdd(pattern, _ => Parse(pattern, null));
            return Parsed.GetOrAdd(pattern + "\n" + scope, _ => Parse(pattern, scope));
        }

        private static readonly Lazy<HashSet<string>> ScopedTypes = new(() =>
            All.Where(e => e.Types != null).SelectMany(e => e.Types!).ToHashSet(StringComparer.OrdinalIgnoreCase));

        private static DietPatternRule Parse(string pattern, string? typeName)
        {
            DietPatternRule Like() => new(pattern, true, pattern.ToLowerInvariant(), Array.Empty<string>(), DietPatternStart.Open, DietPatternEnd.Open);
            if (pattern.Length == 0 || pattern.Contains('_') || pattern.Contains('\\') || pattern.StartsWith("%%", StringComparison.Ordinal) || pattern.EndsWith("%%", StringComparison.Ordinal)) return Like();
            var openStart = pattern.StartsWith('%');
            var openEnd = pattern.EndsWith('%');
            var inner = pattern.Trim('%');
            if (inner.Contains('%')) return Like();
            var words = WordsOf(inner);
            if (words.Count == 0) return Like();
            var leading = !char.IsLetterOrDigit(inner[0]);
            var trailing = !char.IsLetterOrDigit(inner[^1]);
            if (!openStart && leading) return Like();
            if (!openEnd && trailing) return Like();
            var start = !openStart ? DietPatternStart.Text : leading ? DietPatternStart.Word : DietPatternStart.Open;
            var end = !openEnd ? DietPatternEnd.Text : trailing ? DietPatternEnd.Word : DietPatternEnd.Open;
            return new DietPatternRule(pattern, false, string.Join(' ', words), words, start, end, typeName);
        }

        /// <summary>Whether a text (a food's name, an alias, a recipe line) names the pattern's food by the rule.</summary>
        public bool IsMatch(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            if (Substring) return DietCriteriaMatcher.LikeRegex(Pattern).IsMatch(text);
            if (_blank != null) text = _blank.Replace(text, " ");
            return _match!.IsMatch(text);
        }

        /// <summary>
        /// Whether a USDA description ("Flour, almond", "Leavening agents, cream of tartar") names the pattern's food:
        /// read as it stands, and cleared only when a named exception blanks out of an order of speech, one later part
        /// before its first ("almond Flour"; "corn Tortillas" of "Tortillas, ready-to-bake or -fry, corn"), every place
        /// the term holds there (staging checkpoint 4, P18; checkpoint 5, Q19). The order of speech never decides where a
        /// term begins or ends, so a pattern whose reading depends on position (<c>% oat%</c> in "Cereals, oats,
        /// regular") or a substring pattern reads the description as it stands, and the reading is never wider than the
        /// text's own.
        /// </summary>
        public bool IsDescriptionMatch(string? text)
        {
            if (!IsMatch(text)) return false;
            if (_blank == null) return true;
            return SpokenOrders(text).All(spoken => !_match!.IsMatch(spoken) || _match.IsMatch(_blank.Replace(spoken, " ")));
        }

        /// <summary>A USDA description in each order of speech: each later comma-separated part before its first, the other parts after them in their order ("Squash, winter, spaghetti, raw" reads "winter Squash, spaghetti, raw", "spaghetti Squash, winter, raw" and "raw Squash, winter, spaghetti"); the first is <see cref="SpokenOrder"/>.</summary>
        public static IEnumerable<string> SpokenOrders(string? description)
        {
            if (string.IsNullOrWhiteSpace(description)) yield break;
            var parts = description.Split(',');
            if (parts[0].Trim().Length == 0) yield break;
            for (var k = 1; k < parts.Length; k++)
            {
                if (parts[k].Trim().Length == 0) continue;
                var spoken = parts[k].Trim() + " " + parts[0].Trim();
                var rest = parts.Skip(1).Where((_, i) => i + 1 != k).ToList();
                yield return rest.Count == 0 ? spoken : spoken + "," + string.Join(",", rest);
            }
        }

        /// <summary>A USDA description in the order of speech: its second comma-separated part before its first, the rest after them ("Cheese, goat, hard type" reads "goat Cheese, hard type").</summary>
        public static string? SpokenOrder(string? description)
        {
            if (string.IsNullOrWhiteSpace(description)) return description;
            var parts = description.Split(',');
            if (parts.Length < 2 || parts[0].Trim().Length == 0 || parts[1].Trim().Length == 0) return description;
            var spoken = parts[1].Trim() + " " + parts[0].Trim();
            return parts.Length == 2 ? spoken : spoken + "," + string.Join(",", parts.Skip(2));
        }

        /// <summary>Whether an alias names the pattern's food: a USDA description (<c>fdc:description</c>) by <see cref="IsDescriptionMatch"/>, any other alias by <see cref="IsMatch"/>.</summary>
        public bool IsAliasMatch(string? alias, string? sourceContext) =>
            string.Equals(sourceContext?.Trim(), IngredientLinkService.UsdaDescriptionContext, StringComparison.OrdinalIgnoreCase) ? IsDescriptionMatch(alias) : IsMatch(alias);

        private static List<string> WordsOf(string text) =>
            Regex.Matches(text.ToLowerInvariant(), @"[\p{L}\p{N}]+").Select(m => m.Value).Where((w, i) => i == 0 || w != "s").ToList();

        private static string Letter(DietRegexDialect d) => d == DietRegexDialect.Postgres ? "[[:alpha:]]" : @"\p{L}";

        private static string NotLetter(DietRegexDialect d) => d == DietRegexDialect.Postgres ? "[^[:alpha:]]" : @"[^\p{L}]";

        private static string Space(DietRegexDialect d) => d == DietRegexDialect.Postgres ? "[[:space:]-]" : @"[\s-]";

        private static string Joined(IEnumerable<string> words, string separator, bool possessive) => string.Join((possessive ? "(?:['’]?s['’]?)?" : string.Empty) + separator + "+", words);

        private static string Absent(DietRegexDialect d) => "(?![- ]?free(?![- ]?(?:range|run|roam))(?!" + Space(d) + "+(?:from|of)(?!" + Letter(d) + "))(?:" + NotLetter(d) + "|$))";

        /// <summary>
        /// The term as a regular expression of the given dialect, read without case: where it begins a
        /// word (any ending but "less" or "free"), or, for an open start, as a compound's part after at
        /// least <see cref="CompoundPart"/> letters, ending the word (with an <see cref="Inflection"/>)
        /// or followed by at least as many letters. A substring pattern gives its ILIKE expression.
        /// </summary>
        public string Expression(DietRegexDialect dialect)
        {
            if (Substring) return DietCriteriaMatcher.LikeAsRegex(Pattern);
            var letter = Letter(dialect);
            var notLetter = NotLetter(dialect);
            var core = Joined(Words, notLetter, false);
            var head = Start switch
            {
                DietPatternStart.Text => "^",
                DietPatternStart.Word => notLetter,
                _ => "(?:^|" + notLetter + ")",
            };
            var wordEnd = Inflection + "?(?!" + letter + ")";
            var clauses = new List<string>();
            switch (End)
            {
                case DietPatternEnd.Text:
                    clauses.Add(head + core + Inflection + "?$");
                    if (Start == DietPatternStart.Open) clauses.Add(letter + "{" + CompoundPart + "}" + core + Inflection + "?$");
                    break;
                case DietPatternEnd.Word:
                    clauses.Add(head + core + wordEnd + Absent(dialect));
                    if (Start == DietPatternStart.Open) clauses.Add(letter + "{" + CompoundPart + "}" + core + wordEnd + Absent(dialect));
                    break;
                default:
                    clauses.Add(head + core + "(?!less)" + Absent(dialect));
                    if (Start == DietPatternStart.Open)
                        clauses.Add(letter + "{" + CompoundPart + "}" + core + "(?:" + wordEnd + Absent(dialect) + "|(?!less)" + letter + "{" + CompoundPart + "})");
                    break;
            }
            return "(?:" + string.Join("|", clauses) + ")";
        }

        /// <summary>The term's named exceptions as one regular expression of the given dialect, each phrase as whole words with a plural or possessive ending, its words joined only by spaces or hyphens (so "rice, flour" is two foods, not rice flour), blanked out of a text before the term is sought; null when the term has none.</summary>
        public string? ExceptionExpression(DietRegexDialect dialect)
        {
            if (Exceptions.Count == 0) return null;
            var phrases = Exceptions.Select(e => WordsOf(e.Phrase))
                .OrderByDescending(w => w.Sum(x => x.Length + 1))
                .Select(w =>
                {
                    var last = w[^1];
                    var stem = last.Length > 2 && last.EndsWith('y') && !"aeiou".Contains(last[^2]) ? last[..^1] + "(?:y|ies)" : last;
                    return Joined(w.Take(w.Count - 1).Append(stem), Space(dialect), true);
                })
                .Distinct(StringComparer.Ordinal);
            return "(?:^|" + NotLetter(dialect) + ")(?:" + string.Join("|", phrases) + ")(?:s|es|['’]s)?(?!" + Letter(dialect) + ")";
        }

        private static DietPatternException E(string term, string phrase, string reason) => new(term, phrase, reason);

        private static DietPatternException Scoped(string term, string phrase, string reason, params string[] types) => new(term, phrase, reason, types);

        private static readonly string[] NightshadeTypes = { "Nightshade Sensitivity", "Nightshade-Free" };

        private static readonly string[] GlutenTypes = { "Celiac Disease", "Gluten Sensitivity", "Gluten-Free", "Wheat Allergy" };

        private static readonly string[] GlutenFreeTypes = { "Celiac Disease", "Gluten Sensitivity", "Gluten-Free" };

        private static readonly string[] GrainTypes = GlutenTypes.Concat(new[] { "Grain-Free", "Paleo", "Whole30" }).ToArray();

        private static readonly string[] GlutenAndGrainFreeTypes = GlutenTypes.Append("Grain-Free").ToArray();

        private static readonly string[] SyrupTypes = GlutenAndGrainFreeTypes.Concat(new[] { "Milk Allergy", "Dairy-Free", "Lactose-Intolerant", "Lactose Intolerance", "Egg Allergy", "Vegan", "Ethical Vegan", "Raw Vegan", "Plant-Based", "Hindu Vegetarian", "Jain" }).ToArray();

        private static readonly string[] MeatlessTypes = { "Vegan", "Ethical Vegan", "Raw Vegan", "Plant-Based", "Vegetarian", "Hindu Vegetarian", "Buddhist Vegetarian", "Jain", "Pescatarian", "Halal", "Kosher" };

        /// <summary>
        /// The compound foods that hold a term and are not its food, each with its reason. A phrase is
        /// listed only where every default type that holds the term either does not restrict the
        /// compound or still names it by another of its patterns (the reason says which), so an
        /// exception never clears a food a type's own patterns name. An exception that is right for
        /// some types only is scoped to them (<see cref="DietPatternException.Types"/>: "sweet potato"
        /// is no nightshade, but Jain still avoids it as a root) and read only for their patterns.
        /// </summary>
        public static readonly IReadOnlyList<DietPatternException> All = new[]
        {
            E("ham", "graham", "graham flour and graham crackers are whole-wheat products named after Sylvester Graham; no pork; graham crackers and graham flour stay out of gluten types by their own words"),
            E("ham", "hamburger", "hamburger is chopped beef (USDA, 9 CFR 319.15(b)); no pork; meat types hold it by %hamburger%"),
            E("ham", "hamantaschen", "a filled Purim pastry; no pork"),
            E("hamburger", "hamburger bun", "a bread roll; no meat"),
            E("hamburger", "hamburger roll", "a bread roll; no meat"),
            E("hot dog", "hot dog bun", "a bread roll; no meat"),
            E("hot dog", "hot dog roll", "a bread roll; no meat"),
            E("lard", "collard", "collard greens are a leaf vegetable (Brassica oleracea); no lard"),
            E("bacon", "turkey bacon", "cured turkey; no pork (USDA FSIS labels it by its species); meat types hold it by %turkey%"),
            E("bacon", "beef bacon", "cured beef; no pork; meat types hold it by %beef%"),
            E("goat", "goat cheese", "a cheese made from goat's milk, not goat meat; dairy types hold it by %cheese%"),
            E("goat", "goat milk", "milk, not goat meat; dairy types hold it by %milk%"),
            E("goat", "goat yogurt", "yogurt, not goat meat; dairy types hold it by %yogurt%"),
            E("lamb", "lamb's lettuce", "corn salad (Valerianella locusta), a leaf vegetable; no lamb"),
            E("lamb", "lambrusco", "an Italian red wine; no lamb"),
            E("beef", "beefsteak tomato", "a large tomato variety; no beef; nightshade types hold it by %tomato%"),
            E("steak", "steak sauce", "a bottled condiment of tomato, vinegar and spices; no meat"),
            E("steak", "steak seasoning", "a dry spice blend; no meat"),
            E("goose", "gooseberry", "the fruit of Ribes uva-crispa; no goose"),
            E("duck", "duck sauce", "a sweet plum or apricot condiment; no duck"),
            E("poultry", "poultry seasoning", "a blend of dried herbs (sage, thyme, marjoram); no poultry"),
            E("fish", "shellfish", "crustaceans and molluscs, a major allergen apart from fish (FDA, FALCPA); shellfish types hold it by %shellfish%"),
            E("fish", "crawfish", "a crustacean, not a fish; shellfish types hold it by %crawfish%"),
            E("fish", "crayfish", "a crustacean, not a fish; shellfish types hold it by %crayfish%"),
            E("fish", "goldfish cracker", "a brand of baked cheese cracker; no fish (gluten types hold it by %cracker%, dairy types do not read the brand)"),
            E("fish", "swedish fish", "a brand of gummy candy; no fish"),
            E("cod", "coddled", "to coddle is to cook gently below a boil (coddled eggs); no cod"),
            E("cod", "coddle", "to coddle is to cook gently below a boil; no cod"),
            E("salmon", "salmonberry", "the fruit of Rubus spectabilis; no salmon"),
            E("crab", "crab apple", "the fruit of wild Malus species; no crab"),
            E("crab", "crabapple", "the fruit of wild Malus species; no crab"),
            E("oyster", "oyster mushroom", "Pleurotus ostreatus, a fungus; no oyster"),
            E("oyster", "oyster cracker", "a small soup cracker; no oyster (gluten types hold it by %cracker%)"),
            E("lobster", "lobster mushroom", "Hypomyces lactifluorum on a Russula, a fungus; no lobster"),
            E("scallop", "scalloped", "scalloped potatoes are sliced and baked in layers; no scallops (dairy types hold the cream or milk by their own words)"),
            E("scallop", "scallopini", "thin veal cutlets; no scallops; a line that says veal stays out of meat types by its own word"),
            E("egg", "eggplant", "Solanum melongena, a vegetable; no egg; nightshade types hold it by %eggplant%"),
            E("milk", "coconut milk", "pressed coconut flesh; no dairy; Coconut Allergy holds it by %coconut%"),
            E("milk", "almond milk", "a drink of ground almonds; no dairy; tree nut types hold it by %almond%"),
            E("milk", "cashew milk", "a drink of ground cashews; no dairy; tree nut types hold it by %cashew%"),
            E("milk", "soy milk", "a drink of soybeans; no dairy; soy types hold it by %soy%"),
            E("milk", "soymilk", "a drink of soybeans; no dairy; soy types hold it by %soy%"),
            E("milk", "oat milk", "a drink of oats; no dairy; gluten and grain types hold it by their oat patterns"),
            E("milk", "rice milk", "a drink of rice; no dairy; grain types hold it by %rice%"),
            E("milk", "milkweed", "Asclepias, a plant; no dairy"),
            E("milk", "milk thistle", "Silybum marianum, an herb; no dairy"),
            E("milk", "milkfish", "Chanos chanos, a fish; no dairy; fish types hold it by %fish%"),
            E("butter", "peanut butter", "ground peanuts; no dairy (FDA standard of identity, 21 CFR 164.150); peanut types hold it by %peanut%"),
            E("butter", "almond butter", "ground almonds; no dairy; tree nut types hold it by %almond%"),
            E("butter", "cashew butter", "ground cashews; no dairy; tree nut types hold it by %cashew%"),
            E("butter", "nut butter", "ground nuts; no dairy; tree nut types hold it by %nut butter%"),
            E("butter", "seed butter", "ground seeds; no dairy"),
            E("butter", "sunflower butter", "ground sunflower seeds; no dairy"),
            E("butter", "apple butter", "apples cooked down to a spread; no dairy"),
            E("butter", "cocoa butter", "the fat of the cacao bean; no dairy"),
            E("butter", "cacao butter", "the fat of the cacao bean; no dairy"),
            E("butter", "butternut", "butternut squash (Cucurbita moschata); no dairy"),
            E("butter", "butterhead", "a lettuce variety; no dairy"),
            E("butter", "butter lettuce", "a lettuce variety; no dairy"),
            E("butter", "butter bean", "lima beans (Phaseolus lunatus); no dairy; legume types hold them by %bean%"),
            E("cream", "cream of tartar", "potassium bitartrate, a by-product of winemaking; no dairy"),
            E("cream", "cream tartar", "cream of tartar in the older spelling of nineteenth-century cookbooks, also hyphenated; potassium bitartrate, no dairy"),
            E("cream", "marshmallow cream", "marshmallow creme of corn syrup, sugar and egg white; no dairy; types that restrict its corn syrup or egg hold it by %marshmallow cream% (CompositeFoods)"),
            E("cream", "coconut cream", "the fat of pressed coconut; no dairy; Coconut Allergy holds it by %coconut%"),
            E("cream", "cream of coconut", "sweetened coconut cream; no dairy; Coconut Allergy holds it by %coconut%"),
            E("cheese", "cheesecloth", "a cotton cloth for straining; not a food"),
            E("custard", "custard apple", "the fruit of an Annona tree; no custard"),
            E("yogurt", "coconut yogurt", "fermented coconut; no dairy; Coconut Allergy holds it by %coconut%"),
            E("yogurt", "soy yogurt", "fermented soy; no dairy; soy types hold it by %soy%"),
            E("yogurt", "almond yogurt", "fermented almonds; no dairy; tree nut types hold it by %almond%"),
            E("yoghurt", "coconut yoghurt", "fermented coconut; no dairy; Coconut Allergy holds it by %coconut%"),
            E("yoghurt", "soy yoghurt", "fermented soy; no dairy; soy types hold it by %soy%"),
            E("honey", "honeydew", "a melon (Cucumis melo); no honey"),
            E("honey", "honeycrisp", "an apple variety; no honey"),
            E("wheat", "buckwheat", "Fagopyrum esculentum, not a grass and gluten-free (Celiac Disease Foundation); Buckwheat Allergy, Grain-Free, Paleo and Whole30 hold it by %buckwheat%"),
            E("flour", "cornflour", "flour or starch of maize; no wheat, barley or rye; corn types hold it by %corn%"),
            E("flour", "corn flour", "flour of maize; no wheat, barley or rye; corn types hold it by %corn%"),
            E("flour", "rice flour", "flour of rice; no wheat, barley or rye; grain types hold it by %rice%"),
            E("flour", "oat flour", "flour of oats; no wheat; gluten and grain types hold it by their oat patterns"),
            E("flour", "buckwheat flour", "flour of buckwheat, gluten-free (Celiac Disease Foundation); buckwheat types hold it by %buckwheat%"),
            E("flour", "almond flour", "ground almonds; no grain; tree nut types hold it by %almond%"),
            E("flour", "coconut flour", "dried ground coconut; no grain; Coconut Allergy holds it by %coconut%"),
            E("flour", "chickpea flour", "ground chickpeas; no grain; legume types hold it by %chickpea%"),
            E("flour", "potato flour", "dried ground potato; no grain; nightshade and root types hold it by %potato%"),
            E("flour", "tapioca flour", "cassava starch; no grain"),
            E("flour", "cassava flour", "dried ground cassava root; no grain"),
            E("flour", "arrowroot flour", "starch of the arrowroot rhizome; no grain"),
            E("flower", "cauliflower", "Brassica oleracea var. botrytis, a vegetable; no flour"),
            E("flower", "sunflower", "Helianthus annuus, whose seeds and oil hold no grain"),
            E("flower", "safflower", "Carthamus tinctorius, an oil seed; no grain"),
            E("flower", "cornflower", "Centaurea cyanus, an edible blossom; maize flour, its other reading, holds no gluten either"),
            E("flower", "corn flower", "maize flour in the old spelling, or the cornflower blossom; no wheat, barley or rye; corn types hold it by %corn%"),
            E("flower", "elderflower", "the blossom of Sambucus, for cordials and fritters; no grain"),
            E("flower", "elder flower", "the blossom of Sambucus; no grain"),
            E("flower", "orange flower", "orange-flower water is distilled from bitter orange blossoms; no grain"),
            E("flower", "flower water", "orange or rose flower water, a distillate of blossoms; no grain"),
            E("flower", "rose flower", "rose blossoms; no grain"),
            E("flower", "edible flower", "blossoms eaten as a garnish; no grain"),
            E("flower", "squash flower", "squash blossoms (Cucurbita); no grain"),
            E("flower", "zucchini flower", "zucchini blossoms (Cucurbita pepo); no grain"),
            E("flower", "pumpkin flower", "pumpkin blossoms (Cucurbita); no grain"),
            E("flower", "banana flower", "the blossom of the banana plant, a vegetable; no grain"),
            E("flower", "chive flower", "chive blossoms (Allium schoenoprasum); no grain"),
            E("flower", "lavender flower", "lavender blossoms (Lavandula); no grain"),
            E("flower", "chamomile flower", "chamomile blossoms (Matricaria); no grain"),
            E("flower", "hibiscus flower", "hibiscus calyces (Hibiscus sabdariffa); no grain"),
            E("flower", "nasturtium flower", "nasturtium blossoms (Tropaeolum); no grain"),
            E("flower", "jasmine flower", "jasmine blossoms (Jasminum); no grain"),
            E("flower", "flower pepper", "Sichuan pepper (Zanthoxylum), called flower pepper; no grain"),
            E("flower", "flower petal", "blossom petals; no grain"),
            E("flower", "flower bud", "blossom buds (capers, cloves); no grain"),
            E("flower", "floweret", "the small heads of broccoli or cauliflower; no grain"),
            E("flower", "flowerette", "the small heads of broccoli or cauliflower; no grain"),
            E("flower", "flowering", "a flowering plant or its shoots (flowering chives); no grain"),
            E("bread", "sweetbread", "the thymus or pancreas of a calf or lamb; no bread"),
            E("bread", "breadfruit", "the fruit of Artocarpus altilis; no grain"),
            E("wonton", "wonton wrapper", "the empty wheat-flour and egg wrapper, with no pork or shrimp filling; types that restrict wheat or egg hold it by %wonton wrapper% (CompositeFoods)"),
            E("wonton", "wonton skin", "the empty wheat-flour and egg wrapper, with no pork or shrimp filling; types that restrict wheat or egg hold it by %wonton skin% (CompositeFoods)"),
            E("spaghetti", "spaghetti squash", "a winter squash (Cucurbita pepo); no wheat"),
            E("beer", "root beer", "a soft drink; not brewed from grain"),
            E("beer", "ginger beer", "a soft drink; not brewed from grain"),
            E("beer", "birch beer", "a soft drink; not brewed from grain"),
            E("malt", "maltodextrin", "a starch derivative that is gluten-free even when made from wheat (Celiac Disease Foundation)"),
            E("tortilla", "corn tortilla", "a maize flatbread; no wheat; corn types hold it by %corn%"),
            E("tortilla", "flour tortilla", "a wheat flatbread; no corn; wheat and gluten types hold it by %flour%"),
            E("rice", "licorice", "the root of Glycyrrhiza glabra, or a candy flavoured with it; no rice"),
            E("rice", "liquorice", "the root of Glycyrrhiza glabra, or a candy flavoured with it; no rice"),
            E("corn", "peppercorn", "the dried berry of Piper nigrum; no maize"),
            E("corn", "corned", "corned beef is cured with coarse grains ('corns') of salt; no maize"),
            E("corn", "cornish", "Cornish game hen, a chicken breed named for Cornwall; no maize"),
            E("chestnut", "water chestnut", "the corm of Eleocharis dulcis, an aquatic sedge; not a tree nut"),
            E("nut butter", "peanut butter", "peanuts are legumes, a major allergen apart from tree nuts (FDA, FALCPA); peanut types hold it by %peanut%"),
            E("nut butter", "coconut butter", "coconut is no longer a tree nut for allergen labelling (FDA, Questions and Answers Regarding Food Allergens, edition 5, 2025); Coconut Allergy holds it by %coconut%"),
            E("nut", "nutmeg", "the seed of Myristica fragrans, not a tree nut (FDA, edition 5, 2025)"),
            E("nut", "coconut", "not a tree nut for allergen labelling (FDA, edition 5, 2025); Coconut Allergy holds it by %coconut%"),
            E("nut", "butternut", "butternut squash (Cucurbita moschata)"),
            E("nut", "doughnut", "a fried dough cake; no nut"),
            E("nut", "nutritional yeast", "deactivated yeast; no nut"),
            E("pea", "peanut", "a legume that is a major allergen of its own; peanut types hold it by %peanut%"),
            E("pea", "peach", "the fruit of Prunus persica; no pea"),
            E("pea", "pear", "the fruit of Pyrus; no pea"),
            E("pea", "pearl", "pearl onions, pearl barley, tapioca pearls; no pea"),
            E("tamari", "tamarind", "the fruit of Tamarindus indica; no soy"),
            E("pear", "pearl", "pearl onions, pearl barley, tapioca pearls; no pear"),
            E("apple", "pineapple", "Ananas comosus, a bromeliad; no apple (low-FODMAP diets allow it)"),
            E("bean", "vanilla bean", "the cured pod of the vanilla orchid; not a legume"),
            E("bean", "coffee bean", "the seed of Coffea; not a legume"),
            E("bean", "cocoa bean", "the seed of Theobroma cacao; not a legume"),
            E("bean", "cacao bean", "the seed of Theobroma cacao; not a legume"),
            E("lemon", "lemongrass", "Cymbopogon, a grass; not a citrus"),
            E("lemon", "lemon balm", "Melissa officinalis, a mint; not a citrus"),
            E("lemon", "lemon verbena", "Aloysia citrodora; not a citrus"),
            E("lemon", "lemon thyme", "Thymus citriodorus, a thyme; not a citrus"),
            E("orange", "orange roughy", "Hoplostethus atlanticus, a fish; not a citrus"),
            E("mango", "mangosteen", "Garcinia mangostana; not a mango"),
            E("banana", "banana pepper", "a mild Capsicum annuum pepper; no banana"),
            E("chile", "chilean", "from Chile (Chilean sea bass); no chile pepper"),
            E("soda", "baking soda", "sodium bicarbonate; no sugar"),
            E("soda", "bicarbonate of soda", "sodium bicarbonate; no sugar"),
            E("soda", "club soda", "carbonated water; no sugar"),
            E("soda", "soda water", "carbonated water; no sugar"),
            E("cola", "colander", "a kitchen strainer; not a food"),
            E("jam", "jambalaya", "a rice dish of Louisiana; no jam"),
            E("jam", "jamaican", "from Jamaica (Jamaican jerk seasoning); no jam"),
            Scoped("flour", "sorghum flour", "flour of sorghum, gluten-free (Celiac Disease Foundation; staging checkpoint 5, Q19); Grain-Free holds it by %sorghum%; read only for the gluten types, since Grain-Free, Paleo and Whole30 exclude the grain", GlutenTypes),
            Scoped("flour", "millet flour", "flour of millet, gluten-free (Celiac Disease Foundation); Grain-Free holds it by %millet%; read only for the gluten types, since Grain-Free, Paleo and Whole30 exclude the grain", GlutenTypes),
            Scoped("flour", "teff flour", "flour of teff, gluten-free (Celiac Disease Foundation); read only for the gluten types, since Grain-Free, Paleo and Whole30 exclude the grain", GlutenTypes),
            Scoped("flour", "amaranth flour", "flour of amaranth, gluten-free (Celiac Disease Foundation); read only for the gluten types, since Grain-Free, Paleo and Whole30 exclude the grain", GlutenTypes),
            Scoped("flour", "quinoa flour", "flour of quinoa, gluten-free (Celiac Disease Foundation); Grain-Free holds it by %quinoa%; read only for the gluten types, since Grain-Free, Paleo and Whole30 exclude the grain", GlutenTypes),
            E("flour", "hazelnut flour", "ground hazelnuts; no grain; tree nut types hold it by %hazelnut%"),
            E("flour", "peanut flour", "ground peanuts; no grain; peanut types hold it by %peanut%"),
            E("flour", "acorn flour", "ground acorns; no grain; tree nut types hold it by %acorn%"),
            E("flour", "plantain flour", "dried ground plantain; no grain; Banana Allergy holds it by %plantain%"),
            E("flour", "banana flour", "dried ground green banana; no grain; Banana Allergy holds it by %banana%"),
            Scoped("flour", "lupin flour", "ground lupin, a legume; no grain; Lupin Allergy holds it by %lupin%; read only for the gluten and grain types, since Paleo and Whole30 exclude legumes", GlutenAndGrainFreeTypes),
            E("flour", "soy flour", "ground soybeans; no grain; soy types hold it by %soy%"),
            Scoped("flour", "rye flour", "flour of rye, no wheat (FALCPA names wheat); read only for Wheat Allergy, since the gluten types restrict rye", "Wheat Allergy"),
            Scoped("flour", "barley flour", "flour of barley, no wheat (FALCPA names wheat); read only for Wheat Allergy, since the gluten types restrict barley", "Wheat Allergy"),
            Scoped("flour", "barley malt flour", "malted barley flour, no wheat; read only for Wheat Allergy, since the gluten types restrict barley and malt", "Wheat Allergy"),
            E("flower", "flower cluster", "the flower clusters of broccoli or cauliflower (SR: Broccoli, flower clusters; staging checkpoint 5, Q19); no grain"),
            E("noodle", "rice noodle", "noodles of rice flour; no wheat; grain types hold them by %rice%"),
            Scoped("noodle", "glass noodle", "noodles of mung bean or sweet potato starch; no wheat; read only for the gluten and grain types, since Paleo and Whole30 exclude legumes", GlutenAndGrainFreeTypes),
            Scoped("noodle", "cellophane noodle", "noodles of mung bean starch; no wheat; read only for the gluten and grain types, since Paleo and Whole30 exclude legumes", GlutenAndGrainFreeTypes),
            Scoped("noodle", "bean thread noodle", "noodles of mung bean starch; no wheat; read only for the gluten and grain types, since Paleo and Whole30 exclude legumes", GlutenAndGrainFreeTypes),
            E("noodle", "sweet potato noodle", "noodles of sweet potato starch; no wheat"),
            E("noodle", "shirataki noodle", "noodles of konjac; no grain"),
            E("noodle", "kelp noodle", "noodles of seaweed; no grain"),
            E("noodle", "zucchini noodle", "spiralized zucchini; no grain"),
            E("vermicelli", "rice vermicelli", "rice noodles; no wheat; grain types hold them by %rice%"),
            Scoped("pasta", "rice pasta", "pasta of rice flour; no wheat; grain types hold it by %rice%; read only for the wheat and grain types, since Carnivore excludes every plant", GrainTypes),
            Scoped("pasta", "corn pasta", "pasta of maize; no wheat; corn types hold it by %corn%; read only for the wheat and grain types, since Carnivore excludes every plant", GrainTypes),
            Scoped("pasta", "chickpea pasta", "pasta of chickpea flour; no wheat; legume types hold it by %chickpea%; read only for the wheat and grain types, since Carnivore excludes every plant", GrainTypes),
            Scoped("pasta", "lentil pasta", "pasta of lentil flour; no wheat; legume types hold it by %lentil%; read only for the wheat and grain types, since Carnivore excludes every plant", GrainTypes),
            Scoped("pasta", "quinoa pasta", "pasta of quinoa; no wheat; Grain-Free holds it by %quinoa%; read only for the wheat and grain types, since Carnivore excludes every plant", GrainTypes),
            E("cake", "cake of", "a cake of yeast, chocolate, soap or ice is a block, the measure of old cookbooks; no cake"),
            E("cake", "yeast cake", "a cake of compressed yeast; no wheat"),
            E("cake", "cake yeast", "compressed yeast sold in cakes; no wheat"),
            E("cake", "cake compressed yeast", "compressed yeast sold in cakes; no wheat"),
            E("cake", "cake chocolate", "a cake (block) of chocolate, the measure of old cookbooks; no wheat"),
            E("cake", "cake tofu", "a cake (block) of tofu; no wheat; soy types hold it by %tofu%"),
            E("cake", "flourless cake", "a cake made without flour; no wheat (the line says so)"),
            E("cake", "flourless chocolate cake", "a cake of chocolate, eggs and butter made without flour; no wheat"),
            E("cake", "rice cake", "puffed rice; no wheat; grain types hold it by %rice%"),
            Scoped("cake", "pancake syrup", "a table syrup; no wheat; read only for the gluten and grain types, since the sugar types restrict it", GlutenAndGrainFreeTypes),
            Scoped("pancake", "pancake syrup", "a table syrup; no wheat; read only for the types that restrict the pancake for wheat, egg or milk, since the sugar and FODMAP types restrict the syrup", SyrupTypes),
            Scoped("pie", "pie filling", "a fruit filling thickened with starch; no crust; read only for the gluten types, since the grain and sugar types restrict its starch and sugar", GlutenTypes),
            E("pie", "pie spice", "a spice blend (pumpkin pie spice); no crust"),
            E("pie", "pie cherry", "sour cherries (Prunus cerasus), named for their use; no crust"),
            E("pie", "pie pumpkin", "a sugar pumpkin, named for its use; no crust"),
            E("pie", "pie plate", "a baking dish; not a food"),
            E("pie", "pie pan", "a baking dish; not a food"),
            E("pie", "pie dish", "a baking dish; not a food"),
            E("pie", "piece", "a portion (a 1-inch piece, cut into pieces); no crust"),
            E("roll", "rolled", "rolled oats, a rolled roast: a shape or a step, not a bread roll; the oat and meat patterns read the food"),
            E("roll", "rolling", "a step (rolling pin, rolling boil); not a bread roll"),
            E("roll", "roll out", "a step; not a bread roll"),
            Scoped("roll", "fruit roll", "a fruit leather (SR: Snacks, fruit leather, rolls); no wheat; read only for the gluten and grain types, since the sugar types restrict it", GlutenAndGrainFreeTypes),
            Scoped("roll", "sushi roll", "rice rolled in seaweed; no wheat; read only for the gluten types, since the grain types restrict its rice", GlutenTypes),
            E("roll", "rollmop", "rolled pickled herring; no wheat"),
            Scoped("dough", "masa dough", "dough of nixtamalized maize; no wheat; read only for the gluten types, since the grain types restrict maize", GlutenTypes),
            E("port", "portobello", "a mushroom (Agaricus bisporus); no wine"),
            E("port", "portabella", "a mushroom (Agaricus bisporus); no wine"),
            E("port", "portabello", "a mushroom (Agaricus bisporus); no wine"),
            E("port", "portion", "a measure; no wine"),
            E("port", "porterhouse", "a beef steak; no wine"),
            E("port", "portuguese", "from Portugal (Portuguese sausage); no wine"),
            E("port", "port salut", "a cow's milk cheese; no wine"),
            E("port", "support", "not a food"),
            E("flan", "flank", "a cut of beef; no custard"),
            E("fish", "goldfish", "Goldfish, a brand of baked cheese cracker; no fish (milk and gluten types hold it by %goldfish%)"),
            Scoped("bread", "breadnut", "the seed of Brosimum alicastrum, a tree; no grain; read only for the wheat and grain types, since Carnivore excludes every plant", GrainTypes),
            Scoped("oat", "gluten free rolled oat", "oats grown and milled apart from wheat, under 20 ppm gluten (21 CFR 101.91); read only for the gluten types", GlutenFreeTypes),
            Scoped("pizza", "pizza sauce", "a tomato sauce; no crust; read only for the wheat and grain types", GrainTypes),
            Scoped("pizza", "pizza seasoning", "a dry herb blend; no crust; read only for the wheat and grain types", GrainTypes),
            Scoped("pizza", "pizza cheese", "shredded cheese sold for pizza; no crust; read only for the wheat and grain types", GrainTypes),
            Scoped("pizza", "pizza stone", "a baking stone; not a food", GrainTypes),
            Scoped("pizza", "pizza pan", "a baking pan; not a food", GrainTypes),
            Scoped("sandwich", "sandwich spread", "a dressing of mayonnaise and relish; no bread; read only for the gluten and grain types, since Paleo and Whole30 restrict its sugar and oil", GlutenAndGrainFreeTypes),
            E("flour", "sesame flour", "ground sesame seed; no grain; sesame types hold it by %sesame%"),
            E("flour", "seed flour", "ground sunflower, pumpkin or other seed; no grain"),
            E("flour", "cottonseed flour", "ground cottonseed; no grain"),
            Scoped("tortilla", "tortilla chip", "fried pieces of corn tortilla (SR: Snacks, tortilla chips); no wheat; read only for the gluten types, since the corn and grain types restrict the maize", GlutenTypes),
            E("cream", "cream of wheat", "farina, a wheat cereal cooked with water; no dairy; wheat types hold it by %wheat%"),
            E("bran", "brand", "a maker's name (any brand); not a food"),
            E("bran", "brandy", "a distilled wine; no grain"),
            E("bran", "branch", "a sprig (a branch of thyme); no grain"),
            E("bran", "brandade", "a purée of salt cod; no grain"),
            E("bran", "sorghum bran", "the bran of sorghum, gluten-free (Celiac Disease Foundation)"),
            E("bran", "oat bran", "the bran of oats; no wheat; the gluten types hold it by their oat patterns"),
            E("bran", "rice bran", "the bran of rice; no wheat; grain types hold it by %rice%"),
            E("bran", "corn bran", "the bran of maize; no wheat; corn types hold it by %corn%"),
            Scoped("cereal", "gluten free cereal", "a cereal under 20 ppm gluten (21 CFR 101.91); read only for the gluten types", GlutenFreeTypes),
            Scoped("pudding", "black pudding", "a blood sausage; no milk; read only for the milk types", "Milk Allergy", "Dairy-Free"),
            Scoped("pudding", "blood pudding", "a blood sausage; no milk; read only for the milk types", "Milk Allergy", "Dairy-Free"),
            Scoped("snickers", "snickers almond", "the almond Snickers bar holds almonds, not peanuts (LABEL); read only for Peanut Allergy", "Peanut Allergy"),
            E("bun", "bunch", "a bunch of herbs or greens, a measure; no bread"),
            E("roti", "rotisserie", "a spit-roasted meat; no bread"),
            E("drum", "drumstick", "the lower leg of a bird, or the pod of the moringa tree (drumstick vegetable); no fish; the meat types hold a bird's drumstick by its bird where the line names one, as before the fish vocabulary"),
            E("drum", "drumette", "the upper wing of a chicken; no fish; the meat types hold it by its bird where the line names one"),
            E("drum", "drummette", "the upper wing of a chicken; no fish; the meat types hold it by its bird where the line names one"),
            E("ling", "linguine", "a pasta shape; no fish or meat; wheat types hold it by %linguine%"),
            E("ling", "linguini", "a pasta shape; no fish or meat; wheat types hold it by %linguini%"),
            E("ling", "lingonberry", "the fruit of Vaccinium vitis-idaea; no fish or meat"),
            Scoped("ling", "linguica", "a Portuguese pork sausage; no fish; read only for Fish Allergy, since the meat types restrict it or read it on the safe side", "Fish Allergy"),
            Scoped("pout", "poutine", "fries with cheese curds and gravy; no fish; read only for Fish Allergy, since the meat types restrict it or read it on the safe side", "Fish Allergy"),
            Scoped("spot", "spotted", "a mark or a pattern (spotted dick); no fish; read only for Fish Allergy, since the meat types restrict it or read it on the safe side", "Fish Allergy"),
            E("shad", "shadow", "not a food"),
            E("cisco", "francisco", "a place name (San Francisco); no fish or meat"),
            Scoped("carp", "carpaccio", "thinly sliced raw beef; no fish; read only for Fish Allergy, since the meat types restrict it or read it on the safe side", "Fish Allergy"),
            E("pike", "pikelet", "a small griddle cake; no fish or meat"),
            E("eel", "unpeeled", "not peeled (garlic, potatoes, chestnuts); no fish"),
            E("til", "tilapia", "a fish (Oreochromis); no sesame; fish types hold it by %tilapia%"),
            E("til", "tilefish", "a fish (Malacanthidae); no sesame"),
            E("til", "tilsit", "a cow's milk cheese; no sesame"),
            E("til", "tillamook", "a cheese maker's name (Tillamook cheddar); no sesame"),
            E("til", "till", "until (simmer till thick); not a food"),
            E("nut", "nutrition", "not a food"),
            E("nut", "nutritious", "not a food"),
            E("nut", "nutrient", "not a food"),
            E("nut", "nutty", "a flavour or a texture (nutty brown butter); no nut"),
            Scoped("nut", "nutella", "a spread of hazelnuts and milk; no peanut; read only for Peanut Allergy, since tree nut types hold it by %nutella%", "Peanut Allergy"),
            E("dough", "corn dough", "dough of maize; no wheat; corn types hold it by %corn%"),
            E("pita", "pitaya", "dragon fruit (Selenicereus); no bread"),
            E("curd", "bean curd", "tofu; no milk; soy types hold it by %bean curd%"),
            E("curd", "soy curd", "tofu; no milk; soy types hold it by %soy%"),
            E("albumin", "lactalbumin", "a milk protein; no egg; milk types hold it by %lactalbumin%"),
            E("conch", "conchiglie", "a pasta shape (shells); no shellfish; the wheat types hold it as a pasta shape where a line says pasta"),
            E("butter", "fruit butter", "fruit cooked down to a spread (SR: Fruit butters, apple; staging checkpoint 5, Q19); no dairy"),
            E("butter", "pear butter", "pears cooked down to a spread; no dairy"),
            E("butter", "plum butter", "plums cooked down to a spread; no dairy"),
            E("butter", "peach butter", "peaches cooked down to a spread; no dairy"),
            E("butter", "pumpkin butter", "pumpkin cooked down to a spread; no dairy"),
            E("egg", "flax egg", "an egg replacer of ground flaxseed and water (staging checkpoint 5, Q19); no egg"),
            E("egg", "chia egg", "an egg replacer of ground chia seed and water; no egg"),
            Scoped("bacon", "meatless bacon", "a meat analogue (SR: Bacon, meatless, of soy and wheat protein; staging checkpoint 5, Q19); no meat or pork; read only for the meat and pork types, since the fat types restrict it for its fat", MeatlessTypes),
            Scoped("bacon", "vegetarian bacon", "a meat analogue; no meat or pork", MeatlessTypes),
            Scoped("bacon", "vegan bacon", "a meat analogue; no meat or pork", MeatlessTypes),
            Scoped("bacon", "coconut bacon", "toasted coconut flakes; no meat or pork; Coconut Allergy holds it by %coconut%", MeatlessTypes),
            Scoped("bacon", "tempeh bacon", "smoked tempeh; no meat or pork; soy types hold it by %tempeh%", MeatlessTypes),
            Scoped("bacon", "mushroom bacon", "smoked mushrooms; no meat or pork", MeatlessTypes),
            Scoped("fish", "jellyfish", "a cnidarian, not a fish (SR lists it apart from the Fish entries); read only for Fish Allergy, since the vegetarian types mean it as an animal", "Fish Allergy"),
            Scoped("fish", "cuttlefish", "a mollusk, not a fish; read only for Fish Allergy, since the shellfish types hold it by %cuttlefish% and the vegetarian types mean it as an animal", "Fish Allergy"),
            Scoped("spaghetti", "spaghetti sauce", "a tomato sauce (staging checkpoint 5, Q19); no pasta; read only for the wheat and grain types", GrainTypes),
            Scoped("spaghetti", "spaghetti seasoning", "a dry herb blend; no pasta; read only for the wheat and grain types", GrainTypes),
            E("stuffing", "for stuffing", "a step, what a vegetable is for (poblano peppers, for stuffing); no bread"),
            Scoped("pasta", "pasta sauce", "a tomato sauce; no pasta; read only for the wheat and grain types", GrainTypes),
            Scoped("nut", "pine nut", "a tree nut named as such, no peanut (FALCPA names peanuts apart from tree nuts); read only for Peanut Allergy, whose % nut% reads a nut a line does not name", "Peanut Allergy"),
            Scoped("nut", "brazil nut", "a tree nut named as such; no peanut; read only for Peanut Allergy", "Peanut Allergy"),
            Scoped("nut", "macadamia nut", "a tree nut named as such; no peanut; read only for Peanut Allergy", "Peanut Allergy"),
            Scoped("nut", "cashew nut", "a tree nut named as such; no peanut; read only for Peanut Allergy", "Peanut Allergy"),
            Scoped("nut", "pistachio nut", "a tree nut named as such; no peanut; read only for Peanut Allergy", "Peanut Allergy"),
            Scoped("nut", "ginkgo nut", "a tree nut named as such; no peanut; read only for Peanut Allergy", "Peanut Allergy"),
            Scoped("nut", "hickory nut", "a tree nut named as such; no peanut; read only for Peanut Allergy", "Peanut Allergy"),
            Scoped("nut", "hazel nut", "a tree nut named as such; no peanut; read only for Peanut Allergy", "Peanut Allergy"),
            Scoped("nut", "pili nut", "a tree nut named as such; no peanut; read only for Peanut Allergy", "Peanut Allergy"),
            Scoped("nut", "kola nut", "the seed of Cola, a flavouring; no peanut", "Peanut Allergy", "Tree Nut Allergy"),
            Scoped("nut", "tiger nut", "the tuber of Cyperus esculentus; no nut of either class", "Peanut Allergy", "Tree Nut Allergy"),
            Scoped("nut", "ground nut", "groundnuts are peanuts (FDA-QA5), no tree nut; read only for Tree Nut Allergy, since peanut types hold it by % nut%", "Tree Nut Allergy"),
            Scoped("nut", "monkey nut", "monkey nuts are peanuts, no tree nut; read only for Tree Nut Allergy", "Tree Nut Allergy"),
            Scoped("nut", "beer nut", "beer nuts are peanuts, no tree nut; read only for Tree Nut Allergy", "Tree Nut Allergy"),
            Scoped("butternut", "butternut squash", "Cucurbita moschata, a squash; no tree nut; read only for the tree nut types, whose %butternut% means the nut of Juglans cinerea", "Tree Nut Allergy", "Nut Allergy"),
            Scoped("flour", "gluten free flour", "a flour blend under 20 ppm gluten (21 CFR 101.91); read only for the gluten types, since a gluten-free food may hold wheat starch (Wheat Allergy still holds it)", GlutenFreeTypes),
            Scoped("bread", "gluten free bread", "bread under 20 ppm gluten (21 CFR 101.91); read only for the gluten types", GlutenFreeTypes),
            Scoped("breadcrumb", "gluten free breadcrumb", "breadcrumbs under 20 ppm gluten (21 CFR 101.91); read only for the gluten types", GlutenFreeTypes),
            Scoped("pasta", "gluten free pasta", "pasta under 20 ppm gluten (21 CFR 101.91); read only for the gluten types", GlutenFreeTypes),
            Scoped("noodle", "gluten free noodle", "noodles under 20 ppm gluten (21 CFR 101.91); read only for the gluten types", GlutenFreeTypes),
            Scoped("spaghetti", "gluten free spaghetti", "spaghetti under 20 ppm gluten (21 CFR 101.91); read only for the gluten types", GlutenFreeTypes),
            Scoped("cracker", "gluten free cracker", "crackers under 20 ppm gluten (21 CFR 101.91); read only for the gluten types", GlutenFreeTypes),
            Scoped("cake", "gluten free cake", "cake under 20 ppm gluten (21 CFR 101.91); read only for the gluten types", GlutenFreeTypes),
            Scoped("cookie", "gluten free cookie", "cookies under 20 ppm gluten (21 CFR 101.91); read only for the gluten types", GlutenFreeTypes),
            Scoped("oat", "gluten free oat", "oats grown and milled apart from wheat, under 20 ppm gluten (21 CFR 101.91; Celiac Disease Foundation); read only for the gluten types", GlutenFreeTypes),
            Scoped("potato", "sweet potato", "Ipomoea batatas, a morning glory (Convolvulaceae), not a nightshade (Solanaceae); read only for the nightshade types, since Jain avoids it as a root and Carnivore as a plant", NightshadeTypes),
            Scoped("sugar", "sugar snap", "sugar snap peas are a pea pod (Pisum sativum var. macrocarpon) with no added sugar; read only for Sugar-Free, since Carnivore means it as a vegetable", "Sugar-Free"),
            Scoped("licorice", "licorice root", "the dried root of Glycyrrhiza glabra, an herb with no grain; the candy, made with wheat flour, is what the gluten types hold %licorice% for (CompositeFoods)", GlutenTypes),
            Scoped("liquorice", "liquorice root", "the dried root of Glycyrrhiza glabra, an herb with no grain; the candy, made with wheat flour, is what the gluten types hold %liquorice% for (CompositeFoods)", GlutenTypes),
        };
    }
}
