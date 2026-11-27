using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// The diet gate's reading of a recipe's title and method beside its lines (staging checkpoint 6, R3: 139103 Macaroni
    /// and Cheese, whose lines are cheese, corn starch, milk and salt and whose method names the macaroni, and 69144 Scones,
    /// whose lines are milk, sugar and egg, were shown to Celiac Disease). A recipe whose lines were read out of a book's
    /// text (no web source: its lines were extracted from prose, which drops what the text only names) and whose title or
    /// method names a food a severe set restricts, while no line of it does, is unknown for that set and held with its own
    /// reason; the title is read only for a recipe with no method text, since a method says what the dish is made of and a
    /// title names cakes that are made without flour as often as with it ("Potato Cakes", "Almond Cakes"), except for a dish a
    /// crust or a dough defines (<see cref="TitleTerms"/>: "Lemon Pie" whose lines and method leave out the crust) (<see cref="Nom.Data.Plan.DietFlagReason.Title"/>, <see cref="Nom.Data.Plan.DietFlagReason.Method"/>). A
    /// recipe a site publishes as structured data lists its ingredients as its author wrote them, and its title names what
    /// the dish replaces as often as what it holds ("Zucchini Lasagna"), so it is read by its lines only. The title is read
    /// without an imitation it names ("Mock Turtle Soup"); the method is read sentence by sentence, without the terms its
    /// verbs and shapes share (<see cref="MethodSkips"/>: roll the dough, make into flat cakes, the meat's own gravy, "the
    /// nuts" of a line that names them) and without what a serving clause of an accompaniment names (<see cref="Sentences"/>). The
    /// rule is part of <see cref="Version"/>.
    /// </summary>
    public static class DietRecipeText
    {
        public const string Version = "recipe-text/9";

        /// <summary>The terms the method is not read for: words a method uses as a verb, a shape, a meat's juices or a word for what a line already names more often than as the food ("roll it in a cloth", "make into little flat cakes", "the pig's own gravy", "a smooth batter of rice", "a dough of peanut butter", "mix the nuts" of walnut meats).</summary>
        public static readonly IReadOnlyList<string> MethodSkips = new[] { "roll", "cake", "gravy", "batter", "dough", "paste", "nut", "sandwich" };

        /// <summary>The terms the method is read for only when no line names the same kind of food: a method's "the crumbs" mostly echoes a line ("bread crumbs"), but where no line names crumbs the method is the only place the recipe says it holds them ("add more crumbs", "dip in egg and crumbs" under Haricot Kromeskies and the croquettes of old books: proof draw 9 of the ck11 review); a method's "shortening" is any fat of the lines ("Cream shortening, add sugars" where the lines give butter: proof draw 11), so it is read only where no line names a fat; a method's "pancake" is the shape its own batter is fried in where a line names a flour or meal ("pour enough batter to make a pancake" under Prune Blintzes: proof draw 13). Nuts are read the same way, but only as an ingredient (<see cref="Mentions"/>), since a method's "nut" is as often a size or a shape.</summary>
        public static readonly IReadOnlyList<string> MethodEchoes = new[] { "crumb", "shortening", "pancake" };

        /// <summary>The terms the title is read for whatever the method says: a dish made in a crust, a dough or bread by its definition (WEBSTER, 1913: a pie is paste baked with something in it or under it; a pasty, a tart, a turnover and an old cheesecake are the same; a sandwich is two pieces of bread with meat or other food between them; macaroni, spaghetti, vermicelli and noodles are wheat paste dried in tubes, threads or strips), since a pie whose lines and method leave out its crust still has one, and a sandwich its bread, and a dish the title names by its pasta the pasta ("EGG AND BROWN BREAD SANDWICH" and "Spaghetti con Aglio e Pomodori", whose lines leave them out: staging checkpoint 6, R3); and the egg, fish and shellfish a title names as the dish's own food ("Spanish Eggs", "Boiled salmon", "Oysters à la Poulette", whose lines leave them out), in English or in a language of the staged cookbooks (the translations of the vocabulary: "Darne de Saumon Valois", proof draw 12 of the ck11 review). A dough term that only names what an accompaniment is for is not read (<see cref="DoughTerms"/>).</summary>
        public static readonly IReadOnlyList<string> TitleTerms = new[]
        {
            "pie", "pasty", "tart", "turnover", "dumpling", "pastry", "strudel", "patty", "cheesecake", "sandwich",
            "spaghetti", "macaroni", "maccaroni", "maccheroni", "vermicelli", "noodle", "lasagna", "lasagne", "ravioli", "pasta", "pappa",
            "macaroon", "egg", "fish", "oyster", "clam", "lobster", "crab", "shrimp", "prawn", "scallop", "mussel", "salmon", "cod", "whiting", "haddock",
            "halibut", "sole", "trout", "mackerel", "herring", "sardine", "anchov", "turbot", "eel", "perch", "pike", "bass", "shad", "smelt",
            "snapper", "pickerel", "flounder", "sturgeon", "carp", "whitefish", "bluefish", "weakfish",
        };

        /// <summary>The terms of <see cref="TitleTerms"/> a dish is made of as its crust, bread or pasta: where the title names one only as what an accompaniment is for ("Sandwich Filling", "Pie Filling", "Spaghetti Sauce", "Clayton's Popular Sandwich Paste", "Cream cheese for sandwiches", "Hard-Boiled Eggs for Garniture of Sandwiches"), the recipe is the accompaniment and holds none, so the title is not read for it.</summary>
        public static readonly IReadOnlyList<string> DoughTerms = new[]
        {
            "pie", "pasty", "tart", "turnover", "dumpling", "pastry", "strudel", "patty", "cheesecake", "sandwich",
            "spaghetti", "macaroni", "maccaroni", "maccheroni", "vermicelli", "noodle", "lasagna", "lasagne", "ravioli", "pasta", "pappa",
        };

        private static readonly Regex Accompaniment = new(@"(?<![\p{L}])(?:sauces?|dressings?|stuffings?|forcemeats?|garnish(?:es)?|butters?|gravy|gravies|relish(?:es)?|crusts?|pastes?|pastry|pastries|doughs?|icings?|frostings?|fillings?|glazes?|toppings?)\s+(?:for|to\s+serve\s+with|to\s+eat\s+with)\b.*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex Placeholder = new(@"^\s*(?:not\s+(?:specified|given|listed|stated)(?:\s+in\s+the(?:\s+provided)?\s+text)?|ingredients?(?:\s+and\s+steps)?\s+(?:are\s+)?not\s+(?:fully\s+|all\s+)?(?:specified|given|listed|stated)(?:\s+in\s+the(?:\s+provided)?\s+text)?|(?:ingredients?\s+)?(?:none|not)\s+(?:provided|given)|n/?a|none|-+|ingredients?\s+(?:as|the\s+same\s+as)\s+(?:in|for|above|before|given)(?![\p{L}]).*)\s*\.?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex Serving = new(@"(?<![\p{L}])(?:and\s+|then\s+)?(?:serve[sd]?|to\s+serve|to\s+be\s+served|send\s+(?:it\s+)?to\s+(?:the\s+)?table|to\s+be\s+eaten|spread\s+it)\b.*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex Elsewise = new(@"(?<![\p{L}])(?:instead\s+of\s+(?:(?:using|adding|the|a|an)\s+)*[\p{L}'’-]+(?:\s+[\p{L}'’-]+)?|(?:as\s+in|see)\s+the\s+recipe\s+for\b[^,;:]*|as\s+you\s+(?:do|would|did)(?:\s+do)?(?:\s+[\p{L}'’-]+){1,6})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex AccompanimentTitle = new(@"(?<![\p{L}])(?:sauces?|dressings?|butters?|gravy|gravies|relish(?:es)?|garnish(?:es)?|stuffings?|forcemeats?|dips?|spreads?|icings?|frostings?|glazes?|syrups?|fillings?|toppings?|catsup|ketchup|jell(?:y|ies)|gelatine?)(?![\p{L}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex Web = new(@"^\s*https?://", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex Imitation = new(@"(?<![\p{L}])(?:mock|imitation|(?:to\s+eat\s+)?like)(?:[\s-]+[\p{L}'’-]+){1,3}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex Elsewhere = new(@"(?<![\p{L}])(?:(?:made|prepared|directed|given)\s+(?:as\s+)?(?:directed\s+)?in\s+No\.?\s*\d|as\s+directed\s+(?:in|for|under|above)|see\s+No\.?\s*\d|according\s+to\s+(?:No\.?\s*\d|(?:the\s+)?(?:recipe|receipt|directions))|(?:mixture|ingredients?|dough|paste|batter)\s+(?:as|the\s+same\s+as)\s+(?:in|for|above|before|given)(?![\p{L}])|as\s+(?:in|for)\s+(?:the\s+)?(?:recipe|receipt|foregoing|preceding|last|above|former)(?![\p{L}])|as\s+(?:in|for)\s+[""“‘'])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex MethodElsewhere = new(@"(?<![\p{L}])(?:as\s+(?:in|for|with)\s+(?:the\s+)?(?:last|foregoing|preceding|above|former|previous)(?![\p{L}])|(?:as\s+)?(?:directed|given|described)\s+(?:above|before|in\s+(?:the\s+)?(?:last|foregoing|preceding))|as\s+(?:above|before)(?![\p{L}])|the\s+(?:same|foregoing|preceding)\s+(?:receipt|recipe|mixture)|\(No\.?\s*\d+\))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex NamedBase = new(@"(?<![\p{L}])(?:(?:[Mm]ix(?:ing)?|[Mm]ak(?:e|ing)|[Pp]repar(?:e|ing)|[Uu]s(?:e|ing))\s+(?:the\s+|a\s+|your\s+|one\s+)?|(?:to|into)\s+(?:the\s+|a\s+|your\s+)?)(?:\p{Lu}[\p{L}'’-]*\s+){1,4}(?i:batter|dough|mixture|frosting|icing|filling|crust|pastry|paste)(?![\p{L}])", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex Truncated = new(@"(?<![\p{L}])(?:of|and|or|with|the)\s*[,.;:-]*\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex UseSentence = new(@"^\s*(?:it\s+(?:is|may\s+be)\s+)?(?:use[sd]?|good|nice|excellent|delicious)\s+(?:it\s+|this\s+|them\s+|these\s+)?(?:to|for|on|in|with|as)(?![\p{L}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex PortionOfBaked = new(@"(?<![\p{L}])(?:(?:layers?|slices?|pieces?|rounds?|squares?|strips?|fingers?|bits?)\s+of\s+(?:the\s+|stale\s+|plain\s+|sponge\s+)?(cakes?|rolls?)|(?:over|on|upon|between|top\s+of|remaining)\s+(?:(?:one|each|the|a|two|three|four|other|remaining)\s+)*(?:[\p{L}'’-]+\s+)?(cakes?))(?![\p{L}])(?!\s*(?:tins?|pans?|cutters?|griddles?|boards?|turners?|moulds?|molds?|plates?|racks?)(?![\p{L}]))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex NutIngredient = new(@"(?<![\p{L}])(?:(?:(?:chopped|broken|whole|halved|blanched|salted|toasted|roasted|shelled|ground|grated|pounded|minced|sliced|mixed|some|few|any|with|and|or|add|added|adding)\s+)+(?:\p{L}+\s+)?nuts(?:\s*meats?)?|nut\s*meats?|nut\s+(?:butter|filling|paste|cream)|nuts\s+(?:are|may|can|should)\s+(?:be\s+)?(?:added|used|sprinkled|put))(?![\p{L}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex TitleNut = new(@"(?<![\p{L}])(?:(?<before>\p{L}+)[\s-]+)?nuts?(?:\s*meats?)?(?![\p{L}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly HashSet<string> NoNut = new(StringComparer.OrdinalIgnoreCase) { "dough", "ginger", "butter" };

        private static readonly Regex TitleButternut = new(@"(?<![\p{L}])butter[\s-]*nuts?(?![\p{L}])(?![\s-]*squash)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex NoNutWord = new(@"(?<![\p{L}])(?:cocoa?[\s-]*nuts?|dough[\s-]*nuts?|ginger[\s-]*nuts?|butter[\s-]*nuts?(?=[\s-]*squash)|water[\s-]+chestnuts?|(?:corn|maize|wheat|rice|grain|popcorn|hominy|barley)[\s-]+kernels?)(?![\p{L}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>The nuts a book recipe's title names, each with the word before it, where no line and no sentence of the method names a nut (<see cref="NutWord"/>): "Nut" of "Nut and Asparagus Soup" (97393, staging checkpoint 8, T10), whose lines are asparagus, stock and salt. Each is read as a line would be, so an unnamed nut holds the recipe for Peanut Allergy and Tree Nut Allergy alike, as "nuts" in a line does, and a named one ("Hickory Nut Cake") only for what names it. A butter nut ("Butter Nut Cake", "Butter-Nut Taffy") is the butternut, the white walnut of the old American books, and is read as "butternut", as a line reads it; a butternut squash is no nut. A nut that is no nut ("Dough Nuts", "Ginger Nuts") is not one. Empty when the lines or the method name a nut; a word that only looks like one there (coconut, doughnut, ginger nut, butternut squash, water chestnut, corn kernels) names none, so it does not stand for the title's nut (review ck19w31-1, ck19w31-2).</summary>
        public static IReadOnlyList<string> LostNuts(string? title, IEnumerable<string?> rawLines, string? method = null)
        {
            if (string.IsNullOrWhiteSpace(title)) return Array.Empty<string>();
            var read = Title(title);
            var found = TitleNut.Matches(read).Where(m => !NoNut.Contains(m.Groups["before"].Value)).Select(m => m.Value)
                .Concat(TitleButternut.Matches(read).Select(_ => "butternut")).ToList();
            if (found.Count == 0 || rawLines.Any(l => l != null && NamesNut(l)) || (!string.IsNullOrWhiteSpace(method) && NamesNut(method))) return Array.Empty<string>();
            return found;
        }

        private static bool NamesNut(string text) => NutWord.IsMatch(NoNutWord.Replace(text, " "));

        private static readonly Regex NutWord = new(@"(?<![\p{L}])(?:\p{L}*nuts?|nut\s*meats?|almonds?|pecans?|pistachios?|filberts?|cashews?|macadamias?|pignoli\p{L}*|pine\s+kernels?|kernels?)(?![\p{L}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex FatWord = new(@"(?<![\p{L}])(?:butter|lard|shortening|fat|oil|margarine|oleomargarine|crisco|drippings?|suet|ghee|cottolene|snowdrift|spry)(?![\p{L}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex TruncatedWord = new(@"(?<![\p{L}])(\p{L}{4,7})\s*[,.;:]?\s*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly string[] Staples = { "flour", "butter", "sugar", "cream", "bread", "cheese", "vinegar", "molasses", "mustard", "nutmeg" };

        private static readonly Regex FlourWord = new(@"(?<![\p{L}])(?:\p{L}*flour|\p{L}*meal|starch|cornstarch|farina)(?![\p{L}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex MidwayStart = new(@"^\s*(?:then\s+|now\s+)?\p{L}+\s+(?:them|it|these|this)(?![\p{L}])|^\s*(?:No\.?\s*)?\d+\s*\.?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex CrumbWord = new(@"\p{L}*crumb", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Lazy<Regex?> SkippedDishes = new(() =>
        {
            var names = DietDishes.All.SelectMany(d => d.Names.Prepend(d.Term)).Select(n => DietPatternRule.Fold(n).ToLowerInvariant())
                .Where(n => n.Contains(' ') && MethodSkips.Concat(MethodEchoes).Any(w => Regex.IsMatch(n, @"(?<![\p{L}])" + w)))
                .OrderByDescending(n => n.Length).ToList();
            return names.Count == 0 ? null : new Regex(@"(?<![\p{L}])(?:" + string.Join("|", names.Select(Regex.Escape)) + @")(?:e?s)?(?![\p{L}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        });

        private static readonly Regex Garnish = new(@"(?:,|\s(?:with|and)\s)\s*((?:(?!,).)*(?<![\p{L}])(?:sauce|butter|gravy|dressing)(?![\p{L}]).*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Lazy<HashSet<string>> DishNames = new(() => DietDishes.All.SelectMany(d => d.Names.Prepend(d.Term)).Select(n => DietPatternRule.Fold(n).ToLowerInvariant()).ToHashSet(StringComparer.Ordinal));

        private static readonly Regex Sentence = new(@"[.;!?\n\r]+", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>Whether the title is read for a rule (a title's "scallop" in the singular after another food is a dish baked in layers, "Trumese Scallop", and is read only as "scallops" or a sea or bay scallop: proof draw 9 of the ck11 review; "Scallops of Sweetbread" are escalopes: proof draw 16): for every rule when the recipe's method has no text or a line sends the reader to another recipe of the book (<see cref="RefersElsewhere"/>), and for a rule of a <see cref="TitleTerms"/> dish always, and for a dish of <see cref="DietDishes"/> the title names as what the recipe is served with ("Boiled leg of mutton, caper sauce", "Fish with Egg Sauce": proof draw 7 of ck11).</summary>
        public static bool ReadsTitle(string? method, DietPatternRule? rule, bool refersElsewhere = false, string? title = null, IEnumerable<string?>? rawLines = null) =>
            string.IsNullOrWhiteSpace(method) || refersElsewhere || Garnishes(title, rule) || FlourLost(rule, rawLines) || JoinedLost(title, rule, method, rawLines)
            || (rule != null && !rule.Substring && rule.Words.Count > 0 && (TitleTerms.Contains(rule.Words[^1], StringComparer.Ordinal) || TitleTranslations.Value.Contains(string.Join(' ', rule.Words))) && !Accompanies(title, rule) && !MakesTheDish(title, rule) && !ScallopedDish(title, rule));

        /// <summary>The baked goods of flour the title is read for when no line names a flour, a meal or a starch (staging checkpoint 7, S4: "Bread" whose lines are milk, sugar, nut meats and salt; "Canada War Cakes" made "before adding the remaining ingredients"): the head terms of USDA Baked Products the wheat vocabulary takes in (<see cref="DietVocabulary"/>), so a cake or a bread whose flour the lines lost is held while one whose lines give its flour, or that is made of potato or almond, is read by its lines.</summary>
        private static readonly Lazy<HashSet<string>> BakedGoods = new(() => DietVocabulary.All
            .Where(t => t.Class == "wheat" && t.Basis == DietVocabularyBasis.Category && t.Decision == DietVocabularyDecision.In && t.Categories.Split('|').Contains("18"))
            .Select(t => DietPatternRule.For(t.Pattern)).Where(r => !r.Substring && r.Words.Count > 0).Select(r => string.Join(' ', r.Words))
            .ToHashSet(StringComparer.Ordinal));

        private static readonly Regex DoughWord = new(@"(?<![\p{L}])(?:\p{L}*flour|\p{L}*meal|starch|cornstarch|farina|paste|pastry|pastries|crusts?|doughs?|\p{L}*breads?|biscuits?|crackers?|\p{L}*crumbs?|wafers?|macaroni|maccaroni|maccheroni|spaghetti|vermicelli|noodles?|pasta|lasagna|lasagne|ravioli|tortillas?|rolls?|toast|shells?|cases?)(?![\p{L}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>Whether a book recipe's title names, as its last food word, a dish made in a crust, a dough or bread by its definition (<see cref="DoughTerms"/>: "CRANBERRY TART", "Rhubarb Pies") while no line names a flour, a meal, a starch, a paste, a crust, a dough, a bread or the pasta (proof draw 2 of ck17: 103893 Cranberry Tart, whose lines are cranberries and water): the dough the dish is made in is not in the lines, so what it holds is not known to any severe set. A title that names the dish only as what an accompaniment is for ("Pie Filling") is not read.</summary>
        public static bool DoughLost(string? title, IEnumerable<string?> rawLines) =>
            DoughTitled(title) && !rawLines.Any(l => l != null && DoughWord.IsMatch(l));

        /// <summary>Whether a title names, as its last food word, a dish made in a crust, a dough or bread (<see cref="DoughTerms"/> and <see cref="DoughTitles"/>), and not only as what an accompaniment is for.</summary>
        public static bool DoughTitled(string? title)
        {
            if (string.IsNullOrWhiteSpace(title)) return false;
            var words = DietFoodWords.AllWords(Title(title));
            if (words.Count == 0 || !DoughWordSet.Value.Contains(words[^1])) return false;
            return !Accompanies(title, DietPatternRule.For("%" + words[^1] + "%"));
        }

        private static readonly Regex InPaste = new(@"(?<![\p{L}])(?:bake|baked|bakes|boil|boiled|put|line|lined|lines|cover|covered|enclose|enclosed|wrap|wrapped|roll|rolled)\s+(?:(?:it|them|this|the\s+[\p{L}-]+)\s+)?(?:in|with|into)\s+(?:a\s+|the\s+|some\s+)?(?:(?:puff|rich|good|light|short|plain|thin|nice|crisp)\s+)?(?:paste|crust|pastry)(?![\p{L}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>Whether a book recipe's method bakes, boils, lines or wraps the dish in a paste, a crust or a pastry that no line names (proof draw 12 of ck17: 109291 APPLE PUDDING, "beat five eggs very light, and mix all together, and bake in a paste", whose lines are apples, lemon peel, sugar, nutmeg and eggs; proof draw 13: 96979 Lemon Pie that will keep several days, "bake between 2 crusts"; and, whatever flour or crumbs the lines give the filling, a method that fills the lower crust or covers it with the top one, or tops the pie "after the pie is baked", while no line names a paste, a crust, a dough or a shell: proof draw 14, 31363 Mock Mince Pie and 21945 LEMON PIE): the dough the dish is made in is not in the lines, so what it holds is not known to any severe set, as with <see cref="DoughLost"/>.</summary>
        public static bool PasteLost(string? method, IEnumerable<string?> rawLines) =>
            !string.IsNullOrWhiteSpace(method) && ((InPaste.IsMatch(method) || BetweenCrusts.IsMatch(method)) && !rawLines.Any(l => l != null && DoughWord.IsMatch(l))
                || PieCrust.IsMatch(method) && !rawLines.Any(l => l != null && CrustWord.IsMatch(l)));

        private static readonly Regex PieCrust = new(@"(?<![\p{L}])(?:(?:fill|line|lined|cover|covered)\s+(?:with\s+)?(?:the\s+)?(?:lower|under|bottom|top|upper)\s+crusts?|(?:after|when)\s+the\s+(?:pie|tart)\s+is\s+baked)(?![\p{L}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex CrustWord = new(@"(?<![\p{L}])(?:paste|pastry|pastries|crusts?|doughs?|shells?)(?![\p{L}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex BetweenCrusts = new(@"(?<![\p{L}])(?:bake|baked|put|lay|laid)\s+(?:it\s+|them\s+)?between\s+(?:two|2)\s+(?:crusts|pastes|sheets\s+of\s+paste)(?![\p{L}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex BakingPowder = new(@"(?<![\p{L}])baking[\s-]+powder(?![\p{L}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex Soda = new(@"(?<![\p{L}])(?:soda|saleratus|bicarbonate)(?![\p{L}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex Tartar = new(@"(?<![\p{L}])cream\s+(?:of\s+)?tartar(?![\p{L}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>Whether a book recipe's lines name baking powder, or soda with cream of tartar (proof draw 5 of ck17: 94494 Lemon Jumbles, "rolled and cut out with a cake-cutter"), but no flour, meal, starch, dough, crumb or bread for it to raise (proof draw 3 of ck17: 69561 "Simple Dessert", whose lines are baking powder, ginger, baking soda and water): baking powder raises only a batter or a dough, so the lines lost what it is made of.</summary>
        public static bool LeaveningLost(IEnumerable<string?> rawLines) =>
            (rawLines.Any(l => l != null && BakingPowder.IsMatch(l)) || (rawLines.Any(l => l != null && Soda.IsMatch(l)) && rawLines.Any(l => l != null && Tartar.IsMatch(l)))) && !rawLines.Any(l => l != null && DoughWord.IsMatch(l) || l != null && Regex.IsMatch(l, @"(?<![\p{L}])(?:mix|bisquick|pancake|biscuit|cake)(?![\p{L}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));

        private static readonly Regex ConsommeWord = new(@"(?<![\p{L}])consomm[eé]s?(?![\p{L}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex EggWhite = new(@"(?<![\p{L}])(?:whites?\s+of\s+(?:(?:an?|one|two|three|four|six|the|\d+)\s+)?(?:raw\s+|fresh\s+)?eggs?|egg[\s-]*whites?)(?![\p{L}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex ThreeWords = new(@"(?<![\p{L}])(\p{L}+)[\s-]+(\p{L}+)[\s-]+(\p{L}+)(?![\p{L}])", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>Each run of three adjacent words of a method sentence, none of them grammar, read without its middle word ("adding soy bean sauce" also as "soy sauce", but "cream of tartar" never as "cream tartar": proof draw 10 of ck17, 136720 Pei Yu, whose lines leave the sauce out), so a pattern of two words reads the compound a cook writes with a word between.</summary>
        public static IEnumerable<string> Contractions(string? sentence)
        {
            if (string.IsNullOrWhiteSpace(sentence)) yield break;
            for (var start = 0; start < sentence.Length;)
            {
                var m = ThreeWords.Match(sentence, start);
                if (!m.Success) yield break;
                if (!new[] { m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value }.Any(w => DietFoodWords.Grammar.Contains(w.ToLowerInvariant())))
                    yield return m.Groups[1].Value + " " + m.Groups[3].Value;
                start = m.Groups[1].Index + m.Groups[1].Length;
            }
        }

        /// <summary>Whether a text names a consommé (review of ck17-words: Escoffier clarifies his consommé with egg white, so "1 quart consommé" is the book's clarified soup, not the beef consommé or stock the line is linked to).</summary>
        public static bool NamesConsomme(string? text) => !string.IsNullOrEmpty(text) && ConsommeWord.IsMatch(text);

        /// <summary>The egg-white clarifiers a book's own consommé recipe names in its lines and method ("the white of an egg" of 129365 THE PREPARATION OF CLARIFIED CONSOMMÉ), when its title names a consommé; none otherwise. A severe set that flags one of them reads every consommé line of that book as not understood.</summary>
        public static IReadOnlyList<string> ConsommeClarifiers(string? title, IEnumerable<string?> rawLines, string? method) =>
            !NamesConsomme(title)
                ? Array.Empty<string>()
                : rawLines.Append(method).Where(t => !string.IsNullOrEmpty(t)).SelectMany(t => EggWhite.Matches(t!).Select(m => m.Value.ToLowerInvariant())).Distinct(StringComparer.Ordinal).ToList();

        private static readonly Lazy<HashSet<string>> DoughWordSet = new(() => DoughTerms.Select(DietFoodWords.Normal).Concat(DoughTitles.Select(t => t.Word)).ToHashSet(StringComparer.Ordinal));

        public const string DoughTitlesResource = "Nom.Orch.Contents.diet-dough-titles.tsv";

        /// <summary>One row of <c>Contents/diet-dough-titles.tsv</c>: a dish word that ends the titles of book recipes whose lines nearly always name a flour, a meal, a crumb, a bread or another dough (<see cref="NamesDough"/>), with those titles and those that do when it was derived.</summary>
        public sealed record DoughTitle(string Word, int Titles, int WithDough);

        private static readonly Lazy<IReadOnlyList<DoughTitle>> DoughTitleTable = new(() =>
        {
            using var stream = typeof(DietRecipeText).Assembly.GetManifestResourceStream(DoughTitlesResource)
                ?? throw new InvalidOperationException($"{DoughTitlesResource} is not embedded in Nom.Orch");
            using var reader = new StreamReader(stream);
            return ParseDoughTitles(reader.ReadToEnd());
        });

        /// <summary>The dish words of the corpus whose book recipes nearly always carry a dough line (proof draw 8 of ck17: 122491 Brown betty, whose one line is "enough apples to make two cupfuls"), derived, never listed by hand; <see cref="DoughLost"/> reads them beside <see cref="DoughTerms"/>.</summary>
        public static IReadOnlyList<DoughTitle> DoughTitles => DoughTitleTable.Value;

        /// <summary>Reads the dough titles file: comment lines start with #, the header names the columns; a word stands once and was derived from at least ten titles, nine tenths of them with a dough line.</summary>
        public static IReadOnlyList<DoughTitle> ParseDoughTitles(string text)
        {
            var all = new List<DoughTitle>();
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
                if (cells.Length != 3) throw new InvalidDataException($"diet-dough-titles.tsv: {cells.Length} cells where the header has 3: {line}");
                var row = new DoughTitle(cells[0].Trim(), int.Parse(cells[1], CultureInfo.InvariantCulture), int.Parse(cells[2], CultureInfo.InvariantCulture));
                if (row.Titles < 10 || row.WithDough * 10 < row.Titles * 9) throw new InvalidDataException($"diet-dough-titles.tsv: {row.Word} is under the derivation's bar");
                if (!seen.Add(row.Word)) throw new InvalidDataException($"diet-dough-titles.tsv: {row.Word} stands twice");
                all.Add(row);
            }
            return all;
        }

        /// <summary>Whether a line names a flour, a meal, a starch, a paste, a crust, a dough, a bread, a crumb or a pasta (<see cref="DoughLost"/>).</summary>
        public static bool NamesDough(string? rawLine) => !string.IsNullOrEmpty(rawLine) && DoughWord.IsMatch(rawLine);

        private static readonly Regex Joined = new(@"(?<![\p{L}])(?:and|with|&)\s+(.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>Whether the title names, after "and" or "with", a food of the rule that neither a line nor the method names (proof draw 4 of ck17: 105632 "Baked Pears and Cream", whose lines and method give no cream): the title says what the dish is served with or made of, and the lines lost it.</summary>
        private static bool JoinedLost(string? title, DietPatternRule? rule, string? method, IEnumerable<string?>? rawLines)
        {
            if (rule == null || string.IsNullOrWhiteSpace(title)) return false;
            var joined = Joined.Match(Title(title));
            return joined.Success && rule.IsMatch(joined.Groups[1].Value) && !rule.IsMatch(method) && (rawLines == null || !rawLines.Any(rule.IsMatch));
        }

        private static bool FlourLost(DietPatternRule? rule, IEnumerable<string?>? rawLines) =>
            rule != null && rawLines != null && !rule.Substring && rule.Words.Count > 0 && BakedGoods.Value.Contains(string.Join(' ', rule.Words))
            && !rawLines.Any(l => l != null && FlourWord.IsMatch(l));

        private static readonly Lazy<HashSet<string>> TitleTranslations = new(() => DietVocabulary.All
            .Where(t => t.Basis == DietVocabularyBasis.Translation && t.Class is "egg" or "fish" or "shellfish")
            .Select(t => DietPatternRule.For(t.Pattern)).Where(r => !r.Substring && r.Words.Count > 0).Select(r => string.Join(' ', r.Words))
            .ToHashSet(StringComparer.Ordinal));

        private static readonly Regex Scallops = new(@"(?<![\p{L}])(?:scallops|(?:sea|bay)\s+scallop)(?![\p{L}])|^\W*scallop(?![\p{L}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex Escalopes = new(@"(?<![\p{L}])scallops?\s+of\s+(?!(?:the\s+)?(?:sea|bay|oysters?|scallops?)(?![\p{L}]))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static bool ScallopedDish(string? title, DietPatternRule rule) =>
            rule.Words[^1] == "scallop" && !string.IsNullOrWhiteSpace(title) && (!Scallops.IsMatch(DietPatternRule.Fold(title)) || Escalopes.IsMatch(DietPatternRule.Fold(title)));

        private static readonly Regex MadeMacaroons = new(@"^(?:(?!(?<![\p{L}])(?:with|and|of)(?![\p{L}])).)*(?<![\p{L}])macaroons?\W*(?:\(.*\))?\W*$|(?<![\p{L}])macaroons?\s+(?:with|of|a|au|aux)(?![\p{L}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static bool MakesTheDish(string? title, DietPatternRule rule) =>
            rule.Words[^1] == "macaroon" && !string.IsNullOrWhiteSpace(title) && MadeMacaroons.IsMatch(DietPatternRule.Fold(title));

        private static bool Accompanies(string? title, DietPatternRule rule)
        {
            if (string.IsNullOrWhiteSpace(title) || !DoughTerms.Contains(rule.Words[^1], StringComparer.Ordinal)) return false;
            var named = DietPatternRule.Fold(title).ToLowerInvariant();
            var term = @"(?<![\p{L}])" + Regex.Escape(string.Join(' ', rule.Words));
            var accompanied = new Regex(term + @"\p{L}*\s+(?:" + AccompanimentNouns + @")(?![\p{L}])|(?<![\p{L}])(?<!(?:directions?|methods?|recipes?|receipts?|ways?|rules?)\s+)(?:for|garniture\s+of)\s+(?:[\p{L}'’-]+\s+){0,2}" + term.Replace(@"(?<![\p{L}])", string.Empty) + @"\p{L}*", RegexOptions.CultureInvariant);
            return accompanied.IsMatch(named) && !Regex.IsMatch(accompanied.Replace(named, " "), term, RegexOptions.CultureInvariant);
        }

        private const string AccompanimentNouns = @"sauces?|fillings?|spreads?|pastes?|dressings?|toppings?|glazes?|icings?|frostings?|syrups?|seasonings?|butters?|relish(?:es)?";

        private static bool Garnishes(string? title, DietPatternRule? rule)
        {
            if (string.IsNullOrWhiteSpace(title) || rule == null || rule.Substring || rule.Words.Count == 0 || !DishNames.Value.Contains(string.Join(' ', rule.Words))) return false;
            var served = Garnish.Match(Title(title));
            return served.Success && rule.IsMatch(served.Groups[1].Value);
        }

        /// <summary>Whether the lines are not the whole recipe: a line sends the reader to another recipe of the book for what it holds ("Sauce made as directed in No. 1" under Bread Sauce for Pig: proof draw 6 of ck11; "mixture as for marbles", "Yeast dough, according to No. 8": proof draw 9 of the ck11 review), a line breaks off where its food would stand ("a dessert-spoonful of" under Lemon White Sauce) or inside its food's word ("4 ozs. flou" under Walnut Cake: proof draw 11), or the method says the recipe is made as another one ("Proceed with the cucumbers as in the last", "sauce No. 41"), begins where another left off ("Place them in a moderate oven" under Apricot Marmalade Glazed Biscuits), is only another recipe's number ("723") or sends the reader to a page ("batter for Pancakes (recipe page 14)": proof draw 13): the lines then leave out what the title names.</summary>
        public static bool RefersElsewhere(IEnumerable<string?> rawLines, string? method = null) =>
            rawLines.Any(l => l != null && (Elsewhere.IsMatch(l) || Truncated.IsMatch(l) || CutWord(l))) || (!string.IsNullOrWhiteSpace(method) && (MethodElsewhere.IsMatch(method) || MidwayStart.IsMatch(method) || PageReference.IsMatch(method)));

        private static bool CutWord(string line)
        {
            var last = TruncatedWord.Match(line);
            if (!last.Success) return false;
            var word = last.Groups[1].Value.ToLowerInvariant();
            return Staples.Any(s => s.Length > word.Length && s.StartsWith(word, StringComparison.Ordinal));
        }

        /// <summary>Whether a line is only a placeholder an extraction wrote where it found no ingredients ("Not specified in the text", "N/A", "Ingredients as in 'Sponge Cake Roly-Poly'": proof draw 9 of the ck11 review): it names no food, and a recipe whose every line is one has no ingredient line the gate can read (proof draw 1 of ck11). A line with no text is not one: a recipe written in the app stores its lines as foods with no text, and the food is what the gate reads.</summary>
        public static bool IsPlaceholder(string? rawLine) => !string.IsNullOrWhiteSpace(rawLine) && Placeholder.IsMatch(rawLine);

        /// <summary>Whether the method builds the recipe on another recipe of the book, named as a batter, a dough, a mixture, a crust or a frosting the lines do not give ("Set aside while mixing Velvet Crumb Cake batter" under Date Velvet Crumb Cake, "Make Biscuit dough", "Use the Genoese mixture": proof draw 10 of the ck11 review; "Make like \"Brown Gravy\"", or a line "batter as for suet pudding": proof draw 16; a line "ingredients for oyster sauce", a line that is only an amount, "1 teaspoon", or a method that says it is incomplete: proof draw 17; a method that ends mid-sentence, on a letter with no closing mark, "stew it in a pint of water, and a tea-spoonful of salt" under Celery Sauce, white, whose book goes on to an ounce of butter rolled in flour, or one that sends the cook to "the original recipe", to "No. 381" or adds the lines "to Fudge Filling": proof draw 6 of ck17; "Prepare as directed on package" under GLUTEN GRUEL, "Add to the first recipe", "precisely as before", "the mass described above. Treat as before", "finish the same as apple jelly", "in the same manner as strawberries a la mode", "a muscle sauce (No. 70)": proof draw 7; "Prepare the artichokes as above", "Proceed as directed", "Make this with cream instead of milk", a method ending on a comma: proof draw 8; "Let the Fish be fry'd after the common manner" under Pickled Fish, whose lines are only the pickle, "Take sorrel as above", "Same as raspberry.", "see No. 185", "as directed in the Raised Loaf Cake", "Proceed as with the whole apple": proof draw 9; "Make same as Strawberry Mousse", "(Incomplete recipe - steps missing)", "Prepare in same manner as Preserved Green Tomatoes", "Proceed exactly as described for the pheasant", "For sauce, see Turbot à la Mazarine, No. 207": proof draw 10; "the same as given under No. 38, Fish Croquettes", any other recipe of the book named by its number but a can's ("a No. 2 can"), "(see recipe 90)", "in any of the ways directed for turkeys": proof draw 11; "See boiled corned beef, Article 58", "Sauce the same as in the last article", a method that is only another recipe's name and number, "Hashed Potatoes au Gratin, 1004.", "add to above syrup and freeze", "Proceed same as ordinary fancy or drop cake": proof draw 12; "Cook them as directed on page 168", "Mix and bake same as Vanilla Wafers", a line "dressing (such as is used for turkey dressing)": proof draw 13; a method that only varies another recipe, "Use Champagne instead of Madeira wine." under Champagne Sauce, "Substitute a little rose-water for the pineapple.": proof draw 14; a method that only names another recipe, "Prepare Duchess Potato.": proof draw 9; and a method that begins by stirring, folding or beating something in, "Stir in the corn and bake forty-five minutes" under Corn and Butter Pudding, whose lines hold no egg: proof draw 15; "Made in a similar way to Cumberland pudding", "Finish as ripe apricot paste": proof draw 16; "Make exactly as you would a beef-steak pie." under Veal Pie, and a line or a method that measures melted or drawn butter by the pint, the gill or the boat, "three parts of a pint of melted butter" under Egg Sauce, the English butter sauce of flour, butter and water: proof draw 17; "Follow the last recipe in every particular." under Canned raspberries (2), "add to the recipe for foamy omelet", "finish same as peaches", "Make as apple jelly." (but not "make as thin as possible"): draw 18 of ck18; "Prepare as Egg Cordial, omitting wine and cream", "Add ... after the fat is added." under Fruit Biscuit, whose lines give only the fruit, "Make the soufflé preparation, but use tea instead of milk", "bake the pancakes the same as in foregoing recipe": draw 19 of ck18; "Mix as in lemon marmalade.", a method that is only other recipes' numbers, "647, 977", "serve the same as a round", "the meat of one crab, prepared Monza.": draw 20 of ck18; "Omit molasses, add ¼ cup vinegar" under Hot Bean Salad, "Serve like the foregoing.", "Make dressing number two.": draw 21 of ck18): what that base holds is not in the recipe, so a severe set cannot read it and holds it as having no readable ingredient list.</summary>
        public static bool BuildsOnAnother(string? method, IEnumerable<string?>? rawLines = null) =>
            (!string.IsNullOrWhiteSpace(method) && (NamedBase.IsMatch(method) || LikeAnother.IsMatch(method) || Incomplete.IsMatch(method) || SendsElsewhere.IsMatch(method) || BrokenOff.IsMatch(method) || Unfinished.IsMatch(method) || OnlyNamed.IsMatch(method) || InWhat.IsMatch(method) || MeltedButterSauce.IsMatch(method))) || (rawLines != null && rawLines.Any(l => l != null && (BaseLine.IsMatch(l) || AmountOnly.IsMatch(l) || MeltedButterSauce.IsMatch(l))));

        private static readonly Regex OnlyNamed = new(@"^\W*(?i:prepare|make|serve)\s+(?:(?i:the|a|an|some)\s+)?(?:\p{Lu}[\p{L}'’-]*\s*){1,5}[.!]?\s*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex MeltedButterSauce = new(@"(?<![\p{L}])(?:pints?|gills?|quarts?|half-pint|boat|boatful|sauce-boat|sauceboat|tureen)\s+(?:full\s+)?of\s+(?:good\s+|plain\s+|thick\s+|thin\s+|rich\s+|nice\s+|hot\s+)?(?:melted|drawn|oiled)\s+butter(?![\p{L}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex InWhat = new(@"^\W*(?:then\s+|now\s+)?(?:stir|fold|beat)\s+in\s+(?!to(?![\p{L}])|an?\s+(?:mortar|bowl|basin|pan|dish)(?![\p{L}]))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex Unfinished = new(@"(?:\p{Ll}|[,;:])\s*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex Choice = new(@"(?<![\p{L}])(?:of\s+(?:(?:your|one's|one’s)\s+(?:own\s+)?)?(?:choice|choosing|liking)|(?:your|any)\s+(?:favou?rite|preferred)|(?:your|any)\s+choice\s+of|any\s+kind\s+of)(?![\p{L}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>Whether a line leaves its food to the cook ("Fillings of your choice", "nut butter of choice", "your favorite cheese", "any kind of oil": proof draw 6 of ck17, 59672 How to Fold a Burrito): whatever food it is linked to or names, the cook may use one the set restricts.</summary>
        public static bool LeavesChoice(string? rawLine) => !string.IsNullOrEmpty(rawLine) && Choice.IsMatch(rawLine);

        private static readonly Regex BrokenOff = new(@"(?<![\p{L}])(?:in|into|of|with|and|or|the|a|an|to|until|till|then|add|adding)\s*[,;:-]*\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex SendsElsewhere = new(@"(?<![\p{L}])(?:(?:make|made|prepare|prepared|cook|cooked|proceed)\s+(?:it\s+|them\s+|this\s+)?(?:exactly\s+|just\s+)?(?:like|the\s+same\s+as|as\s+(?:(?:is|are)\s+(?=\p{L})|(?:for|in|above|before)(?![\p{L}])))|(?:use|follow)\s+(?:the\s+)?(?:recipe|receipt|directions?)\s+(?:for|of|given)(?![\p{L}])|(?:any|one|either)\s+of\s+the\s+(?:recipes?|receipts?)\s+(?:for|of|given)(?![\p{L}])|(?:given|described)\s+in\s+the\s+(?:recipe|receipt)\s+for(?![\p{L}])|as\s+for\s+(?:the\s+)?\p{L}|see\s+(?:page|p\.)\s*\d+|as\s+(?:in|for|under)\s+No\.?\s*\d+|(?:to|in|as\s+(?:in|for))\s+(?:the\s+)?(?:first|second|third|last|foregoing|preceding|former|previous)\s+(?:recipe|receipt|article)(?![\p{L}])|(?:precisely|exactly|just)\s+as\s+(?:before|above)(?![\p{L}])|treat(?:ed)?\s+(?:it\s+|them\s+)?as\s+(?:before|above)(?![\p{L}])|(?:mass|mixture|paste|batter|dough|syrup)\s+(?:described|given|mentioned)\s+(?:above|before)(?![\p{L}])|as\s+directed\s+on\s+(?:the\s+)?(?:package|packet|box|label|can|tin|carton)(?![\p{L}])|(?:in\s+)?the\s+same\s+(?:manner|way)\s+as(?![\p{L}])|(?:in\s+)?a\s+similar\s+(?:manner|way)\s+(?:to|as)(?![\p{L}])|(?:make|made|prepare|prepared|cook|cooked|proceed|dress|dressed)\s+(?:it\s+|them\s+|this\s+)?(?:exactly\s+|just\s+|precisely\s+)?as\s+you\s+would(?![\p{L}])|(?:finish|finished)\s+(?:it\s+|them\s+)?as\s+(?!soon\b|quickly\b|usual\b)\p{L}|(?:finish|finished|treat|treated)\s+(?:it\s+|them\s+)?(?:exactly\s+|just\s+)?the\s+same\s+as(?![\p{L}])|\(No\.?\s*\d+\)|(?:prepare|prepared|proceed|finish|finished|treat|treated|make|made|dress|dressed|take|taken)\s+(?:[\p{L}'’-]+\s+){0,4}?(?:exactly\s+|just\s+|precisely\s+)?(?:the\s+same\s+as|as\s+(?:the\s+)?(?:above|before|directed|others?|foregoing|preceding|last)|as\s+with(?![\p{L}])|(?:after|in)\s+the\s+(?:common|usual|ordinary)\s+(?:manner|way))(?![\p{L}])|(?:fry|fried|fry'd|fry’d)\s+(?:[\p{L}'’-]+\s+){0,3}?(?:after|in)\s+the\s+(?:common|usual|ordinary)\s+(?:manner|way)(?![\p{L}])|(?:the\s+)?same\s+as\s+(?:No\.?\s*\d+|the\s+(?:above|last|foregoing|preceding))|^\W*(?:the\s+)?same\s+as(?![\p{L}])|see\s+No\.?\s*\d+|as\s+directed\s+(?:in|for|under)\s+(?:the\s+)?(?:\p{Lu}|[""“‘']|No\.?\s*\d|recipe|receipt|foregoing|preceding)|(?:in\s+)?the\s+same\s+(?:manner|way)\s+(?:described|directed|given)(?![\p{L}])|in\s+(?:the\s+)?same\s+(?:manner|way)\s+as(?![\p{L}])|(?<![\p{L}])[Nn]o\.\s*\d+(?![\d½¼¾⅓⅔]*\s*(?:cans?|tins?|jars?|sizes?|bottles?)(?![\p{L}]))|see\s+recipe\s+\d+|(?:ways?|manner)\s+directed\s+for(?![\p{L}])|as\s+described\s+(?:for|in|under|above)(?![\p{L}])|see\s+(?:\p{Lu}[\p{L}'’-]*\s+){1,6}[^.;]*?No\.?\s*\d+|(?:make|made|use|using)\s+(?:this|it|them)\s+(?:with|of)\s+[^.;]*?instead\s+of(?![\p{L}])|^\W*instead\s+of(?![\p{L}])|^\W*(?:use|using)\s+[^.;]{1,60}?\s+instead\s+of(?![\p{L}])|^\W*substitute\s+[^.;]{1,60}?\s+for(?![\p{L}])|according\s+to\s+(?:the\s+)?(?:directions?|recipe|receipt|rules?)\s+(?:previously\s+|already\s+|before\s+)?(?:given|on|for|in|above)(?![\p{L}])|(?:prepared|made|done)\s+as\s+(?:above|before|directed)|(?:the|an?)\s+original\s+(?:recipe|receipt)|^\W*omit\s+(?:the\s+)?\p{L}+|like\s+(?:the\s+)?(?:foregoing|preceding|above|last|former)(?![\p{L}])|(?:make|use|see|prepare)\s+(?:[\p{L}-]+\s+){0,3}number\s+(?:one|two|three|four|five|six|seven|eight|nine|ten|\d+)(?![\p{L}])|(?:mix|mixed|serve|served)\s+(?:it\s+|them\s+)?(?:exactly\s+|just\s+)?(?:the\s+same\s+as|as\s+(?:in|above|before)(?![\p{L}]))|^\W*\d{2,4}(?:\s*(?:,|;|&|and)\s*\d{2,4})*\W*$|(?<![\p{L}])(?:prepared|served|dressed)\s+(?-i:\p{Lu}\p{Ll}+)\s*[.;]|(?:prepare|prepared|make|made)\s+(?:it\s+|them\s+)?as\s+(?:an?\s+|the\s+)?(?-i:\p{Lu}\p{Ll}+(?:\s+\p{Lu}\p{Ll}+)+)|^\W*add\b[^;]*?\bafter\s+the\s+\p{L}+(?:\s+\p{L}+)?\s+(?:is|are|has\s+been|have\s+been)\s+(?:added|mixed|put\s+in|rubbed\s+in)|(?:make|prepare)\s+(?:the|a)\s+[\p{L}_’\x27-]+(?:\s+[\p{L}_’\x27-]+)?\s+preparation(?![\p{L}])|(?:follow|use)\s+(?:the\s+)?(?:last|foregoing|preceding|previous|above|former|first|second)\s+(?:recipe|receipt|directions?)(?![\p{L}])|(?:to|into|with)\s+the\s+(?:recipe|receipt)\s+for(?![\p{L}])|(?:finish|finished|treat|treated)\s+(?:it\s+|them\s+)?(?:exactly\s+|just\s+)?same\s+as(?![\p{L}])|(?:^|[.;!]\s*)(?:make|made)\s+(?:it\s+|them\s+)?as\s+(?![^.;]*?\bas\b)(?!soon\b|usual\b|follows?\b|directed\b|above\b|before\b|desired\b|needed\b|required\b|wanted\b|liked\b)(?:an?\s+|the\s+)?\p{L}+(?:\s+\p{L}+){0,3}\s*(?:[.;]|$)|(?:make|made|prepare|prepared|proceed|bake|baked|mix|mixed|cook|cooked)\s+same\s+as(?![\p{L}])|(?:on|at)\s+page\s+\d+|see\s+[^.;]{0,60}?(?<![\p{L}])art(?:icle|\.)\s*\d+|(?:to|with|into|in|over)\s+(?:the\s+)?above\s+(?:syrup|mixture|batter|paste|dough|custard|cream|sauce|jelly|recipe|receipt)(?![\p{L}])|^\W*\p{Lu}[^.;,]{0,80},\s*\d{2,4}\.?\s*$|\(\s*incomplete(?![\p{L}])|incomplete\s+recipe(?![\p{L}])|(?:recipe|text)\s+(?:is\s+)?(?:incomplete|truncated)|\((?:incomplete|truncated)\))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex Incomplete = new(@"^\W*(?:this\s+)?(?:recipe\s+(?:is\s+)?)?(?:incomplete|truncated)(?![\p{L}])|^\s*(?:No\.?\s*)?\d+\s*\.?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex AmountOnly = new(@"^\W*(?:\d+(?:[./]\d+)?|[½¼¾⅓⅔⅛]|one|two|three|four|five|six|half|a|an)(?:\s+(?:and\s+)?(?:a\s+)?(?:half|quarter|\d+/\d+))?\s+(?:level\s+|heaping\s+|scant\s+)?(?:teaspoon|tablespoon|dessertspoon|spoon|cup|pint|quart|gallon|gill|pound|ounce|oz|lb|tsp|tbsp|tbs|c|pt|qt|dozen|pinch|dash)(?:s|ful|fuls|sful)?\.?\W*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex LikeAnother = new(@"(?<![\p{L}])(?:make|made|prepare|prepared)\s+(?:it\s+|this\s+)?(?:like|the\s+same\s+as|as)\s+[""“‘']", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex BaseLine = new(@"^\W*(?:[\p{L}'’-]+\s+){0,2}(?:batter|dough|paste|crust|mixture|forcemeat|stuffing)\s+(?:made\s+)?(?:as|the\s+same\s+as|like)\s+(?:in|for)\s+[\p{L}""“‘']|(?<![\p{L}])(?:batter|dough|paste|crust|mixture|forcemeat|stuffing|dressing|filling)\s*[(,]?\s*(?:such\s+as\s+(?:is\s+)?(?:used|made|given|served)\s+for|as\s+(?:is\s+)?used\s+for)(?![\p{L}])|^\W*(?:the\s+)?ingredients?\s+(?:for|of)\s+\p{L}|^\W*only\s+(?:\d|[½¼¾⅓⅔]|one|two|three|four|half|a\s|an\s)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex PageReference = new(@"\((?:see\s+)?(?:recipe\s+)?(?:page|p\.)\s*\d+\)|(?<![\p{L}])recipe\s+page\s+\d+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex PurposeClause = new(@"(?<![\p{L}])(?<!(?:directions?|methods?|recipes?|receipts?|ways?|rules?)\s+)for\s+[\p{L}].*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>Whether a recipe's lines were read out of a book's text: it was imported (<paramref name="scrapedAtUtc"/>) and has no web source. A recipe a household writes in the app has neither, lists its own ingredients and is read by its lines only.</summary>
        public static bool FromBook(string? sourceUrl, DateTime? scrapedAtUtc) => scrapedAtUtc != null && (string.IsNullOrWhiteSpace(sourceUrl) || !Web.IsMatch(sourceUrl));

        /// <summary>The title as the gate reads it: an imitation it names ("Mock Turtle", "Imitation Crab", "Mock Fried Oysters": proof draw 14; "To souce Veal to eat like Sturgeon": proof draw 17) blanked, and what an accompaniment is for ("Sauce for Boiled Cod" reads as Sauce, "Potato Crust for Meat Pies" as Potato Crust, "Coffee Icing for Eclairs" as Coffee Icing), and what any recipe says it is for ("To keep Damsons for Pies" reads as To keep Damsons: proof draw 13), unless the title gives directions for the dish ("Directions for Mince Pie"); and its number in a series ("RHUBARB PIE No. 2", "FRIED HALIBUT--II", "TOMATO SOUP (1)": proof draw 10 of ck17) dropped, so its last word is the dish.</summary>
        public static string Title(string? title) => string.IsNullOrWhiteSpace(title) ? string.Empty : PurposeClause.Replace(Accompaniment.Replace(Imitation.Replace(Numbering.Replace(title, " "), " "), m => m.Value.Split(' ')[0]), " ");

        private static readonly Regex Numbering = new(@"(?:[\s,.\-—–(]+(?:[Nn]o\.?\s*\d+|\d+|[IVX]+)\s*[).]*)+\s*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>
        /// The method's sentences, each read on its own. What a serving clause names ("serve with wine sauce", "serve on hot
        /// toast") is read as part of the dish, except where the recipe is itself an accompaniment (its title names a sauce, a
        /// dressing, a butter, a jelly ...: "Maître d'Hôtel Sauce ... serve with hot fish", "Banana Sauce ... serve with the
        /// fritters") or the clause leaves the choice open ("serve with cold meat or fish"): then it names what the recipe goes
        /// with, not what it is made of (proof draws 1 to 3 of ck11); what an accompaniment's title says it is for is not read in its method ("pour over the fish" under
        /// Sauce for Fish, "spread on meat pies" under Potato Crust for Meat Pies: proof draw 6; "pour around Baked Lake Trout" under Sauce à l'Italienne: proof draw 16). What a sentence leaves out, sends elsewhere or only compares with is not read ("instead of adding the oysters", "as in the
        /// recipe for tripe and oysters": proof draw 6; "as you do flower on frying fish": proof draw 15). Where the title names a shellfish and no line says fish, "the fish" is that
        /// shellfish ("preparing the fish as for potted shrimps" under Potted Shrimps, Diable: proof draw 17).
        /// </summary>
        public static IEnumerable<string> Sentences(string? method, string? title = null, IEnumerable<string?>? rawLines = null)
        {
            if (string.IsNullOrWhiteSpace(method)) yield break;
            var shellfish = !string.IsNullOrWhiteSpace(title) && ShellfishWord.IsMatch(title) && !ServedWithShellfish.IsMatch(title) && rawLines != null && !rawLines.Any(l => l != null && BareFishWord.IsMatch(l));
            var accompaniment = !string.IsNullOrWhiteSpace(title) && AccompanimentTitle.IsMatch(title) && !ServedIn.IsMatch(title);
            var servedWith = ServedWith(title);
            foreach (var raw in Sentence.Split(method))
            {
                var s = raw.Trim();
                if (s.Length == 0 || UseSentence.IsMatch(s) || UsedFor.IsMatch(s) || Variation.IsMatch(s) || PassSeparately.IsMatch(s)) continue;
                s = WithoutOptional(s);
                if (s.Length == 0) continue;
                s = Simile.Replace(Without.Replace(s, " "), " ").Trim();
                var serving = Serving.Match(s);
                if (serving.Success && (accompaniment || serving.Value.Contains(" or ", StringComparison.OrdinalIgnoreCase))) s = s[..serving.Index].Trim();
                if (accompaniment && PourAround.Match(s) is { Success: true } around) s = s[..around.Index].Trim();
                if (accompaniment && PourOver.Match(s) is { Success: true } over) s = s[..over.Index].Trim();
                if (servedWith != null) s = servedWith.Replace(s, " ").Trim();
                s = Elsewise.Replace(s, " ").Trim();
                if (shellfish) s = TheFish.Replace(s, "the shellfish");
                if (s.Length > 0) yield return s;
            }
        }

        private static readonly Regex ServedIn = new(@"^.*\p{L}.*(?<![\p{L}])(?:with|in|on)\s+(?:[\p{L}'’-]+\s+){0,3}(?:sauces?|dressings?|butters?|gravy|gravies|relish(?:es)?|jell(?:y|ies))\W*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex Variation = new(@"(?<![\p{L}])(?:another\s+(?:sauce|way|variety|variation|kind|flavou?r)|variations?)(?![\p{L}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex OptionalAfter = new(@"(?<![\p{L}])(?:if\s+(?:desired|liked|preferred|wished|convenient|at\s+hand|you\s+(?:like|wish|prefer|choose|want))|optional(?:ly)?)(?![\p{L}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex OptionalSubject = new(@"(?<![\p{L}])(?:may|can|might|could)\s+(?:also\s+)?be\s+(?:added|used|substituted|varied|omitted|sprinkled|served|put|mixed|stirred)(?![\p{L}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex PhraseBreak = new(@"[,;:()]|(?<![\p{L}])(?:and|or|with|then)(?![\p{L}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex ClauseBreak = new(@",\s*(?:and|but|or|then)(?![\p{L}])|[;:]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>A method sentence without its optional additions, and only them (staging checkpoint 7, S5; review ck16-1: "Sift the flour with the salt and add sugar if desired" still reads the flour): what "if desired", "if liked" or "optional" follows is the phrase back to the last break ("and sugar if desired", "nutmeg (optional)"), what it leads is the phrase up to the next break ("if liked, a little sherry"), and what "may be added" says of is its clause ("and raisins may be added, then bake").</summary>
        public static string WithoutOptional(string sentence)
        {
            var s = sentence;
            for (var guard = 0; guard < 8 && OptionalAfter.Match(s) is { Success: true } m; guard++)
            {
                var rest = s[(m.Index + m.Length)..];
                if (rest.TrimStart().StartsWith(','))
                {
                    var comma = s.IndexOf(',', m.Index + m.Length);
                    var end = s.Length;
                    foreach (Match b in PhraseBreak.Matches(s, comma + 1))
                        if (s[(comma + 1)..b.Index].Any(char.IsLetter)) { end = b.Index; break; }
                    s = (s[..m.Index] + " " + s[end..]).Trim();
                    continue;
                }
                var start = 0;
                var breaks = PhraseBreak.Matches(s[..m.Index]).Cast<Match>().Reverse().ToList();
                for (var k = 0; k < breaks.Count; k++)
                {
                    if (!s[(breaks[k].Index + breaks[k].Length)..m.Index].Any(char.IsLetter)) continue;
                    start = breaks[k].Index;
                    break;
                }
                s = (s[..start] + " " + s[(m.Index + m.Length)..]).Trim();
            }
            for (var guard = 0; guard < 8 && OptionalSubject.Match(s) is { Success: true } m; guard++)
            {
                var start = ClauseBreak.Matches(s[..m.Index]).Cast<Match>().LastOrDefault()?.Index ?? 0;
                var next = s.IndexOfAny(new[] { ',', ';', ':' }, m.Index + m.Length);
                s = (s[..start] + " " + (next < 0 ? string.Empty : s[next..])).Trim();
            }
            return s.Trim(' ', ',', ';', ':').Trim();
        }

        private static readonly Regex UsedFor = new(@"^\s*(?:this|the|it|these|they|which)\s+(?:[\p{L}'’-]+\s+){0,2}(?:is|are|may\s+be|can\s+be|will\s+be)\s+(?:used|served|good|nice|excellent|eaten|suitable|intended)(?![\p{L}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex PassSeparately = new(@"(?<![\p{L}])(?:pass|passed|hand|handed)\s+(?:round\s+)?(?:it\s+|them\s+)?separately(?![\p{L}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex Without = new(@"(?<![\p{L}])(?:without|omitting|omit|no)\s+(?:the\s+|any\s+)?[\p{L}'’-]+(?:\s+(?:or|and)\s+[\p{L}'’-]+)?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex Simile = new(@"(?<![\p{L}])(?:(?:just\s+)?like\s+(?:a|an|the|so\s+many)\s+(?:[\p{L}'’-]+\s+){0,2}[\p{L}'’-]+(?:\s+of\s+[\p{L}'’-]+)?|as\s+(?:big|large|small|thick|thin|stiff|soft|smooth|round|light|white|firm)\s+as\s+(?:a|an|the)?\s*[\p{L}'’-]+(?:\s+[\p{L}'’-]+)?|(?:the\s+)?size\s+of\s+(?:a|an|the)?\s*[\p{L}'’-]+(?:\s+[\p{L}'’-]+)?|come\s+(?:una?\s+|le\s+|i\s+|gli\s+)?[\p{L}'’-]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex PourOver = new(@"(?<![\p{L}])(?:pour|poured|strain|strained|serve|served|spread|put)\s+(?:it\s+|this\s+|them\s+|the\s+sauce\s+)?(?:over|on|upon)\s+(?:the|your|any)(?![\p{L}]).*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex PourAround = new(@"(?<![\p{L}])pour\s+(?:it\s+|this\s+|the\s+sauce\s+)?(?:a)?round\b.*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static Regex? ServedWith(string? title)
        {
            var match = string.IsNullOrWhiteSpace(title) ? null : Accompaniment.Match(title);
            if (match is not { Success: true }) return null;
            var words = Regex.Matches(match.Value, @"\p{L}{3,}").Select(w => w.Value.ToLowerInvariant().TrimEnd('s')).Skip(1)
                .Where(w => w is not ("for" or "serve" or "with" or "eat")).Distinct().ToList();
            return words.Count == 0 ? null : new Regex(@"(?<![\p{L}])(?:" + string.Join('|', words.Select(Regex.Escape)) + @")(?:e?s)?(?![\p{L}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        /// <summary>Whether the method is read for a rule: every rule but one whose term starts with a word of <see cref="MethodSkips"/>, and one of <see cref="MethodEchoes"/> only when no line of <paramref name="rawLines"/> names that kind of food (none given: not read).</summary>
        public static bool ReadsMethod(DietPatternRule rule, IEnumerable<string?>? rawLines = null)
        {
            if (rule.Substring || rule.Words.Count == 0) return true;
            var first = rule.Words[0];
            if (MethodSkips.Contains(first, StringComparer.Ordinal)) return false;
            if (!MethodEchoes.Contains(first, StringComparer.Ordinal)) return true;
            if (rawLines == null) return false;
            var named = first switch { "shortening" => FatWord, "pancake" => FlourWord, _ => CrumbWord };
            return !rawLines.Any(l => l != null && named.IsMatch(l));
        }

        /// <summary>Whether a rule's term is one of <see cref="MethodEchoes"/>, read in the method per recipe by <see cref="ReadsMethod"/>.</summary>
        public static bool Echoes(DietPatternRule rule) => !rule.Substring && rule.Words.Count > 0 && MethodEchoes.Contains(rule.Words[0], StringComparer.Ordinal);

        private static readonly Regex ServedWithShellfish = new(@"(?<![\p{L}])(?:with|and|in|au|aux)\s+(?:[\p{L}'’-]+\s+){0,2}(?:shrimps?|prawns?|oysters?|lobsters?|crabs?|clams?|mussels?|scallops?)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex BareFishWord = new(@"(?<![\p{L}])fish(?![\p{L}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex TheFish = new(@"(?<![\p{L}])the\s+fish(?![\p{L}-])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex ShellfishWord = new(@"(?<![\p{L}])(?:shrimps?|prawns?|oysters?|lobsters?|crabs?|clams?|mussels?|cray(?:fish|fishes)|craw(?:fish|fishes)|crawdads?|langoustines?|cockles?|whelks?|winkles?|periwinkles?|terrapins?)(?![\p{L}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>
        /// What a method sentence names as an ingredient with a term the method is otherwise not read for, each read as a line
        /// would be: a dish of <see cref="DietDishes"/> ("Dip into frying batter", "line the dish with puff paste"), since a
        /// named preparation is the food it is made of, not a shape or the recipe's own mixture; a portion of a cake or a roll
        /// ("spread between layers of cake" under PEACH-CREAM FILLING, "dip small pieces of cake into the icing": proof draw 15 of
        /// ck17), and a cake a method spreads, lays or pours something on ("Spread over one cake ... repeat for remaining
        /// cakes" under BANANA FILLING: proof draw 16; "Spread over hot cake" under Broiled Coconut Topping: proof draw 17), which is the baked food, not a cake's shape or its tin; and, where no line of
        /// <paramref name="rawLines"/> names a nut, nuts named as an ingredient ("Put on whole nuts if desired", "add nuts and
        /// raisins", "broken nut meats"), never a nut's size or shape ("butter as large as a nut", "roll into nuts", "dough
        /// nuts": proof draw 9 of the ck11 review).
        /// </summary>
        public static IEnumerable<string> Mentions(string sentence, IEnumerable<string?>? rawLines = null)
        {
            if (string.IsNullOrWhiteSpace(sentence)) yield break;
            var folded = DietPatternRule.Fold(sentence);
            if (SkippedDishes.Value is { } dishes)
                foreach (Match m in dishes.Matches(folded)) yield return m.Value;
            foreach (Match m in PortionOfBaked.Matches(folded)) yield return m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
            if (rawLines == null || rawLines.Any(l => l != null && NutWord.IsMatch(l))) yield break;
            foreach (Match m in NutIngredient.Matches(folded)) yield return m.Value;
        }
    }
}
