using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Nom.Data.Nutrition;
using Nom.Orch.Services;

namespace Nom.Orch.Services.Support
{
    /// <summary>A generic curated food as the branded consensus reads it: its id, its FDC id (none for a seed food), its catalog name, its USDA category and its USDA description (its <c>fdc:description</c> alias, or its name when it has none).</summary>
    public sealed record ConsensusFood(long Id, string? FdcId, string Name, int? CategoryId, string Description);

    /// <summary>One branded product of FoodData Central with the ingredient statement its maker gives: its FDC id, its description, its branded food category and the statement.</summary>
    public sealed record ConsensusProduct(string FdcId, string Description, string? Category, string Statement);

    /// <summary>What a generic food's kind is matched by: the head word of its name (the product it is) and every word of its name that is not a qualifier, singular.</summary>
    public sealed record ConsensusKey(string Head, IReadOnlyList<string> Words);

    /// <summary>One row of <c>Contents/diet-branded-consensus.tsv</c>: a generic curated food by its FDC id (<c>-</c> for a seed food) and its name, since catalog ids differ between databases; a class of what severe types restrict; how its branded products read for it; how many of them were read and how many state the class; and three example FDC ids of products that state it.</summary>
    public sealed record ConsensusRow(string Fdc, string Name, string Class, string Verdict, int Products, int Stating, IReadOnlyList<string> Examples);

    /// <summary>What the branded products matched to one generic food read: how many statements were read, and for each class how many state it, with the FDC ids that do.</summary>
    public sealed class ConsensusTally
    {
        public int Matched { get; set; }
        public int Read { get; set; }
        public Dictionary<string, List<string>> Stating { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>
    /// The contents of a generic made product from the label statements of its branded products (staging checkpoint 7's
    /// diet proof, draws 6 to 17: a curated "Condensed Cream Of Chicken Soup" read clear for Celiac Disease and Wheat
    /// Allergy, since its USDA category and name do not say it is thickened with wheat flour). A curated food that is a
    /// made product (<see cref="IsMadeProduct"/>) is matched to the US branded foods of FoodData Central whose product is
    /// the same kind (<see cref="Match"/>), and each matched product's ingredient statement is read by the prepared-contents
    /// reader and the label terms with no catalog, as the gate reads a statement (<see cref="StatedClasses"/>). For each
    /// class of the diet vocabulary (<see cref="DietVocabulary.Classes"/>), when at least <see cref="MinProducts"/> statements
    /// were read: a share of at least <see cref="UsuallyShare"/> that state the class makes the food
    /// <see cref="Usually"/> contain it, a share above <see cref="ClearShare"/> and below that makes its contents for the
    /// class <see cref="Split"/> (unknown), and a smaller share leaves it as its own data reads. No model reads or decides
    /// anything, and no nutrition number is touched: the rows are an allergen fact from the makers' label statements
    /// (FDC branded data, public domain). The derived rows are committed (<see cref="ResourceName"/>) and read by the diet
    /// gate for the severe criteria only (<see cref="DietFoodReader"/>).
    /// </summary>
    public static class BrandedConsensus
    {
        public const string Version = "branded-consensus/2";
        public const string ResourceName = "Nom.Orch.Contents.diet-branded-consensus.tsv";

        /// <summary>The source a fact of the consensus is journaled with.</summary>
        public const string Source = "fdc-branded:consensus";

        /// <summary>The source of a stored Contains fact a consensus gave (<c>DietFoodFact.Source</c>).</summary>
        public const string FactSource = "consensus";

        /// <summary>The source of a stored Unknown fact a split consensus gave.</summary>
        public const string SplitFactSource = "consensus-split";

        public const string Usually = "usually";
        public const string Split = "split";

        /// <summary>The fewest read statements a consensus is drawn from; fewer leave the food as its own data reads.</summary>
        public const int MinProducts = 10;

        /// <summary>The share of read statements that must state a class for the food to usually contain it.</summary>
        public const double UsuallyShare = 0.75;

        /// <summary>The share of read statements at or below which nearly none state the class, so the food stays as its own data reads.</summary>
        public const double ClearShare = 0.10;

        /// <summary>The share of a generic food's matches a branded category must hold for its products to count, so a category the words only touch does not decide.</summary>
        public const double CategoryShare = 0.20;

        /// <summary>The USDA categories whose foods are made products: Fats and Oils, Soups, Sauces and Gravies, Sausages and Luncheon Meats, Breakfast Cereals, Baked Products, Sweets and Snacks.</summary>
        public static readonly IReadOnlySet<int> MadeCategories = new HashSet<int> { 4, 6, 7, 8, 18, 19, 23 };

        /// <summary>The USDA categories never read: the prepared foods (never a recipe line's food) and the American Indian/Alaska Native foods, which no brand makes.</summary>
        public static readonly IReadOnlySet<int> SkippedCategories = new HashSet<int> { 3, 21, 22, 24, 25 };

        /// <summary>The words of a description that make a food of any other category a made product: canned, dried, pickled, cured, sweetened, a sauce, a paste, a spread, a mix, a condiment, a meatless analogue ("Meatballs, meatless").</summary>
        public static readonly IReadOnlySet<string> Markers = new HashSet<string>(StringComparer.Ordinal)
        {
            "canned", "prepared", "mix", "dried", "dehydrated", "desiccated", "pickled", "pickle", "relish", "cured", "sauce", "paste", "spread", "vinegar",
            "catsup", "ketchup", "sweetened", "condensed", "breaded", "batter", "candied", "glazed", "syrup", "jam", "jelly", "preserve", "marmalade", "chili",
            "molasse", "mustard", "gravy", "soup", "bouillon", "broth", "stock", "margarine", "shortening", "mayonnaise", "dressing", "worcestershire", "meatless",
        };

        /// <summary>Words of a name or description that say how a food is kept, cooked, sold or labelled, not what product it is; they are not matched.</summary>
        public static readonly IReadOnlySet<string> Qualifiers = new HashSet<string>(StringComparer.Ordinal)
        {
            "canned", "commercially", "prepared", "regular", "enriched", "unenriched", "ready", "serve", "dry", "raw", "cooked", "unprepared", "with", "without",
            "water", "made", "from", "recipe", "includes", "include", "solid", "liquid", "added", "low", "sodium", "reduced", "calorie", "diet", "nonfat", "lite",
            "all", "varietie", "variety", "bottled", "frozen", "refrigerated", "shelf", "stable", "eat", "home", "homemade", "commercial", "uncooked", "heated",
            "microwaved", "boiled", "drained", "pan", "fried", "roasted", "broiled", "toasted", "or", "and", "of", "in", "the", "a", "pack", "packed",
            "chilled", "brand", "name", "fl", "oz", "form", "other", "usda", "food", "distribution", "program", "for", "fresh", "to", "on", "as", "at", "by", "per",
        };

        /// <summary>Words of a branded product that make it another form of the food or the food with something on it (dried apricots are not canned apricots, yogurt pretzels not pretzels, a marinated salmon not a canned one); a product holding one its generic food's description does not is not matched.</summary>
        public static readonly IReadOnlySet<string> Forms = new HashSet<string>(StringComparer.Ordinal)
        {
            "dried", "dehydrated", "freeze", "instant", "mix", "powder", "powdered", "frozen", "smoked", "marinated", "marinade", "coated", "covered",
            "dipped", "yogurt", "breaded", "battered", "filled", "stuffed", "wrapped", "pickled", "frosted", "iced", "frosting",
        };

        /// <summary>What a USDA description's "with" part may name and still only qualify the food: salt, skin, an added nutrient.</summary>
        public static readonly IReadOnlySet<string> AddedTrace = new HashSet<string>(StringComparer.Ordinal)
        {
            "salt", "skin", "ascorbic", "acid", "vitamin", "c", "d", "calcium", "iron", "sulfate", "nutrient", "fortified",
        };

        /// <summary>The word each class is probed with: a severe criterion reads a class when it names this word as a line would ("barley" for gluten, so Wheat Allergy, which does not restrict barley, does not read it; "almond" for the tree nuts, so Peanut Allergy's unnamed nut does not read it).</summary>
        public static readonly IReadOnlyDictionary<string, string> Probes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["wheat"] = "wheat",
            ["gluten"] = "barley",
            ["milk"] = "milk",
            ["egg"] = "egg",
            ["soy"] = "soy",
            ["peanut"] = "peanut",
            ["treenut"] = "almond",
            ["fish"] = "fish",
            ["shellfish"] = "shellfish",
            ["sesame"] = "sesame",
            ["sulfite"] = "sulfite",
        };

        private static readonly Regex Word = new("[a-z]+", RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex Bracket = new(@"\([^)]*\)", RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex QualifierPart = new(@"^(with|without|includes?|made with|prepared with|in )", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ProductTail = new(@"\bin\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex MakerWord = new(@"(?<![\p{L}'’])[A-Z][A-Z'’&\-]{2,}(?![\p{L}])", RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex Addition = new(@"\bwith\b|\bw/", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex Home = new(@"\bhome[- ]?(prepared|recipe|preserved|made)\b|\bhomemade\b|\bprepared[- ](from|by)[- ]recipe\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Whether USDA describes a food as made at home ("Potatoes, mashed, home-prepared, whole milk added", "Sauce, homemade, white, medium", "Cake, sponge, prepared from recipe"), not by a maker: a maker's "Home Style" product is not.</summary>
        public static bool HomeMade(string description) => Home.IsMatch(description);

        /// <summary>A word read as its singular, by the plain English endings ("cherries" cherry, "cookies" cookie, "pies" pie, "tomatoes" tomato, "molasses" molasse, "glass" kept).</summary>
        public static string Singular(string w)
        {
            if (w.Length > 3 && w.EndsWith("ies", StringComparison.Ordinal))
                return w.Length <= 4 || w.EndsWith("kies", StringComparison.Ordinal) || w.EndsWith("nies", StringComparison.Ordinal) || w.EndsWith("vies", StringComparison.Ordinal) ? w[..^1] : w[..^3] + "y";
            if (w.Length > 4 && w.EndsWith("oes", StringComparison.Ordinal)) return w[..^2];
            if (w.Length > 3 && w.EndsWith("es", StringComparison.Ordinal) && "sxz".Contains(w[^3]) && !w.EndsWith("ses", StringComparison.Ordinal)) return w[..^2];
            if (w.Length > 3 && w.EndsWith('s') && !w.EndsWith("ss", StringComparison.Ordinal) && !w.EndsWith("us", StringComparison.Ordinal)) return w[..^1];
            return w;
        }

        /// <summary>The words of a text, lower case and singular, a possessive "'s" dropped.</summary>
        public static List<string> Tokens(string? text) =>
            Word.Matches((text ?? string.Empty).ToLowerInvariant().Replace("'s", string.Empty, StringComparison.Ordinal)).Select(m => Singular(m.Value)).ToList();

        /// <summary>Whether a generic food is a made product: of a made category, of no USDA category at all (a food the catalog's seed made by hand, "Dark Chocolate Chips"), or of another category that is not skipped when its description holds a marker word.</summary>
        public static bool IsMadeProduct(int? categoryId, string description)
        {
            if (categoryId == null) return true;
            if (categoryId is { } c && SkippedCategories.Contains(c)) return false;
            if (categoryId is { } m && MadeCategories.Contains(m)) return true;
            return Tokens(description).Any(Markers.Contains);
        }

        /// <summary>
        /// The key of a generic food: a display name of one phrase ("Condensed Cream Of Chicken Soup") gives its words and
        /// its last word as the head; otherwise its USDA description gives the words of its parts, without brackets, without
        /// the parts that only qualify ("with salt added") and without a maker's name in capitals after the first part, and
        /// the last word of its first part as the head ("Soup, cream of chicken, canned, condensed": soup). Qualifiers are
        /// left out of both. Null when nothing is left; for a maker's own product (a description that names its maker or
        /// brand in capitals: "Cereals ready-to-eat, MALT-O-MEAL, BERRY COLOSSAL CRUNCH", "Candies, REESE'S Peanut Butter
        /// Cups"), which other makers' products of its kind do not describe; and for a food with another food in it ("Candies,
        /// milk chocolate, with almonds", "Beans, baked, canned, with franks"), whose products a branded description names only
        /// with the addition. A "with" of salt, skin or an added nutrient (<see cref="AddedTrace"/>) only qualifies.
        /// </summary>
        public static ConsensusKey? KeyOf(string name, string description)
        {
            if (MakerWord.IsMatch(Bracket.Replace(description, " ")) || MakerWord.IsMatch(Bracket.Replace(name, " "))) return null;
            var all = Bracket.Replace(string.IsNullOrWhiteSpace(description) ? name : description, " ").Split(',').Select(p => p.Trim()).ToList();
            if (all.Any(p => p.StartsWith("with ", StringComparison.OrdinalIgnoreCase) && Tokens(p).Any(t => !Qualifiers.Contains(t) && !AddedTrace.Contains(t)))) return null;
            if (!name.Contains(',') && !name.Contains('('))
            {
                var named = Tokens(name).Where(t => !Qualifiers.Contains(t)).ToList();
                return named.Count == 0 ? null : new ConsensusKey(named[^1], named.Distinct(StringComparer.Ordinal).ToList());
            }
            var parts = all.Where(p => p.Length > 0 && !QualifierPart.IsMatch(p)).ToList();
            var keep = new List<string>();
            for (var i = 0; i < parts.Count; i++)
            {
                var letters = new string(parts[i].Where(char.IsLetter).ToArray());
                if (i > 0 && letters.Length > 2 && letters.All(char.IsUpper)) continue;
                keep.Add(parts[i]);
            }
            if (keep.Count == 0) return null;
            var first = Tokens(keep[0]).Where(t => !Qualifiers.Contains(t)).ToList();
            var key = keep.SelectMany(Tokens).Where(t => !Qualifiers.Contains(t)).Distinct(StringComparer.Ordinal).ToList();
            return first.Count == 0 || key.Count == 0 ? null : new ConsensusKey(first[^1], key);
        }

        /// <summary>
        /// The product a branded description names, as words: its first comma part, up to an "in" ("tuna in water" is tuna);
        /// the second part when the first is a maker's name of at most two words that names no product head; and, when that
        /// is a single word, the next part before it (the inverted "SOUP, CREAM OF CHICKEN"). The flavour a description
        /// repeats after its product ("SUGAR COOKIES, SUGAR") is not the product. A product named with an addition ("vanilla
        /// ice cream with cookie dough", "baked beans with molasses") is another product and names none: no words.
        /// </summary>
        public static List<string> ProductWords(string description, IReadOnlySet<string> heads)
        {
            var raw = description.Split(',').Where(p => p.Trim().Length > 0).ToList();
            var parts = raw.Select(p => ProductTail.Split(p)[0]).ToList();
            if (parts.Count == 0) return new List<string>();
            var at = 0;
            var words = Tokens(parts[0]);
            if (parts.Count > 1 && words.Count <= 2 && !words.Any(heads.Contains))
            {
                at = 1;
                words = Tokens(parts[1]);
            }
            var used = new List<int> { at };
            if (words.Count == 1 && parts.Count > 1)
            {
                var next = at == 0 ? 1 : 2;
                if (next < parts.Count)
                {
                    used.Add(next);
                    words = Tokens(parts[next]).Concat(words).ToList();
                }
            }
            return used.Any(i => Addition.IsMatch(raw[i])) ? new List<string>() : words;
        }

        /// <summary>
        /// The branded products of each generic food's kind: a product matches when the product its description names holds
        /// every word of the food's key and ends with the key's head, holds no form word (<see cref="Forms"/>), in its words or as the first word of its branded category ("Frozen Fruit"), that the food's own
        /// description does not, has right before its head (qualifiers aside) no other food's head that the key lacks and no word
        /// naming a class the food's own words do not (<see cref="ClassesNamed"/>) ("rice cakes" are no corn cakes, "vegetable beef soup" no vegetable
        /// soup, "white clam sauce" no white sauce; <paramref name="foodHeads"/>, the head of every curated food's description), names no other food in a later part of three or more words ("SPINACH ALFREDO
        /// MOZZARELLA, ..., ON A THIN CRUST PIZZA" is a pizza), and is not of a whole-meal category (<see cref="PreparedFoods.BrandedMealCategories"/>);
        /// of those, only the products of branded categories that hold at least <see cref="CategoryShare"/> of the food's
        /// matches count.
        /// </summary>
        public static Dictionary<long, List<ConsensusProduct>> Match(IReadOnlyCollection<ConsensusFood> foods, IReadOnlyList<ConsensusProduct> products, IReadOnlySet<string> foodHeads)
        {
            var keys = foods.Select(f => (Food: f, Key: KeyOf(f.Name, f.Description))).Where(x => x.Key != null).ToList();
            var heads = keys.Select(k => k.Key!.Head).ToHashSet(StringComparer.Ordinal);
            var meals = PreparedFoods.BrandedMealCategories.ToHashSet(StringComparer.Ordinal);
            var phrase = new List<string>[products.Count];
            var later = new List<string>[products.Count];
            var index = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            for (var i = 0; i < products.Count; i++)
            {
                phrase[i] = ProductWords(products[i].Description, heads);
                later[i] = products[i].Description.Split(',').Skip(1).Select(Tokens).Where(t => t.Count >= 3 && foodHeads.Contains(t[^1])).Select(t => t[^1]).ToList();
                if (products[i].Category is { } cat && meals.Contains(cat)) continue;
                foreach (var w in phrase[i].Distinct(StringComparer.Ordinal))
                {
                    if (!index.TryGetValue(w, out var list)) index[w] = list = new List<int>();
                    list.Add(i);
                }
            }
            var result = new Dictionary<long, List<ConsensusProduct>>();
            foreach (var (food, key) in keys)
            {
                HashSet<int>? candidates = null;
                foreach (var w in key!.Words.OrderBy(w => index.TryGetValue(w, out var l) ? l.Count : 0))
                {
                    if (!index.TryGetValue(w, out var list)) { candidates = new HashSet<int>(); break; }
                    if (candidates == null) candidates = list.ToHashSet();
                    else candidates.IntersectWith(list);
                    if (candidates.Count == 0) break;
                }
                var own = Tokens(food.Description).Concat(Tokens(food.Name)).Concat(key.Words).ToHashSet(StringComparer.Ordinal);
                var ownClasses = own.SelectMany(ClassesNamed).ToHashSet(StringComparer.Ordinal);
                bool Fits(int i)
                {
                    var p = phrase[i];
                    if (p.Count == 0 || p[^1] != key.Head) return false;
                    if (p.Any(w => Forms.Contains(w) && !own.Contains(w))) return false;
                    if (Tokens(products[i].Category).FirstOrDefault() is { } form && Forms.Contains(form) && !own.Contains(form)) return false;
                    if (later[i].Any(h => !own.Contains(h))) return false;
                    var plain = p.Where(w => !Qualifiers.Contains(w)).ToList();
                    return plain.Count < 2 || own.Contains(plain[^2]) || (!foodHeads.Contains(plain[^2]) && ClassesNamed(plain[^2]).All(ownClasses.Contains));
                }
                var matched = (candidates ?? new HashSet<int>()).Where(Fits).OrderBy(i => i).ToList();
                var byCategory = matched.GroupBy(i => products[i].Category ?? string.Empty).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
                var kept = byCategory.Where(kv => kv.Value >= CategoryShare * matched.Count).Select(kv => kv.Key).ToHashSet(StringComparer.Ordinal);
                result[food.Id] = matched.Where(i => kept.Contains(products[i].Category ?? string.Empty)).Select(i => products[i]).ToList();
            }
            return result;
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, IReadOnlySet<string>> ClassWords = new(StringComparer.Ordinal);

        /// <summary>The classes of the vocabulary a word alone names ("clam" shellfish, "almond" the tree nuts, "mayo" egg), read by the classes' rules as a statement's ingredient is: a product named, right before its head, for a food of a class the generic food's own words do not name is another product ("white clam sauce" is no white sauce, "lemon meringue pie" no lemon pie), whether or not a curated description is headed by that word; one whose word names only what the food's words already name is the same kind ("mayo mayonnaise", "sourdough bread").</summary>
        public static IReadOnlySet<string> ClassesNamed(string word) =>
            ClassWords.GetOrAdd(word, w =>
            {
                var rules = Rules.Value;
                var hit = rules.Matcher.Read(new[] { new DietLineRules.Line(0, 0, 0, w) }, false).Rules.ToHashSet(StringComparer.Ordinal);
                return rules.Classes.Where(c => c.Keys.Overlaps(hit)).Select(c => c.Class).ToHashSet(StringComparer.Ordinal);
            });

        /// <summary>The head of a food's description: the last word of its first part that is not a qualifier ("Rice, white, long-grain": rice; "Beef, ground": beef).</summary>
        public static string? HeadOf(string description)
        {
            var first = Tokens(Bracket.Replace(description, " ").Split(',')[0]).Where(t => !Qualifiers.Contains(t)).ToList();
            return first.Count == 0 ? null : first[^1];
        }

        private sealed record ClassRules(DietLineRules.Matcher Matcher, IReadOnlyList<(string Class, HashSet<string> Keys)> Classes);

        private static readonly Lazy<ClassRules> Rules = new(() =>
        {
            var classes = DietVocabulary.Classes.Select(c => (c.Name, Criteria: c.Name == SulfiteClass ? SulfitingAgents() : DefaultCriteria(c.ReferenceType))).ToList();
            var keys = classes.Select(c => (c.Name, c.Criteria.Select(DietLineRules.KeyOf).OfType<string>().ToHashSet(StringComparer.Ordinal))).ToList();
            return new ClassRules(new DietLineRules.Matcher(DietLineRules.Rules(classes.SelectMany(c => c.Criteria))), keys);
        });

        private const string SulfiteClass = "sulfite";

        /// <summary>The sulfite class is stated by the sulfiting agents a label names (the label terms of the class that name one, 21 CFR 101.100(a)(4) and 182.3616 to 182.3862: sulfites, bisulfites, metabisulfites, sulfur dioxide, sulfured), not by the whole Sulfite Sensitivity type, whose wine and vinegar patterns name what a product is made with, not whether it was sulfited.</summary>
        private static List<DietDataCriterion> SulfitingAgents()
        {
            var type = DietVocabulary.ClassOf(SulfiteClass).ReferenceType;
            var severity = DefaultRestrictionCriteria.ByName[type].Severity;
            var id = 1L;
            return DietVocabulary.All
                .Where(t => t.Class == SulfiteClass && t.Basis == DietVocabularyBasis.Label && (t.Term.Contains("sulf", StringComparison.Ordinal) || t.Term.Contains("sulph", StringComparison.Ordinal)))
                .Select(t => t.Pattern).Distinct(StringComparer.Ordinal)
                .Select(p => new DietDataCriterion(id++, 1, null, null, null, null, severity, null, p, type))
                .ToList();
        }

        private static List<DietDataCriterion> DefaultCriteria(string type)
        {
            var (severity, patterns) = DefaultRestrictionCriteria.ByName[type];
            var id = 1L;
            return patterns.Select(p => DietCriteriaMatcher.IsWordPattern(p)
                    ? new DietDataCriterion(id++, 1, p, null, null, null, severity, null, null, type)
                    : new DietDataCriterion(id++, 1, null, null, null, null, severity, null, p, type))
                .ToList();
        }

        /// <summary>
        /// The classes one statement states, read as the gate reads a prepared meal's statement with no catalog
        /// (<see cref="PreparedContentsGate.RestrictedNames"/> against the default criteria of the class's reference type, by
        /// the line index's rules): an ingredient whose own words, or the words of what its label term contains, name what
        /// the type restricts, or a declared allergen of it with the words of its class. A "may contain" or shared-facility
        /// warning is not a statement of contents and is left out. Null when the statement is not read (unbalanced brackets,
        /// a run-on item, no text): such a product is not counted.
        /// </summary>
        public static IReadOnlySet<string>? StatedClasses(string statement)
        {
            var reading = IngredientStatementReader.Read(statement);
            if (!reading.Read) return null;
            var contents = new IngredientStatementReading { Read = true, Ingredients = reading.Ingredients, Declared = reading.Declared };
            var row = PreparedContentsService.Build(new ContentsStatement(0, Source, statement), statement, contents, new Dictionary<string, long>(), Version, DateTime.UnixEpoch);
            if (row.Outcome != Nom.Data.Recipe.IngredientContentsOutcome.Read) return null;
            var texts = new List<string>();
            var allergens = new List<StatementAllergen>();
            foreach (var item in PreparedContentsService.Items(row))
            {
                texts.Add(item.Name);
                allergens.AddRange((item.LabelTerms()?.SelectMany(t => t.Contains) ?? Array.Empty<string>()).Select(f => new StatementAllergen(f, IngredientStatementReader.ClassOf(f))));
            }
            allergens.AddRange(PreparedContentsService.Allergens(row).Declared);
            var rules = Rules.Value;
            var stated = new HashSet<string>(StringComparer.Ordinal);
            void Read(IEnumerable<string> lines, IReadOnlySet<string>? only)
            {
                var hit = rules.Matcher.Read(lines.Select(t => new DietLineRules.Line(0, 0, 0, t)).ToList(), false).Rules.ToHashSet(StringComparer.Ordinal);
                foreach (var c in rules.Classes)
                    if ((only == null || only.Contains(c.Class)) && c.Keys.Overlaps(hit)) stated.Add(c.Class);
            }
            Read(texts, null);
            foreach (var allergen in allergens.Where(a => !NamesCoconut(a.Word)))
                Read(IngredientStatementReader.GateWords(allergen), allergen.Class is { } major && ClassesOf.TryGetValue(major, out var classes) ? classes : null);
            return stated;
        }

        /// <summary>The vocabulary classes a declared major allergen states: its own only, so a "Contains: tree nuts (almonds)" line is no statement of peanut, although Peanut Allergy reads an unnamed "nuts".</summary>
        private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> ClassesOf = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["milk"] = new HashSet<string> { "milk" },
            ["egg"] = new HashSet<string> { "egg" },
            ["fish"] = new HashSet<string> { "fish" },
            ["shellfish"] = new HashSet<string> { "shellfish" },
            ["tree nuts"] = new HashSet<string> { "treenut" },
            ["peanut"] = new HashSet<string> { "peanut" },
            ["wheat"] = new HashSet<string> { "wheat", "gluten" },
            ["soy"] = new HashSet<string> { "soy" },
            ["sesame"] = new HashSet<string> { "sesame" },
        };

        /// <summary>A declared or contained allergen that is coconut: labels declare it as a tree nut, but the Tree Nut set does not read coconut (the Coconut Allergy type does, as the 2025 FDA list has it), so a coconut product does not state the tree nuts.</summary>
        private static bool NamesCoconut(string word) => Tokens(word).Contains("coconut");

        /// <summary>Counts, for each matched food, its read statements and the products that state each class.</summary>
        public static Dictionary<long, ConsensusTally> Tally(Dictionary<long, List<ConsensusProduct>> matches)
        {
            var stated = new Dictionary<string, IReadOnlySet<string>?>(StringComparer.Ordinal);
            foreach (var p in matches.Values.SelectMany(m => m).DistinctBy(p => p.FdcId).ToList().AsParallel().Select(p => (p.FdcId, Classes: StatedClasses(p.Statement))).ToList())
                stated[p.FdcId] = p.Classes;
            var result = new Dictionary<long, ConsensusTally>();
            foreach (var (food, products) in matches)
            {
                var tally = new ConsensusTally { Matched = products.Count };
                foreach (var p in products.OrderBy(p => long.TryParse(p.FdcId, out var n) ? n : long.MaxValue))
                {
                    if (stated[p.FdcId] is not { } classes) continue;
                    tally.Read++;
                    foreach (var c in classes)
                    {
                        if (!tally.Stating.TryGetValue(c, out var ids)) tally.Stating[c] = ids = new List<string>();
                        ids.Add(p.FdcId);
                    }
                }
                result[food] = tally;
            }
            return result;
        }

        /// <summary>The verdict of one class from its counts: <see cref="Usually"/>, <see cref="Split"/> or null (too few statements, or nearly none state it).</summary>
        public static string? VerdictOf(int read, int stating)
        {
            if (read < MinProducts) return null;
            var share = (double)stating / read;
            if (share >= UsuallyShare) return Usually;
            return share > ClearShare ? Split : null;
        }

        /// <summary>The rows of the consensus for the given foods and their tallies, in food and class order. A food made at home (<see cref="HomeMade"/>) takes from its kind's brands only what they usually state, what the dish is made of (the flour of a pie, the eggs of a sponge cake): where they are split, the split is what each maker adds, and a home recipe's own choice is in its own lines; and it never takes the sulfite class, a preservative a maker adds and a home kitchen does not (refrigerated mashed potatoes with sodium bisulfite are no home-prepared mashed potatoes).</summary>
        public static IReadOnlyList<ConsensusRow> Rows(IEnumerable<ConsensusFood> foods, IReadOnlyDictionary<long, ConsensusTally> tallies)
        {
            var rows = new List<ConsensusRow>();
            foreach (var food in foods.OrderBy(f => f.Name, StringComparer.Ordinal).ThenBy(f => f.Id))
            {
                if (!tallies.TryGetValue(food.Id, out var tally)) continue;
                var home = HomeMade(food.Description);
                foreach (var c in DietVocabulary.Classes)
                {
                    if (home && c.Name == SulfiteClass) continue;
                    var ids = tally.Stating.GetValueOrDefault(c.Name) ?? new List<string>();
                    if (VerdictOf(tally.Read, ids.Count) is { } verdict && (!home || verdict == Usually))
                        rows.Add(new ConsensusRow(string.IsNullOrWhiteSpace(food.FdcId) ? "-" : food.FdcId.Trim(), food.Name, c.Name, verdict, tally.Read, ids.Count, ids.Take(3).ToList()));
                }
            }
            return rows;
        }

        /// <summary>The file's text for the rows, with the comment lines that say how they were derived.</summary>
        public static string Write(IEnumerable<ConsensusRow> rows, string release)
        {
            var text = new System.Text.StringBuilder();
            text.Append("# Branded consensus (").Append(Version).Append("): the contents of generic curated made products from the ingredient statements of their US branded products in FoodData Central (").Append(release).Append(", public domain), derived by Nom.Import --branded-consensus, never edited by hand.\n");
            text.Append("# products: the matched products whose statement was read; stating: those that state the class (the gate's reading of the statement against the default criteria of the class's type, warnings left out); examples: up to three FDC ids that state it.\n");
            text.Append("# verdict: usually when at least ").Append(UsuallyShare.ToString("0.##", CultureInfo.InvariantCulture)).Append(" of at least ").Append(MinProducts)
                .Append(" products state it, split when more than ").Append(ClearShare.ToString("0.##", CultureInfo.InvariantCulture)).Append(" and less than that; a food or class with no row reads as its own data.\n");
            text.Append("fdc\tname\tclass\tverdict\tproducts\tstating\texamples\tsource\n");
            foreach (var r in rows)
                text.Append(r.Fdc).Append('\t').Append(r.Name.Replace('\t', ' ')).Append('\t').Append(r.Class).Append('\t').Append(r.Verdict).Append('\t')
                    .Append(r.Products.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(r.Stating.ToString(CultureInfo.InvariantCulture)).Append('\t')
                    .Append(string.Join(' ', r.Examples)).Append('\t').Append(Source).Append('\n');
            return text.ToString();
        }

        /// <summary>Reads the file: comment lines start with #, the header names the columns; a food and class stand once, each class is a class of the vocabulary and each verdict one of the two.</summary>
        public static IReadOnlyList<ConsensusRow> Parse(string text)
        {
            var rows = new List<ConsensusRow>();
            var seen = new HashSet<(string, string, string)>();
            string[]? header = null;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (line.Length == 0 || line.StartsWith('#')) continue;
                var cells = line.Split('\t');
                if (header == null)
                {
                    header = cells;
                    continue;
                }
                if (cells.Length != header.Length) throw new InvalidDataException($"diet-branded-consensus.tsv: {cells.Length} cells where the header has {header.Length}: {line}");
                var cell = header.Select((h, i) => (h, cells[i].Trim())).ToDictionary(x => x.h, x => x.Item2, StringComparer.Ordinal);
                var row = new ConsensusRow(cell["fdc"], cell["name"], cell["class"], cell["verdict"],
                    int.Parse(cell["products"], CultureInfo.InvariantCulture), int.Parse(cell["stating"], CultureInfo.InvariantCulture),
                    cell["examples"].Split(' ', StringSplitOptions.RemoveEmptyEntries));
                if (!Probes.ContainsKey(row.Class)) throw new InvalidDataException($"diet-branded-consensus.tsv: {row.Class} is no class of the vocabulary");
                if (row.Verdict != Usually && row.Verdict != Split) throw new InvalidDataException($"diet-branded-consensus.tsv: {row.Verdict} is no verdict");
                if (cell["source"] != Source) throw new InvalidDataException($"diet-branded-consensus.tsv: {cell["source"]} is not {Source}");
                if (!seen.Add((row.Fdc, row.Name.ToLowerInvariant(), row.Class))) throw new InvalidDataException($"diet-branded-consensus.tsv: {row.Fdc} {row.Name} {row.Class} stands twice");
                rows.Add(row);
            }
            return rows;
        }

        private static readonly Lazy<IReadOnlyList<ConsensusRow>> Table = new(Load);

        private static IReadOnlyList<ConsensusRow> Load()
        {
            using var stream = typeof(BrandedConsensus).Assembly.GetManifestResourceStream(ResourceName)
                ?? throw new InvalidOperationException($"{ResourceName} is not embedded in Nom.Orch");
            using var reader = new StreamReader(stream);
            return Parse(reader.ReadToEnd());
        }

        /// <summary>Every row of the committed file.</summary>
        public static IReadOnlyList<ConsensusRow> All => Table.Value;

        private static readonly Lazy<Dictionary<string, ConsensusRow[]>> ByName = new(() => All.GroupBy(r => r.Name.Trim().ToLowerInvariant()).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal));

        /// <summary>The rows of a food: those of its name, and of its FDC id when the row has one, so a renamed food is another food until the consensus is derived again.</summary>
        public static IReadOnlyList<ConsensusRow> ForFood(string name, string? fdcId, IReadOnlyList<ConsensusRow>? rows = null)
        {
            var key = name.Trim().ToLowerInvariant();
            var found = rows == null
                ? ByName.Value.GetValueOrDefault(key) ?? Array.Empty<ConsensusRow>()
                : rows.Where(r => r.Name.Trim().ToLowerInvariant() == key).ToArray();
            return found.Where(r => r.Fdc == "-" || string.Equals(r.Fdc, fdcId?.Trim(), StringComparison.Ordinal)).ToList();
        }

        /// <summary>Whether a severe criterion reads a class: it names the class's probe word as a recipe line's own text would.</summary>
        public static bool Reads(DietDataCriterion criterion, string className) =>
            criterion.Severity >= 4 && (criterion.LikePattern != null || criterion.WordPattern != null)
            && Probes.TryGetValue(className, out var probe) && DietCriteriaMatcher.LineNames(criterion, probe);
    }
}
