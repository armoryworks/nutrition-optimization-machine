using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Nom.Data.Nutrition;

namespace Nom.Orch.Services.Support
{
    /// <summary>What the rest of a recipe tells about one of its lines: the title, the other lines, the step text, and whether the recipe came from a web source (a modern recipe).</summary>
    public sealed record AlcoholRecipeContext(string? Title = null, IReadOnlyList<string>? OtherLines = null, string? Steps = null, bool HasSourceSite = false);

    /// <summary>How a sense of an alcohol word stands to the foods a line may be linked to.</summary>
    public enum AlcoholSenseKind
    {
        /// <summary>No alcohol word: the rules say nothing.</summary>
        None,
        /// <summary>A list or alternatives: each part has its own reading.</summary>
        List,
        /// <summary>Not a drink (a vinegar, a flavouring, a measure, a dish, a soft drink ...): never an alcoholic food.</summary>
        NotDrink,
        /// <summary>Not food at all ("spirits of turpentine"): no food.</summary>
        NotFood,
        /// <summary>A drink with USDA foods of its sense: only those.</summary>
        Drink,
        /// <summary>A drink USDA has no food for (vermouth, mirin, most liqueurs): no food.</summary>
        Held,
        /// <summary>A drink whose kind the line and its recipe cannot tell (bare cider, bare wine in an old text): no food.</summary>
        Unclear,
    }

    /// <summary>One line's alcohol sense: its code, its kind, the rule that gave it and, for a list, each part's reading.</summary>
    public sealed record AlcoholReading(string Sense, AlcoholSenseKind Kind, string Rule, IReadOnlyList<AlcoholReading>? Parts = null)
    {
        public static readonly AlcoholReading Nothing = new(AlcoholLineContext.NoSense, AlcoholSenseKind.None, "no alcohol word");

        public override string ToString() => Parts == null ? Sense : $"{Sense} ({string.Join(" | ", Parts.Select(p => p.Sense))})";
    }

    /// <summary>Why a reading refuses a food: the link gate rule code and the detail.</summary>
    public sealed record AlcoholRefusal(string Code, string Detail);

    /// <summary>
    /// Reads the sense of an alcohol word in a recipe line from the line and its recipe, by rules,
    /// with no model (alcohol design 2.1): a list, something that is not a drink (a vinegar, a
    /// non-alcoholic drink, a meat or yeast extract, ale yeast, a cooking liquid, not food, Scotch
    /// barley and the like), a measure ("a wineglass of milk"), a dish named after a drink, a
    /// flavouring extract, cooking wine and rice wines, soft drinks, cider, fortified wine, spirits,
    /// liqueurs, beer, and wine by colour, with bare "wine" read from the recipe. The first rule
    /// that fires wins. Each sense allows a fixed set of USDA foods (<see cref="Allowed"/>); a drink
    /// USDA has no food for, or one the recipe cannot tell, allows none, and a sense that is not a
    /// drink allows no alcoholic food. The link gate holds any answer a line's sense refuses, the
    /// name matcher's shortlist keeps only the foods a wording's sense allows, and the diet gate
    /// counts a food with no alcohol data by the sense of its name. See docs/architecture/alcohol-context.md.
    /// </summary>
    public static class AlcoholLineContext
    {
        public const string Version = "alcohol-context/6";

        public const string NoSense = "none";
        public const string ListSense = "list";
        private const string MeasureSense = "measure";

        private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

        private sealed record SenseSpec(AlcoholSenseKind Kind, string[] Allowed, string[]? Requires = null, string[]? Refuses = null);

        private static readonly string[] RedTable = { "173190", "171872", "171925", "173191", "173192", "173193", "173194", "173208", "174109", "174833", "174834", "174835", "174836", "174838", "174839", "174840" };
        private static readonly string[] WhiteTable = { "174837", "173195", "173196", "173198", "173200", "173201", "174110", "174841", "174843", "174845", "174848", "174849" };

        private static readonly Dictionary<string, SenseSpec> Senses = new(StringComparer.Ordinal)
        {
            ["vinegar"] = new(AlcoholSenseKind.NotDrink, Array.Empty<string>(), Requires: new[] { "vinegar" }),
            ["vinaigrette"] = new(AlcoholSenseKind.NotDrink, Array.Empty<string>()),
            ["non-alcoholic"] = new(AlcoholSenseKind.NotDrink, Array.Empty<string>()),
            ["extract-food"] = new(AlcoholSenseKind.NotDrink, Array.Empty<string>()),
            ["yeast"] = new(AlcoholSenseKind.NotDrink, Array.Empty<string>(), Requires: new[] { "yeast" }),
            ["liquid"] = new(AlcoholSenseKind.NotDrink, Array.Empty<string>()),
            ["other"] = new(AlcoholSenseKind.NotDrink, Array.Empty<string>()),
            ["not-food"] = new(AlcoholSenseKind.NotFood, Array.Empty<string>()),
            ["measure"] = new(AlcoholSenseKind.NotDrink, Array.Empty<string>()),
            ["dish"] = new(AlcoholSenseKind.NotDrink, Array.Empty<string>()),
            ["extract"] = new(AlcoholSenseKind.NotDrink, Array.Empty<string>(), Requires: new[] { "extract", "flavor", "flavoring", "flavour", "flavouring", "essence" }),
            ["soft-drink"] = new(AlcoholSenseKind.NotDrink, Array.Empty<string>(), Refuses: new[] { "vinegar" }),
            ["apple-juice"] = new(AlcoholSenseKind.NotDrink, Array.Empty<string>(), Requires: new[] { "apple" }, Refuses: new[] { "vinegar" }),
            ["cooking-wine"] = new(AlcoholSenseKind.Drink, new[] { "169070" }),
            ["sake"] = new(AlcoholSenseKind.Drink, new[] { "167723" }),
            ["hard-cider"] = new(AlcoholSenseKind.Drink, new[] { "174146" }),
            ["fortified-dry"] = new(AlcoholSenseKind.Drink, new[] { "175112" }),
            ["fortified-sweet"] = new(AlcoholSenseKind.Drink, new[] { "173176" }),
            ["fortified"] = new(AlcoholSenseKind.Drink, new[] { "175112", "173176" }),
            ["spirit"] = new(AlcoholSenseKind.Drink, new[] { "174815" }),
            ["rum"] = new(AlcoholSenseKind.Drink, new[] { "174817", "174815" }),
            ["vodka"] = new(AlcoholSenseKind.Drink, new[] { "174818", "174815" }),
            ["whiskey"] = new(AlcoholSenseKind.Drink, new[] { "174819", "174815" }),
            ["coffee-liqueur"] = new(AlcoholSenseKind.Drink, new[] { "175097", "173665" }),
            ["cream-liqueur"] = new(AlcoholSenseKind.Drink, new[] { "175098" }),
            ["creme-de-menthe"] = new(AlcoholSenseKind.Drink, new[] { "173168" }),
            ["beer"] = new(AlcoholSenseKind.Drink, new[] { "168746" }),
            ["light-beer"] = new(AlcoholSenseKind.Drink, new[] { "168749" }),
            ["malt-liquor"] = new(AlcoholSenseKind.Drink, new[] { "171907" }),
            ["wine-red"] = new(AlcoholSenseKind.Drink, RedTable),
            ["wine-white"] = new(AlcoholSenseKind.Drink, WhiteTable),
            ["wine-rose"] = new(AlcoholSenseKind.Drink, new[] { "171908" }),
            ["wine-late-harvest"] = new(AlcoholSenseKind.Drink, new[] { "173202", "174844" }),
            ["wine-table"] = new(AlcoholSenseKind.Drink, new[] { "173185", "173190", "174837" }),
            ["rice-wine"] = new(AlcoholSenseKind.Held, Array.Empty<string>()),
            ["boiled-cider"] = new(AlcoholSenseKind.Held, Array.Empty<string>()),
            ["liqueur"] = new(AlcoholSenseKind.Held, Array.Empty<string>()),
            ["vermouth"] = new(AlcoholSenseKind.Held, Array.Empty<string>()),
            ["country-wine"] = new(AlcoholSenseKind.Held, Array.Empty<string>()),
            ["wine-unclear"] = new(AlcoholSenseKind.Unclear, Array.Empty<string>()),
            ["cider-unclear"] = new(AlcoholSenseKind.Unclear, Array.Empty<string>()),
            ["alcohol-unclear"] = new(AlcoholSenseKind.Unclear, Array.Empty<string>()),
        };

        private const string DrinkWords =
            @"wines?|sherry|brandy|rum|whiske?y|bourbon|beers?|ales?|cider|port|madeira|marsala|kirsch|cognac|champagne|liqueurs?|gin|vodka|claret|sack|stout|vermouth|tequila|sake|calvados|grand marnier|cointreau|amaretto|kahlua|hock|burgundy|chablis";

        internal static readonly Regex Trigger = new(
            @"\b(?:wines?|wineglass\w*|sherry|brandy|brandies|rum|whiske?y|bourbon|scotch|beers?|ales?|lager|stout|porter|pilsn?er|ipa|bock|guinness|cider|perry|scrumpy|port|sack|madeira|marsala|vermouth|liqueurs?|liquor|spirits?|alcohol\w*|bitters|angostura|extract|essence|flavou?r(?:ing)?|sake|mirin|shaoxing|shaohsing|hsing|gin|vodka|tequila|mezcal|cognac|armagnac|calvados|applejack|kirsch\w*|grappa|hollands|arrack|arak|schnapps?|champagne|prosecco|cava|claret|burgundy|bordeaux|chablis|hocks?|brewer'?s|brewers|rhenish|riesling|chardonnay|sauvignon|merlot|cabernet|chianti|zinfandel|syrah|shiraz|rioja|malbec|pinot|sangiovese|gamay|beaujolais|grenache|tempranillo|barolo|valpolicella|sauternes?|tokay|tokaji|malaga|muscatel|muscatels|moscatel|amontillado|fino|oloroso|manzanilla|sercial|malmsey|kahlua|baileys|curacao|curacoa|cointreau|maraschino|noyau|noyeau|amaretto|frangelico|grand marnier|chartreuse|benedictine|drambuie|limoncello|sambuca|anisette|ratafia|cassis|creme de|ouzo|pastis|absinthe|galliano|strega|advocaat|pisco|everclear|eau de vie|mead|barm|moselle|muscadet|sancerre|soave|gewurztraminer|semillon|sekt|asti|frascati|orvieto|eiswein|icewine|vins?|catawba)\b",
            Options);

        private static readonly Regex ListSplit = new(@"\s+(?:and\s*/\s*or|or|and|&)\s+|\s*\(\s*or\s+|(?<=\p{L})\s*/\s*(?=\p{L})", Options);
        private static readonly Regex ListJoiner = new(@"\s(?:and|or|&)\s|\(\s*or\s|(?<=\p{L})\s*/\s*(?=\p{L})", Options);
        private const string AmountWord = @"(?:a|an|one|two|three|four|five|six|seven|eight|nine|ten|twelve|half|amount|\d+(?:[./]\d+)?)";
        private const string AmountMeasure = @"(?:cups?|cupfuls?|teacups?|teacupfuls?|spoons?|spoonfuls?|teaspoons?|teaspoonfuls?|tablespoons?|tablespoonfuls?|dessertspoons?|dessertspoonfuls?|tsps?|tbsps?|gallons?|quarts?|pints?|gills?|jills?|glass(?:es|ful|fuls|esful)?|wineglass(?:es|ful|fuls|esful)?|tumblers?|tumblerfuls?|bottles?|pounds?|lbs?|ounces?|oz|drachms?|dozen|handfuls?|pinch(?:es)?|dash(?:es)?|drops?|liters?|litres?|ml)";
        private static readonly Regex AmountJoiner = new(@"\b" + AmountWord + @"(?:\s+" + AmountMeasure + @")?\s+(?:and|or)\s+" + AmountWord + @"(?:\s+(?:halfs?|halves|quarters?|thirds?))?\b", Options);
        private static readonly Regex DishBeforeJoiner = new(@"\b(?:soaked|soak|boil'?d|boiled|steeped|macerated|marinated|poached|stewed)\s+in\b", Options);

        private static readonly Regex NotFood = new(@"\b(?:turpentine|camphor|nitre|niter|hartshorn|lamp|varnish|bay rum|rubbing alcohol|isopropyl|denatured|methylated|white spirits?|spirits? of ammonia|sal volatile|surgical spirit|wood alcohol)\b", Options);

        private static readonly (string Sense, string Rule, Regex Pattern)[] NotDrink =
        {
            ("vinegar", "vinegar", new Regex(@"\bvinegars?\b", Options)),
            ("vinaigrette", "vinaigrette", new Regex(@"\bvinaigrettes?\b", Options)),
            ("non-alcoholic", "non-alcoholic", new Regex(@"\bnon ?alcoholic\b|\balcohol free\b|\balcohol removed\b|\bde ?alcoholi[sz]ed\b|\bzero proof\b|\bno alcohol\b|\bwithout alcohol\b", Options)),
            ("extract-food", "a meat, fish, malt or yeast extract", new Regex(@"\b(?:meat|beef|malt|yeast|vegetable|chicken|liebig'?s?|anchov(?:y|ies)|shrimps?|fish|mushrooms?|lobster|tomato)\s+(?:extract|essence)\b|\b(?:extract|essence) of (?:meat|beef|malt|yeast|vegetables?|chicken|anchov(?:y|ies)|shrimps?|fish|mushrooms?|lobsters?|tomato(?:es)?)\b", Options)),
            ("yeast", "ale or beer yeast", new Regex(@"\b(?:ale|beer|brewer'?s?|brewers)\s+(?:yeasts?|yests?)\b|\b(?:yeast|yest) of (?:ale|beer)\b|\bbarm\b", Options)),
            ("not-food", "not food", NotFood),
            ("other", "Scotch barley, oats and the like", new Regex(@"\bscotch\s+(?:barley|oats?|oatmeal|groats|bonnets?|broth|eggs?|kale|pancakes?|woodcock|collops|shortbread|pies?|mist|tape)\b", Options)),
            ("other", "a sack of", new Regex(@"\bsacks? of\b", Options)),
            ("other", "a ham hock", new Regex(@"\b(?:ham|pork|smoked|beef|veal|lamb)\s+hocks?\b|\bhocks? of\b|\bhock end\b|\bhocks\b", Options)),
            ("other", "Port Salut", new Regex(@"\bport salut\b|\bport du salut\b", Options)),
            ("other", "Muscatel or Malaga raisins and grapes", new Regex(@"\b(?:malaga|muscatel|moscatel)\s+(?:raisins?|grapes?)\b|\bmuscatel raisins?\b|\bmuscatels\b", Options)),
            ("other", "a drink's name for a kind of another food", new Regex(@"\b(?:malaga|tokay|tokaji|muscatel|moscatel|catawba)\s+(?:lemons?|limes?|oranges?|citrons?|grapes?|raisins?|figs?|melons?)\b|\bmanzanilla\s+olives?\b|\bscotch\s+caraways?\b", Options)),
            ("other", "a food called after a drink", new Regex(@"\b(?:called|named|known as)\s+(?:the\s+)?(?:" + DrinkWords + @")", Options)),
            ("other", "ratafia biscuits", new Regex(@"\bratafias?\s+(?:crumbs?|biscuits?|cakes?|drops?|macaroons?|cookies?|wafers?)\b|\bratafias\b", Options)),
            ("other", "stout as strong", new Regex(@"\bstout\s+(?:\p{L}+\s+)?(?:muslin|cloth|bags?|string|twine|paper|thread|cord|needle|skewer|stick|board|pan|jar|crock|knife|fork|woman|man|hen|fowl|bird)\b", Options)),
            ("other", "champagne mango or grapes", new Regex(@"\bchampagne (?:mangos?|mangoes|grapes?)\b", Options)),
            ("other", "maraschino cherries", new Regex(@"\bmaraschino cherr(?:y|ies)\b", Options)),
            ("other", "malt for ale or beer", new Regex(@"\bmalt for (?:ale|beer)\b", Options)),
            ("measure", "an ale or wine measure", new Regex(@"\b(?:ale|wine|beer)\s+measure\b", Options)),
            ("other", "for the rum", new Regex(@"\bfor the rum\b", Options)),
        };

        private static readonly Regex LiquorWord = new(@"\bliquor\b", Options);
        private static readonly Regex SpiritWord = new(@"\b(?:brandy|rum|whiske?y|bourbon|gin|vodka|tequila|cognac|liqueur|malt liquor|orange liquor|coffee liquor)\b", Options);

        private static readonly Regex Measure = new(@"\b(?:wine|claret|sherry|liqueur|whiske?y|brandy|port|champagne|beer|cordial)\s?glass(?:es|ful|fuls|esful)?\b", Options);
        private static readonly Regex OfPart = new(@"\bof\b\s*(.*)$", Options);

        private static readonly Regex DishAfterDrink = new(
            @"\b(?:" + DrinkWords + @")\s+(?:\p{L}+\s+)?(?:brats?|bratwursts?|wursts?|sauce|butter|jell(?:y|ies)|syrup|cakes?|glaze|gravy|cream|custard|paper|punch|sangria|frosting|icing|marinade|trifle|posset|sorbet|granita|batter|bread|salami|sausages?|snaps|balls|truffles?|pudding|soup|reduction|spritzer|cocktail|mustard|jam|cheese|biscuits?|cookies?|caramel)\b",
            Options);
        private static readonly Regex DrinkFlavoured = new(@"\b(?:" + DrinkWords + @")\s+flavou?red\b", Options);
        private static readonly Regex WithDrink = new(@"\b(?:flavou?red|moistened|mixed|wetted|sprinkled|soaked|steeped|macerated|perfumed|seasoned)\s+with\b[^,;]*\b(?:" + DrinkWords + @")\b|\bto every\b.*\ballow\b.*\b(?:" + DrinkWords + @")\b", Options);
        private static readonly Regex SpiritsOf = new(@"\bspirits? of (?!wine\b)\p{L}+", Options);
        private static readonly Regex Note = new(@"\([^()]*\)|\bfor\b[^,;()]*\bflavou?r\w*|\bor\s+flavou?r\w*\s+to\s+taste\b|\*.*$", Options);
        private static readonly Regex AuVin = new(@"\bau vin\b", Options);
        private static readonly Regex SoakedIn = new(@"\b(?:soaked|soak|boil'?d|boiled|steeped|macerated|marinated|poached|stewed)\s+in\b.*\b(?:" + DrinkWords + @")\b", Options);

        private static readonly Regex Flavouring = new(@"\bextracts?\b|\bessence\b|\b(?!(?:for|to|as|of|the|a|and|or|more|extra|add|with|added|natural|good|any|sweeter|richer|stronger|milder|deeper|less|intense|full|great|nice|best|better|bit|little|lot)\b)\p{L}+\s+flavou?r(?:ing)?s?\b(?!\s+(?!(?:extract|essence|to|or|and|if|for|as|optional|divided)\b)\p{L})", Options);

        private static readonly (string Sense, string Rule, Regex Pattern)[] Rules =
        {
            ("rice-wine", "Shaoxing, Chinese rice wine or mirin", new Regex(@"\bshaoxing\b|\bshao ?hsing\b|\bchinese (?:rice |cooking )?wine\b|\brice (?:cooking )?wine\b|\bmirin\b|\bhua ?tiao\b|\bhuadiao\b", Options)),
            ("cooking-wine", "cooking wine or cooking sherry", new Regex(@"\bcooking (?:wine|sherry|sake|marsala)\b", Options)),
            ("sake", "sake", new Regex(@"(?<!\bfor\b[^,]*)\bsake\b(?!\s*'s)", Options)),
            ("soft-drink", "ginger beer, root beer, ginger ale", new Regex(@"\b(?:ginger|root|spruce|birch|sarsaparilla)\s+(?:beers?|ales?)\b", Options)),
            ("boiled-cider", "boiled cider", new Regex(@"\bboiled cider\b", Options)),
            ("hard-cider", "hard cider or perry", new Regex(@"\bhard cider\b|\bperry\b|\bscrumpy\b|\bfermented cider\b", Options)),
            ("apple-juice", "sweet, fresh or apple cider", new Regex(@"\b(?:sweet|fresh|new|unfermented|apple|mulled|unfiltered|cold)\s+cider\b", Options)),
            ("cider-unclear", "bare cider", new Regex(@"\bciders?\b", Options)),
            ("fortified-sweet", "sweet fortified wine", new Regex(@"\b(?:cream sherry|sweet sherry|port|tokay|tokaji|malaga|muscatel|moscatel|malmsey|sweet marsala|sweet madeira|vin santo|dessert wine|sweet wine|mavrodaphne|banyuls)\b", Options)),
            ("fortified-dry", "dry fortified wine", new Regex(@"\b(?:sherry|sack|amontillado|fino|oloroso|manzanilla|sercial|dry madeira|dry marsala)\b", Options)),
            ("fortified", "Madeira or Marsala", new Regex(@"\b(?:madeira|marsala)\b", Options)),
            ("coffee-liqueur", "coffee liqueur", new Regex(@"\bcoffee (?:liqueur|brandy|liquor)\b|\bkahlua\b|\btia maria\b", Options)),
            ("cream-liqueur", "Irish cream", new Regex(@"\birish cream\b|\bbaileys\b|\bbailey's\b|\bcream liqueur\b", Options)),
            ("creme-de-menthe", "creme de menthe", new Regex(@"\bcreme de menthe\b", Options)),
            ("liqueur", "a liqueur or fruit brandy USDA has no food for", new Regex(@"\b(?:peach|cherry|apricot|blackberry|plum|raspberry|ginger|orange)\s+brand(?:y|ies)\b|\b(?:grand marnier|cointreau|curacao|curacoa|triple sec|maraschino|noyau|noyeau|amaretto|frangelico|chartreuse|benedictine|drambuie|limoncello|sambuca|anisette|ratafia|creme de \p{L}+|cassis|liqueurs?|bitters|angostura|ouzo|pastis|absinthe|galliano|strega|advocaat|schnapps?|orange liquor)\b", Options)),
            ("rum", "rum", new Regex(@"\brum\b", Options)),
            ("vodka", "vodka", new Regex(@"\bvodka\b", Options)),
            ("whiskey", "whiskey", new Regex(@"\bwhiske?y\b|\bbourbon\b|\bscotch\b", Options)),
            ("spirit", "a distilled spirit", new Regex(@"\b(?:brandy|brandies|cognac|armagnac|calvados|applejack|kirsch|kirschwasser|grappa|gin|hollands|tequila|mezcal|arrack|arak|pisco|everclear|eau de vie|proof spirit|rectified spirit|neutral spirit|grain spirit|grain alcohol|pure alcohol|spirits? of wine|spirits)\b", Options)),
            ("light-beer", "light beer", new Regex(@"\b(?:light|lite)\s+beers?\b", Options)),
            ("malt-liquor", "malt liquor", new Regex(@"\bmalt liquor\b", Options)),
            ("beer", "beer, ale, lager, stout", new Regex(@"\b(?:beers?|ales?|lager|stout|porter|pilsn?er|ipa|bock|guinness)\b", Options)),
            ("vermouth", "vermouth", new Regex(@"\bvermouth\b", Options)),
            ("wine-late-harvest", "late harvest or Sauternes", new Regex(@"\blate harvest\b|\bsauternes?\b|\bice ?wine\b|\beiswein\b", Options)),
            ("country-wine", "a fruit or flower wine", new Regex(@"\b(?:ginger|elder|elderberry|elderflower|cowslip|currant|gooseberry|raisin|orange|cherry|plum|damson|dandelion|primrose|birch|blackberry|rhubarb|parsnip|clary|balm|raspberry|apricot|peach|palm|rice|honey)\s+wines?\b|\bmead\b", Options)),
            ("wine-rose", "rose wine", new Regex(@"\brose wines?\b|\bblush wines?\b|\bwhite zinfandel\b", Options)),
            ("wine-white", "white burgundy", new Regex(@"\bwhite burgundy\b", Options)),
            ("wine-red", "a red wine name", new Regex(@"\b(?:claret|burgundy|bordeaux|merlot|cabernet|chianti|zinfandel|syrah|shiraz|rioja|malbec|pinot noir|sangiovese|gamay|beaujolais|grenache|tempranillo|barolo|valpolicella|vin rouge)\b", Options)),
            ("wine-white", "a white wine name", new Regex(@"\b(?:chablis|riesling|chardonnay|sauvignon|pinot grigio|pinot gris|moselle|rhine wine|rhenish|hock|champagne|prosecco|cava|sparkling wine|muscadet|sancerre|soave|vinho verde|gewurztraminer|semillon|chenin blanc|pinot blanc|vermentino|asti|frascati|orvieto|sekt|vin blanc)\b", Options)),
            ("wine-unclear", "vin, or a grape USDA gives no colour for", new Regex(@"\bvins?\b|\bcatawba\b", Options)),
        };

        private static readonly Regex NotAvailable = new(@"\bNA\b", RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex RoseAccented = new(@"\brosé\b(?!\s*wine)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex RedNextToWine = new(@"\bred\s+(?:\p{L}+\s+)?wines?\b", Options);
        private static readonly Regex WhiteNextToWine = new(@"\bwhite\s+(?:\p{L}+\s+)?wines?\b", Options);
        private static readonly Regex RedWord = new(@"\bred\b", Options);
        private static readonly Regex WhiteWord = new(@"\bwhite\b", Options);
        private static readonly Regex Wine = new(@"\bwines?\b", Options);
        private static readonly Regex Alcohol = new(@"\balcohol\b", Options);
        private static readonly Regex RedContext = new(@"\bred wines?\b|\b(?:claret|burgundy|bordeaux|merlot|cabernet|chianti|zinfandel|syrah|shiraz|rioja|malbec|pinot noir|sangiovese|beaujolais)\b", Options);
        private static readonly Regex WhiteContext = new(@"\bwhite wines?\b|\b(?:chablis|riesling|chardonnay|sauvignon blanc|pinot grigio|moselle|rhine wine|rhenish|hock|champagne|prosecco|sauternes)\b", Options);

        private static readonly Regex AlcoholicFood = new(@"^\s*alcoholic beverages?\b|\bliqueur\b|\bhard cider\b|\bmalt liquor\b", Options);
        private static readonly Regex NotAlcoholicFood = new(@"\bnon ?alcoholic\b|\balcohol free\b|\bno alcohol\b|\bwithout alcohol\b", Options);
        private static readonly Regex NonWord = new(@"[^\p{L}\p{N}']+", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly string[] DrinkNameWords =
        {
            "wines?", "vins?", "beers?", "ales?", "lager", "stout", "porter", "pilsn?er", "ipa", "bock", "guinness", "ciders?", "perry", "scrumpy",
            "vodka", "rum", "whiske?y", "bourbon", "scotch", "brandy", "brandies", "cognac", "armagnac", "calvados", "applejack", "kirsch",
            "kirschwasser", "grappa", "gin", "hollands", "tequila", "mezcal", "arrack", "arak", "pisco", "everclear", "eau", "schnapps?",
            "liqueurs?", "liquor", "sake", "mirin", "shaoxing", "shaohsing", "hsing", "sherry", "sack", "port", "madeira", "marsala", "tokay",
            "tokaji", "malaga", "muscatel", "moscatel", "amontillado", "fino", "oloroso", "manzanilla", "sercial", "malmsey", "vermouth",
            "champagne", "prosecco", "cava", "sekt", "asti", "claret", "burgundy", "bordeaux", "chablis", "hocks?", "rhenish", "moselle",
            "riesling", "chardonnay", "sauvignon", "merlot", "cabernet", "chianti", "zinfandel", "syrah", "shiraz", "rioja", "malbec", "pinot",
            "sangiovese", "gamay", "grenache", "tempranillo", "barolo", "valpolicella", "beaujolais", "sauternes?", "muscadet", "sancerre", "soave", "gew[uü]rztraminer", "s[eé]millon", "frascati", "orvieto", "catawba",
            "ros[eé]", "kahlua", "baileys", "bailey", "cura[cç]ao", "cura[cç]oa", "cointreau", "marnier", "amaretto", "frangelico", "chartreuse",
            "b[eé]n[eé]dictine", "drambuie", "limoncello", "sambuca", "anisette", "ratafia", "cassis", "cr[eè]me", "menthe", "ouzo", "pastis",
            "absinthe", "galliano", "strega", "advocaat", "noyau", "noyeau", "maraschino", "bitters", "angostura", "spirits?", "alcohol",
            "mead", "eiswein", "icewine",
        };

        /// <summary>
        /// Every word a drink's name or a drink line may hold, as one Postgres word pattern
        /// (<c>\m(?:...)\M</c>, read with <c>~*</c>, with the accented spellings cooks write): the
        /// diet gate's probe for foods with no alcohol figure whose own name may read as a drink, and
        /// for the recipe lines whose own text may. The rules then read each name and line it finds,
        /// so every word that can give a line a sense the diet gate counts must be here.
        /// </summary>
        public static readonly string DrinkNamePattern = @"\m(?:" + string.Join('|', DrinkNameWords) + @")\M";

        private static readonly HashSet<string> DrinkIds =Senses.Values.Where(s => s.Kind == AlcoholSenseKind.Drink).SelectMany(s => s.Allowed).ToHashSet(StringComparer.Ordinal);

        private static readonly Lazy<string> SignatureValue = new(() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(SignatureSource())))[..16].ToLowerInvariant());

        /// <summary>A hash of <see cref="Version"/>, every sense rule and the allowed-food table, so any change to a rule shows as a new signature.</summary>
        public static string Signature => SignatureValue.Value;

        /// <summary>Every sense code the rules can give, with no list.</summary>
        public static IReadOnlyCollection<string> SenseCodes => Senses.Keys.Append(NoSense).ToList();

        /// <summary>The USDA FDC ids a sense allows; empty for a sense that is not a drink or that has no food.</summary>
        public static IReadOnlyList<string> Allowed(string sense) => Senses.TryGetValue(sense, out var spec) ? spec.Allowed : Array.Empty<string>();

        /// <summary>The kind of a sense code.</summary>
        public static AlcoholSenseKind KindOf(string sense) =>
            sense == ListSense ? AlcoholSenseKind.List : Senses.TryGetValue(sense, out var spec) ? spec.Kind : AlcoholSenseKind.None;

        /// <summary>
        /// The sense of the line's alcohol word, from the line and, when given, its recipe. A line
        /// with no alcohol word reads <see cref="NoSense"/>. A list or alternatives ("brandy or
        /// rum", "red wine (or chicken broth)") reads each part on its own; amounts joined by "and"
        /// or "or" ("one and one-half gallons", "a cup and a half", "a spoonful or two") are one
        /// amount, not a list.
        /// </summary>
        public static AlcoholReading Read(string? line, AlcoholRecipeContext? context = null)
        {
            var text = Normalize(line);
            if (!Trigger.IsMatch(text)) return AlcoholReading.Nothing;
            if (line != null && NotAvailable.IsMatch(line)) return Reading("non-alcoholic", "NA");
            var listed = Amounts(text);
            if (ListJoiner.IsMatch(listed) && !DishBeforeJoiner.IsMatch(listed))
            {
                var parts = SharedHead(ListSplit.Split(listed).SelectMany(p => p.Split(',')).Select(p => p.Trim(' ', ')', '(')).Where(p => p.Length > 0).ToList());
                if (parts.Count > 1)
                {
                    var readings = parts.Select(p => ReadOne(p, context)).ToList();
                    if (readings.All(r => r.Kind == AlcoholSenseKind.None)) return ReadOne(text, context);
                    return new AlcoholReading(ListSense, AlcoholSenseKind.List, "a list or alternatives", readings);
                }
            }
            return ReadOne(text, context);
        }

        /// <summary>
        /// Why the reading refuses the food (by its FDC id and its USDA description or name), or null
        /// when the food is one of the sense's: a drink is refused any food outside its allowed
        /// table; a drink with no USDA food, or an unclear one, is refused every food; a sense that
        /// is not a drink is refused an alcoholic food and a food that is not its kind (a vinegar
        /// for "sherry vinegar", an extract or flavouring for "rum extract"), and a measure ("a
        /// wineglass of milk") is refused a food named by an alcohol word as well; not food is refused
        /// everything. A list is refused only when every part refuses, and a part with no alcohol word
        /// refuses an alcoholic food and a food named by an alcohol word ("sugar or brandy" is not
        /// Non-Alcoholic Wine by its first part).
        /// </summary>
        public static AlcoholRefusal? Refuse(AlcoholReading reading, string? fdcId, string? foodText)
        {
            switch (reading.Kind)
            {
                case AlcoholSenseKind.None:
                    return null;
                case AlcoholSenseKind.List:
                {
                    AlcoholRefusal? first = null;
                    foreach (var part in reading.Parts ?? Array.Empty<AlcoholReading>())
                    {
                        var refusal = part.Kind == AlcoholSenseKind.None
                            ? IsAlcoholicFood(fdcId, foodText) ? new AlcoholRefusal(LinkGate.AlcoholDrink, "list: an alcoholic food no part names")
                            : NamesAlcoholWord(foodText) ? new AlcoholRefusal(LinkGate.AlcoholNotDrink, "list: a food named by an alcohol word no part names")
                            : null
                            : Refuse(part, fdcId, foodText);
                        if (refusal == null) return null;
                        first ??= refusal;
                    }
                    return first;
                }
                case AlcoholSenseKind.Drink:
                    return fdcId != null && Senses[reading.Sense].Allowed.Contains(fdcId) ? null : new AlcoholRefusal(LinkGate.AlcoholDrink, reading.Sense);
                case AlcoholSenseKind.Held:
                case AlcoholSenseKind.Unclear:
                    return new AlcoholRefusal(LinkGate.AlcoholNoFood, reading.Sense);
                case AlcoholSenseKind.NotFood:
                    return new AlcoholRefusal(LinkGate.AlcoholNotDrink, reading.Sense);
                default:
                {
                    var spec = Senses[reading.Sense];
                    if (IsAlcoholicFood(fdcId, foodText)) return new AlcoholRefusal(LinkGate.AlcoholNotDrink, reading.Sense + ": an alcoholic food");
                    if (reading.Sense == MeasureSense && NamesAlcoholWord(foodText)) return new AlcoholRefusal(LinkGate.AlcoholNotDrink, reading.Sense + ": a food named by an alcohol word the measured food is not");
                    var words = Words(foodText);
                    if (spec.Requires != null && !spec.Requires.Any(words.Contains)) return new AlcoholRefusal(LinkGate.AlcoholNotDrink, reading.Sense + ": not " + spec.Requires[0]);
                    if (spec.Refuses != null && spec.Refuses.Any(words.Contains)) return new AlcoholRefusal(LinkGate.AlcoholNotDrink, reading.Sense + ": " + spec.Refuses.First(words.Contains));
                    return null;
                }
            }
        }

        /// <summary>
        /// Whether a food is an alcoholic drink: a food of a drink sense's allowed table, or a USDA
        /// description led by "Alcoholic beverage" or naming a liqueur, hard cider or malt liquor,
        /// unless it says it is non-alcoholic.
        /// </summary>
        public static bool IsAlcoholicFood(string? fdcId, string? foodText)
        {
            if (fdcId != null && DrinkIds.Contains(fdcId)) return true;
            var text = Normalize(foodText);
            return AlcoholicFood.IsMatch(text) && !NotAlcoholicFood.IsMatch(text);
        }

        /// <summary>
        /// Whether a line of this reading counts as alcohol for the diet gate when its food stores no
        /// alcohol figure: a drink, a drink with no USDA food, an unclear one, or a dish named after a
        /// drink (it carries the drink); for a list, any part. A vinegar, a flavouring, a measure, a
        /// soft drink, a cooking liquid, yeast or a non-alcoholic drink does not.
        /// </summary>
        public static bool CountsForDiet(AlcoholReading reading) => reading.Kind switch
        {
            AlcoholSenseKind.Drink or AlcoholSenseKind.Held or AlcoholSenseKind.Unclear => true,
            AlcoholSenseKind.List => reading.Parts?.Any(CountsForDiet) == true,
            _ => reading.Sense == "dish",
        };

        /// <summary>Whether the reading is a flavouring extract ("vanilla extract", "rum flavoring"), which the diet gate leaves to a note (owner answer 6).</summary>
        public static bool IsFlavouring(AlcoholReading reading) =>
            reading.Sense == "extract" || (reading.Kind == AlcoholSenseKind.List && reading.Parts?.All(p => p.Kind == AlcoholSenseKind.None || p.Sense == "extract") == true && reading.Parts.Any(p => p.Sense == "extract"));

        /// <summary>
        /// The one reading a wording's lines share, for the shortlist: the reading every line with
        /// an alcohol word gives when they all agree on its sense; null when none has one or they
        /// disagree.
        /// </summary>
        public static AlcoholReading? Shared(IEnumerable<AlcoholReading> readings)
        {
            AlcoholReading? shared = null;
            foreach (var reading in readings.Where(r => r.Kind != AlcoholSenseKind.None))
            {
                if (shared == null) shared = reading;
                else if (shared.ToString() != reading.ToString()) return null;
            }
            return shared;
        }

        private static AlcoholReading ReadOne(string text, AlcoholRecipeContext? context)
        {
            if (!Trigger.IsMatch(text)) return AlcoholReading.Nothing;

            foreach (var (sense, rule, pattern) in NotDrink)
                if (pattern.IsMatch(text)) return Reading(sense, rule);
            if (LiquorWord.IsMatch(text) && !SpiritWord.IsMatch(text)) return Reading("liquid", "liquor as a cooking liquid");

            var measure = Measure.Match(text);
            if (measure.Success)
            {
                var rest = text.Remove(measure.Index, measure.Length);
                var of = OfPart.Match(rest);
                var inner = ReadOne((of.Success ? of.Groups[1].Value : rest).Trim(), context);
                return inner.Kind == AlcoholSenseKind.None ? Reading("measure", "a measure named after a drink") : inner with { Rule = "a measure of " + inner.Rule };
            }

            if (AuVin.IsMatch(text) && ReadOne(AuVin.Replace(text, " "), context) is { Kind: not AlcoholSenseKind.None } named) return named;
            if (DishAfterDrink.IsMatch(text) || DrinkFlavoured.IsMatch(text) || SoakedIn.IsMatch(text) || AuVin.IsMatch(text)) return Reading("dish", "a dish named after a drink");
            if (WithDrink.IsMatch(text)) return Reading("dish", "a food made or measured with a drink");
            if (SpiritsOf.IsMatch(text)) return Reading("extract", "spirits of a flavouring");
            if (Flavouring.IsMatch(WithoutNotes(text))) return Reading("extract", "a flavouring extract");

            foreach (var (sense, rule, pattern) in Rules)
                if (pattern.IsMatch(text)) return Reading(sense, rule);

            if (Wine.IsMatch(text))
            {
                var nextRed = RedNextToWine.IsMatch(text);
                var nextWhite = WhiteNextToWine.IsMatch(text);
                if (nextRed != nextWhite) return nextRed ? Reading("wine-red", "red wine") : Reading("wine-white", "white wine");
                var red = RedWord.IsMatch(text);
                var white = WhiteWord.IsMatch(text);
                if (red && white) return Reading("wine-unclear", "red and white in one line");
                if (red || white) return red ? Reading("wine-red", "red wine") : Reading("wine-white", "white wine");
            }

            if (Alcohol.IsMatch(text))
            {
                var others = context?.OtherLines;
                return others is { Count: > 0 } && !others.Any(o => NotFood.IsMatch(Normalize(o)))
                    ? Reading("spirit", "alcohol in a recipe of food")
                    : Reading("alcohol-unclear", "alcohol with no food context");
            }

            if (Wine.IsMatch(text)) return BareWine(context);
            return AlcoholReading.Nothing;
        }

        private static readonly HashSet<string> HeadModifiers = new(StringComparer.Ordinal)
        {
            "dry", "sweet", "medium", "white", "red", "rose", "light", "dark", "pale", "golden", "sparkling", "still", "cream", "good", "cheap", "fruity",
            "full", "bodied", "crisp", "young", "old", "aged", "spiced", "amber", "brown",
        };

        /// <summary>
        /// The parts of a list with a part of only colour or dryness words given the drink of the
        /// part after it ("dry white or red wine" is dry white wine or red wine).
        /// </summary>
        private static List<string> SharedHead(List<string> parts)
        {
            for (var i = parts.Count - 2; i >= 0; i--)
            {
                var words = parts[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var later = parts[i + 1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (words.Length > 0 && words.All(HeadModifiers.Contains) && later.Length > 1 && Trigger.IsMatch(later[^1]))
                    parts[i] = parts[i] + " " + later[^1];
            }
            return parts;
        }

        /// <summary>The text without a cook's notes (brackets, "for a richer flavor", "or flavor to taste", a starred footnote), where a flavour word never makes the line an extract.</summary>
        private static string WithoutNotes(string text)
        {
            for (var i = 0; i < 3; i++)
            {
                var next = Note.Replace(text, " ");
                if (next == text) break;
                text = next;
            }
            return text;
        }

        /// <summary>The text with every amount joined by "and" or "or" ("one and one-half", "a cup and a half", "a spoonful or two") read as one amount, so the joiner is not read as a list.</summary>
        private static string Amounts(string text)
        {
            for (var i = 0; i < 4; i++)
            {
                var next = AmountJoiner.Replace(text, "amount");
                if (next == text) break;
                text = next;
            }
            return text;
        }

        private static bool NamesAlcoholWord(string? foodText) => Trigger.IsMatch(Normalize(foodText));

        private static AlcoholReading BareWine(AlcoholRecipeContext? context)
        {
            if (context == null) return Reading("wine-unclear", "bare wine with no recipe");
            var recipe = Normalize(string.Join(" \n ", new[] { context.Title, context.Steps }.Concat(context.OtherLines ?? Array.Empty<string>()).Where(s => !string.IsNullOrWhiteSpace(s))));
            var red = RedContext.IsMatch(recipe);
            var white = WhiteContext.IsMatch(recipe);
            if (red && !white) return Reading("wine-red", "red from the recipe");
            if (white && !red) return Reading("wine-white", "white from the recipe");
            if (red && white) return Reading("wine-unclear", "both colours in the recipe");
            return context.HasSourceSite ? Reading("wine-table", "bare wine in a modern recipe") : Reading("wine-unclear", "bare wine in an old text");
        }

        private static AlcoholReading Reading(string sense, string rule) => new(sense, Senses[sense].Kind, rule);

        private static string Normalize(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            var s = RoseAccented.Replace(text.ToLowerInvariant(), "rose wine").Replace('’', '\'').Replace('‘', '\'');
            if (s.Any(c => c >= 128))
            {
                var decomposed = s.Normalize(NormalizationForm.FormD);
                s = new string(decomposed.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).Normalize(NormalizationForm.FormC);
            }
            s = s.Replace('-', ' ').Replace('_', ' ');
            return Regex.Replace(s, @"\s+", " ").Trim();
        }

        private static HashSet<string> Words(string? text) =>
            NonWord.Split(Normalize(text)).Where(w => w.Length > 0).Select(w => w.Trim('\'')).ToHashSet(StringComparer.Ordinal);

        /// <summary>The text <see cref="Signature"/> hashes: <see cref="Version"/>, every rule pattern in order and the sense table.</summary>
        internal static string SignatureSource()
        {
            var text = new StringBuilder(Version);
            text.Append("\ntrigger:").Append(Trigger);
            text.Append("\nlist:").Append(ListSplit).Append('|').Append(ListJoiner).Append('|').Append(DishBeforeJoiner).Append('|').Append(AmountJoiner);
            foreach (var (sense, _, pattern) in NotDrink) text.Append("\nnot-drink:").Append(sense).Append('=').Append(pattern);
            text.Append("\nliquor:").Append(LiquorWord).Append('|').Append(SpiritWord);
            text.Append("\nmeasure:").Append(Measure).Append('|').Append(OfPart);
            text.Append("\ndish:").Append(DishAfterDrink).Append('|').Append(DrinkFlavoured).Append('|').Append(SoakedIn).Append('|').Append(AuVin);
            text.Append("\nflavouring:").Append(Flavouring).Append('|').Append(WithDrink).Append('|').Append(SpiritsOf).Append('|').Append(Note);
            text.Append("\nhead-modifiers:").Append(string.Join(',', HeadModifiers.OrderBy(w => w, StringComparer.Ordinal)));
            foreach (var (sense, _, pattern) in Rules) text.Append("\nrule:").Append(sense).Append('=').Append(pattern);
            text.Append("\nwine:").Append(NotAvailable).Append('|').Append(RoseAccented).Append('|').Append(RedNextToWine).Append('|').Append(WhiteNextToWine)
                .Append('|').Append(RedWord).Append('|').Append(WhiteWord).Append('|').Append(Wine).Append('|').Append(Alcohol).Append('|').Append(RedContext).Append('|').Append(WhiteContext);
            text.Append("\nfood:").Append(AlcoholicFood).Append('|').Append(NotAlcoholicFood);
            text.Append("\nnames:").Append(DrinkNamePattern);
            foreach (var (sense, spec) in Senses.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                text.Append("\nsense:").Append(sense).Append('=').Append(spec.Kind).Append(':').Append(string.Join(',', spec.Allowed))
                    .Append(':').Append(string.Join(',', spec.Requires ?? Array.Empty<string>())).Append(':').Append(string.Join(',', spec.Refuses ?? Array.Empty<string>()));
            text.Append("\nspices:").Append(CatalogAdmissionRule.SpicesAndHerbs.ToString(CultureInfo.InvariantCulture));
            return text.ToString();
        }
    }
}
