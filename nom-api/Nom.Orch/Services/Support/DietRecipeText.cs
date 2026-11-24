using System;
using System.Collections.Generic;
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
        public const string Version = "recipe-text/2";

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

        private static readonly Regex NamedBase = new(@"(?<![\p{L}])(?:[Mm]ix(?:ing)?|[Mm]ak(?:e|ing)|[Pp]repar(?:e|ing)|[Uu]s(?:e|ing))\s+(?:the\s+|a\s+|your\s+|one\s+)?(?:\p{Lu}[\p{L}'’-]*\s+){1,4}(?:batter|dough|mixture|frosting|icing|filling|crust|pastry|paste)(?![\p{L}])", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex Truncated = new(@"(?<![\p{L}])(?:of|and|or|with|the)\s*[,.;:-]*\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex UseSentence = new(@"^\s*(?:it\s+(?:is|may\s+be)\s+)?(?:use[sd]?|good|nice|excellent|delicious)\s+(?:it\s+|this\s+|them\s+|these\s+)?(?:to|for|on|in|with|as)(?![\p{L}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex NutIngredient = new(@"(?<![\p{L}])(?:(?:(?:chopped|broken|whole|halved|blanched|salted|toasted|roasted|shelled|ground|grated|pounded|minced|sliced|mixed|some|few|any|with|and|or|add|added|adding)\s+)+(?:\p{L}+\s+)?nuts(?:\s*meats?)?|nut\s*meats?|nut\s+(?:butter|filling|paste|cream)|nuts\s+(?:are|may|can|should)\s+(?:be\s+)?(?:added|used|sprinkled|put))(?![\p{L}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

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
        public static bool ReadsTitle(string? method, DietPatternRule? rule, bool refersElsewhere = false, string? title = null) =>
            string.IsNullOrWhiteSpace(method) || refersElsewhere || Garnishes(title, rule)
            || (rule != null && !rule.Substring && rule.Words.Count > 0 && (TitleTerms.Contains(rule.Words[^1], StringComparer.Ordinal) || TitleTranslations.Value.Contains(string.Join(' ', rule.Words))) && !Accompanies(title, rule) && !MakesTheDish(title, rule) && !ScallopedDish(title, rule));

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

        /// <summary>Whether the method builds the recipe on another recipe of the book, named as a batter, a dough, a mixture, a crust or a frosting the lines do not give ("Set aside while mixing Velvet Crumb Cake batter" under Date Velvet Crumb Cake, "Make Biscuit dough", "Use the Genoese mixture": proof draw 10 of the ck11 review; "Make like \"Brown Gravy\"", or a line "batter as for suet pudding": proof draw 16; a line "ingredients for oyster sauce", a line that is only an amount, "1 teaspoon", or a method that says it is incomplete: proof draw 17): what that base holds is not in the recipe, so a severe set cannot read it and holds it as having no readable ingredient list.</summary>
        public static bool BuildsOnAnother(string? method, IEnumerable<string?>? rawLines = null) =>
            (!string.IsNullOrWhiteSpace(method) && (NamedBase.IsMatch(method) || LikeAnother.IsMatch(method) || Incomplete.IsMatch(method))) || (rawLines != null && rawLines.Any(l => l != null && (BaseLine.IsMatch(l) || AmountOnly.IsMatch(l))));

        private static readonly Regex Incomplete = new(@"^\W*(?:this\s+)?(?:recipe\s+(?:is\s+)?)?(?:incomplete|truncated)(?![\p{L}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex AmountOnly = new(@"^\W*(?:\d+(?:[./]\d+)?|[½¼¾⅓⅔⅛]|one|two|three|four|five|six|half|a|an)(?:\s+(?:and\s+)?(?:a\s+)?(?:half|quarter|\d+/\d+))?\s+(?:level\s+|heaping\s+|scant\s+)?(?:teaspoon|tablespoon|dessertspoon|spoon|cup|pint|quart|gallon|gill|pound|ounce|oz|lb|tsp|tbsp|tbs|c|pt|qt|dozen|pinch|dash)(?:s|ful|fuls|sful)?\.?\W*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex LikeAnother = new(@"(?<![\p{L}])(?:make|made|prepare|prepared)\s+(?:it\s+|this\s+)?(?:like|the\s+same\s+as|as)\s+[""“‘']", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex BaseLine = new(@"^\W*(?:[\p{L}'’-]+\s+){0,2}(?:batter|dough|paste|crust|mixture|forcemeat|stuffing)\s+(?:made\s+)?(?:as|the\s+same\s+as|like)\s+(?:in|for)\s+[\p{L}""“‘']|^\W*(?:the\s+)?ingredients?\s+(?:for|of)\s+\p{L}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex PageReference = new(@"\((?:see\s+)?(?:recipe\s+)?(?:page|p\.)\s*\d+\)|(?<![\p{L}])recipe\s+page\s+\d+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex PurposeClause = new(@"(?<![\p{L}])(?<!(?:directions?|methods?|recipes?|receipts?|ways?|rules?)\s+)for\s+[\p{L}].*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>Whether a recipe's lines were read out of a book's text: it was imported (<paramref name="scrapedAtUtc"/>) and has no web source. A recipe a household writes in the app has neither, lists its own ingredients and is read by its lines only.</summary>
        public static bool FromBook(string? sourceUrl, DateTime? scrapedAtUtc) => scrapedAtUtc != null && (string.IsNullOrWhiteSpace(sourceUrl) || !Web.IsMatch(sourceUrl));

        /// <summary>The title as the gate reads it: an imitation it names ("Mock Turtle", "Imitation Crab", "Mock Fried Oysters": proof draw 14; "To souce Veal to eat like Sturgeon": proof draw 17) blanked, and what an accompaniment is for ("Sauce for Boiled Cod" reads as Sauce, "Potato Crust for Meat Pies" as Potato Crust, "Coffee Icing for Eclairs" as Coffee Icing), and what any recipe says it is for ("To keep Damsons for Pies" reads as To keep Damsons: proof draw 13), unless the title gives directions for the dish ("Directions for Mince Pie").</summary>
        public static string Title(string? title) => string.IsNullOrWhiteSpace(title) ? string.Empty : PurposeClause.Replace(Accompaniment.Replace(Imitation.Replace(title, " "), m => m.Value.Split(' ')[0]), " ");

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
                if (s.Length == 0 || UseSentence.IsMatch(s)) continue;
                var serving = Serving.Match(s);
                if (serving.Success && (accompaniment || serving.Value.Contains(" or ", StringComparison.OrdinalIgnoreCase))) s = s[..serving.Index].Trim();
                if (accompaniment && PourAround.Match(s) is { Success: true } around) s = s[..around.Index].Trim();
                if (servedWith != null) s = servedWith.Replace(s, " ").Trim();
                s = Elsewise.Replace(s, " ").Trim();
                if (shellfish) s = TheFish.Replace(s, "the shellfish");
                if (s.Length > 0) yield return s;
            }
        }

        private static readonly Regex ServedIn = new(@"^.*\p{L}.*(?<![\p{L}])(?:with|in|on)\s+(?:[\p{L}'’-]+\s+){0,3}(?:sauces?|dressings?|butters?|gravy|gravies|relish(?:es)?|jell(?:y|ies))\W*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

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
        /// named preparation is the food it is made of, not a shape or the recipe's own mixture; and, where no line of
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
            if (rawLines == null || rawLines.Any(l => l != null && NutWord.IsMatch(l))) yield break;
            foreach (Match m in NutIngredient.Matches(folded)) yield return m.Value;
        }
    }
}
