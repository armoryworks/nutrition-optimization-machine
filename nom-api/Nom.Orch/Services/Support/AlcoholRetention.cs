using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Nom.Orch.Services.Support
{
    /// <summary>One row of the USDA alcohol retention table (Nutrition/usda-retention-r6-alcohol.tsv) with its citation.</summary>
    public sealed record AlcoholRetentionRow(string Code, string Description, string Method, int? Minutes, string Stirred, int Percent, string Title, string Publisher, string Year, string Page, string Locator, string Note)
    {
        public decimal Factor => Percent / 100m;
    }

    /// <summary>
    /// How much of a line's alcohol stays after the recipe's method: the factor (1 = all of it), the
    /// basis stored with the line (<see cref="AlcoholRetention.BasisOf"/>), how it was read, the
    /// cooking method and minutes when the steps give them, and the USDA row the factor comes from
    /// (none for no heat or no steps, where nothing is lost).
    /// </summary>
    public sealed record AlcoholRetentionReading(decimal Factor, string Kind, string? Method, int? Minutes, string? Code, bool Unnamed = false)
    {
        public string Basis => AlcoholRetention.BasisOf(Kind, Method, Minutes);

        public bool UpTo => Kind == AlcoholRetention.UpTo;

        /// <summary>Whether the reading is an estimate: no step names the drink (it is placed by a step that mixes all the ingredients, by another drink word, or not at all), or the heat has no time the reader can take ("up to").</summary>
        public bool Estimated => Unnamed || UpTo;
    }

    /// <summary>
    /// The alcohol retention reader (alcohol design A7): rules, no model. It finds the first step
    /// sentence that names the line's drink and reads how the dish is heated from there on, up to a
    /// separate preparation or through a step that chills, sets aside or serves the dish: a
    /// heating word with a time ("simmer for 30 minutes", "bake 1 1/2 hours") gives the USDA
    /// factor for baked or simmered dishes with the alcohol stirred in (15 minutes 40 %, 30
    /// minutes 35 %, 1 hour 25 %, 1.5 hours 20 %, 2 hours 10 %, 2.5 hours 5 %; baked under 30
    /// minutes 45 %, the not-stirred-in row), flaming 75 %, stirred into a liquid just off the heat
    /// 85 %, no heat but stored overnight 70 %, and no heat at all 100 %. Heating with no time it
    /// can read, or under 15 minutes, gives the highest factor of the method ("up to" 85 % on the
    /// stove, 45 % in the oven), and so does heating anywhere in a recipe whose steps never name
    /// the drink: the reader overstates rather than hides alcohol, the safe side for a No Alcohol
    /// household. A step that names the drink only as "liquor", "spirits" or "alcohol" with no
    /// determiner ("add the spice and liquor"; "the liquor" is most often a cooking liquid) names it.
    /// When a step names some other drink word ("pour the spirits over"), the highest factor of
    /// reading from each such step and of the "up to" reading is kept. When the steps name no drink
    /// word at all, the first step that mixes all the ingredients together holds it (the USDA rows
    /// 5004-5009 measure a drink stirred into the mixture that is then baked or simmered). "Cook" or "heat" in a sentence that bakes or roasts in the oven is the
    /// oven's heat, and "every 15 minutes" is how often, not how long. Preheating the oven cooks
    /// nothing, and "boiled icing" names a thing made apart. A drink that soaks an ingredient ("soak
    /// the fruit in the liquor overnight") goes where that ingredient goes: the method is read on from
    /// it past the rest that soaks it. A separate bowl of the dry ingredients is the other
    /// half of a batter when the next step stirs it into the drink's own mixture, and "in a clean cloth"
    /// wraps a pudding (staging checkpoint 3, N20). The soak is read up to a separate preparation like
    /// any other reading, and when the steps name the drink only by a drink word, the highest of the
    /// readings from each step that names it is kept (review of ck6-data-fixes). A line with no alcohol word on
    /// an alcoholic food ("1 teaspoon vanilla") is found by its first word that is no amount, unit or
    /// modifier. A recipe with no steps keeps all of it. The factors are the USDA Table of
    /// Nutrient Retention Factors, Release 6 (2007), rows 5001-5010, shipped as a cited data file.
    /// See docs/architecture/alcohol-retention.md.
    /// </summary>
    public static class AlcoholRetention
    {
        public const string Version = "alcohol-retention/7";
        public const string ResourceName = "Nom.Orch.Nutrition.usda-retention-r6-alcohol.tsv";

        /// <summary>Kilocalories per gram of alcohol, the FDC calorie factor; energy falls by this much for each gram of alcohol that does not stay.</summary>
        public const decimal KcalPerGram = 6.93m;

        public const string NoSteps = "no-steps";
        public const string NoHeat = "no-heat";
        public const string Overnight = "overnight";
        public const string HotLiquid = "hot-liquid";
        public const string Flamed = "flamed";
        public const string Timed = "timed";
        public const string UpTo = "up-to";

        public const string Simmer = "simmer";
        public const string Bake = "bake";

        private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

        private static readonly Lazy<IReadOnlyList<AlcoholRetentionRow>> Embedded = new(() => Parse(ReadResource()));

        /// <summary>The USDA rows, as shipped.</summary>
        public static IReadOnlyList<AlcoholRetentionRow> Rows => Embedded.Value;

        private static readonly Regex SentenceSplit = new(@"(?<=[.!?;])\s+|\n+", Options);

        private static readonly Regex NotHeating = new(
            @"\b(?:remove|removed|removing|take|taken|taking|lift|lifted|move|moved|set|pull)\s+(?:it\s+|them\s+|the\s+\p{L}+\s+|from\s+)?(?:from|off)\s+(?:the\s+)?(?:heat|fire|stove|burner|flame|hob)\b|\boff\s+(?:the\s+)?(?:heat|fire|flame|stove|hob)\b|\baway from (?:the )?(?:heat|fire)\b|\bpre ?heat\w*\s+(?:the\s+|your\s+|an\s+)?oven\b|\bheat\s+(?:the\s+|your\s+)?oven\s+to\b|\bpre ?heat\w*\b|\bboiled\s+(?:icing|frosting|dressing)\b|\bblanched\b|\bbaking\s+(?:soda|powder|dish|pans?|sheets?|tins?|paper|trays?|parchment)\b|\b(?:oven|heat)\s*proof\b|\bovenproof\b|\bcooking spray\b|\bcooking liquid\b|\bboiled cider\b|\bhard ?boiled\b|\bcooled\b",
            Options);

        private static readonly Regex OffHeat = new(
            @"\b(?:remove|removed|removing|take|taken|taking|lift|lifted|move|moved|set|pull)\s+(?:it\s+|them\s+|the\s+\p{L}+\s+|from\s+)?(?:from|off)\s+(?:the\s+)?(?:heat|fire|stove|burner|flame|hob)\b|\boff\s+(?:the\s+)?(?:heat|fire|flame|stove|hob)\b",
            Options);

        private static readonly Regex GasFlame = new(@"\b(?:low|medium|moderate|high|gentle|slow|brisk|clear|small|good|steady|open|direct|naked|hot)\s+(?:flame|fire)\b|\bover\s+(?:a\s+|the\s+)?(?:flame|fire)\b|\bon\s+(?:the|a)\s+(?:\p{L}+\s+)?(?:fire|stove|range|hob)\b", Options);

        private static readonly Regex TimeOnly = new(
            @"^\W*(?:(?:it|this|they|which|that|the\s+\p{L}+)\s+)?(?:will\s+|should\s+|must\s+|may\s+)?(?:take|takes|require|requires|want|wants|need|needs)\b|^\W*(?:about|nearly|for|in all)\b",
            Options);

        private static readonly Regex NotCooking = new(
            @"\b(?:stand|stands|standing|rest|rests|resting|cool|cools|cooling|set|sets|setting|chill\w*|sit|sits|sitting|soak\w*|steep\w*|marinat\w*|macerat\w*|ripen\w*|mellow\w*|matur\w*|age|ages|aging|ageing|keep|keeps|freez\w*|refrigerat\w*|rise|rises|rising|prove|proves|proving|drain\w*|dry|dries|drying|before\s+serving)\b",
            Options);

        private static readonly Regex Served = new(@"\b(?:serve|serves|served|serving|send|sent|pass|eat|eaten|accompan\w*)\b", Options);

        private static readonly Regex NewComponent = new(
            @"^\W*(?:(?:for|to make|to prepare|make|prepare)\s+(?:the\s+|an?\s+|your\s+)?(?:\p{L}+\s+){0,2}?(?:icing|frosting|glaze|filling|topping|sauce|dressing|syrup|custard|batter|dough|crust|pastry|garnish|meringue|streusel|crumble|marinade|whipped cream)\b|(?:icing|frosting|glaze|filling|topping|sauce|dressing|syrup)\s*:)",
            Options);

        private static readonly Regex HotInto = new(
            @"\b(?:into|to|over|on|in|with)\s+(?:the\s+|a\s+)?(?:\p{L}+\s+)?(?:hot|boiling|scalding|just\s+boiled)\b|\b(?:until|till)\s+(?:quite\s+|very\s+|thoroughly\s+)?hot\b",
            Options);

        private static readonly Regex Stove = new(
            @"\b(?:simmer\w*|boil\w*|parboil\w*|boyl\w*|stew(?:s|ed|ing|pans?)?|brais\w*|braiz\w*|seeth\w*|coddl\w*|fricass\w*|reduc(?:e|es|ed|ing)|poach\w*|fry|fries|fried|frying|saut[eé]\w*|sear(?:s|ed|ing)?|steam(?:s|ed|ing)?|scald\w*|deglaz\w*|blanch\w*|grill\w*|microwave\w*|stir[- ]fr\w*|caramel[iy][sz]\w*|thicken\w*)\b",
            Options);

        private static readonly Regex GenericHeat = new(@"\b(?:cook(?:s|ed|ing)?|heat(?:s|ed|ing)?)\b", Options);

        private static readonly Regex Oven = new(@"\b(?:bak(?:e|es|ed|ing)|roast(?:s|ed|ing)?|broil\w*|oven)\b", Options);

        private static readonly Regex Flame = new(
            @"\b(?:flam(?:e|es|ed|ing)|flamb[eé]\w*|ignit\w*|alight|aflame|ablaze|light\s+(?:it|them|the\s+(?:brandy|rum|cognac|whiske?y|bourbon|liquor|alcohol|pudding|spirit))|set\s+(?:it\s+|them\s+)?on\s+fire|set\s+fire\s+to|catch(?:es)?\s+fire|burn\s+off)\b",
            Options);

        private static readonly Regex OvernightWords = new(@"\bover ?night\b|\bnext day\b|\bday before\b|\b\d+\s*(?:to\s*\d+\s*)?days?\b|\b(?:a|one|two|three|four|five|six|seven|several|a few)\s+days?\b|\b(?:24|36|48)\s*hours?\b", Options);

        private const string Number = @"(?:\d+\s+\d/\d|\d+/\d+|\d+(?:\.\d+)?|a couple of|a couple|a few|several|a|an|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|fifteen|twenty|twenty five|thirty|forty|forty five|fifty|sixty|ninety)";

        private static readonly Regex Duration = new(@"\b(?<a>" + Number + @")(?:\s*(?:-|to|or)\s*(?<b>" + Number + @"))?\s*(?<u>hours?|hrs?|h|minutes?|mins?)\b", Options);

        private static readonly Regex MoreMinutes = new(@"^\s*,?\s*(?:and\s+)?(?<a>" + Number + @")\s*(?:minutes?|mins?)\b", Options);

        private static readonly (Regex Pattern, string Replacement)[] Phrases =
        {
            (new Regex(@"\b(?:an|one|1)\s+hours?\s+and\s+three\s+quarters?\b|\b(?:one|1)\s+and\s+three\s+quarters?\s+(?:of\s+an\s+)?hours?\b", Options), "105 minutes"),
            (new Regex(@"\b(?:one|1)\s+and\s+(?:a\s+|one\s+)?quarter\s+hours?\b", Options), "75 minutes"),
            (new Regex(@"\b(?:two|2)\s+hours?\s+and\s+a\s+quarter\b|\b(?:two|2)\s+and\s+(?:a\s+|one\s+)?quarter\s+hours?\b", Options), "135 minutes"),
            (new Regex(@"\b(?:an|one|1)\s+hours?\s+and\s+a\s+half\b|\b(?:one|1)\s+and\s+(?:a\s+|one\s+)?half\s+hours?\b|\b1\s+and\s+1/2\s+hours?\b", Options), "90 minutes"),
            (new Regex(@"\b(?:two|2)\s+hours?\s+and\s+a\s+half\b|\b(?:two|2)\s+and\s+(?:a\s+|one\s+)?half\s+hours?\b|\b2\s+and\s+1/2\s+hours?\b", Options), "150 minutes"),
            (new Regex(@"\b(?:an|one|1)\s+hours?\s+and\s+a\s+quarter\b", Options), "75 minutes"),
            (new Regex(@"\bthree\s+quarters?\s+of\s+an\s+hour\b", Options), "45 minutes"),
            (new Regex(@"\b(?:a\s+)?quarter\s+(?:of\s+an\s+)?hour\b", Options), "15 minutes"),
            (new Regex(@"\bhalf\s+(?:an\s+)?hour\b|\ba\s+half\s+hour\b|\bhalf-hour\b", Options), "30 minutes"),
        };

        private static readonly Regex SeparateStep = new(
            @"^\W*(?:meanwhile|in the meantime|separately|while\s+(?:the|you|it|that|this)\b)|\bseparately\b|\b(?:in|into|using|on)\s+(?:a|an)\s+(?:separate|second|clean|different|other)\b(?![\s,]+(?:[\p{L}-]+[\s,]+)?(?:cloth|towel|napkin|bag)s?\b)|\b(?:in|into|using|on)\s+another\b",
            Options);

        private static readonly Regex PauseStep = new(
            @"\bchill(?:s|ing)?\b|\brefrigerat(?:e|es|ing)\b|\bfreez(?:e|es|ing)\b|\bin(?:to)? the (?:fridge|refrigerator|freezer)\b|\b(?:let|leave|allow|set)\b[^.;]{0,25}\bcool\b|\bcool(?:s|ing)?\s+(?:completely|slightly|thoroughly|down|for|until|to|on|in)\b|^\W*cool\b|\bset\s+(?:it\s+|them\s+|the\s+\p{L}+\s+)?aside\b|\breserves?\b|\bserves?\b",
            Options);

        private static readonly Regex Frozen = new(@"\bfreez(?:e|es|ing)\b|\bin(?:to)?\s+(?:the\s+|a\s+)?(?:freezer|ice cream (?:freezer|maker))\b|\buntil\s+(?:firm(?:ly)?\s+)?frozen\b", Options);

        private static readonly Regex Cold = new(@"\b(?:cold|cool|cooled|chilled|lukewarm|tepid|iced)\b", Options);

        private static readonly Regex ClauseVerb = new(
            @"\b(?:add|adds|added|adding|stir\w*|pour\w*|whisk\w*|mix\w*|fold\w*|splash\w*|swirl\w*|beat|beats|blend\w*|combine\w*|put|puts|place\w*|season\w*|cover\w*|moisten\w*|wet|baste\w*|flavou?r\w*|sprinkle\w*|serve\w*|let|leave|set|brush\w*|dip\w*|marinate\w*|toss\w*|soak\w*|steep\w*|simmer\w*|boil\w*|stew\w*|brais\w*|cook\w*|heat\w*|bake\w*|roast\w*|fry|fries|fried|frying|reduce\w*|poach\w*|flam\w*)\b",
            Options);

        private static readonly Regex DryHalf = new(@"\b(?:flour|dry ingredients|baking (?:powder|soda))\b", Options);

        private static readonly Regex Mixture = new(@"\b(?<w>(?!(?:the|a|an|this|that|dry|flour)\b)\p{L}{3,})\s+mixture\b", Options);

        private static readonly Regex Soak = new(@"\b(?:soak\w*|steep(?:s|ed|ing)?|macerat\w*|plump(?:s|ed|ing)?)\b", Options);

        private static readonly Regex ClauseBreak = new(@",\s*|\s+(?:and\s+)?then\s+", Options);

        private static readonly Regex AddVerb = new(@"\b(?:add|adds|added|adding|stir|stirs|stirred|stirring|pour|pours|poured|pouring|whisk\w*|mix|mixes|mixed|fold\w*|splash\w*|swirl\w*|beat|beats|blend\w*|combine\w*)\b", Options);

        private static readonly Regex DrinkGeneric = new(@"^(?:extracts?|essence|flavou?r(?:ing)?s?|spirits?|liquor|alcohol\w*)$", Options);

        private static readonly Regex GenericMention = new(
            @"(?<!\b(?:the|its|their|this|that|own|same|oyster|oysters|clam|clams|mussel|mussels|pickle|pot|meat|fish|vegetable|cooking|ham|chicken|beef|mushroom)\s+)\b(?:liquors?|spirits?|alcohol)\b(?!\s+(?:from|of|in)\b)",
            Options);

        private static readonly Regex MixAll = new(
            @"\b(?:mix|mixes|mixed|stir|stirs|stirred|beat|beats|combine\w*|blend\w*|whisk\w*|fold\w*|toss\w*|put|add|adds|work)\b[^.;]*?\b(?:all\s+(?:the\s+)?(?:other\s+|remaining\s+)?ingredients|(?:all|them|it|everything)\s+(?:well\s+)?together|everything|all\s+(?:in|into)|(?:the\s+)?(?:remaining|other)\s+ingredients|(?:the\s+)?rest\s+of\s+the\s+ingredients)\b",
            Options);

        private static readonly Regex DryIngredients = new(@"\b(?:dry|dried)\s+ingredients\b", Options);

        private static readonly Regex NotMention = new(
            @"^(?:cups?|cupfuls?|teacups?|tablespoons?|tablespoonfuls?|teaspoons?|teaspoonfuls?|dessertspoons?|dessertspoonfuls?|spoons?|spoonfuls?|tsps?|tbsps?|tbs|ounces?|oz|pounds?|lbs?|pints?|quarts?|gills?|drops?|dashes|dash|pinch(?:es)?|glass(?:es|ful|fuls)?|wineglass(?:es|ful|fuls)?|bottles?|grams?|ml|litres?|liters?|few|little|some|pure|good|best|real|imitation|natural|about|scant|heaping|rounded|level|large|small|half|quarter|one|two|three|four|five|six|optional|plus|more|extra|taste|each|then|with|from|into|well)$",
            Options);

        private static readonly Regex EveryBefore = new(@"\b(?:every|each)\s*$", Options);
        private static readonly Regex LineWord = new(@"\p{L}[\p{L}']*", Options);

        private static readonly Dictionary<string, decimal> NumberWords = new(StringComparer.OrdinalIgnoreCase)
        {
            ["a"] = 1, ["an"] = 1, ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6, ["seven"] = 7, ["eight"] = 8,
            ["nine"] = 9, ["ten"] = 10, ["eleven"] = 11, ["twelve"] = 12, ["fifteen"] = 15, ["twenty"] = 20, ["twenty five"] = 25, ["thirty"] = 30,
            ["forty"] = 40, ["forty five"] = 45, ["fifty"] = 50, ["sixty"] = 60, ["ninety"] = 90, ["a couple of"] = 2, ["a couple"] = 2, ["a few"] = 2, ["several"] = 2,
        };

        private static readonly Lazy<string> SignatureValue = new(() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(SignatureSource())))[..16].ToLowerInvariant());

        /// <summary>A hash of <see cref="Version"/>, every pattern and the USDA rows, so any change to the reader or the table shows as a new signature.</summary>
        public static string Signature => SignatureValue.Value;

        /// <summary>The basis stored with a line: the kind, then the method and minutes when read ("timed:simmer:30", "up-to:bake", "flamed").</summary>
        public static string BasisOf(string kind, string? method, int? minutes) =>
            method == null ? kind : minutes == null ? $"{kind}:{method}" : string.Create(CultureInfo.InvariantCulture, $"{kind}:{method}:{minutes}");

        /// <summary>The kind, method and minutes of a stored basis; null for an empty one.</summary>
        public static (string Kind, string? Method, int? Minutes)? ReadBasis(string? basis)
        {
            if (string.IsNullOrWhiteSpace(basis)) return null;
            var parts = basis.Split(':');
            int? minutes = parts.Length > 2 && int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var m) ? m : null;
            return (parts[0], parts.Length > 1 ? parts[1] : null, minutes);
        }

        /// <summary>The USDA row of a reading's kind, method and minutes; null for no heat or no steps.</summary>
        public static AlcoholRetentionRow? RowFor(string kind, string? method, int? minutes)
        {
            string? code = kind switch
            {
                Overnight => "5001",
                HotLiquid => "5002",
                Flamed => "5003",
                UpTo => method == Bake ? "5010" : "5002",
                Timed when method == Bake && minutes is >= 15 and < 30 => "5010",
                Timed when minutes is >= 150 => "5009",
                Timed when minutes is >= 120 => "5008",
                Timed when minutes is >= 90 => "5007",
                Timed when minutes is >= 60 => "5006",
                Timed when minutes is >= 30 => "5005",
                Timed when minutes is >= 15 => "5004",
                _ => null,
            };
            return code == null ? null : Rows.First(r => r.Code == code);
        }

        /// <summary>The reading for a kind, method and minutes, with the factor of its USDA row (1 when nothing is lost).</summary>
        public static AlcoholRetentionReading Of(string kind, string? method = null, int? minutes = null)
        {
            var row = RowFor(kind, method, minutes);
            return new AlcoholRetentionReading(row?.Factor ?? 1m, kind, method, minutes, row?.Code);
        }

        /// <summary>
        /// How much of the line's alcohol stays, read from the recipe's steps in order. The drink is
        /// found by the line's alcohol words (or, for "vanilla extract", the word before the
        /// flavouring word); the steps from the sentence that names it on are read for heat.
        /// </summary>
        public static AlcoholRetentionReading Read(IEnumerable<string?>? steps, string? line)
        {
            var sentences = (steps ?? Enumerable.Empty<string?>())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .SelectMany(s => SentenceSplit.Split(Normalize(s!)))
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToList();
            if (sentences.Count == 0) return Of(NoSteps);

            var mention = Mention(line);
            var index = -1;
            var position = 0;
            if (mention != null)
            {
                for (var i = 0; i < sentences.Count && index < 0; i++)
                {
                    var m = mention.Match(sentences[i]);
                    if (m.Success)
                    {
                        index = i;
                        position = m.Index;
                    }
                }
            }

            for (var i = 0; i < sentences.Count && index < 0 && mention != null; i++)
            {
                var m = GenericMention.Match(sentences[i]);
                if (m.Success)
                {
                    index = i;
                    position = m.Index;
                }
            }
            if (index >= 0) return ReadAt(sentences, index, position);

            var family = Family(AlcoholLineContext.Read(line));
            var drinkWords = Enumerable.Range(0, sentences.Count)
                .Select(i => (Index: i, Match: AlcoholLineContext.Trigger.Matches(sentences[i]).FirstOrDefault(m => MayName(m.Value, family))))
                .Where(t => t.Match != null)
                .ToList();
            if (drinkWords.Any(t => Soak.IsMatch(sentences[t.Index])))
                return drinkWords.Select(t => ReadAt(sentences, t.Index, t.Match!.Index)).MaxBy(r => r.Factor)! with { Unnamed = true };
            if (drinkWords.Count > 0)
                return drinkWords.Select(t => ReadAt(sentences, t.Index, t.Match!.Index)).Append(Unplaced(sentences)).MaxBy(r => r.Factor)! with { Unnamed = true };

            for (var i = 0; i < sentences.Count; i++)
            {
                if (MixAll.IsMatch(sentences[i]) && !DryIngredients.IsMatch(sentences[i]))
                    return ReadAt(sentences, i, 0) with { Unnamed = true };
            }

            return Unplaced(sentences);
        }

        private static AlcoholRetentionReading Unplaced(List<string> sentences)
        {
            var all = Scan(sentences);
            if (!all.Stove && !all.Oven && !all.Flame) return Of(NoHeat) with { Unnamed = true };
            return Of(UpTo, all.Stove || all.Flame || !all.Oven ? Simmer : Bake) with { Unnamed = true };
        }

        /// <summary>
        /// The kind of drink a sense is (wine, spirit, beer or cider), so a step's drink word of another kind is not read
        /// as the line's drink ("Serve with white wine sauce" never holds the brandy of the line); null for a sense of no
        /// one kind (a flavouring, alcohol, a list), which any drink word may name.
        /// </summary>
        private static string? Family(AlcoholReading reading) => reading.Sense switch
        {
            "spirit" or "rum" or "vodka" or "whiskey" or "liqueur" or "coffee-liqueur" or "cream-liqueur" or "creme-de-menthe" => "spirit",
            "beer" or "light-beer" or "malt-liquor" => "beer",
            "hard-cider" or "cider-unclear" => "cider",
            "cooking-wine" or "sake" or "rice-wine" or "fortified-dry" or "fortified-sweet" or "fortified" or "vermouth" or "country-wine" => "wine",
            var sense when sense.StartsWith("wine-", StringComparison.Ordinal) => "wine",
            _ => null,
        };

        private static bool MayName(string word, string? family) =>
            family == null || DrinkGeneric.IsMatch(word) || Family(AlcoholLineContext.Read(word)) is not { } other || other == family;

        private static AlcoholRetentionReading ReadAt(List<string> sentences, int index, int position)
        {
            var sentence = sentences[index];
            var clauseStart = ClauseBreak.Matches(sentence[..position]).Select(m => m.Index + m.Length)
                .Where(start => ClauseVerb.IsMatch(sentence[start..position])).DefaultIfEmpty(0).Max();
            var after = Segment(sentence[clauseStart..], sentences.Skip(index + 1).ToList(), sentence, Soak.IsMatch(sentence));
            var scan = Scan(after);

            if (!scan.Stove && !scan.Oven)
            {
                if (scan.Flame) return Of(Flamed);
                if (after.Any(s => Frozen.IsMatch(s))) return Of(NoHeat);
                var ownPart = NewComponent.IsMatch(sentence) || SeparateStep.IsMatch(sentence);
                var before = sentence[..clauseStart] + " " + (index > 0 && !ownPart ? sentences[index - 1] : string.Empty);
                if (!Cold.IsMatch(sentence[..position])
                    && (OffHeat.IsMatch(sentence) || OffHeat.IsMatch(before) || HotInto.IsMatch(BeforeServing(sentence[clauseStart..]))
                        || (AddVerb.IsMatch(sentence[clauseStart..]) && Scan(new[] { before }).Stove))) return Of(HotLiquid);
                if (after.Any(s => OvernightWords.IsMatch(s))) return Of(Overnight);
                return Of(NoHeat);
            }

            if (scan.Minutes >= 15)
            {
                var timed = Of(Timed, scan.TimedOvenOnly ? Bake : Simmer, scan.Minutes);
                if (scan.Flame && timed.Factor > RowFor(Flamed, null, null)!.Factor) return Of(Flamed);
                return timed;
            }
            if (scan.Flame) return Of(Flamed);
            return Of(UpTo, scan.Stove ? Simmer : Bake, scan.Minutes > 0 ? scan.Minutes : null);
        }

        /// <summary>
        /// The part of the method that cooks the drink: its clause and the sentences after it, up to a
        /// sentence that starts a separate preparation ("Meanwhile boil the pasta", "In another pan"),
        /// which is left out, or through a sentence that chills, sets aside or serves the dish, after
        /// which the heat is for something else ("Bake the shortcakes"). A separate bowl of only dry
        /// ingredients is passed over when the next sentence stirs its mixture into the drink's own ("the
        /// egg mixture" after "beat the eggs ... and vanilla"). A drink that soaks an ingredient is read on
        /// past the rest that soaks it: the pause in its own sentence and the sentences right after it
        /// that only rest it overnight.
        /// </summary>
        private static List<string> Segment(string clause, IReadOnlyList<string> rest, string drinkSentence, bool soaked)
        {
            var segment = new List<string> { clause };
            if (PauseStep.IsMatch(clause) && !soaked) return segment;
            var resting = soaked;
            for (var i = 0; i < rest.Count; i++)
            {
                var sentence = rest[i];
                if (SeparateStep.IsMatch(sentence))
                {
                    if (DryHalf.IsMatch(sentence) && Scan(new[] { sentence }) is { Stove: false, Oven: false, Flame: false }
                        && i + 1 < rest.Count && JoinsBack(rest[i + 1], segment.Prepend(drinkSentence))) continue;
                    break;
                }
                if (NewComponent.IsMatch(sentence)) break;
                segment.Add(sentence);
                if (resting && OvernightWords.IsMatch(sentence) && Scan(new[] { sentence }) is { Stove: false, Oven: false, Flame: false }) continue;
                resting = false;
                if (PauseStep.IsMatch(sentence)) break;
            }
            return segment;
        }

        private static bool JoinsBack(string next, IEnumerable<string> drinkPart)
        {
            var text = string.Join(" ", drinkPart);
            return Mixture.Matches(next).Any(m => !DryHalf.IsMatch(m.Value) && Regex.IsMatch(text, @"\b" + Regex.Escape(m.Groups["w"].Value), Options));
        }

        private static string BeforeServing(string clause) => Served.Match(clause) is { Success: true } serve ? clause[..serve.Index] : clause;

        private sealed record HeatScan(bool Stove, bool Oven, bool Flame, int Minutes, bool TimedOvenOnly);

        /// <summary>
        /// How the sentences heat: stove, oven or flame, and the minutes their heating sentences state, added up. A sentence
        /// that only states how long ("It will take about an hour") times the heat of the sentence before it, unless the time is
        /// one of standing, resting, chilling or setting ("It needs to stand two hours"), which is no heat, and a range
        /// ("12 to 15 minutes") after heat the steps give no time for (the drink boiled with the broth first) counts at its upper
        /// end (staging checkpoint 5, Q18).
        /// </summary>
        private static HeatScan Scan(IReadOnlyList<string> sentences)
        {
            bool stove = false, oven = false, flame = false, timedStove = false, heatedBefore = false, stoveBefore = false, untimed = false;
            var minutes = 0;
            for (var i = 0; i < sentences.Count; i++)
            {
                var text = NotHeating.Replace(sentences[i], " ");
                var gas = GasFlame.IsMatch(text);
                var withoutGas = GasFlame.Replace(text, " ");
                var o = Oven.IsMatch(withoutGas);
                var s = gas || Stove.IsMatch(withoutGas) || (!o && GenericHeat.IsMatch(withoutGas));
                var f = Flame.IsMatch(withoutGas);
                stove |= s;
                oven |= o;
                flame |= f;
                if ((s || o) && TimesIn(text) is { } t && t.Low > 0)
                {
                    minutes += untimed && t.High is { } high ? high : t.Low;
                    if (s) timedStove = true;
                }
                else if (!s && !o && heatedBefore && TimeOnly.IsMatch(text) && !NotCooking.IsMatch(text) && TimesIn(text) is { } only && only.Low > 0)
                {
                    minutes += only.Low;
                    if (stoveBefore) timedStove = true;
                    continue;
                }
                else if (s || o)
                {
                    untimed = true;
                }
                heatedBefore = s || o;
                stoveBefore = s;
            }
            return new HeatScan(stove, oven, flame, minutes, minutes > 0 && !timedStove);
        }

        /// <summary>The first cooking time a sentence gives, in whole minutes (the lower end of a range, with "1 hour 15 minutes" read as one time); null when it gives none.</summary>
        public static int? MinutesIn(string sentence) => TimesIn(sentence)?.Low;

        /// <summary>The first cooking time a sentence gives, in whole minutes, as <see cref="MinutesIn"/>, with the upper end of a range when it gives one.</summary>
        private static (int Low, int? High)? TimesIn(string sentence)
        {
            var text = Normalize(sentence);
            var m = Duration.Match(text);
            while (m.Success && EveryBefore.IsMatch(text[..m.Index])) m = m.NextMatch();
            if (!m.Success) return null;
            var amount = NumberOf(m.Groups["a"].Value);
            if (amount == null) return null;
            var hours = m.Groups["u"].Value.StartsWith("h", StringComparison.OrdinalIgnoreCase);
            var total = hours ? amount.Value * 60m : amount.Value;
            if (hours && MoreMinutes.Match(text[(m.Index + m.Length)..]) is { Success: true } more && NumberOf(more.Groups["a"].Value) is { } extra)
                total += extra;
            int? high = m.Groups["b"].Success && NumberOf(m.Groups["b"].Value) is { } upper && upper > amount.Value ? (int)Math.Floor(hours ? upper * 60m : upper) : null;
            return ((int)Math.Floor(total), high);
        }

        private static decimal? NumberOf(string text)
        {
            text = text.Trim();
            if (NumberWords.TryGetValue(text, out var word)) return word;
            var mixed = Regex.Match(text, @"^(\d+)\s+(\d)/(\d)$");
            if (mixed.Success) return decimal.Parse(mixed.Groups[1].Value, CultureInfo.InvariantCulture) + decimal.Parse(mixed.Groups[2].Value, CultureInfo.InvariantCulture) / decimal.Parse(mixed.Groups[3].Value, CultureInfo.InvariantCulture);
            var fraction = Regex.Match(text, @"^(\d+)/(\d+)$");
            if (fraction.Success) return decimal.Parse(fraction.Groups[1].Value, CultureInfo.InvariantCulture) / Math.Max(1m, decimal.Parse(fraction.Groups[2].Value, CultureInfo.InvariantCulture));
            return decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var n) ? n : null;
        }

        /// <summary>The words that name the line's drink in a step: its alcohol words, for a flavouring the word before it ("vanilla" for "vanilla extract"), and for a line with no alcohol word on an alcoholic food ("1 teaspoon vanilla") its first word that is no amount, unit or modifier.</summary>
        private static Regex? Mention(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;
            var text = Normalize(line);
            var words = new List<string>();
            var lineWords = LineWord.Matches(text).Select(w => w.Value).ToList();
            foreach (Match m in AlcoholLineContext.Trigger.Matches(text))
            {
                var word = m.Value.ToLowerInvariant();
                if (DrinkGeneric.IsMatch(word))
                {
                    var at = lineWords.FindIndex(w => w.Equals(m.Value, StringComparison.OrdinalIgnoreCase));
                    if (at > 0 && lineWords[at - 1].Length > 2) words.Add(lineWords[at - 1].ToLowerInvariant());
                    if (word.StartsWith("spirit", StringComparison.Ordinal) || word.StartsWith("liquor", StringComparison.Ordinal) || word.StartsWith("alcohol", StringComparison.Ordinal)) words.Add(word);
                    continue;
                }
                words.Add(word.EndsWith("s", StringComparison.Ordinal) && word.Length > 4 ? word[..^1] : word);
            }
            if (words.Count == 0 && lineWords.FirstOrDefault(w => w.Length > 2 && !NotMention.IsMatch(w)) is { } head) words.Add(head.ToLowerInvariant());
            if (words.Count == 0) return null;
            return new Regex(@"\b(?:" + string.Join('|', words.Distinct().Select(Regex.Escape)) + @")(?:s|es)?\b", Options);
        }

        private static string Normalize(string text)
        {
            var s = text.Replace('’', '\'').Replace('–', '-').Replace('—', '-')
                .Replace("½", " 1/2").Replace("¼", " 1/4").Replace("¾", " 3/4").Replace("⅓", " 1/3").Replace("⅔", " 2/3");
            s = Regex.Replace(s, @"(\d+)\s+(\d/\d)", "$1 $2");
            s = Regex.Replace(s, @"(\d)-(?=\d/)", "$1 ");
            s = Regex.Replace(s, @"(?<=\p{L})-(?=\p{L})", " ");
            foreach (var (pattern, replacement) in Phrases) s = pattern.Replace(s, replacement);
            s = Regex.Replace(s, @"\b(\d+)\s+(?:and\s+)?(1/2)\s+(hours?|hrs?)\b", m => string.Create(CultureInfo.InvariantCulture, $"{int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * 60 + 30} minutes"));
            return Regex.Replace(s, @"[ \t]+", " ");
        }

        public static IReadOnlyList<AlcoholRetentionRow> Parse(string tsv)
        {
            var rows = new List<AlcoholRetentionRow>();
            var lines = tsv.Replace("\r", string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0) return rows;
            var header = lines[0].Split('\t');
            foreach (var line in lines.Skip(1))
            {
                var f = line.Split('\t');
                string Cell(string name) => Array.IndexOf(header, name) is var i && i >= 0 && i < f.Length ? f[i].Trim() : string.Empty;
                int? Minutes = int.TryParse(Cell("minutes"), NumberStyles.None, CultureInfo.InvariantCulture, out var m) ? m : null;
                rows.Add(new AlcoholRetentionRow(Cell("code"), Cell("description"), Cell("method"), Minutes, Cell("stirred"),
                    int.Parse(Cell("percent"), NumberStyles.None, CultureInfo.InvariantCulture), Cell("title"), Cell("publisher"), Cell("year"), Cell("page"), Cell("locator"), Cell("note")));
            }
            return rows;
        }

        /// <summary>Why the table may not ship: a row without its citation, a percent outside 0-100, or a repeated code.</summary>
        public static IReadOnlyList<string> Problems(IReadOnlyList<AlcoholRetentionRow> rows)
        {
            var problems = new List<string>();
            foreach (var row in rows)
            {
                if (string.IsNullOrWhiteSpace(row.Title)) problems.Add($"{row.Code}: no title");
                if (string.IsNullOrWhiteSpace(row.Publisher)) problems.Add($"{row.Code}: no publisher");
                if (row.Year != "2007") problems.Add($"{row.Code}: not Release 6 (2007)");
                if (string.IsNullOrWhiteSpace(row.Page)) problems.Add($"{row.Code}: no page");
                if (string.IsNullOrWhiteSpace(row.Locator)) problems.Add($"{row.Code}: no locator");
                if (row.Percent is < 0 or > 100) problems.Add($"{row.Code}: percent {row.Percent}");
            }
            problems.AddRange(rows.GroupBy(r => r.Code).Where(g => g.Count() > 1).Select(g => $"{g.Key}: code repeated"));
            return problems;
        }

        private static string ReadResource()
        {
            using var stream = typeof(AlcoholRetention).Assembly.GetManifestResourceStream(ResourceName)
                ?? throw new InvalidOperationException($"Embedded resource {ResourceName} is missing from Nom.Orch.");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        /// <summary>The text <see cref="Signature"/> hashes.</summary>
        internal static string SignatureSource()
        {
            var text = new StringBuilder(Version);
            foreach (var regex in new[] { SentenceSplit, NotHeating, OffHeat, GasFlame, TimeOnly, NotCooking, Served, NewComponent, HotInto, Stove, GenericHeat, Oven, Flame, OvernightWords, Duration, MoreMinutes, SeparateStep, PauseStep, Frozen, Cold, ClauseVerb, DryHalf, Mixture, Soak, ClauseBreak, AddVerb, DrinkGeneric, GenericMention, MixAll, DryIngredients, NotMention, EveryBefore })
                text.Append('\n').Append(regex);
            foreach (var (pattern, replacement) in Phrases) text.Append('\n').Append(pattern).Append('=').Append(replacement);
            foreach (var row in Rows) text.Append('\n').Append(row.Code).Append('=').Append(row.Percent.ToString(CultureInfo.InvariantCulture)).Append(':').Append(row.Minutes?.ToString(CultureInfo.InvariantCulture));
            text.Append("\ntrigger:").Append(AlcoholLineContext.Trigger);
            text.Append("\nfamilies:").Append(AlcoholLineContext.Signature);
            return text.ToString();
        }
    }
}
