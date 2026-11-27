using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Nom.Data.Nutrition;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// What a recipe's words mean to a severe restriction set when no curated food decides them (staging checkpoint 7, S3:
    /// four checkpoints each failed on words nobody had listed: sponge-fingers, gaufrettes, a brownie, lo mein, cookies,
    /// Spry, balsamic, dried currants, dried cranberries). A line was safe unless a listed term matched it; it is now safe
    /// only when it is understood. Each food word of the line (its words without amounts, measures, grammar and the
    /// line words of <c>Contents/diet-line-words.tsv</c>) is read against the curated catalog, per set: a word the catalog
    /// knows only on foods the set flags is <see cref="WordClass.Flagged"/> (on a flagged food whose own name or trusted alias
    /// holds a term of the set, the term's word when it is one word; on one whose name holds none, the words of its USDA
    /// description's first part; and the one word of the second part of a description whose first part is a term of the
    /// set, when no clear curated food holds it: "Vinegar, balsamic", "Cookies, brownies"), a word known only on plain
    /// foods the set leaves clear is <see cref="WordClass.Clear"/>, and a word known on both sides
    /// (<see cref="WordClass.Both"/>), or known only on prepared mixtures whose name lists nothing they hold, or not known
    /// at all (<see cref="WordClass.NotKnown"/>), is unknown. Plain foods are the curated foods of the USDA categories of single commodities and
    /// their plain forms (<see cref="PlainCategories"/>); a prepared mixture's clear verdict is no knowledge of its words,
    /// since its name does not list what it is made of ("Restaurant, Chinese, vegetable lo mein" is clear only because its
    /// name says no noodle). A processing fact is read by kind, not by example: the treatment word of a marker term of the
    /// vocabulary (<see cref="DietVocabularyBasis.Marker"/>: "dried apricot" from "Apricots, dried, sulfured") flags any line
    /// that holds it with a head word of a curated food of the categories the marker came from, so the dried form of any
    /// fruit is read for Sulfite Sensitivity whatever food the line is on. The rule is part of <see cref="Version"/>.
    /// </summary>
    public static class DietFoodWords
    {
        public const string Version = "diet-food-words/5";
        public const string LineWordsResource = "Nom.Orch.Contents.diet-line-words.tsv";
        public const string MethodFoodsResource = "Nom.Orch.Contents.diet-method-foods.tsv";
        public const string MethodDishesResource = "Nom.Orch.Contents.diet-method-dishes.tsv";
        public const string SpellingsResource = "Nom.Orch.Contents.diet-spellings.tsv";

        /// <summary>The USDA categories whose curated foods are single commodities or their plain forms (FoodData Central SR Legacy categories): Dairy and Egg Products, Spices and Herbs, Fats and Oils, Poultry, Fruits and Fruit Juices, Pork, Vegetables, Nuts and Seeds, Beef, Beverages, Finfish and Shellfish, Legumes, Lamb, Veal and Game, Cereal Grains and Pasta, and American Indian/Alaska Native Foods. The others (Baby Foods, Soups, Sauces and Gravies, Sausages and Luncheon Meats, Breakfast Cereals, Baked Products, Sweets, Fast Foods, Meals, Entrees and Side Dishes, Snacks, Restaurant Foods) are prepared mixtures.</summary>
        public static readonly IReadOnlySet<int> PlainCategories = new HashSet<int> { 1, 2, 4, 5, 9, 10, 11, 12, 13, 14, 15, 16, 17, 20, 35 };

        /// <summary>The words of English grammar a line or a food name uses beside its food words: articles, pronouns, prepositions, conjunctions, auxiliaries and the like. They name no food.</summary>
        public static readonly IReadOnlySet<string> Grammar = new HashSet<string>(StringComparer.Ordinal)
        {
            "a", "an", "the", "and", "or", "nor", "but", "of", "for", "to", "in", "into", "onto", "on", "at", "by", "with", "without", "from", "as", "than", "then",
            "so", "if", "until", "till", "about", "above", "below", "over", "under", "up", "down", "out", "off", "per", "each", "every", "any", "some", "all", "both",
            "either", "neither", "no", "not", "other", "another", "more", "most", "less", "few", "several", "such", "same", "this", "that", "these", "those", "it",
            "its", "they", "them", "their", "there", "here", "which", "who", "whom", "whose", "what", "when", "where", "while", "be", "is", "are", "was", "were",
            "been", "being", "do", "does", "did", "done", "have", "has", "had", "will", "would", "shall", "should", "may", "might", "can", "could", "must", "my",
            "your", "our", "his", "her", "we", "you", "me", "us", "he", "she", "also", "just", "only", "very", "too", "even", "well", "yet", "still", "again", "etc",
            "plus", "via", "amp", "nbsp", "frac", "sub", "sup", "approx", "approximately", "around", "between", "through", "after", "before", "during", "against",
            "among", "upon", "within", "along", "across", "behind", "beside", "besides", "near", "toward", "towards", "whether", "though", "although", "because",
            "since", "unless", "how", "why", "whatever", "whichever", "much", "many", "enough", "own", "like", "one's", "s", "t", "d", "ll", "re", "ve", "don",
            "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety", "hundred", "thousand",
        };

        private static readonly Regex Letters = new(@"\p{L}+(?:['’]\p{L}+)*", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Lazy<HashSet<string>> Measures = new(() => IngredientLineParser.MeasureWords.ToHashSet(StringComparer.Ordinal));

        private static readonly Lazy<HashSet<string>> LineWordSet = new(() => LineWords.Select(w => w.Word).ToHashSet(StringComparer.Ordinal));

        /// <summary>One word of <c>Contents/diet-line-words.tsv</c>: a word recipe lines use beside their food (chopped, divided, softened, garnish, optional) more often than they name a food by it, that no severe restriction reads, with the distinct curated foods and the lines it stood beside when it was derived, the lines that held it alone and the lines in which it named the food.</summary>
        public sealed record LineWord(string Word, int Foods, int Lines, int Alone, int Own);

        private static readonly Lazy<IReadOnlyList<LineWord>> LineWordTable = new(LoadLineWords);

        /// <summary>The committed line words, in the file's order.</summary>
        public static IReadOnlyList<LineWord> LineWords => LineWordTable.Value;

        /// <summary>One word of <c>Contents/diet-method-foods.tsv</c>: the whole food name of uncurated foods that recipe lines are linked to ("rizotto", "pullet"), with the foods, the distinct recipes whose lines name it and the recipes whose method names it when it was derived.</summary>
        public sealed record MethodFood(string Word, int Foods, int Recipes, int Methods);

        private static readonly Lazy<IReadOnlyList<MethodFood>> MethodFoodTable = new(LoadMethodFoods);

        private static readonly Lazy<Dictionary<string, MethodFood>> MethodFoodSet = new(() => MethodFoodTable.Value.ToDictionary(w => w.Word, StringComparer.Ordinal));

        /// <summary>The share of the recipes whose method names a word that the lines must name for a word no curated food knows to be read as a food in a method (<see cref="IsMethodFood"/>); <c>Contents/diet-method-foods.tsv</c> keeps the words down to half of it, for a word the catalog already knows on a flagged food.</summary>
        public const double MethodFoodShare = 0.2;

        /// <summary>The single-commodity USDA categories whose commodities a word must stand with to be read as a form (<see cref="FormKind"/>).</summary>
        public const int FormCategories = 3;

        /// <summary>One row of <c>Contents/diet-method-dishes.tsv</c>: a dish or preparation the book recipes of the corpus are named for that no curated food knows ("pye", "fricassee"), with the book recipes whose title ends with it and those whose method names it when it was derived.</summary>
        public sealed record MethodDish(string Word, int Titles, int Methods);

        private static readonly Lazy<IReadOnlyList<MethodDish>> MethodDishTable = new(() => ParseMethodDishes(Resource(MethodDishesResource)));

        private static readonly Lazy<HashSet<string>> MethodDishSet = new(() => MethodDishTable.Value.Select(d => d.Word).ToHashSet(StringComparer.Ordinal));

        /// <summary>The committed method dishes, in the file's order.</summary>
        public static IReadOnlyList<MethodDish> MethodDishes => MethodDishTable.Value;

        /// <summary>Whether a word is a dish the book recipes of the corpus are named for that no curated food knows (<see cref="MethodDishes"/>): a method that names it where no line carries it makes a dish the lines do not list (review ck16-4: 68410's "Make a pye according to this form").</summary>
        public static bool IsMethodDish(string word) => MethodDishSet.Value.Contains(word);

        /// <summary>One row of <c>Contents/diet-spellings.tsv</c>: an old or variant spelling of the corpus and the curated catalog's word it is read as, with the live lines that held it, their recipes, those on a curated food and those on a curated food that holds the word when it was derived.</summary>
        public sealed record Spelling(string Variant, string Word, int Lines, int Recipes, int Curated, int WordLines);

        private static readonly Lazy<IReadOnlyList<Spelling>> SpellingTable = new(() => ParseSpellings(Resource(SpellingsResource)));

        private static readonly Lazy<Dictionary<string, string>> SpellingSet = new(() => SpellingTable.Value.ToDictionary(v => v.Variant, v => v.Word, StringComparer.Ordinal));

        /// <summary>The committed spellings, in the file's order.</summary>
        public static IReadOnlyList<Spelling> Spellings => SpellingTable.Value;

        /// <summary>The committed method foods, in the file's order.</summary>
        public static IReadOnlyList<MethodFood> MethodFoods => MethodFoodTable.Value;

        /// <summary>Whether a word is the whole name of a food the recipe corpus names that no curated food holds (<see cref="MethodFoods"/>): a method that names it where no line carries it names a food the lines lost (review ck16-2: 133979 Poularde Valencienne's "garnish of rizotto").</summary>
        public static bool IsMethodFood(string word) => IsMethodFood(word, MethodFoodShare);

        /// <summary>Whether a word of <see cref="MethodFoods"/> has at least <paramref name="share"/> of the recipes whose method names it naming it in their lines.</summary>
        public static bool IsMethodFood(string word, double share) =>
            MethodFoodSet.Value.TryGetValue(word, out var food) && food.Recipes >= share * food.Methods;

        /// <summary>The food words of a method sentence a recipe's lines do not carry (<paramref name="carried"/>): its <see cref="Words"/> without the terms a method is not read for (<see cref="DietRecipeText.MethodSkips"/>).</summary>
        public static IEnumerable<string> MethodWords(string? sentence, IReadOnlySet<string> carried) => MethodPairs(sentence, carried).Select(p => p.Word);

        /// <summary>The <see cref="MethodWords"/> of a method sentence, each with the head of the words right after it in the text (only a space or a hyphen between, up to three words, the last of them), normalised, when it is no grammar or measure ("sponge" before "cake", "soy" before "sauce" in "adding soy bean sauce": proof draw 10 of ck17, 136720 Pei Yu; "mince" before nothing in "mince the onion").</summary>
        public static IEnumerable<(string Word, string? Next)> MethodPairs(string? sentence, IReadOnlySet<string> carried)
        {
            if (string.IsNullOrWhiteSpace(sentence)) yield break;
            var matches = Letters.Matches(sentence);
            for (var i = 0; i < matches.Count; i++)
            {
                var m = matches[i];
                var raw = DietPatternRule.Fold(m.Value).ToLowerInvariant().Replace('’', '\'');
                if (raw.Length < 2 || Grammar.Contains(raw) || Measures.Value.Contains(raw)) continue;
                var w = Normal(raw);
                if (w.Length < 2 || Grammar.Contains(w) || Measures.Value.Contains(w) || LineWordSet.Value.Contains(w)) continue;
                if (carried.Contains(w) || MethodSkipSet.Value.Contains(w)) continue;
                string? next = null;
                for (var j = i + 1; j < matches.Count && j <= i + 3 && sentence[(matches[j - 1].Index + matches[j - 1].Length)..matches[j].Index].All(c => c == ' ' || c == '-'); j++)
                {
                    var after = Normal(matches[j].Value);
                    if (Grammar.Contains(after) || Measures.Value.Contains(after)) break;
                    next = after;
                }
                yield return (w, next);
            }
        }

        private static readonly Lazy<HashSet<string>> MethodSkipSet = new(() => DietRecipeText.MethodSkips.Select(Normal).ToHashSet(StringComparer.Ordinal));

        /// <summary>A word as the reading keeps it: folded, lower case, its last letters read as a singular (<see cref="DietVocabulary.Singular"/>), its possessive dropped, and an old or variant spelling read as its catalog word (<see cref="Spellings"/>: "cocoanut" is "coconut").</summary>
        public static string Normal(string word)
        {
            var w = DietPatternRule.Fold(word).ToLowerInvariant().Replace('’', '\'');
            if (w.EndsWith("'s", StringComparison.Ordinal)) w = w[..^2];
            w = w.Replace("'", string.Empty);
            w = DietVocabulary.Singular(w);
            return SpellingSet.Value.TryGetValue(w, out var spelt) ? spelt : w;
        }

        /// <summary>A text with each pair of words a hyphen joins written as one word where that word is a term of the vocabulary ("pea-nuts" is peanuts, so the Tree Nut set does not read its "nuts": staging checkpoint 8, T9), each old or variant spelling of <see cref="Spellings"/> written as its catalog word, so that the restriction patterns read it as they read the word ("grated cocoanut" is read as "grated coconut"), in the spelling's own case ("Prepare Cocoanut Pudding." is read as "Prepare Coconut Pudding.", so the referral rules still read a capitalised recipe name: review ck17dpr-1), and without the sizes it gives by a food ("a walnut-sized piece of butter", "butter the size of an egg": proof draw 14 of ck17, 74666 The Exquisite, held for tree nuts by its walnut; any nut, "about half the size of a hickory-nut": staging checkpoint 8, T9); null stays null.</summary>
        public static string? Respell(string? text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            text = SizeOf.Replace(text, " ");
            text = HyphenPair.Replace(text, m => HyphenTerms.Value.Contains(DietVocabulary.Singular((m.Groups[1].Value + m.Groups[2].Value).ToLowerInvariant())) ? m.Groups[1].Value + m.Groups[2].Value : m.Value);
            if (SpellingSet.Value.Count == 0) return text;
            return Letters.Replace(text, m =>
            {
                var w = DietPatternRule.Fold(m.Value).ToLowerInvariant().Replace('’', '\'');
                if (w.EndsWith("'s", StringComparison.Ordinal)) w = w[..^2];
                w = DietVocabulary.Singular(w.Replace("'", string.Empty));
                return SpellingSet.Value.TryGetValue(w, out var spelt) ? CasedAs(m.Value, spelt) : m.Value;
            });
        }

        private static readonly Regex HyphenPair = new(@"(?<![\p{L}])(\p{L}+)-(\p{L}+)(?![\p{L}-])", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Lazy<HashSet<string>> HyphenTerms = new(() => DietVocabulary.All.Select(t => t.Term.Trim().ToLowerInvariant()).Where(t => t.Length > 0 && t.All(char.IsLetter)).Select(DietVocabulary.Singular).ToHashSet(StringComparer.Ordinal));

        private static string CasedAs(string original, string word)
        {
            if (original.Length > 1 && original.All(c => !char.IsLetter(c) || char.IsUpper(c))) return word.ToUpperInvariant();
            return char.IsUpper(original[0]) ? char.ToUpperInvariant(word[0]) + word[1..] : word;
        }

        private static readonly Regex SizeOf = new(@"(?<![\p{L}])(?:(?:about\s+)?(?:the|a)\s+)?(?:size|bigness)\s+of\s+(?:a|an|one|two)\s+(?:small\s+|large\s+|good[\s-]sized\s+|hen'?s\s+|hen’s\s+|pullet'?s\s+)?(?:\p{L}*[\s-]?nut|egg|filbert|nutmeg|pea|bean|almond)s?(?![\p{L}])|(?<![\p{L}])(?:\p{L}*nut|egg|filbert)[\s-]+sized?(?![\p{L}])|(?<![\p{L}])as\s+(?:big|large)\s+as\s+(?:a|an)\s+(?:small\s+|large\s+)?(?:walnut|nut|egg|nutmeg|pea|hazelnut|filbert|chestnut)s?(?![\p{L}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>Every word of a text, normalised (<see cref="Normal"/>), without its amounts and measures and the grammar of English (<see cref="Grammar"/>); the line words are kept (a food name reads them too).</summary>
        public static List<string> AllWords(string? text)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return result;
            foreach (Match m in Letters.Matches(text))
            {
                var raw = DietPatternRule.Fold(m.Value).ToLowerInvariant().Replace('’', '\'');
                if (raw.Length < 2 || Grammar.Contains(raw) || Measures.Value.Contains(raw)) continue;
                var w = Normal(raw);
                if (w.Length < 2 || Grammar.Contains(w) || Measures.Value.Contains(w)) continue;
                result.Add(w);
            }
            return result;
        }

        /// <summary>The food words of a recipe line: <see cref="AllWords"/> without the line words.</summary>
        public static List<string> Words(string? text)
        {
            var words = AllWords(text);
            words.RemoveAll(LineWordSet.Value.Contains);
            return words;
        }

        /// <summary>
        /// A form: a word the curated catalog uses for the shape or the dish a food is made into rather than for a food
        /// (staging checkpoint 7, review ck16-4: 77479 Turkey Patties, whose line "patties" means pastry shells, read clear
        /// because of beef patties). A form is a noun (it ends the first part of a curated food's description) that is no
        /// food of its own (it is not the whole first part of a curated food of a single-commodity category) and stands with
        /// the commodities of at least <see cref="FormCategories"/> single-commodity categories (as the varieties of
        /// "Puddings, banana", "Corn pudding", "Rice puddings"; or as the one word of a later part of "Beef, ground, patty",
        /// "Turkey, ground, patties", "Pork, cured, ham, patties"). What its clear foods say is no knowledge of what a line
        /// that names it is made of, so a severe set never reads it as clear.
        /// </summary>
        public enum FormKind : byte
        {
            /// <summary>Not a form.</summary>
            None = 0,

            /// <summary>A shape a commodity is made into ("patty", "breast", "loin"): alone in a line it is not understood; beside a word that is ("beef patties"), the line is read by that word.</summary>
            Shape = 1,

            /// <summary>A form that is also what a prepared mixture of the catalog is: the whole first part of the description of a curated food of a prepared category ("Puddings, rice", "Pie, apple", "Rolls, dinner", "Sauce, pasta", "Bread, white"): it is never understood, so a line that names it is unknown ("rice pudding"), while a form that only ends a longer first part ("Luncheon meat", "Chicken breast tenders") is a shape (ck17 proof: read as dishes, meat, breast, plain and green held 1,800 to 6,100 type-recipes whose other words were understood).</summary>
            Dish = 2,
        }

        /// <summary>How a set reads one word.</summary>
        public enum WordClass : byte
        {
            /// <summary>No curated food knows the word, or only a prepared mixture does.</summary>
            NotKnown = 0,

            /// <summary>Known only on plain foods the set leaves clear.</summary>
            Clear = 1,

            /// <summary>Known only on foods the set flags.</summary>
            Flagged = 2,

            /// <summary>Known on foods the set flags and on plain foods it leaves clear.</summary>
            Both = 3,

            /// <summary>A one-word term of the set, or the treatment word of one of its kinds: its own rule reads it, not the word reading.</summary>
            Ruled = 4,
        }

        /// <summary>A curated food as the vocabulary reads it: its name, its USDA category, its USDA description, if it has one, and its other trusted aliases.</summary>
        public sealed record Food(long Id, string Name, int? CategoryId, string? Description, IReadOnlyList<string>? Aliases = null);

        /// <summary>A processing kind of a set: the treatment word and the categories whose curated foods' head words it flags with ("dried" and Fruits and Fruit Juices).</summary>
        public sealed record Kind(string Word, IReadOnlySet<int> Categories);

        /// <summary>The kinds of the vocabulary's marker terms of two or more words whose pattern is among <paramref name="likePatterns"/> (a set that reads "%dried apricot%" reads the dried form of any fruit).</summary>
        public static IReadOnlyList<Kind> KindsOf(IEnumerable<string> likePatterns)
        {
            var patterns = likePatterns.ToHashSet(StringComparer.OrdinalIgnoreCase);
            return DietVocabulary.All
                .Where(t => t.Basis == DietVocabularyBasis.Marker && patterns.Contains(t.Pattern) && t.Term.Contains(' '))
                .GroupBy(t => Normal(t.Term[..t.Term.LastIndexOf(' ')]), StringComparer.Ordinal)
                .Select(g => new Kind(g.Key, g.SelectMany(t => t.Categories.Split('|', StringSplitOptions.RemoveEmptyEntries)).Select(c => int.Parse(c, CultureInfo.InvariantCulture)).ToHashSet()))
                .OrderBy(k => k.Word, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>The words of a term (a name pattern's words, or the letters of a word pattern), normalised.</summary>
        public static List<string> TermWords(DietDataCriterion criterion)
        {
            if (criterion.Rule is { } rule)
                return rule.Substring ? AllWords(rule.Pattern.Replace('%', ' ').Replace('_', ' ')) : rule.Words.SelectMany(AllWords).ToList();
            if (criterion.WordPattern is { } pattern)
            {
                var literal = Regex.Replace(pattern, @"\\[mMyY]", " ");
                var open = literal.IndexOfAny(new[] { '(', '[', '|', '?', '*', '+', '{', '\\' });
                return AllWords(open < 0 ? literal : literal[..open]);
            }
            return new List<string>();
        }

        /// <summary>
        /// The words a set's reading needs, shared by every set of a cycle: each word of a curated food's name and USDA
        /// description gets an index, and the head words of each USDA category (the first part of the description, or of the
        /// name) are kept for the kinds.
        /// </summary>
        public sealed class Table
        {
            private readonly Dictionary<string, int> _index = new(StringComparer.Ordinal);
            private readonly List<string> _words = new();

            internal Table(IEnumerable<Food> curated)
            {
                Foods = curated.OrderBy(f => f.Id).ToList();
                FoodWords = new int[Foods.Count][];
                FoodHeads = new int[Foods.Count][];
                FoodKinds = new int[Foods.Count][];
                var heads = new Dictionary<int, HashSet<int>>();
                var commodities = new Dictionary<int, HashSet<int>>();
                var varieties = new Dictionary<int, HashSet<int>>();
                var nouns = new HashSet<int>();
                var dishes = new HashSet<int>();
                void Variety(int form, int of)
                {
                    if (form == of) return;
                    if (!varieties.TryGetValue(form, out var set)) varieties[form] = set = new HashSet<int>();
                    set.Add(of);
                }
                for (var i = 0; i < Foods.Count; i++)
                {
                    var food = Foods[i];
                    FoodWords[i] = AllWords(food.Name).Concat(AllWords(food.Description)).Concat((food.Aliases ?? Array.Empty<string>()).SelectMany(AllWords))
                        .Distinct(StringComparer.Ordinal).Select(Add).ToArray();
                    var source = !string.IsNullOrWhiteSpace(food.Description) ? food.Description! : food.Name;
                    var parts = Regex.Replace(source, @"\([^)]*\)", " ").Split(',');
                    var first = AllWords(parts[0]).Select(Add).ToList();
                    FoodHeads[i] = first.Distinct().ToArray();
                    FoodKinds[i] = parts.Length > 1 ? AllWords(parts[1]).Distinct(StringComparer.Ordinal).Select(Add).ToArray() : Array.Empty<int>();
                    var second = parts.Length > 1 ? AllWords(parts[1]).Select(Add).ToList() : new List<int>();
                    if (first.Count == 1 && food.CategoryId is { } plain && PlainCategories.Contains(plain))
                    {
                        if (!commodities.TryGetValue(first[0], out var of)) commodities[first[0]] = of = new HashSet<int>();
                        of.Add(plain);
                    }
                    if (first.Count > 0) nouns.Add(first[^1]);
                    if (first.Count == 1 && food.CategoryId is { } made && !PlainCategories.Contains(made)) dishes.Add(first[^1]);
                    if (first.Count == 1)
                        foreach (var w in second) Variety(first[0], w);
                    else if (first.Count > 1)
                        foreach (var w in first.Take(first.Count - 1)) Variety(first[^1], w);
                    if (first.Count > 0)
                        foreach (var part in parts.Skip(1).Select(AllWords).Where(p => p.Count == 1)) Variety(Add(part[0]), first[0]);
                    if (food.CategoryId is not { } category) continue;
                    if (!heads.TryGetValue(category, out var set)) heads[category] = set = new HashSet<int>();
                    foreach (var w in FoodHeads[i]) set.Add(w);
                }
                Heads = heads.ToDictionary(kv => kv.Key, kv => (IReadOnlySet<int>)kv.Value);
                Forms = new FormKind[_words.Count];
                foreach (var w in MethodSkipSet.Value.Select(IndexOf).Where(w => w >= 0))
                    Forms[w] = FormKind.Shape;
                foreach (var (w, of) in varieties)
                    if (nouns.Contains(w) && !commodities.ContainsKey(w) && of.Where(commodities.ContainsKey).SelectMany(v => commodities[v]).Distinct().Count() >= FormCategories)
                        Forms[w] = dishes.Contains(w) ? FormKind.Dish : FormKind.Shape;
            }

            /// <summary>Per word index, the form it is, if any (<see cref="FormKind"/>).</summary>
            internal FormKind[] Forms { get; }

            /// <summary>The curated foods, by id.</summary>
            public IReadOnlyList<Food> Foods { get; }

            internal int[][] FoodWords { get; }

            internal int[][] FoodHeads { get; }

            internal int[][] FoodKinds { get; }

            internal IReadOnlyDictionary<int, IReadOnlySet<int>> Heads { get; }

            public int Count => _words.Count;

            private int Add(string word)
            {
                if (_index.TryGetValue(word, out var id)) return id;
                id = _words.Count;
                _index[word] = id;
                _words.Add(word);
                return id;
            }

            private Dictionary<long, HashSet<int>>? _byFood;

            /// <summary>Whether every food word of a line (<paramref name="words"/>) is a word of the curated food it is on: a line that names more than its food ("Basic Rabbit" on Rabbit, "Broiled protose" on coconut meat) is not that food alone.</summary>
            public bool Names(long foodId, int[] words)
            {
                _byFood ??= Foods.Select((f, i) => (f.Id, i)).ToDictionary(x => x.Id, x => FoodWords[x.i].ToHashSet());
                return _byFood.TryGetValue(foodId, out var own) && words.All(own.Contains);
            }

            /// <summary>The index of a word, or -1 when no curated food holds it.</summary>
            public int IndexOf(string word) => _index.TryGetValue(word, out var id) ? id : -1;

            /// <summary>The word of an index.</summary>
            public string WordAt(int index) => _words[index];

            /// <summary>The indexes of a line's food words (<see cref="Words"/>), -1 for a word no curated food holds.</summary>
            public int[] Read(string? text) => Words(text).Select(IndexOf).ToArray();
        }

        /// <summary>The table of a cycle's curated foods.</summary>
        public static Table TableOf(IEnumerable<Food> curated) => new(curated);

        /// <summary>What a set reads in a line, with the word that decided it (the flagged or unknown word, or the kind's treatment word).</summary>
        public readonly record struct LineReading(WordClass Value, int Word);

        /// <summary>One set's reading of words: a class per word of the table, its one-word terms and its kinds.</summary>
        public sealed class Vocabulary
        {
            private readonly WordClass[] _classes;
            private readonly (int Word, IReadOnlySet<int> Heads)[] _kinds;
            private readonly bool[] _named;
            private readonly bool[] _methodFoods;
            private readonly bool[] _beside;
            private readonly bool[] _besideFoods;
            private readonly FormKind[] _forms;

            internal Vocabulary(WordClass[] classes, (int Word, IReadOnlySet<int> Heads)[] kinds, bool[] named, bool[] methodFoods, bool[] beside, bool[] besideFoods, FormKind[] forms)
            {
                _forms = forms;
                _besideFoods = besideFoods;
                _classes = classes;
                _kinds = kinds;
                _named = named;
                _methodFoods = methodFoods;
                _beside = beside;
            }

            /// <summary>
            /// Whether a method word the recipe's lines do not carry holds the recipe as unknown (staging checkpoint 7, S4; review
            /// ck16-2 and ck16-5): a word flagged or known on both sides only where it is what a flagged food is (the one word
            /// of its USDA description's first part: "wafer" of "Wafers, vanilla", "candy" of "Candies, ..."), not a word of a
            /// first part of more ("leavening" and "agent" of "Leavening agents", "ice" of "Ice creams", "ring" of "Onion rings")
            /// or of the second part ("evaporated" of "Milk, evaporated", "mince" of "Pie, mince"), which a
            /// method uses as a verb or a tool far more often than as a food, unless it stands right before a food word the set
            /// does not read as clear (<paramref name="next"/>: "slices of sponge cake", "hand sponge fingers with it", "dinner
            /// rolls", but not "mince the onion", "a brick oven", "whipped whites" or "ice water"), or, for the one word of a
            /// description's second part, when the corpus names it as a food at half the share asked of an unknown word
            /// (<see cref="MethodFoodShare"/>: "add cider, scalding hot" of "Vinegar, cider"); and a word no curated food
            /// knows as more than a prepared mixture's that is the whole name of a food of the corpus (<see cref="IsMethodFood"/>).
            /// A dish the book recipes are named for that no curated food holds at all ("make a pye", <see cref="IsMethodDish"/>)
            /// is read by the recipe's reading, not here; a dish the catalog knows ("pudding", "pie", "soup") is read by its class
            /// like any word, since a method names the dish it makes and the dishes it is served with alike (ck17 proof:
            /// 3,000 to 4,800 type-recipes held per type by "serve with the pudding" and "add to the soup").
            /// </summary>
            public bool HoldsInMethod(int word, int next = -1)
            {
                if (word < 0 || word >= _classes.Length) return false;
                return _classes[word] switch
                {
                    WordClass.Flagged or WordClass.Both => _named[word] || (_beside[word] && _besideFoods[word]) || (next >= 0 && next != word && ClassOf(next) != WordClass.Clear),
                    WordClass.NotKnown => _methodFoods[word],
                    _ => false,
                };
            }

            /// <summary>The form a word the set would otherwise read as clear is (<see cref="FormKind"/>): a dish it reads as not understood, a shape it reads as not understood when no other food word of the line is.</summary>
            public FormKind FormOf(int word) => word >= 0 && word < _forms.Length ? _forms[word] : FormKind.None;

            /// <summary>The class of a word index (<see cref="WordClass.NotKnown"/> for -1).</summary>
            public WordClass ClassOf(int word) => word < 0 || word >= _classes.Length ? WordClass.NotKnown : _classes[word];

            /// <summary>Whether a line's words hold a kind: its treatment word with a head word of the kind's categories.</summary>
            public int KindIn(int[] words)
            {
                foreach (var (word, heads) in _kinds)
                    if (Array.IndexOf(words, word) >= 0 && words.Any(w => w != word && heads.Contains(w))) return word;
                return -1;
            }

            /// <summary>
            /// The reading of a line's food words: <see cref="WordClass.Flagged"/> when a word is flagged or a kind is held,
            /// <see cref="WordClass.Unknown"/>-like (<see cref="WordClass.NotKnown"/> or <see cref="WordClass.Both"/>, the first
            /// such word) when a word is not understood or the line names no food word at all ("one cupful of chopped": the extraction lost its food), otherwise <see cref="WordClass.Clear"/> (a line whose words are all ruled is clear: its rules decide it).
            /// </summary>
            public LineReading Read(int[] words)
            {
                var kind = KindIn(words);
                if (kind >= 0) return new LineReading(WordClass.Flagged, kind);
                if (words.Length == 0) return new LineReading(WordClass.NotKnown, -1);
                var unknown = -2;
                var unknownClass = WordClass.Clear;
                var shape = -1;
                var understood = false;
                foreach (var w in words)
                {
                    var c = ClassOf(w);
                    if (c == WordClass.Flagged) return new LineReading(WordClass.Flagged, w);
                    if (unknown == -2 && c is WordClass.NotKnown or WordClass.Both)
                    {
                        unknown = w;
                        unknownClass = c;
                    }
                    if (c != WordClass.Clear) continue;
                    if (FormOf(w) == FormKind.Shape) shape = shape < 0 ? w : shape;
                    else understood = true;
                }
                if (unknown != -2) return new LineReading(unknownClass, unknown);
                return shape >= 0 && !understood ? new LineReading(WordClass.NotKnown, shape) : new LineReading(WordClass.Clear, -1);
            }

            /// <summary>The signature of the set's reading: every word's class and the kinds.</summary>
            internal void AddTo(DietFoodCatalog.Fnv hash, Table table)
            {
                for (var i = 0; i < _classes.Length; i++)
                    if (_classes[i] != WordClass.NotKnown || _methodFoods[i] || _forms[i] != FormKind.None) hash.Add(table.WordAt(i)).Add((long)_classes[i]).Add(HoldsInMethod(i) ? 1L : 0L).Add((long)_forms[i]);
                foreach (var (word, heads) in _kinds)
                {
                    hash.Add("kind").Add(table.WordAt(word));
                    foreach (var h in heads.OrderBy(h => h)) hash.Add(table.WordAt(h));
                }
            }
        }

        /// <summary>The words that name a composite whose name does not list what it holds ("Salad dressing, ...", "Margarine, ...", "Catsup"): a curated food of a single-commodity category does not make such a word clear, as a prepared mixture's clear verdict is no knowledge of its words (draw 21 of ck18: "salad dressing" read clear for Egg Allergy); its other words it clears as before, and a composite word flagged elsewhere is known on both sides.</summary>
        public static readonly IReadOnlySet<string> CompositeWords = new HashSet<string>(StringComparer.Ordinal)
        {
            "sauce", "spread", "dressing", "mayonnaise", "margarine", "shortening", "ketchup", "catsup", "relish", "gravy", "soup",
            "mix", "worcestershire", "meatless", "batter", "breaded",
        };

        /// <summary>
        /// A set's vocabulary from the table: <paramref name="flagged"/> says whether the set flags a curated food (by its index
        /// in <see cref="Table.Foods"/>), <paramref name="terms"/> gives the words of each of the set's terms its name holds,
        /// <paramref name="oneWordTerms"/> the set's terms of one word, and <paramref name="kinds"/> its kinds.
        /// </summary>
        public static Vocabulary Build(Table table, Func<int, bool> flagged, Func<int, IReadOnlyList<IReadOnlyList<string>>> terms, IReadOnlySet<string> oneWordTerms, IReadOnlyList<Kind> kinds)
        {
            var count = table.Count;
            var flag = new bool[count];
            var beside = new bool[count];
            var clear = new bool[count];
            var composite = new bool[count];
            var cleared = new bool[count];
            var nouns = new bool[count];
            for (var i = 0; i < table.Foods.Count; i++)
            {
                var words = table.FoodWords[i];
                if (flagged(i))
                {
                    var named = terms(i);
                    if (table.FoodHeads[i].Length == 1) nouns[table.FoodHeads[i][0]] = true;
                    if (named.Count == 0)
                    {
                        foreach (var w in table.FoodHeads[i]) flag[w] = true;
                        continue;
                    }
                    var termWords = new HashSet<int>();
                    foreach (var term in named)
                        foreach (var t in term.Where(oneWordTerms.Contains))
                            if (table.IndexOf(t) is var id and >= 0) termWords.Add(id);
                    foreach (var w in termWords) flag[w] = true;
                    if (table.FoodHeads[i].Length == 1 && termWords.Contains(table.FoodHeads[i][0]) && table.FoodKinds[i].Length == 1)
                        beside[table.FoodKinds[i][0]] = true;
                }
                else
                {
                    foreach (var w in words) cleared[w] = true;
                    if (table.Foods[i].CategoryId is { } category && PlainCategories.Contains(category))
                        foreach (var w in words)
                            if (CompositeWords.Contains(table.WordAt(w))) composite[w] = true;
                            else clear[w] = true;
                }
            }
            var classes = new WordClass[count];
            for (var w = 0; w < count; w++)
                classes[w] = flag[w] && (clear[w] || composite[w]) ? WordClass.Both
                    : flag[w] ? WordClass.Flagged
                    : clear[w] ? WordClass.Clear
                    : beside[w] && !cleared[w] ? WordClass.Flagged
                    : WordClass.NotKnown;
            foreach (var t in oneWordTerms)
                if (table.IndexOf(t) is var id and >= 0) classes[id] = WordClass.Ruled;
            var kindList = new List<(int, IReadOnlySet<int>)>();
            foreach (var kind in kinds)
            {
                if (table.IndexOf(kind.Word) is not (var id and >= 0)) continue;
                classes[id] = WordClass.Ruled;
                var heads = kind.Categories.SelectMany(c => table.Heads.TryGetValue(c, out var h) ? h : (IEnumerable<int>)Array.Empty<int>()).Where(h => h != id).ToHashSet();
                kindList.Add((id, heads));
            }
            var methodFoods = new bool[count];
            var besideFoods = new bool[count];
            for (var w = 0; w < count; w++)
            {
                methodFoods[w] = IsMethodFood(table.WordAt(w));
                besideFoods[w] = IsMethodFood(table.WordAt(w), MethodFoodShare / 2);
            }
            var forms = new FormKind[count];
            for (var w = 0; w < count; w++)
            {
                if (classes[w] != WordClass.Clear) continue;
                forms[w] = table.Forms[w];
                if (forms[w] == FormKind.Dish) classes[w] = WordClass.NotKnown;
            }
            return new Vocabulary(classes, kindList.ToArray(), nouns, methodFoods, beside, besideFoods, forms);
        }

        private static string Resource(string name)
        {
            using var stream = typeof(DietFoodWords).Assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"{name} is not embedded in Nom.Orch");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        private static List<Dictionary<string, string>> Rows(string text, string file)
        {
            var rows = new List<Dictionary<string, string>>();
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
                if (cells.Length != header.Length) throw new InvalidDataException($"{file}: {cells.Length} cells where the header has {header.Length}: {line}");
                rows.Add(header.Select((h, i) => (h, cells[i].Trim())).ToDictionary(x => x.h, x => x.Item2, StringComparer.Ordinal));
            }
            return rows;
        }

        /// <summary>Reads the method dishes file: comment lines start with #, the header names the columns; a word stands once.</summary>
        public static IReadOnlyList<MethodDish> ParseMethodDishes(string text)
        {
            var all = new List<MethodDish>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in Rows(text, "diet-method-dishes.tsv"))
            {
                var dish = new MethodDish(row["word"], int.Parse(row["titles"], CultureInfo.InvariantCulture), int.Parse(row["methods"], CultureInfo.InvariantCulture));
                if (!seen.Add(dish.Word)) throw new InvalidDataException($"diet-method-dishes.tsv: {dish.Word} stands twice");
                all.Add(dish);
            }
            return all;
        }

        /// <summary>Reads the spellings file: comment lines start with #, the header names the columns; a spelling stands once and is never itself the word of another.</summary>
        public static IReadOnlyList<Spelling> ParseSpellings(string text)
        {
            var all = new List<Spelling>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            int Number(Dictionary<string, string> row, string name) => int.Parse(row[name], CultureInfo.InvariantCulture);
            foreach (var row in Rows(text, "diet-spellings.tsv"))
            {
                var spelling = new Spelling(row["spelling"], row["word"], Number(row, "lines"), Number(row, "recipes"), Number(row, "curated"), Number(row, "word lines"));
                if (!seen.Add(spelling.Variant)) throw new InvalidDataException($"diet-spellings.tsv: {spelling.Variant} stands twice");
                all.Add(spelling);
            }
            foreach (var spelling in all.Where(v => seen.Contains(v.Word)))
                throw new InvalidDataException($"diet-spellings.tsv: {spelling.Word} is both a spelling and a word");
            return all;
        }

        private static IReadOnlyList<MethodFood> LoadMethodFoods()
        {
            using var stream = typeof(DietFoodWords).Assembly.GetManifestResourceStream(MethodFoodsResource)
                ?? throw new InvalidOperationException($"{MethodFoodsResource} is not embedded in Nom.Orch");
            using var reader = new StreamReader(stream);
            return ParseMethodFoods(reader.ReadToEnd());
        }

        /// <summary>Reads the method foods file: comment lines start with #, the header names the columns; a word stands once.</summary>
        public static IReadOnlyList<MethodFood> ParseMethodFoods(string text)
        {
            var all = new List<MethodFood>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
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
                if (cells.Length != header.Length) throw new InvalidDataException($"diet-method-foods.tsv: {cells.Length} cells where the header has {header.Length}: {line}");
                string Cell(string name) => cells[Array.IndexOf(header, name)].Trim();
                var word = new MethodFood(Cell("word"), int.Parse(Cell("foods"), CultureInfo.InvariantCulture), int.Parse(Cell("recipes"), CultureInfo.InvariantCulture), int.Parse(Cell("methods"), CultureInfo.InvariantCulture));
                if (!seen.Add(word.Word)) throw new InvalidDataException($"diet-method-foods.tsv: {word.Word} stands twice");
                all.Add(word);
            }
            return all;
        }

        private static IReadOnlyList<LineWord> LoadLineWords()
        {
            using var stream = typeof(DietFoodWords).Assembly.GetManifestResourceStream(LineWordsResource)
                ?? throw new InvalidOperationException($"{LineWordsResource} is not embedded in Nom.Orch");
            using var reader = new StreamReader(stream);
            return ParseLineWords(reader.ReadToEnd());
        }

        /// <summary>Reads the line words file: comment lines start with #, the header names the columns; a word stands once.</summary>
        public static IReadOnlyList<LineWord> ParseLineWords(string text)
        {
            var all = new List<LineWord>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
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
                if (cells.Length != header.Length) throw new InvalidDataException($"diet-line-words.tsv: {cells.Length} cells where the header has {header.Length}: {line}");
                string Cell(string name) => cells[Array.IndexOf(header, name)].Trim();
                int Number(string name) => int.Parse(Cell(name), CultureInfo.InvariantCulture);
                var word = new LineWord(Cell("word"), Number("foods"), Number("lines"), Number("alone"), Number("own"));
                if (!seen.Add(word.Word)) throw new InvalidDataException($"diet-line-words.tsv: {word.Word} stands twice");
                all.Add(word);
            }
            return all;
        }
    }
}
