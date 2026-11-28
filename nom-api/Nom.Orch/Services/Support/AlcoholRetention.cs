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
    /// basis stored with the line (<see cref="AlcoholRetention.BasisOf(string, string?, int?, string?)"/>), how it was read, the
    /// cooking method and minutes when the steps give them, the USDA row the factor comes from
    /// (none for no heat or no steps, where nothing is lost), and, for an upper bound, why the steps do not
    /// determine a single value (<see cref="AlcoholRetention.Reasons"/>; null for heat with no time the reader can take).
    /// </summary>
    public sealed record AlcoholRetentionReading(decimal Factor, string Kind, string? Method, int? Minutes, string? Code, bool Unnamed = false, string? Reason = null)
    {
        public string Basis => AlcoholRetention.BasisOf(Kind, Method, Minutes, Reason);

        public bool UpTo => Kind == AlcoholRetention.UpTo;

        /// <summary>Whether the reading is an estimate: no step names the drink (it is placed by a step that mixes all the ingredients, by another drink word, or not at all), or the factor is an upper bound ("up to").</summary>
        public bool Estimated => Unnamed || UpTo;
    }

    /// <summary>
    /// The alcohol retention reader (alcohol design A7): rules, no model. It finds the first step
    /// sentence that names the line's drink and reads how the dish is heated from there on, up to a
    /// separate preparation or through a step that chills, sets aside or serves the dish: a
    /// heating word with a time ("simmer for 30 minutes", "bake 1 1/2 hours") gives the USDA
    /// factor for baked or simmered dishes with the alcohol stirred in (15 minutes 40 %, 30
    /// minutes 35 %, 1 hour 25 %, 1.5 hours 20 %, 2 hours 10 %, 2.5 hours 5 %; baked under 30
    /// minutes 45 %, the not-stirred-in row), flaming up to 85 % (how long it burns is not stated), stirred into a liquid just off the heat
    /// 85 %, no heat but stored overnight 70 %, and no heat at all 100 %. Heating with no time it
    /// can read, or under 15 minutes, gives the highest factor of the method ("up to" 85 % on the
    /// stove, 45 % in the oven), and so does heating anywhere in a recipe whose steps never name
    /// the drink: the reader overstates rather than hides alcohol, the safe side for a No Alcohol
    /// household. A step that names the drink only as "liquor", "spirits" or "alcohol" with no
    /// determiner ("add the spice and liquor"; "the liquor" is most often a cooking liquid) names it.
    /// "Do not cook" or "do not boil" inside a sentence is no heat ("Stir thoroughly but do not cook": staging checkpoint
    /// 9, a fresh draw of ck23), while "do not let it boil" still says the dish is on the heat. When a step names some other drink word ("pour the spirits over"), the highest factor of
    /// reading from each such step and of the "up to" reading is kept. When the steps name no drink
    /// word at all, a step that mixes all the ingredients together, or the wet, icing, filling or
    /// sauce ingredients, holds it, and of several such steps the highest reading is kept (the USDA rows
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
        public const string Version = "alcohol-retention/11";
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

        /// <summary>The recipe has more than one part, and the steps do not make clear that the drink's part is the one cooked.</summary>
        public const string ReasonParts = "parts";

        /// <summary>The drink's part is set aside, chilled or left before the cooking, and the steps do not make clear that the cooking is of that part.</summary>
        public const string ReasonPause = "pause";

        /// <summary>No step names the drink, so when it goes in is not known.</summary>
        public const string ReasonUnnamed = "unnamed";

        /// <summary>The drink goes in at more than one point, or several lines of it, and the steps do not say which amount goes where.</summary>
        public const string ReasonLines = "lines";

        /// <summary>The steps offer a choice (another method, an optional or conditional step) that may leave the drink less cooked.</summary>
        public const string ReasonChoice = "choice";

        /// <summary>The drink is flamed, and how long it burns is not stated.</summary>
        public const string ReasonFlamed = "flamed";

        /// <summary>The drink soaks or marinates a food, and how much of it goes on with the food, and how, is not stated.</summary>
        public const string ReasonMarinade = "marinade";

        /// <summary>Every reason an upper bound may carry; an "up to" reading with none is heat with no time the reader can take.</summary>
        public static readonly IReadOnlyList<string> Reasons = new[] { ReasonParts, ReasonPause, ReasonUnnamed, ReasonLines, ReasonChoice, ReasonFlamed, ReasonMarinade };

        private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

        private static readonly Lazy<IReadOnlyList<AlcoholRetentionRow>> Embedded = new(() => Parse(ReadResource()));

        /// <summary>The USDA rows, as shipped.</summary>
        public static IReadOnlyList<AlcoholRetentionRow> Rows => Embedded.Value;

        private static readonly Regex SentenceSplit = new(@"(?<=[.!?;]\)?)\s+|\n+", Options);

        private static readonly Regex NotHeating = new(
            @"\bheat\s*treated\b|\bkeep\s+(?:it\s+|them\s+|the\s+\p{L}+\s+)?warm\b[^.;]*|\b(?:the|in|with|of|and|add|fold\s+in|stir\s+in|top\s+with)\s+(?:\p{L}+\s+)?(?:roasted|toasted|baked|grilled|fried|cooked|boiled|steamed|poached|caramelized|browned)\s+(?!(?:for|until|till|in|on|at|about|over|under|with|and|then|to)\b)\p{L}+|\b(?:double\s+)?boilers?\b|\bfor\s+(?:later|future)\s+(?:baking|cooking|use)\b|\b(?:later|future)\s+(?:baking|cooking)\b|\b(?:microwave|heat|warm)\w*\b(?=[^.;]*?\b\d+(?:\s*(?:-|to)\s*\d+)?[\s-]*seconds?\b)(?![^.;]*\b(?:minutes?|boil\w*|simmer\w*)\b)|\b(?:has|have|had|is|are|was|were|gets?|got|becomes?|it)\s+(?:\p{L}+\s+)?thicken\w*|\bthickened\s+up\b|\bthickens?\s+(?:up\s+)?(?:as\s+it\s+(?:cools|sits|stands)|in\s+the\s+(?:fridge|refrigerator))|\b(?:reduc|lower|increas)\w*\s+(?:the\s+)?(?:mixer\s+)?(?:speed|to\s+(?:low|medium|high)\s+speed)\b|\breduced\s+(?!(?:by|to|until|till|heat|over|for|in|and|slightly)\b)\p{L}+|\bpre\s*baked\b|\bbaked\s+(?:pie\s+|tart\s+)?(?:crusts?|shells?|cases?)\b|\bunbaked\b|\b(?:remove|removed|removing|take|taken|taking|lift|lifted|move|moved|set|pull)\s+(?:it\s+|them\s+|the\s+\p{L}+\s+|from\s+)?(?:from|off)\s+(?:the\s+)?(?:heat|fire|stove|burner|flame|hob)\b|\boff\s+(?:the\s+)?(?:heat|fire|flame|stove|hob)\b|\baway from (?:the )?(?:heat|fire)\b|\bpre ?heat\w*\s+(?:the\s+|your\s+|an\s+)?oven\b|\bheat\s+(?:the\s+|your\s+)?oven\s+to\b|\bpre ?heat\w*\b|\bboiled\s+(?:icing|frosting|dressing)\b|\bblanched\b|\bbaking\s+(?:soda|powder|dish|pans?|sheets?|tins?|paper|trays?|parchment|mats?|stones?|steel|spray|cups?|liners?)\b|\broasting\s+(?:pans?|tins?|dish|racks?)\b|\b(?:oven|heat)\s*(?:proof|safe)\b|\bovenproof\b|\bcooking spray\b|\bcooking liquid\b|\bboiled cider\b|\bhard ?boiled\b|\bcooled\b|\b(?:turn|turns|turned|switch|switches|switched)\s+off\s+(?:the\s+)?oven\b|\bboil(?:s|ed|ing)?\s+(?:enough\s+|some\s+)?(?:hot\s+)?water\s+(?:to|for)\b|\bwater\s+bath\b|\b(?:before|after)\s+(?:\p{L}+\s+and\s+)?(?:baking|cooking|roasting|frying|grilling)\b|\boven\s+(?:heats|is\s+heating|preheats)\b|\b(?:reduc|lower)\w*\s+(?:the\s+)?(?:oven(?:\s+temperature)?|temperature)\b|\b(?:total\s+)?(?:baking|cooking|bake|cook)\s+times?\b|\bblanch(?:ed)?\s+(?:and\s+\p{L}+\s+)?(?:the\s+)?(?:almonds|nuts|pistachios|walnuts|filberts)\b|\b(?:out\s+of|from)\s+(?:the\s+)?oven\b|\bbaked\s+goods\b|\bfor\s+the\s+(?:first|last|final)\s+\d+(?:\s*(?:-|to)\s*\d+)?\s+minutes\b|\bby\s+boiling\s+water\b|\b(?:pour|add)\w*\s+(?:in\s+)?(?:the\s+)?boiling\s+water\s+in(?:to)?\s+the\s+(?:\p{L}+\s+)?pan\b|\bover\s*(?:bak|cook)\w*\b|\b(?:turn|turns|turned|switch|switches|switched)\s+(?:(?:the\s+)?(?:oven|heat)\s+off|off\s+(?:the\s+)?(?:oven|heat))\b[^.;]*|\bafter\s+(?:about\s+)?(?:an?|one|\d+)\s+(?:\p{L}+\s+)?(?:of\s+)?(?:baking|cooking)\b|\bif\s+(?:you\s+are\s+)?bak\w*\b|\bno[\s-]+bak\w*\b|\bstewed\s+(?:apricots|prunes|fruits?|apples|peaches|figs|tomatoes|cherries|pears|plums|rhubarb|raisins)\b|\b(?:pour|pours|poured|put|puts)\s+(?:on|in|over|upon)?\s*(?:\p{L}+\s+){0,3}?boiling\s+water\b|\breduc\w*\s+(?:the\s+|its\s+)?(?:strength|quantity|amount|sweetness)\b|\breduc\w*\s+(?:the\s+)?\p{L}+\s+by\s+\S+\s+(?:tablespoons?|teaspoons?|cups?|ounces?|grams?|ml)\b|\b(?:do\s+not|don'?t|never)\s+(?:cook|boil|heat|bake|simmer|scald)\w*\b",
            Options);

        private static readonly Regex OffHeat = new(
            @"\b(?:remove|removed|removing|take|taken|taking|lift|lifted|move|moved|set|pull)\s+(?:it\s+|them\s+|the\s+\p{L}+\s+|from\s+)?(?:from|off)\s+(?:the\s+)?(?:heat|fire|stove|burner|flame|hob)\b|\boff\s+(?:the\s+)?(?:heat|fire|flame|stove|hob)\b",
            Options);

        private static readonly Regex GasFlame = new(@"\b(?:low|medium|moderate|high|gentle|slow|brisk|clear|small|good|steady|open|direct|naked|hot)\s+(?:flame|fire)\b|\bover\s+(?:a\s+|the\s+)?(?:flame|fire)\b|\bon\s+(?:the|a)\s+(?:\p{L}+\s+)?(?:fire|stove|range|hob)\b", Options);

        private static readonly Regex TimeOnly = new(
            @"^\W*(?:(?:it|this|they|which|that|(?:the|this)\s+\p{L}+)\s+)?(?:will\s+|should\s+|must\s+|may\s+|can\s+|could\s+)?(?:take|takes|require|requires|want|wants|need|needs)\b|^\W*(?:about|nearly|for|in all)\b|\b(?:will|would)\s+do\s+(?:it|them)\b",
            Options);

        private static readonly Regex NotCooking = new(
            @"\b(?:stand|stands|standing|rest|rests|resting|cool|cools|cooling|set|sets|setting|chill\w*|sit|sits|sitting|soak\w*|steep\w*|marinat\w*|macerat\w*|ripen\w*|mellow\w*|matur\w*|age|ages|aging|ageing|keep|keeps|freez\w*|refrigerat\w*|rise|rises|rising|prove|proves|proving|drain\w*|dry|dries|drying|before\s+serving)\b",
            Options);

        private static readonly Regex Served = new(@"\b(?:serve|serves|served|serving|send|sent|pass|eat|eaten|accompan\w*)\b", Options);

        private static readonly Regex NewComponent = new(
            @"^\W*(?:(?:for|to make|to prepare|make|prepare)\s+(?:the\s+|an?\s+|your\s+)?(?:\p{L}+\s+){0,2}?(?:icing|frosting|glaze|filling|topping|sauce|dressing|syrup|custard|batter|dough|crust|pastry|garnish|meringue|streusel|crumble|marinade|whipped cream|compote|curd|ganache|caramel)\b|(?:icing|frosting|glaze|filling|topping|sauce|dressing|syrup)\s*:)",
            Options);

        private static readonly Regex HotInto = new(
            @"\b(?:into|to|over|on|in|with)\s+(?:the\s+|a\s+)?(?:\p{L}+\s+)?(?:hot|boiling|scalding|just\s+boiled)\b(?!\s+water\b)|\b(?:until|till)\s+(?:quite\s+|very\s+|thoroughly\s+)?hot\b|\bgravy\b|\b(?:it|them)\s+(?:quite\s+|very\s+)?hot\b",
            Options);

        private static readonly Regex HotThrough = new(
            @"\b(?:until|till)\s+(?:it\s+is\s+|they\s+are\s+)?(?:quite\s+|very\s+|thoroughly\s+|well\s+)?hot\b|\bhot\s+through\b|\bmake\s+(?:it|them)\s+(?:quite\s+|very\s+)?hot\b",
            Options);

        private static readonly Regex SauceMade = new(
            @"\bmake\s+(?:a|the)\s+(?:\p{L}+\s+)?(?:sauce|gravy)\s+(?:of|with)\b[^.;]*\b(?:flour|roux|gravy|stock|broth)\b",
            Options);

        private static readonly Regex NewMixtureVerb = new(
            @"^\W*(?:then\s+)?(?:mix|mixes|mixed|beat|beats|whisk\w*|whip\w*|cream|blend\w*|combine\w*)\s+(?:up\s+|together\s+|well\s+)?(?<o>\S+(?:\s+\S+)?)",
            Options);

        private static readonly Regex BackReference = new(
            @"^(?:it|them|this|these|that|those|all|everything|in|into|the\s+(?:mixture|sauce|gravy|liquor|liquid|syrup|soup|stew|broth|stock|pan|pot|saucepan|stewpan|kettle|skillet|juices?|contents|rest|whole|custard))\b",
            Options);

        private static readonly Regex WorkedCold = new(
            @"\b(?:beat|beats|beaten|pound\w*|mash\w*|rub|rubs|rubbed|chop\w*|cut|cuts|slic\w*|grat\w*|pick\w*|wash\w*|dr(?:y|ies|ied)|whip\w*|knead\w*|roll\w*|shap\w*|minc\w*|bruis\w*|pulp\w*|squeez\w*|scrap\w*|par(?:e|es|ed|ing)|cor(?:e|es|ed|ing)|ston(?:e|es|ed|ing)|split\w*|mortar)\b",
            Options);

        private static readonly Regex LastClause = new(@"[,;]\s*|\s+(?:and\s+)?then\s+|\s+and\s+(?=\p{L}+\s)", Options);

        private static readonly Regex PartNoun = new(
            @"\b(?:icing|frosting|glaze|filling|topping|(?<!\b(?:soy|fish|oyster|hot|worcestershire|chil[il]i?|hoisin|tabasco|tomato|apple|cranberry|pepper)\s)sauce|dressing|(?<!\b(?:maple|corn|golden|agave|date|rice|simple)\s)syrup|custard|streusel|crumble|marinade|meringue)\b",
            Options);

        private static readonly Regex Protein = new(
            @"(?<!\bfor\s+(?:the\s+|your\s+|a\s+|an\s+|each\s+)?)\b(?:chicken|beef|pork|lamb|mutton|veal|meat|fish|shrimps?|prawns?|tofu|duck|turkey|steaks?|salmon|ribs|wings|thighs|breasts?|fillets?|chops?|scallops|squid)\b",
            Options);

        private static readonly Regex Coated = new(@"\b(?:marinat\w*|marinades?|soak\w*|steep(?:s|ed|ing)?|macerat\w*|coat(?:s|ed|ing)?|toss(?:es|ed|ing)?)\b", Options);

        private static readonly Regex LeadVerb = new(@"^\W*(?:then\s+)?\p{L}+\s+(?:the\s+|your\s+|its\s+|their\s+)?(?:\p{L}+\s+)?$", Options);

        private static readonly Regex Stove = new(
            @"\b(?:simmer\w*|boil\w*|parboil\w*|boyl\w*|stew(?:s|ed|ing|pans?)?|brais\w*|braiz\w*|seeth\w*|coddl\w*|fricass\w*|reduc(?:e|es|ed|ing)|poach\w*|fry|fries|fried|frying|saut[eé]\w*|sear(?:s|ed|ing)?|steam(?:s|ed|ing)?|scald\w*|deglaz\w*|blanch\w*|grill\w*|microwave\w*|stir[- ]fr\w*|caramel[iy][sz]\w*|(?<!\b(?:until|to)\s+(?:\p{L}+\s+){0,4})thicken\w*|hash(?:es|ed|ing)?(?=\s+(?:it|them|the)\b))\b",
            Options);

        private static readonly Regex GenericHeat = new(@"\b(?:cook(?:s|ed|ing)?|heat(?:s|ed|ing)?)\b", Options);

        private static readonly Regex Oven = new(@"\b(?:bak(?:e|es|ed|ing)|roast(?:s|ed|ing)?|broil\w*|oven|stove\s+door)\b", Options);

        private static readonly Regex Flame = new(
            @"\b(?:flam(?:e|es|ed|ing)|flamb[eé]\w*|ignit\w*|alight|aflame|ablaze|light\s+(?:it|them|the\s+(?:brandy|rum|cognac|whiske?y|bourbon|liquor|alcohol|pudding|spirit))|set\s+(?:it\s+|them\s+)?on\s+fire|set\s+fire\s+to|catch(?:es)?\s+fire|burn\s+off)\b",
            Options);

        private static readonly Regex OvernightWords = new(@"\bover ?night\b|\bnext day\b|\bday before\b|\b\d+\s*(?:to\s*\d+\s*)?days?\b|\b(?:a|one|two|three|four|five|six|seven|several|a few)\s+days?\b|\b(?:24|36|48)\s*hours?\b", Options);

        private static readonly Regex Storage = new(@"\b(?:stores?|stored|storing|leftovers?|(?:will\s+)?keeps?\s+(?:well\s+)?(?:for\s+)?up\s+to|for\s+up\s+to\s+\S+\s+(?:days?|weeks?|months?))\b", Options);

        private static readonly Regex OwnPart = new(@"\b(?:dough|batter)\b", Options);

        private static readonly Regex PauseObject = new(
            @"\b(?:place|put|chill|refrigerate|freeze|transfer|set|let|leave|cover|wrap|keep)\s+(?:(?:the|your)\s+)?(?<n>(?!(?:pan|pans|bowl|dish|tray|sheet|tin|mould|mold|form|plate|jar|oven|fridge|refrigerator|freezer|mixture|rest|whole|lid|cooled|chilled|the|your|its|each|all|any|some|for|until|overnight|about|and|with|them|they|this|that|aside|covered|tightly|loosely|again|immediately|while|least|completely|briefly|well|into|onto|over|out|back|down)\b)\p{L}{3,})\b",
            Options);

        private static readonly Regex HotJoin = new(
            @"\b(?:stir|stirs|stirred|pour|pours|poured|add|adds|added|put|mix|mixes|mixed)\s+(?:it|them|this|the\s+mixture)\s+(?:in|into|to|with)\s+the\s+(?:sauce|gravy|soup|broth|stock|custard)\b",
            Options);

        private static readonly Regex Alternative = new(@"\bif\s+(?:using|you\s+(?:use|are\s+using|make|bake)|making|baking)\b|\bfor\s+(?:smaller|larger|mini|individual)\b|^\W*or\b|^\W*(?:for|if\s+you\s+(?:like|prefer|want))\s+(?:a\s+|an\s+)?(?:more|less|extra|softer|firmer|crisper|crispier|chewier|gooier|fudgier|darker|lighter|browner|thicker|thinner|deeper|richer|drier|moister)\b", Options);

        private static readonly Regex WetHalf = new(@"\bwet\s+(?:ingredients|mixture)\b", Options);

        private static readonly Regex ChillPause = new(@"\bchill(?:s|ed|ing)?\b|\brefrigerat\w*|\bin(?:to)?\s+the\s+(?:fridge|refrigerator)\b|\bfreez(?:e|es|ing)\b", Options);

        private static readonly Regex Vessel = new(
            @"\b(?:into|in|to|onto)\s+(?:the\s+|your\s+)?(?:prepared|greased|buttered|lined|oiled)\s+(?:\p{L}+\s+)?(?:dish|pan|tin|mould|mold|ramekins?|cups|pie\s+(?:plate|shell|crust))\b|\bon\s+(?:the\s+|a\s+)?(?:prepared|lined|greased)\s+(?:baking\s+|cookie\s+)?(?:sheets?|trays?)\b|\b(?:into|in|to)\s+(?:a|an|the)\s+(?:[\p{L}\d-]+\s+){0,2}(?:baking|casserole|gratin|souffl[eé])\s+(?:dish|pan)\b",
            Options);

        private static readonly Regex TheMixture = new(@"\bthe\s+mixture\b", Options);

        private static readonly Regex ThenClause = new(@",?\s*(?:and\s+)?then\b|;", Options);

        private static readonly Regex HeatNoun = new(@"\b(?:the|a|an|your|this|that|its|their|each|any)\s+(?:\p{L}+\s+)?(?:bak(?:e|es|ed)|roast(?:s|ed)?|stew(?:s|ed)?|cook(?:ed)?|heat(?:ed)?)\b", Options);

        private static readonly Regex RestClause = new(@"\b(?:let|leave|allow)\b[^.;]*?\b(?:rest|stand|sit|cool)\w*|\b(?:rest|rests|stand|stands|sit|sits|cool|cools)\s+(?:for\s+)?(?:about\s+)?\d", Options);

        private static readonly Regex Ratio = new(@"\b(?:per|to\s+(?:each|every)|for\s+(?:each|every))\b", Options);

        private static readonly Regex NoHeatChoice = new(
            @"\b(?:refrigerat\w*|chill\w*|freez\w*)\b[^.;]{0,40}\bor\s+(?:\p{L}+\s+){0,2}?(?:bak(?:e|ed|ing)|cook\w*|fry|heat\w*)\b|\b(?:bak(?:e|ed|ing)|cook\w*)\b[^.;]{0,40}\bor\s+(?:\p{L}+\s+){0,2}?(?:refrigerat\w*|chill\w*|freez\w*|eat\s+(?:it\s+|them\s+)?raw|no[- ]bake)\b",
            Options);

        private static readonly Regex Sealed = new(@"\bbottl\w*|\bcork(?:s|ed|ing)?\b|\b(?:tie|ties|tied)\s+(?:it\s+|them\s+)?(?:down|close|closely|up)\b|\bclosely\s+(?:covered|stopped|corked)\b|\bair\s*tight\b", Options);

        private static readonly Regex NotHeatingSentence = new(@"\b(?:blow\s*)?torch\w*\b|^\W*if\s+(?:broil|grill|toast|bak)\w*\b|^\W*(?:do\s+not|don't|never|avoid|monitor|check|watch|test)\w*\b|\boven\s+(?:has\s+)?cool\w*|\b(?:turned\s+off|closed)\s+oven\b|\brecommend\w*\b", Options);

        private static readonly Regex NotPause = new(@"\bset\s+aside\s+(?:\d|1/|a\s+(?:few|little|handful|quarter|third|half)|some|half|about)|\b(?:before|after)\s+or\s+(?:before|after)\s+(?:chilling|freezing|refrigerating)\b|\b(?:can|may)\s+be\s+done\s+(?:before|after)\b", Options);

        private static readonly Regex Conditional = new(@"^\W*if\b", Options);

        private static readonly Regex FromCold = new(@"\b(?:from|out\s+of)\s+the\s+(?:fridge|refrigerator|freezer)\b", Options);

        private static readonly Regex MeltedFat = new(@"\b(?:chocolate|chips|butter|caramels?|marshmallows?|coconut\s+oil)\b", Options);

        private static readonly Regex BareFlavour = new(@"^flavou?rs?$", Options);

        private static readonly Regex FlavourNoun = new(@"\b(?:best|for|more|of|the|in|extra|great|full|and|good|its|their|this)\s+$", Options);

        private static readonly Regex SetAsideRest = new(@"\b(?:store|stores|keep|keeps|refrigerate|freeze|wrap)\s+(?:the\s+|any\s+)?(?:remaining|leftover|extra|unused|rest\s+of\s+the)\b", Options);

        private static readonly Regex JoinMixture = new(@"\b(?<w>(?!(?:the|a|an|this|that|dry)\b)\p{L}{3,})\s+mixture\b", Options);

        private static readonly Regex LaterPart = new(@"\b(?<w>(?!(?:the|a|an|this|that|dry|remaining|prepared|your|of|and|with)\b)\p{L}{3,})\s+(?:mixture|filling|frosting|icing|topping|glaze|batter|dough|custard|cream)\b", Options);

        private static readonly Regex OptionalPart = new(@"\boptional\b", Options);

        private static readonly Regex AltPurpose = new(@"^\W*(?:for|if\s+you\s+(?:like|prefer|want))\s+(?:a\s+|an\s+)?(?:more|less|extra|softer|firmer|crisper|crispier|chewier|gooier|fudgier|darker|lighter|browner|thicker|thinner|deeper|richer|drier|moister)\b", Options);

        private static readonly Regex AddedTime = new(@"\b(?:minutes?|mins?|hours?)\s+(?:more|longer)\b|\b\d+\s+more\s+(?:minutes?|mins?|hours?)\b|\b(?:another|additional|an?\s+extra|further)\s+(?:\d|\p{L}+\s+(?:minutes?|mins?|hours?))", Options);

        private static readonly Regex SameHeat = new(@"\b(?<v>bak|roast|broil|grill|simmer|boil|cook|steam|poach|stew|toast|fr)(?:e|es|ed|ing|s|y|ying|ied|ies)?\b", Options);

        private static readonly Regex ShortOvernight = new(@"\b(?:\d+|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|several|a\s+few|an?)\s+(?:to\s+(?:\d+|\p{L}+)\s+)?hours?\s*,?\s+(?:or|and|preferably|ideally)\s+(?:up\s+to\s+)?(?:over ?night|\d+\s+days?|a\s+day)\b|\(?\s*,?\s*(?:and\s+|or\s+)?up\s+to\s+(?:\d+\s*(?:-|to)?\s*\d*\s+(?:hours?|days?)|over ?night)", Options);

        private static readonly Regex ColdAct = new(@"\b(?:cold|cool|cooled|chilled|lukewarm|tepid|iced)\b(?!\s+water\b)", Options);

        private static readonly Regex ForLater = new(@"\b(?:set\s+aside|reserve|keep)\b[^.;]*\bfor\s+later\b", Options);

        private static readonly Regex OrBetween = new(@"^[^.;]{0,30}\bor\b[^.;]{0,30}$", Options);

        private static readonly Regex AltMethod = new(@"^\W*(?:to\s+(?:bake|cook|make)\s+(?:it\s+|them\s+)?(?:in|on)\s+(?:the|an?)\s+\p{L}+|alternatively|or)\b", Options);

        private static readonly Regex CoolsInOven = new(@"\b(?:cool\w*|sit|sits|rest|rests|stand|stands|remain|remains|stay|stays|leave|left)\s+(?:\p{L}+\s+){0,2}?in\s+the\s+(?:closed\s+|turned\s+off\s+|warm\s+|cooling\s+)?(?:oven|air\s+fryer)\b[^.;]*\b(?:cool\w*|off|set)\b|\bcool\w*\s+in\s+the\s+(?:closed\s+|turned\s+off\s+|warm\s+)?oven\b|\b(?:sit|sits|rest|rests|stand|stands|remain|remains|stay|stays|leave|left)\s+(?:\p{L}+\s+){0,3}?in\s+the\s+(?:\p{L}+\s+)?oven\b", Options);

        private static readonly Regex NextMorning = new(@"^\W*(?:the\s+next\s+(?:morning|day)|next\s+(?:morning|day)|in\s+the\s+morning)\b", Options);

        private static readonly Regex AndThenCold = new(@"\s+and\s+(?:then\s+)?(?:cool|chill|refrigerat|freez|let|leave|set|pour|spread)\w*\b", Options);

        private static readonly Regex PlacedInPan = new(@"\b(?:press|spread|pour|transfer|spoon|scrape|lay|laid|pack|arrang)\w*\b[^.;]*\b(?:into|in|onto|over)\s+(?:the|a|an)\s+(?:[\p{L}\d-]+\s+){0,3}?(?:pan|dish|tin|crust|mould|mold)\b", Options);

        private static readonly Regex NewVessel = new(@"^\W*(?:\p{L}+\s+){0,4}?(?:in|into)\s+(?:a|another)\s+(?:[\p{L}-]+\s+){0,2}(?:bowl|saucepan|pot|skillet|mug|jar)\b", Options);

        private static readonly Regex IntoTheMixture = new(@"\b(?:into|to)\s+(?:the\s+)?mixture\s+of\b", Options);

        private static readonly Regex Continued = new(@"^\W*continu\w*\b", Options);

        private static readonly Regex UsedInstead = new(@"^\W*if\s+(?:(?:a|an|the)\s+)?(?:\p{L}+\s+){1,3}(?:is|are)\s+used\b", Options);

        private static readonly Regex AppliedPart = new(@"^\W*(?:then\s+)?(?:dip|dips|dipped|spread|frost|drizzle|ice)\b", Options);

        private static readonly Regex Marinade = new(@"\bmarinat\w*|\bmarinades?\b", Options);

        private static readonly Regex NewPrep = new(
            @"^\W*(?:then\s+)?(?:melt|toast|brown|chop|dice|slice|grate|peel|rinse|heat)\s+(?:together\s+|down\s+)?(?:the\s+|some\s+|your\s+|a\s+|an\s+)?(?:[\d/.]+\s*(?:ounces?|oz|cups?|grams?|g|tablespoons?|teaspoons?|tbsps?|tsps?|pounds?|lbs?)\s+(?:of\s+)?)?(?>(?:(?:large|small|medium|heavy|nonstick|non|stick|cast|iron|deep|shallow|big|clean|dry|electric)[\s-]+)*)(?!(?:to|in|into|on|onto|over|up|down|back|out|it|them|this|these|all|until|for|at|together|well|gently|each|both|half|one|and|or|any|every|skillet|pan|griddle|oven|grill|waffle|wok|pot|saucepan|oil|butter|fat|lard|broiler|fryer|frying|remaining)\b)(?<o>\p{L}{3,})",
            Options);

        private static readonly Regex Heading = new(@"^\W*(?:\p{L}+[\s&'-]*){1,4}(?:\(\s*optional\s*\)\s*)?:|^\W*to\s+(?:make|prepare|assemble|finish)\b", Options);

        private static readonly Regex IntoPan = new(@"\b(?:pour|spoon|spread|scrape|transfer|divide|press|fill|apply|pipe|dollop)\w*\b[^.;]*\b(?:in|into|among|between|onto|to|on|over)\s+(?:the\s+|each\s+|a\s+|your\s+)?(?:prepared\s+|greased\s+|lined\s+|baking\s+|cooled\s+|warm\s+|baked\s+|chilled\s+)?(?:[\p{L}\d-]+\s+){0,2}?(?:pans?|dish|dishes|tins?|ramekins?|cups|crusts?|shells?|molds?|moulds?|skillet|sheets?|liners|cavities|cavity|wells|pastry|pastries|rectangles?|squares|rounds|tarts?)\b", Options);

        private static readonly Regex TheMass = new(@"\b(?:batter|dough)\b", Options);

        private static readonly Regex LineAmount = new(@"(?:^|\s)(?<n>\d+(?:\s+\d/\d|/\d+|\.\d+)?|\d/\d)\s*(?<u>teaspoons?|teaspoonfuls?|tsps?|tablespoons?|tablespoonfuls?|tbsps?|tbs|cups?|ounces?|oz)\b", Options);

        private static readonly Regex SidePart = new(@"\b(?:the|a|an|this|your|for\s+the|to\s+the|into\s+the)\s+(?:\p{L}+\s+)?(?:icing|frosting|glaze|filling|topping|(?<!\b(?:soy|fish|oyster|hot|worcestershire|chil[il]i?|hoisin|tabasco|tomato|apple|cranberry|pepper)\s)sauce|dressing|(?<!\b(?:maple|corn|golden|agave|date|rice|simple)\s)syrup|custard|streusel|crumble|marinade|meringue)\b", Options);

        private static readonly Regex OntoDish = new(@"\b(?:over|onto|on\s+top\s+of|into|in)\s+the\s+(?:\p{L}+\s+)?(?:batter|cake|dough|filling|pan|dish|crust|mixture)\b|\bfill\w*\s+(?:up\s+)?the\s+(?:\p{L}+\s+)?(?:mould|mold|dish|pan|tin|basin|crust|shell)\b", Options);

        private static readonly Regex JoinedHere = new(@"\b(?:add|adds|added|stir|stirs|stirred|fold|folds|folded|mix|mixes|mixed|toss|tosses|tossed|pour|pours|poured)\s+(?:(?:it|them|this|these|all)\s+)?(?:in\s+)?(?:(?!(?:and|then|bake|boil|cook|put|pour)\b)\p{L}+\s+){0,3}?(?:(?:in)?to|with)\s+the\s+(?:rest\s+of\s+the\s+|remaining\s+)?(?<w>\p{L}+)", Options);

        private static readonly Regex VesselHeated = new(@"\b(?:boil|steam|simmer|stew|bake|cook)\w*\s+(?:or\s+\p{L}+\s+)?(?:it|them|the\s+(?:mould|mold|basin|pudding|dish|tin))\b", Options);

        private static readonly Regex Sufficient = new(@"\b(?:will|would|should|may|might)\s+(?:be\s+)?(?:quite\s+)?(?:sufficient|enough|suffice|do)\b|\bsuffices?\b|\b(?:is|are)\s+(?:quite\s+)?(?:sufficient|enough)\b", Options);

        private static readonly Regex LaterUse = new(@"\bfor\s+(?:later|future)\s+(?:use|baking|cooking)\b|\bto\s+bake\s+later\b|\bbake\s+(?:them\s+|it\s+)?later\b", Options);

        private static readonly Regex SplitUse = new(@"\b(?:serve|serving|pass|top|drizzle|spoon)\w*\b[^.;]*\b(?:extra|remaining|reserved|rest\s+of\s+the|leftover|more|additional)\s+(?:\p{L}+\s+)?(?<n>sauce|glaze|syrup|dressing|marinade|icing|frosting|gravy|custard)\b|\b(?:reserve|save)\w*\s+(?:about\s+|some\s+|half\s+|a\s+(?:little|quarter|third|cup)\s+)?(?:of\s+)?(?:the\s+)?(?:\p{L}+\s+)?(?<n>sauce|glaze|syrup|dressing|marinade|mixture|gravy)\b", Options);

        private static readonly Regex Portion = new(@"\b(?:\d[\d/\s.]*\s*(?:cups?|tablespoons?|teaspoons?|tbsps?|tsps?)|half|some|part|a\s+little|a\s+portion|a\s+third|a\s+quarter)\s+(?:a\s+)?(?:cups?\s+)?(?:of\s+)?(?:the\s+)?(?:\p{L}+\s+)?(?<n>sauce|glaze|syrup|dressing|marinade|mixture|gravy|icing|frosting|custard)\b", Options);

        private static readonly Regex AddedTo = new(@"^\W*(?:then\s+)?(?:add|stir|fold|pour|mix|whisk|beat)\w*\s+(?:(?:it|them|this|these|the\s+\p{L}+(?:\s+\p{L}+)?)\s+)?(?:in)?to\s+the\s+(?:\p{L}+\s+)?(?:mixture|batter|dough)\b", Options);

        private static readonly Regex OverCrust = new(@"\b(?:pour|spoon|spread|scrape|transfer|put)\w*\b[^.;]*\b(?:over|into|onto|in)\s+(?:the\s+)?(?:\p{L}+\s+){0,2}?(?:crusts?|shells?|pastry|base)\b", Options);

        private static readonly Regex ThenGoesOn = new(@"\b(?:cool|rest|stand|sit)\w*\b[^.;]*?,?\s*(?:and\s+)?then\s+(?:mix|add|stir|fold|whisk|beat|combine|pour|spread|roll|shape|scoop|knead)\w*\b", Options);

        private static readonly Regex WarmOnly = new(@"\b(?:warm|warmed|melt\w*|soften\w*|lukewarm)\b(?![^.;]*\b(?:boil\w*|simmer\w*|thick\w*|bubbl\w*)\b)", Options);

        private static readonly Regex VesselFirst = new(@"^\W*(?:in|into|to|using)\s+(?:a|an|another|the)\b", Options);

        private static readonly Regex LiquorWord = new(@"^liquors?$", Options);

        private static readonly Regex DryWord = new(@"^(?:flour|dry|wet|sugar|salt|spices?|cocoa|oats?|cornstarch|crumbs?|cinnamon|nut|nuts)$", Options);

        private static readonly Regex OwnMass = new(@"\b(?:the|this|that|your|each|both|of\s+the)\s+(?:(?<w>\p{L}+)\s+)?(?:batter|dough|mixture|paste)\b", Options);

        private static readonly Regex ChoiceTime = new(@"\d[^.;]{0,15}?(?:minutes?|mins?|hours?)\b[^.;]{0,25}?\bif\s+(?:using|you\s+(?:use|are\s+using|make|bake|prefer|like|want)|making|baking)\b|\bif\s+(?:using|you\s+(?:use|are\s+using|make|bake|prefer|like|want)|making|baking)\b[^.;]{0,40}?\d[^.;]{0,15}?(?:minutes?|mins?|hours?)\b|\bfor\s+(?:smaller|larger|mini|individual)\b[^.;]*\d", Options);

        private static readonly Regex OrMethod = new(@"\bor\s+(?:in|on|over|with|under|using)\s+(?:a|an|the)\b", Options);

        private static readonly Regex Seconds = new(@"\b\d+(?:\s*(?:-|to)\s*\d+)?\s*(?:seconds?|secs?)\b", Options);

        private static readonly Regex ChoiceLead = new(@"^\W*(?:if|optional(?:ly)?|alternatively|or|for\s+(?:smaller|larger|mini|individual))\b|^\W*\(|^\W*(?:for|if\s+you\s+(?:like|prefer|want))\s+(?:a\s+|an\s+)?(?:more|less|extra|softer|firmer|crisper|crispier|chewier|gooier|fudgier|darker|lighter|browner|thicker|thinner|deeper|richer|drier|moister)\b", Options);

        private static readonly Regex MixOn = new(@"^(?:together|up|until|till|on|at|for|with|gently|just|briefly|again|slowly|by|thoroughly|to|in|everything|all|it|them|well|lightly)\b", Options);

        private static readonly Regex Strewn = new(@"\b(?:sprinkl\w*|scatter\w*|dust\w*|top\s+(?:with|each|the)|over\s+the\s+top|on\s+top|(?:roll|coat|dredge|dip)\w*\b[^.;]*\bin\s+the\s+(?:\p{L}+\s+)?mixture)\b", Options);

        private static readonly Regex SetFirm = new(@"\b(?:cool\w*|harden\w*|firm\w*|set\s+up|until\s+set|to\s+set)\b", Options);

        private static readonly Regex AppliedCold = new(
            @"^\W*(?:then\s+)?(?:spread|drizzle|frost|ice|pour|spoon|pipe|dollop|top)\w*\b[^.;]*\b(?:over|onto|on\s+top\s+of|on|with)\s+(?:the\s+|each\s+)?(?:cooled|baked|cold|chilled|finished|frosted)\b|^\W*(?:then\s+)?(?:frost|ice)\s+(?:the\s+|each\s+)?(?:cooled\s+)?(?:cakes?|cupcakes|cookies|brownies|bars|layers?)\b",
            Options);

        private static readonly Regex Choice = new(
            @"\bif\s+(?:using|desired|preferred|you\s+(?:use|are\s+using|make|bake|prefer|like|want|wish|have)|making|baking|serving)\b|^\W*(?:alternatively|or|optional(?:ly)?)\b|^\W*if\b|^\W*\(\s*optional\w*|\bfor\s+(?:smaller|larger|mini|individual|a\s+(?:crisp|softer|firmer|crispier))\b|^\W*(?:for|if\s+you\s+(?:like|prefer|want))\s+(?:a\s+|an\s+)?(?:more|less|extra|softer|firmer|crisper|crispier|chewier|gooier|fudgier|darker|lighter|browner|thicker|thinner|deeper|richer|drier|moister)\b|^\W*to\s+(?:bake|cook|make)\s+(?:it\s+|them\s+)?(?:in|on)\s+(?:the|an?)\s+\p{L}+",
            Options);

        private static readonly Regex PanVessel = new(@"\b(?:saucepan|sauce\s*pan|stewpan|pot|skillet|frying\s+pan|double\s+boiler|over\s+(?:low|medium|high|medium[\s-]+low|medium[\s-]+high|moderate|gentle|a\s+low|a\s+medium)\s+(?:heat|flame|fire))\b", Options);

        private static readonly Regex DryJoin = new(@"\b(?:dry\s+ingredients|dry\s+mixture|flour\s+mixture|two\s+mixtures|wet\s+ingredients|wet\s+mixture)\b", Options);

        private static readonly Regex HeatObject = new(
            @"^\W*(?:then\s+)?(?:bake|roast|fry|deep\s+fry|grill|broil|cook|simmer|boil|poach|steam|toast|microwave|sear|saut[eé])\s+(?:the|your|each|both|all\s+the)\s+(?!(?:for|at|in|on|until|till|about|uncovered|covered|over|under|with|and|to|or|from|into|by|as|gently|slowly|briefly|quickly|another|again|stirring|low|medium|high|it|them|this|these|those|all|everything|together|well|a|an|the|until|according|undisturbed|halfway|longer|more|another|time|minutes?|hours?|one|two|three|four|five|six|seven|eight|nine|ten|twelve|fifteen|twenty|thirty|forty|fifty|sixty|half|quarter|several|few)\b)(?<o>\p{L}+(?:\s+(?!(?:for|at|in|on|until|till|about|uncovered|covered|over|under|with|and|to|or|from|into|by|as|minutes?|hours?|mins?)\b)\p{L}+)?)",
            Options);

        private static readonly Regex HeatObjectAny = new(
            @"^\W*(?:then\s+)?(?:bake|roast|fry|deep\s+fry|grill|broil|cook|simmer|boil|poach|steam|toast|microwave|sear|saut[eé])\s+(?:(?:the|your|each|both|all\s+the)\s+)?(?!(?:for|at|in|on|until|till|about|uncovered|covered|over|under|with|and|to|or|from|into|by|as|gently|slowly|briefly|quickly|another|again|stirring|low|medium|high|it|them|this|these|those|all|everything|together|well|a|an|the|until|according|undisturbed|halfway|longer|more|another|time|minutes?|hours?|one|two|three|four|five|six|seven|eight|nine|ten|twelve|fifteen|twenty|thirty|forty|fifty|sixty|half|quarter|several|few)\b)(?<o>\p{L}+(?:\s+(?!(?:for|at|in|on|until|till|about|uncovered|covered|over|under|with|and|to|or|from|into|by|as|minutes?|hours?|mins?)\b)\p{L}+)?)",
            Options);

        private static readonly Regex DishNoun = new(
            @"^(?:it|them|cakes?|cookies?|bread|breads|loaf|loaves|muffins?|cupcakes?|brownies?|blondies?|bars?|squares|pies?|tarts?|puddings?|casseroles?|dish|mixture|batter|dough|cheesecakes?|scones?|biscuits?|rolls?|buns?|pans?|sheets?|trays?|tins?|layers?|rounds?|loaf|slices|pieces|balls|logs?|crackers?|waffles?|pancakes?|doughnuts?|donuts?|fritters?)$",
            Options);

        private static readonly Regex ThisPart = new(@"^\W*(?:then\s+)?(?:\p{L}+\s+)?(?:it|them|the\s+mixture|this\s+mixture)\b", Options);

        private static readonly Regex NotServed = new(@"\bserving\s+(?:size|spoon|pieces)\b|\b(?:before|ready\s+to|until)\s+serv\w*\b", Options);

        private static readonly Regex SameRest = new(@"\b(?:chill(?:s|ed)?|refrigerat\w*|cover(?:s|ed)?|rest|rests|rise|rises|freez(?:e|es)|stand|stands|sit|sits)\b|\b(?:back\s+)?in(?:to)?\s+the\s+(?:fridge|refrigerator)\b", Options);

        private static readonly Regex SetAside = new(@"\bset\s+(?:it\s+|them\s+|this\s+|the\s+(?:\p{L}+\s+){1,2})?aside\b|\breserv\w*\b|\bfor\s+later\b|\buntil\s+(?:ready|needed)\b", Options);

        private static readonly Regex WetWords = new(@"\b(?:eggs?|yolks?|whites|butter|milk|cream|oil|water|juices?|yogh?urt|buttermilk|vanilla|extract|honey|bananas?|pumpkin|puree|melted)\b", Options);

        private static readonly Regex WhippedCream = new(@"\bwhip\w*\b[^.;]*\bcream\b|\bcream\b[^.;]*\bwhip\w*|\bbutter\s*cream\b|\bganache\b|\bmeringue\b", Options);

        private static readonly Regex Held = new(@"\b(?:except|excepting|excluding|save|apart\s+from|other\s+than|without|minus|reserv(?:e|es|ing)|(?:everything|all|ingredients|else)\s+but)\b", Options);

        private static readonly Regex HeldList = new(@"^[\p{L}\d/.\s,&'-]*$", Options);

        private static readonly Regex PriorHeat = new(
            @"\b(?:after|once|when)\s+(?:(?:the|it|they|this|that|your|its)\s+)?(?:\p{L}+\s+){0,2}?(?:(?:has|have|had)\s+(?:been\s+)?(?:\p{L}+ly\s+)?)?(?:simmer(?:ed|ing)|boil(?:ed|ing)|cook(?:ed|ing)|bak(?:ed|ing)|roast(?:ed|ing)|stew(?:ed|ing)|brais(?:ed|ing)|reduc(?:ed|ing)|heat(?:ed|ing)|fried|frying|poach(?:ed|ing)|steam(?:ed|ing)|saut[eé](?:ed|ing)?)\b(?:(?!\s+(?:and|then)\b)[^.;,])*",
            Options);

        private static readonly Regex AddedAgain = new(@"\b(?:brush\w*|sprinkl\w*|drizzl\w*|spoon\w*|dous\w*|bast(?:e|es|ed|ing)|moisten\w*|dribbl\w*|splash\w*|pour\w*|top(?:s|ped|ping)?)\b", Options);

        private static readonly Regex ItsMixture = new(@"^\p{L}+\s+(?:mixture|liquid|sauce|syrup|glaze|butter|cream|marinade|reduction)\b", Options);

        private static readonly Regex HalfHourRange =new(@"\b(\d+)\s*(?:-|to)\s*(\d+)\s+(?:and\s+)?1/2\s+(?:hours?|hrs?)\b", Options);

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

        private static readonly Regex Soak = new(@"\b(?:soak\w*|steep(?:s|ed|ing)?|macerat\w*|plump(?:s|ed|ing)?|marinat\w*)\b", Options);

        private static readonly Regex ClauseBreak = new(@",\s*|\s+(?:and\s+)?then\s+", Options);

        private static readonly Regex AddVerb = new(@"\b(?:add|adds|added|adding|stir|stirs|stirred|stirring|pour|pours|poured|pouring|whisk\w*|mix|mixes|mixed|fold\w*|splash\w*|swirl\w*|beat|beats|blend\w*|combine\w*|flavou?r(?:s|ed|ing)?\s+(?:it\s+|them\s+)?with|season(?:s|ed|ing)?\s+(?:it\s+|them\s+)?with|put\s+in(?!\s+(?:a|an|the)\s+(?:\p{L}+\s+)?(?:dish|pan|tin|mould|mold|oven|jar|bottle|glass|bowl|basin|cup)s?\b))\b", Options);

        private static readonly Regex DrinkGeneric = new(@"^(?:extracts?|essence|flavou?r(?:ing)?s?|spirits?|liquor|alcohol\w*)$", Options);

        private static readonly Regex GenericMention = new(
            @"(?<!\b(?:the|its|their|this|that|own|same|oyster|oysters|clam|clams|mussel|mussels|pickle|pot|meat|fish|vegetable|cooking|ham|chicken|beef|mushroom)\s+)\b(?:liquors?|spirits?|alcohol)\b(?!\s+(?:from|of|in)\b)",
            Options);

        private static readonly Regex MixAll = new(
            @"\b(?:mix|mixes|mixed|stir|stirs|stirred|beat|beats|combine\w*|blend\w*|whisk\w*|fold\w*|toss\w*|put|add|adds|work)\b[^.;]*?\b(?:all\s+(?:the\s+)?(?:other\s+|remaining\s+)?(?:\p{L}+\s+)?ingredients|(?:icing|frosting|glaze|filling|topping|sauce|dressing|wet|liquid)\s+ingredients|(?:all|them|it|everything)\s+(?:well\s+)?together|everything|all\s+(?:in|into)|(?:the\s+)?(?:remaining|other)\s+ingredients|(?:the\s+)?rest\s+of\s+the\s+ingredients)\b|^\W*(?:mix|stir|beat|combine|blend)\s+(?:well\s+)?together\s*(?:[,.;]|$)|^\W*(?:mix|stir|beat|combine|blend)\s+(?:well|thoroughly)\s*(?:[,.;]|$)",
            Options);

        private static readonly Regex DryIngredients = new(@"\b(?:dry|dried)\s+ingredients\b", Options);

        private static readonly Regex NotMention = new(
            @"^(?:cups?|cupfuls?|teacups?|tablespoons?|tablespoonfuls?|teaspoons?|teaspoonfuls?|dessertspoons?|dessertspoonfuls?|spoons?|spoonfuls?|tsps?|tbsps?|tbs|ounces?|oz|pounds?|lbs?|pints?|quarts?|gills?|drops?|dashes|dash|pinch(?:es)?|glass(?:es|ful|fuls)?|wineglass(?:es|ful|fuls)?|bottles?|grams?|ml|litres?|liters?|few|little|some|pure|good|best|real|imitation|natural|about|scant|heaping|rounded|level|large|small|half|quarter|one|two|three|four|five|six|optional|plus|more|extra|taste|each|then|with|from|into|well)$",
            Options);

        private const string BareExtract = @"(?<=(?:^|[,;:(]|\b(?:and|or|the|with|plus|add|in))\s*)extracts?\b";

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
        public static string BasisOf(string kind, string? method, int? minutes) => BasisOf(kind, method, minutes, null);

        /// <summary>
        /// The basis stored with a line, with the reason of an upper bound as a fourth part ("up-to:no-heat::parts",
        /// "up-to:simmer:30:pause"); without a reason it is <see cref="BasisOf(string, string?, int?)"/>.
        /// </summary>
        public static string BasisOf(string kind, string? method, int? minutes, string? reason) =>
            reason != null ? string.Create(CultureInfo.InvariantCulture, $"{kind}:{method}:{minutes}:{reason}")
            : method == null ? kind : minutes == null ? $"{kind}:{method}" : string.Create(CultureInfo.InvariantCulture, $"{kind}:{method}:{minutes}");

        /// <summary>The kind, method, minutes and upper-bound reason of a stored basis; null for an empty one.</summary>
        public static (string Kind, string? Method, int? Minutes, string? Reason)? ReadBasis(string? basis)
        {
            if (string.IsNullOrWhiteSpace(basis)) return null;
            var parts = basis.Split(':');
            int? minutes = parts.Length > 2 && int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var m) ? m : null;
            return (parts[0], parts.Length > 1 && parts[1].Length > 0 ? parts[1] : null, minutes, parts.Length > 3 && parts[3].Length > 0 ? parts[3] : null);
        }

        /// <summary>
        /// The USDA row of a reading's kind, method and minutes; null for no heat or no steps. An upper bound ("up to")
        /// takes the row of what is certain: a timed heat of 15 minutes or more its timed row, heat with less or no time the
        /// method's highest row (5002 on the stove, 5010 in the oven), a hot liquid or a flaming 5002 (warmed, burnt for a
        /// time not stated), a stand overnight 5001, and no certain heat none (all of it).
        /// </summary>
        public static AlcoholRetentionRow? RowFor(string kind, string? method, int? minutes)
        {
            string? code = kind switch
            {
                Overnight => "5001",
                HotLiquid => "5002",
                Flamed => "5003",
                UpTo when method is Simmer or Bake && minutes is >= 15 => TimedCode(method, minutes),
                UpTo when method == Bake => "5010",
                UpTo when method is Simmer or HotLiquid or Flamed => "5002",
                UpTo when method == Overnight => "5001",
                UpTo => null,
                Timed => TimedCode(method, minutes),
                _ => null,
            };
            return code == null ? null : Rows.First(r => r.Code == code);
        }

        private static string? TimedCode(string? method, int? minutes) => minutes switch
        {
            >= 15 and < 30 when method == Bake => "5010",
            >= 150 => "5009",
            >= 120 => "5008",
            >= 90 => "5007",
            >= 60 => "5006",
            >= 30 => "5005",
            >= 15 => "5004",
            _ => null,
        };

        /// <summary>The reading for a kind, method and minutes, with the factor of its USDA row (1 when nothing is lost).</summary>
        public static AlcoholRetentionReading Of(string kind, string? method = null, int? minutes = null)
        {
            var row = RowFor(kind, method, minutes);
            return new AlcoholRetentionReading(row?.Factor ?? 1m, kind, method, minutes, row?.Code);
        }

        /// <summary>
        /// A reading made an upper bound for <paramref name="reason"/>: what it states becomes the most that may stay ("up to"),
        /// marked estimated. A timed heat keeps its minutes, heat with no time keeps its method, a hot liquid, a flaming or a
        /// stand overnight keeps its row, and no heat (or no steps) is all of it.
        /// </summary>
        public static AlcoholRetentionReading Bound(AlcoholRetentionReading reading, string reason)
        {
            var bound = reading.Kind switch
            {
                Timed or UpTo => Of(UpTo, reading.Method, reading.Minutes),
                HotLiquid or Flamed or Overnight => Of(UpTo, reading.Kind),
                _ => Of(UpTo, NoHeat),
            };
            return bound with { Unnamed = reading.Unnamed, Reason = reading.UpTo && reading.Reason != null ? reading.Reason : reason };
        }

        /// <summary>
        /// How much of the line's alcohol stays, read from the recipe's steps in order. The drink is
        /// found by the line's alcohol words (or, for "vanilla extract", the word before the
        /// flavouring word); the steps from the sentence that names it on are read for heat.
        /// </summary>
        public static AlcoholRetentionReading Read(IEnumerable<string?>? steps, string? line) => ReadRecipe(steps, new[] { line })[0];

        /// <summary>
        /// The reading of each drink line of one recipe, in the order the lines are given (alcohol-retention/10, staging
        /// checkpoint 6 R14 and the fresh draws of ck12). A single value is given only where the steps determine it: the
        /// steps name the drink, and the heat that reaches it (what is done to it, for how long, in which part) is read from
        /// the run that certainly goes on with its mixture (<see cref="CertainSegment"/>), so no part, pause or choice stands
        /// between the drink and that heat. Where the full reading (<see cref="ReadAt"/>: parts resumed after a pause, a
        /// filling joined into a batter) would take more heat than that run, or the steps never name the drink, name it at
        /// more than one point or for several lines, flame it, or soak or marinate a food in it, the reading is an upper
        /// bound (<see cref="Bound"/>): the most that may stay, from the heat that is certain, marked "up to" and estimated,
        /// with its reason. The cost is over-statement, never under-statement. Several lines of one drink are read each from
        /// the sentence that names it in the same turn only when the steps name it once for each line.
        /// </summary>
        public static IReadOnlyList<AlcoholRetentionReading> ReadRecipe(IEnumerable<string?>? steps, IReadOnlyList<string?> lines)
        {
            var sentences = (steps ?? Enumerable.Empty<string?>())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .SelectMany(s => SentenceSplit.Split(Normalize(s!)))
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToList();
            var keys = lines.Select(l => Mention(l)?.ToString()).ToList();
            var readings = new List<AlcoholRetentionReading>();
            for (var j = 0; j < lines.Count; j++)
            {
                if (sentences.Count == 0)
                {
                    readings.Add(Of(NoSteps));
                    continue;
                }
                var same = keys[j] == null ? new List<int> { j } : Enumerable.Range(0, lines.Count).Where(i => keys[i] == keys[j]).ToList();
                readings.Add(Decide(sentences, lines[j], same.IndexOf(j), same.Select(i => lines[i]).ToList()));
            }
            return readings;
        }

        private static AlcoholRetentionReading Decide(List<string> sentences, string? line, int turn, IReadOnlyList<string?> siblings)
        {
            var turns = siblings.Count;
            var mention = Mention(line);
            var named = mention == null ? new List<(int Index, int Position)>() : Enumerable.Range(0, sentences.Count)
                .Select(i => (Index: i, Match: mention.Matches(sentences[i]).FirstOrDefault(m => !HeldBack(sentences[i], m.Index))))
                .Where(t => t.Match != null && !ForLater.IsMatch(sentences[t.Index]))
                .Select(t => (Index: t.Index, Position: t.Match!.Index))
                .ToList();
            if (named.Count == 0 && mention != null && turns == 1)
                named = Enumerable.Range(0, sentences.Count).Select(i => (Index: i, Match: GenericMention.Match(sentences[i]))).Where(t => t.Match.Success).Select(t => (Index: t.Index, Position: t.Match.Index)).Take(1).ToList();
            if (named.Count == 0 && mention != null && sentences.Any(s => mention.Matches(s).Any(m => HeldBack(s, m.Index))))
                return Bound(Of(NoHeat), ReasonUnnamed) with { Unnamed = true };
            if (named.Count == 0) return turn == 0 ? Unnamed(sentences, line) : Bound(Of(NoHeat), ReasonLines) with { Unnamed = true };

            if (turns > 1)
            {
                var each = named.Select(n => At(sentences, n.Index, n.Position).Reading).ToList();
                var amounts = siblings.Select(AmountOf).ToList();
                if (named.Count < turns && named.Select(n => n.Index).Distinct().Count() == named.Count && amounts.All(a => a != null && a == amounts[0]))
                    return turn < named.Count ? each[turn] : Bound(Of(NoHeat), ReasonLines) with { Unnamed = true };
                if (named.Count == turns && named.Select(n => n.Index).Distinct().Count() == turns)
                {
                    if (amounts.All(a => a != null && a == amounts[0])) return each[turn];
                    if (amounts[turn] is { } mine && amounts.Count(a => a == mine) == 1
                        && named.Select((n, k) => (k, Hit: StatesAmount(sentences[n.Index], mine, mention!))).Where(t => t.Hit).ToList() is { Count: 1 } hit)
                        return each[hit[0].k];
                }
                var possible = each.Concat(named.Count < turns ? new[] { Of(NoHeat) } : Array.Empty<AlcoholRetentionReading>()).ToList();
                return named.Count >= turns && possible.All(r => r.Factor == possible[0].Factor && r.Basis == possible[0].Basis) ? possible[0] : Highest(possible, ReasonLines);
            }

            var first = At(sentences, named[0].Index, named[0].Position);
            var firstClause = ClauseStart(sentences[named[0].Index], named[0].Position);
            var again = Enumerable.Range(named[0].Index, Math.Min(first.End, sentences.Count - 1) - named[0].Index + 1)
                .SelectMany(i => mention!.Matches(sentences[i]).Select(m => (Index: i, Position: m.Index)))
                .Where(n => (n.Index > named[0].Index || ClauseStart(sentences[n.Index], n.Position) > firstClause) && AddsAgain(sentences[n.Index], n.Position));
            var others = named.Where(n => n.Index > first.End).Concat(again).Select(n => At(sentences, n.Index, n.Position).Reading).ToList();
            return others.Any(o => o.Factor > first.Reading.Factor) ? Highest(others.Append(first.Reading), ReasonLines) : first.Reading;
        }

        /// <summary>Whether the drink named at a position is held back from the mixture its sentence makes ("all the ingredients except the rum", "everything but the brandy", "reserve 2 tablespoons of the wine").</summary>
        private static bool HeldBack(string sentence, int position)
        {
            var before = sentence[..position];
            var held = Held.Matches(before).LastOrDefault();
            if (held == null) return false;
            var list = before[(held.Index + held.Length)..];
            return HeldList.IsMatch(list) && !ClauseVerb.IsMatch(list) && list.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 6;
        }

        /// <summary>Whether the drink named again at a position is added once more, in its own clause, by a verb that puts it in or on the dish ("stir in the remaining wine", "brush the warm cake with the brandy"), not named as the mixture it is already in ("the brandy mixture").</summary>
        private static bool AddsAgain(string sentence, int position)
        {
            if (HeldBack(sentence, position) || ItsMixture.IsMatch(sentence[position..])) return false;
            var lead = sentence[ClauseStart(sentence, position)..position];
            return AddVerb.IsMatch(lead) || AddedAgain.IsMatch(lead);
        }

        /// <summary>The amount a line opens with, as a number and a unit ("1 teaspoon", "2 tablespoon"); null when it opens with none.</summary>
        private static string? AmountOf(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;
            var m = LineAmount.Match(Regex.Replace(Normalize(line), @"(\d+)\s+and\s+(\d/\d)", "$1 $2"));
            return m.Success ? $"{Regex.Replace(m.Groups["n"].Value.Trim(), @"\s+", " ")} {UnitOf(m.Groups["u"].Value)}" : null;
        }

        private static string UnitOf(string unit) => unit.ToLowerInvariant() switch
        {
            var u when u.StartsWith("t", StringComparison.Ordinal) && (u.StartsWith("tb", StringComparison.Ordinal) || u.StartsWith("tablesp", StringComparison.Ordinal)) => "tablespoon",
            var u when u.StartsWith("t", StringComparison.Ordinal) => "teaspoon",
            var u when u.StartsWith("c", StringComparison.Ordinal) => "cup",
            var u when u.StartsWith("o", StringComparison.Ordinal) => "ounce",
            var u => u,
        };

        /// <summary>Whether a step sentence states that amount of the drink, the amount right before its name ("1 tablespoon of vanilla extract").</summary>
        private static bool StatesAmount(string sentence, string amount, Regex mention)
        {
            var text = " " + Normalize(sentence);
            foreach (Match m in LineAmount.Matches(text))
                if ($"{Regex.Replace(m.Groups["n"].Value.Trim(), @"\s+", " ")} {UnitOf(m.Groups["u"].Value)}" == amount
                    && mention.Match(text, m.Index + m.Length) is { Success: true } named && Regex.IsMatch(text[(m.Index + m.Length)..named.Index], @"^\s*(?:of\s+)?(?:[\p{L}-]+\s+){0,2}$", Options))
                    return true;
            return false;
        }

        private sealed record Placed(AlcoholRetentionReading Reading, int End);

        /// <summary>The reading of a drink placed at a sentence: the certain run's reading, a single value unless a flame, a soak or a marinade, or more heat the full reading takes beyond that run, makes it an upper bound.</summary>
        private static Placed At(List<string> sentences, int index, int position)
        {
            var certain = ReadCertain(sentences, index, position);
            var reading = certain.Reading;
            var run = string.Join(" ", sentences.Skip(index).Take(certain.End - index + 1));
            bool Ours(Match m) => m.Success && (m.Groups["n"].Value.Equals("mixture", StringComparison.OrdinalIgnoreCase) || Regex.IsMatch(run, @"\b" + WordForms(m.Groups["n"].Value) + @"\b", Options) || Portion.IsMatch(run));
            if (Enumerable.Range(index + 1, sentences.Count - index - 1).Any(i => Ours(SplitUse.Match(sentences[i])))
                && Enumerable.Range(index + 1, sentences.Count - index - 1).FirstOrDefault(i => Ours(Portion.Match(sentences[i])), -1) is var portion and > 0)
                return new Placed(Bound(ReadCertain(sentences.Take(portion).ToList(), index, position).Reading, ReasonLines), certain.End);
            if (Soak.IsMatch(sentences[index]) || Marinade.IsMatch(sentences[index])) reading = Bound(reading, ReasonMarinade);
            else if (certain.Flame) reading = Bound(reading, ReasonFlamed);
            else if (ReadAt(sentences, index, position).Factor < reading.Factor) reading = Bound(reading, certain.Stop ?? ReasonParts);
            else if (!reading.UpTo && certain.Stop != null && sentences.Skip(certain.End + 1).Any(Heats)) reading = Bound(reading, certain.Stop);
            return new Placed(reading, certain.End);
        }

        private static AlcoholRetentionReading Highest(IEnumerable<AlcoholRetentionReading> readings, string reason) =>
            Bound(readings.MaxBy(r => r.Factor)!, reason);

        /// <summary>
        /// A drink no step names. When no drink word of its kind is named either and one step mixes all the ingredients
        /// together (not only the dry ones), the drink is in that mixture and is read from there, marked as an estimate;
        /// otherwise the reading is an upper bound: the highest reading of the steps that name a drink word of its kind or mix
        /// all the ingredients, or all of it when there are none (it may go in after all the heat).
        /// </summary>
        private static AlcoholRetentionReading Unnamed(List<string> sentences, string? line)
        {
            var family = Family(AlcoholLineContext.Read(line));
            var drinkWords = Enumerable.Range(0, sentences.Count)
                .Select(i => (Index: i, Match: AlcoholLineContext.Trigger.Matches(sentences[i]).FirstOrDefault(m => MayName(m.Value, family) && !(BareFlavour.IsMatch(m.Value) && FlavourNoun.IsMatch(sentences[i][..m.Index])))))
                .Where(t => t.Match != null)
                .ToList();
            if (drinkWords.Count > 0)
                return Highest(drinkWords.Select(t => At(sentences, t.Index, t.Match!.Index).Reading)
                    .Concat(drinkWords.Any(t => LiquorWord.IsMatch(t.Match!.Value)) ? new[] { Of(NoHeat) } : Array.Empty<AlcoholRetentionReading>()), ReasonUnnamed) with { Unnamed = true };

            var mixed = Enumerable.Range(0, sentences.Count)
                .Where(i => MixAll.IsMatch(sentences[i]) && (!DryIngredients.IsMatch(sentences[i]) || WetHalf.IsMatch(sentences[i])))
                .ToList();
            if (mixed.Count == 1) return At(sentences, mixed[0], 0).Reading with { Unnamed = true };
            if (mixed.Count > 1) return Highest(mixed.Select(i => At(sentences, i, 0).Reading), ReasonUnnamed) with { Unnamed = true };
            return Bound(Of(NoHeat), ReasonUnnamed) with { Unnamed = true };
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

        private static int ClauseStart(string sentence, int position) =>
            ClauseBreak.Matches(sentence[..position]).Select(m => m.Index + m.Length)
                .Where(start => ClauseVerb.IsMatch(sentence[start..position])).DefaultIfEmpty(0).Max();

        private static AlcoholRetentionReading ReadAt(List<string> sentences, int index, int position)
        {
            var sentence = sentences[index];
            var clauseStart = ClauseStart(sentence, position);
            var after = Segment(OwnClause(sentence, clauseStart), sentences.Skip(index + 1).ToList(), string.Join(" ", sentences.Take(index + 1)), Soak.IsMatch(sentence));
            var scan = Scan(after);
            if (!scan.Stove && !scan.Oven && !scan.Flame
                && new Func<List<string>?>[] { () => Resume(sentences, index, after), () => MadeAhead(sentences, index, after), () => Assembled(sentences, index, after) }
                    .Select(read => read()).FirstOrDefault(r => r != null && Scan(r) is { Stove: true } or { Oven: true } or { Flame: true }) is { } resumed)
            {
                after = after.Concat(resumed).ToList();
                scan = Scan(after);
            }
            return Interpret(sentences, index, position, clauseStart, after, scan);
        }

        /// <summary>The drink's clause, without a heat it says is already done when the drink goes in ("after the sauce has simmered for 30 minutes", "once boiled").</summary>
        private static string OwnClause(string sentence, int clauseStart) => PriorHeat.Replace(sentence[clauseStart..], " ");

        private sealed record Certain(AlcoholRetentionReading Reading, string? Stop, int End, bool Flame);

        /// <summary>
        /// The reading of only the heat that certainly reaches the drink: its clause and the sentences after it while they
        /// go on with the same mixture (<see cref="CertainSegment"/>), with no part resumed after a pause, a separate bowl or
        /// a new part. It can only keep more alcohol than the full reading, never less; <c>Stop</c> says why the run ended
        /// before the end of the steps (null when it did not, or ended after the dish was cooked or served).
        /// </summary>
        private static Certain ReadCertain(List<string> sentences, int index, int position)
        {
            var sentence = sentences[index];
            var clauseStart = ClauseStart(sentence, position);
            var after = CertainSegment(sentences, index, OwnClause(sentence, clauseStart), out var stop, out var end, out var cut, out var alternative);
            var scan = Scan(after, true);
            var reading = Interpret(sentences, index, position, clauseStart, after, scan);
            if (cut > 0 && reading.Kind is Timed or UpTo && reading.Minutes is { } minutes)
                reading = Bound(Of(Timed, scan.Oven ? Bake : reading.Method, Math.Max(0, minutes - cut)), ReasonChoice);
            else if (alternative && reading.Kind is Timed) reading = Bound(reading, ReasonChoice);
            return new Certain(reading, stop, end, scan.Flame);
        }

        private static AlcoholRetentionReading Interpret(List<string> sentences, int index, int position, int clauseStart, List<string> after, HeatScan scan)
        {
            var sentence = sentences[index];
            if (!scan.Stove && !scan.Oven)
            {
                if (scan.Flame) return Of(Flamed);
                if (after.Any(s => Frozen.IsMatch(s)) && !(OffHeat.IsMatch(sentence[..position]) && !Cold.IsMatch(sentence[..position]) && !WarmOnly.IsMatch(sentence[..position]))) return Of(NoHeat);
                var ownPart = NewComponent.IsMatch(sentence) || SeparateStep.IsMatch(sentence);
                var before = (index > 0 && !ownPart ? sentences[index - 1] : string.Empty) + " " + sentence[..clauseStart];
                var clause = sentence[clauseStart..];
                var own = ClauseBreak.Match(sentence, position) is { Success: true } end ? sentence[clauseStart..end.Index] : clause;
                if (AndThenCold.Match(own) is { Success: true, Index: > 0 } cold) own = own[..cold.Index];
                if (!Cold.IsMatch(sentence[..position])
                    && (OffHeat.IsMatch(sentence) || (OffHeat.IsMatch(before) && !ColdAct.IsMatch(before[OffHeat.Match(before).Index..])) || HotInto.IsMatch(BeforeServing(clause))
                        || (AddVerb.IsMatch(clause) && !Cold.IsMatch(BeforeServing(own)) && !NewMixture(clause) && HeatLast(before) && !MeltedOnly(before, sentences, index)
                            && (Scan(new[] { before }).Stove || (!ownPart && sentence[..clauseStart].Trim().Length == 0 && StillOnTheHeat(sentences, index))))
                        || (HotJoin.IsMatch(BeforeServing(clause)) && HeatedBefore(sentences, index)))) return Of(HotLiquid);
                if (after.Any(s => OvernightWords.IsMatch(s) && !Storage.IsMatch(s) && !ShortOvernight.IsMatch(s)) && !after.Any(s => Sealed.IsMatch(s))) return Of(Overnight);
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
                    if (Scan(new[] { sentence }) is { Stove: false, Oven: false, Flame: false }
                        && i + 1 < rest.Count && JoinsBack(rest[i + 1], segment.Prepend(drinkSentence))) continue;
                    break;
                }
                if (NewComponent.IsMatch(sentence) && OptionalPart.IsMatch(sentence)) continue;
                if (NewComponent.IsMatch(sentence)) break;
                if (UsedInstead.IsMatch(sentence) || SetAsideRest.IsMatch(sentence)) continue;
                if ((AppliedPart.IsMatch(segment[^1]) || PlacedInPan.IsMatch(segment[^1])) && (NewMixture(sentence) || NewVessel.IsMatch(sentence))) break;
                if (NewMixture(sentence) && Scan(segment) is { Stove: false, Oven: false, Flame: false }
                    && rest.Skip(i).Take(3).Any(s => Stove.IsMatch(NotHeating.Replace(s, " "))) && !rest.Skip(i).Take(3).Any(s => Oven.IsMatch(NotHeating.Replace(s, " ")))) break;
                segment.Add(sentence);
                if (resting && OvernightWords.IsMatch(sentence) && Scan(new[] { sentence }) is { Stove: false, Oven: false, Flame: false }) continue;
                resting = false;
                if (PauseStep.IsMatch(sentence) && !NotPause.IsMatch(sentence)) break;
            }
            return segment;
        }

        /// <summary>The first heat verb of a sentence, lower case ("bak" for "Bake"), null when it has none.</summary>
        private static string? HeatVerb(string sentence) => SameHeat.Match(sentence) is { Success: true } m ? m.Groups["v"].Value.ToLowerInvariant() : null;

        /// <summary>
        /// The run of the method that certainly cooks the drink: its clause and the sentences after it while they go on
        /// with the same mixture. The run ends before a separate preparation, a new bowl or pan, a new part ("For the
        /// glaze") or a new mixture of something else ("Beat the egg whites"), unless that is a bowl of only dry ingredients
        /// the next sentence stirs into the drink's mixture; before a sentence that cooks a named food the run has not named
        /// ("Bake the shortcakes"), and, for a drink in a side part (an icing, sauce, filling, whipped cream and the like),
        /// before any heat that does not name that part or go on with it on the stove; and after a sentence that sets the
        /// mixture aside, chills, cools or serves it, unless it only rests or chills the same dough or mixture before it
        /// goes on uncooked. A sentence that offers a choice (another method, "if using", an optional part, another time
        /// for another result: "For a firmer bar, bake for 30 minutes") is left out, and its time is never added to
        /// the time before it: a shorter time it offers for another result by the same heat (not one it adds: "5 minutes more", nor "For a crisper top, broil it 1 minute" after a bake) or as enough in a conditional sentence ("If not thick, an hour will be
        /// sufficient"; not "If the crust darkens, lay foil over it after 20 minutes") is taken as <paramref name="cut"/>, and a time offered for another result makes the reading an
        /// upper bound (<paramref name="alternative"/>; staging checkpoint 9, U12: 18 and 30 minutes were read as 48). A
        /// sentence that starts a new preparation but adds it, in a later clause, back into the run's mass goes on with
        /// the run from that clause on, the heat of what was prepared before it not being the drink's (<see cref="JoinAt"/>),
        /// and so does a dough put back in the fridge or frozen for later use.
        /// <paramref name="stop"/> is why the run ended early (<see cref="ReasonParts"/>, <see cref="ReasonPause"/> or
        /// <see cref="ReasonChoice"/>), null when it reached the end or the dish was cooked; <paramref name="end"/> is the
        /// index of the last sentence it read.
        /// </summary>
        private static List<string> CertainSegment(List<string> sentences, int index, string clause, out string? stop, out int end, out int cut, out bool alternative)
        {
            cut = 0;
            alternative = false;
            string? lastHeat = Heats(clause) ? clause : null;
            if (lastHeat != null && (ChoiceTime.IsMatch(clause) || OrMethod.IsMatch(clause)) && TimesIn(clause) is { } own0 && AllMinutes(clause) is { Count: > 0 } offered0)
                cut += Math.Max(0, own0.Low - (Seconds.IsMatch(clause) ? 0 : offered0.Min()));
            stop = null;
            end = index;
            var segment = new List<string> { clause };
            var sentence = sentences[index];
            var side = SideOf(sentence, clause) ?? Enumerable.Range(Math.Max(0, index - 2), Math.Min(2, index)).Reverse()
                .TakeWhile(k => !Heats(sentences[k]))
                .Where(k => NewComponent.IsMatch(sentences[k])).Select(k => SideOf(sentences[k], sentences[k])).FirstOrDefault(r => r != null);
            var stoveSide = side != null && (Scan(new[] { sentence }).Stove || PanVessel.IsMatch(sentence) || (index > 0 && Scan(new[] { sentences[index - 1] }).Stove && !NewComponent.IsMatch(sentence)));
            var choice = false;
            var returns = 0;
            if (Ends(clause, segment, out var pauseFirst))
            {
                if (pauseFirst) stop = ReasonPause;
                return segment;
            }
            for (var i = index + 1; i < sentences.Count; i++)
            {
                var s = sentences[i];
                var mass = segment.Prepend(sentence).Concat(sentences.Skip(Math.Max(0, index - 3)).Take(Math.Min(3, index))).ToList();
                var text = string.Join(" ", mass);
                if (AltMethod.IsMatch(s) && !Heats(s) && (!Frozen.IsMatch(s) || LaterUse.IsMatch(s))) continue;
                if (AltMethod.IsMatch(s))
                {
                    choice = true;
                    break;
                }
                if ((Choice.IsMatch(s) && (!Heats(s) || (ChoiceLead.IsMatch(s) && !(ThenClause.Split(s) is { Length: > 1 } parts && Heats(parts[^1]))))) || UsedInstead.IsMatch(s) || (NewComponent.IsMatch(s) && OptionalPart.IsMatch(s)))
                {
                    choice |= Heats(s);
                    if (lastHeat != null && ChoiceTime.IsMatch(s) && !NotCooking.IsMatch(s) && AllMinutes(s) is { Count: > 0 } offered && TimesIn(lastHeat) is { } counted)
                        cut += Math.Max(0, counted.Low - offered.Min());
                    else if (lastHeat != null && Conditional.IsMatch(s) && Sufficient.IsMatch(s) && !Heats(s) && !NotCooking.IsMatch(s) && AllMinutes(s) is { Count: > 0 } shorter && TimesIn(lastHeat) is { } before)
                        cut += Math.Max(0, before.Low - shorter.Min());
                    else if (lastHeat != null && Heats(s) && AltPurpose.IsMatch(s) && !AddedTime.IsMatch(s) && HeatVerb(s) is { } verb && verb == HeatVerb(lastHeat)
                        && AllMinutes(s) is { Count: > 0 } other && TimesIn(lastHeat) is { } first)
                        cut += Math.Max(0, first.Low - other.Min());
                    if (lastHeat != null && Heats(s) && AltPurpose.IsMatch(s) && AllMinutes(s).Count > 0) alternative = true;
                    continue;
                }
                if (SetAsideRest.IsMatch(s) || Storage.IsMatch(s)) continue;
                var heated = Heats(segment);
                var own = OwnMass.Matches(s).Any(m => !DryWord.IsMatch(m.Groups["w"].Value));
                var prep = !own && NewPrep.Match(s) is { Success: true } p && !BackReference.IsMatch(p.Groups["o"].Value) && !DishNoun.IsMatch(p.Groups["o"].Value)
                    && (!Regex.IsMatch(text, @"\b" + WordForms(p.Groups["o"].Value) + @"\b", Options) || !Regex.IsMatch(s, @"^\W*(?:then\s+)?(?:heat|brown)\b", Options));
                var floured = DryHalf.IsMatch(string.Join(" ", segment.Prepend(sentence)));
                var mixed = !own && NewMixture(s) && !MixOn.IsMatch(NewMixtureVerb.Match(s).Groups["o"].Value)
                    && !(JoinsBack(s, mass) && !(floured && DryJoin.IsMatch(s))) && !(DryJoin.IsMatch(s) && !floured);
                if (!heated && mixed && DryOnly(s) && !SeparateStep.IsMatch(s) && !NewVessel.IsMatch(s) && !floured)
                    mixed = false;
                if ((prep || mixed) && !SeparateStep.IsMatch(s) && !NewVessel.IsMatch(s) && JoinAt(s, text) is var join and > 0)
                    (prep, mixed, s) = (false, false, s[join..]);
                if (SeparateStep.IsMatch(s) || (NewVessel.IsMatch(s) && !own && (VesselFirst.IsMatch(s) || !Heats(s) || !(HeatObjectAny.Match(s) is { Success: true } ho && Regex.Match(ho.Groups["o"].Value, @"\p{L}+$").Value is var hn && (DishNoun.IsMatch(hn) || Regex.IsMatch(text, @"\b" + WordForms(hn) + @"\b", Options))))) || prep || mixed)
                {
                    if (!heated && !floured && DryOnly(s) && i + 1 < sentences.Count && (JoinsBack(sentences[i + 1], mass) || DryJoin.IsMatch(sentences[i + 1])))
                        continue;
                    if (!heated && !PartNoun.IsMatch(s) && Enumerable.Range(i + 1, Math.Min(2, sentences.Count - i - 1))
                            .FirstOrDefault(k => (JoinsBack(sentences[k], mass) || (side == null && AddedTo.IsMatch(sentences[k]))) && !Heats(sentences[k]), -1) is var joined and > 0
                        && Enumerable.Range(i + 1, joined - i - 1).All(k => !NewComponent.IsMatch(sentences[k]) && !SeparateStep.IsMatch(sentences[k])))
                    {
                        i = joined - 1;
                        continue;
                    }
                    if (!heated && (mixed || NewVessel.IsMatch(s)) && !SeparateStep.IsMatch(s) && !Heats(s) && i + 1 < sentences.Count && Strewn.IsMatch(sentences[i + 1]) && !NewMixture(sentences[i + 1]))
                    {
                        i++;
                        continue;
                    }
                    stop = heated ? null : ReasonParts;
                    if ((side == null || InPan(string.Join(" ", segment))) && returns < 2 && Return(sentences, i, segment, sentence, heated, side != null) is { } back)
                    {
                        (stop, returns, i) = (null, returns + 1, back - 1);
                        continue;
                    }
                    break;
                }
                if (NewComponent.IsMatch(s) || Heading.IsMatch(s))
                {
                    stop = heated ? null : ReasonParts;
                    if ((side == null || InPan(string.Join(" ", segment))) && returns < 2 && Return(sentences, i, segment, sentence, heated, side != null) is { } back)
                    {
                        (stop, returns, i) = (null, returns + 1, back - 1);
                        continue;
                    }
                    break;
                }
                if (Heats(s))
                {
                    var noun = HeatObject.Match(s) is { Success: true } o ? Regex.Match(o.Groups["o"].Value, @"\p{L}+$").Value : null;
                    var foreign = noun != null && !BackReference.IsMatch(noun) && !DishNoun.IsMatch(noun)
                        && !Regex.IsMatch(text, @"\b" + WordForms(noun) + @"\b", Options)
                        && !(side == null && Oven.IsMatch(s) && InPan(string.Join(" ", segment)));
                    var ofSide = side == null || side.IsMatch(s) || ThisPart.IsMatch(s) || (stoveSide && !Oven.IsMatch(NotHeating.Replace(s, " ")) && !HeatObject.IsMatch(s))
                        || (Oven.IsMatch(NotHeating.Replace(s, " ")) && (InPan(string.Join(" ", segment)) || segment.Any(x => OverCrust.IsMatch(x))));
                    if (foreign || !ofSide)
                    {
                        stop = heated ? null : ReasonParts;
                        break;
                    }
                }
                segment.Add(s);
                end = i;
                if (Heats(s))
                {
                    lastHeat = s;
                    if ((ChoiceTime.IsMatch(s) || OrMethod.IsMatch(s)) && TimesIn(s) is { } taken && AllMinutes(s) is { Count: > 0 } within)
                        cut += Math.Max(0, taken.Low - (Seconds.IsMatch(s) ? 0 : within.Min()));
                }
                if (Ends(s, segment, out var paused, out var applied))
                {
                    if (paused && !Heats(segment)) stop = ReasonPause;
                    if (applied) stop = ReasonParts;
                    if (stop != null && (side == null || InPan(string.Join(" ", segment))) && returns < 2 && Return(sentences, i + 1, segment, sentence, false, side != null) is { } back)
                    {
                        (stop, returns, i) = (null, returns + 1, back - 1);
                        continue;
                    }
                    break;
                }
            }
            if (stop == null && choice && !Heats(segment)) stop = ReasonChoice;
            return segment;
        }

        /// <summary>
        /// Where the drink's batter or dough goes on after a separate preparation or a rest, when the steps make that clear:
        /// the first later sentence that names it again ("Pour the pancake batter into the skillet"), when its run named a
        /// batter or dough (or mixed in flour), that joins the dry ingredients into it ("Stir these dry ingredients into the wet
        /// mixture"), or that names an "<c>x</c> mixture", "<c>x</c> filling" or the like whose <c>x</c> its run named ("into the egg
        /// mixture"); or, when its run put it into its baking pan, dish, crust or cups, the first later oven sentence
        /// (whatever it calls the dish), provided any part made in between was put over or into the dish first
        /// ("For the topping ... Sprinkle over the cake. Bake for 35 minutes"). When the run has already cooked the dish
        /// (a crust baked before its filling), only that later oven sentence counts, and only after a sentence puts a part
        /// over or into the dish ("Pour the filling over the warm crust"). A run that laid its food in a mould or the like goes
        /// on, past a part that fills that mould ("Make a custard ... Fill up the mould with the custard"), to the sentence
        /// that boils or steams it ("tie it down with a cloth, and boil or steam it for an hour": staging checkpoint 9, U12).
        /// Null when a sentence serves the dish or offers another method first, or nothing is found.
        /// </summary>
        private static int? Return(List<string> sentences, int from, List<string> segment, string sentence, bool heated, bool side)
        {
            var run = string.Join(" ", segment);
            var inPan = InPan(run);
            var named = !heated && !side && OwnPart.IsMatch(run + " " + sentence);
            var mass = OwnPart.Matches(run + " " + sentence).Select(m => m.Value.ToLowerInvariant()).Distinct().ToList();
            if (mass.Count == 0 && !heated && !side && DryHalf.IsMatch(run)) (named, mass) = (true, new List<string> { "batter", "dough" });
            var crossed = heated;
            for (var j = from; j < sentences.Count; j++)
            {
                var s = sentences[j];
                if ((Served.IsMatch(s) && !Heats(s) && !NotServed.IsMatch(s)) || AltMethod.IsMatch(s)) return null;
                if (named && TheMass.Matches(s).Select(m => m.Value.ToLowerInvariant()).Distinct().ToList() is { Count: > 0 } said && said.All(mass.Contains)
                    && !NewComponent.IsMatch(s) && !Heading.IsMatch(s) && !SeparateStep.IsMatch(s)) return j;
                if (!heated && !side && !NewComponent.IsMatch(s) && !Heading.IsMatch(s) && !SeparateStep.IsMatch(s)
                    && ((DryJoin.IsMatch(s) && !DryHalf.IsMatch(run)) || LaterPart.Matches(s).Any(m => !DryWord.IsMatch(m.Groups["w"].Value) && Regex.IsMatch(run + " " + sentence, @"\b" + WordForms(m.Groups["w"].Value) + @"\b", Options)))) return j;
                if (NewComponent.IsMatch(s) || Heading.IsMatch(s) || SeparateStep.IsMatch(s) || NewVessel.IsMatch(s)) crossed = true;
                if (Strewn.IsMatch(s) || OntoDish.IsMatch(s)) crossed = false;
                if (inPan && !crossed && Oven.IsMatch(NotHeating.Replace(s, " ")) && !NotHeatingSentence.IsMatch(s) && !Choice.IsMatch(s)) return j;
                if (inPan && !crossed && !heated && Heats(s) && !Oven.IsMatch(NotHeating.Replace(s, " ")) && (ThisPart.IsMatch(s) || VesselHeated.IsMatch(s)) && !NotHeatingSentence.IsMatch(s) && !Choice.IsMatch(s)) return j;
            }
            return null;
        }

        /// <summary>Whether a sentence ends the certain run: it serves the dish, or sets aside, chills, cools or reserves it (out <paramref name="pause"/>), unless it only rests or chills the same mixture before the dish is cooked.</summary>
        private static bool Ends(string s, List<string> segment, out bool pause) => Ends(s, segment, out pause, out _);

        private static bool Ends(string s, List<string> segment, out bool pause, out bool applied)
        {
            pause = false;
            applied = false;
            if (Served.IsMatch(s) && !Heats(s) && !NotServed.IsMatch(s)) return true;
            if (!Heats(segment) && AppliedCold.IsMatch(s)) return applied = true;
            if (ThenGoesOn.IsMatch(s)) return false;
            if (!PauseStep.IsMatch(s) || NotPause.IsMatch(s)) return false;
            if (!Heats(segment) && SameRest.IsMatch(s) && !SetAside.IsMatch(s) && !Served.IsMatch(s) && !SetFirm.IsMatch(s)) return false;
            pause = true;
            return true;
        }

        /// <summary>Whether a run has put its mixture into or onto its baking pan, dish, crust, cups or pastry.</summary>
        private static bool InPan(string run) => Vessel.IsMatch(run) || PlacedInPan.IsMatch(run) || IntoPan.IsMatch(run);

        private static bool Heats(string s) => Scan(new[] { s }) is { Stove: true } or { Oven: true } or { Flame: true };

        private static bool Heats(IReadOnlyList<string> s) => Scan(s) is { Stove: true } or { Oven: true } or { Flame: true };

        private static bool DryOnly(string s) => DryHalf.IsMatch(s) && !Heats(s) && !WetWords.IsMatch(s) && !PartNoun.IsMatch(s);

        /// <summary>The words that name the side part a drink's sentence makes (its icing, sauce, filling, whipped cream and the like), or null when the drink goes into the dish itself.</summary>
        private static Regex? SideOf(string sentence, string clause)
        {
            var words = SidePart.Matches(sentence).Select(m => PartNoun.Match(m.Value).Value.ToLowerInvariant()).ToList();
            if (WhippedCream.IsMatch(sentence)) words.Add("cream");
            if (words.Count == 0 && !NewComponent.IsMatch(sentence)) return null;
            if (words.Count == 0) words.AddRange(NewComponent.Match(sentence).Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => w.Trim(':', ',').ToLowerInvariant()).Where(w => w.Length > 3).TakeLast(1));
            return new Regex(@"\b(?:" + string.Join('|', words.Distinct().Select(Regex.Escape)) + @")(?:s|es)?\b", Options);
        }

        /// <summary>
        /// Whether the drink's clause starts a new mixture: it begins by mixing, beating or whisking something that is not
        /// what was just heated ("Mix some powdered sugar with a little milk and flavor with vanilla"), so the heat of the
        /// sentence before is not the drink's (staging checkpoint 6, R14).
        /// </summary>
        private static bool NewMixture(string clause) =>
            NewMixtureVerb.Match(clause) is { Success: true } m && !BackReference.IsMatch(m.Groups["o"].Value);

        /// <summary>
        /// Whether the two sentences before the drink's heat a pot and end with that heat, so a drink mixed apart and then
        /// stirred into "the sauce" or "the gravy" goes into a hot liquid ("having stirred over the fire until it is thick ...
        /// Mix a wine-glass of brandy with the sherry, stir it into the sauce").
        /// </summary>
        private static bool StillOnTheHeat(List<string> sentences, int index) =>
            index >= 2 && !PauseStep.IsMatch(sentences[index - 1]) && !Cold.IsMatch(sentences[index - 1]) && !Oven.IsMatch(sentences[index - 1]) && !WorkedCold.IsMatch(sentences[index - 1])
            && Scan(new[] { sentences[index - 1] }) is { Stove: false, Oven: false, Flame: false } && Scan(new[] { sentences[index - 2] }).Stove
            && !NewComponent.IsMatch(sentences[index - 1]) && !SeparateStep.IsMatch(sentences[index - 1]);

        /// <summary>Whether the only heat of the text before the drink melts chocolate, butter or the like in the microwave, which warms and does not boil ("Microwave for 30 seconds, stirring until smooth. Add vanilla").</summary>
        private static bool MeltedOnly(string before, List<string> sentences, int index) =>
            Stove.Matches(before) is { Count: > 0 } heat && heat.All(m => m.Value.StartsWith("microwave", StringComparison.OrdinalIgnoreCase))
            && MeltedFat.IsMatch(string.Join(" ", sentences.Skip(Math.Max(0, index - 3)).Take(Math.Min(3, index)).Append(before)));

        private static bool HeatedBefore(List<string> sentences, int index)
        {
            var before = sentences.Skip(Math.Max(0, index - 2)).Take(Math.Min(2, index)).ToList();
            return before.Count > 0 && Scan(before).Stove && HeatLast(string.Join(" ", before));
        }

        /// <summary>Whether the heat of the text before the drink is its last act: a last clause that beats, pounds, chops or the like works the food cold after it ("Boil the rind tender, beat it fine in a mortar").</summary>
        private static bool HeatLast(string before)
        {
            var last = LastClause.Split(before.Trim().TrimEnd('.', ' ')).LastOrDefault(c => !string.IsNullOrWhiteSpace(c)) ?? string.Empty;
            return !((WorkedCold.IsMatch(last) || ColdAct.IsMatch(last) || PauseStep.IsMatch(last)) && Scan(new[] { last }) is { Stove: false, Oven: false, Flame: false });
        }

        /// <summary>
        /// The part of the method a drink's part goes on to, when the drink's own reading stops before any heat (a filling
        /// set aside, a marinade left while the pan heats): the first later sentence that names the part (an icing,
        /// filling, sauce, marinade, dough, batter or "<c>x</c> mixture" its sentence names, a later "<c>x</c> mixture",
        /// "<c>x</c> filling", "<c>x</c> batter" or the like whose <c>x</c> its reading names, or the sentence before it when the
        /// drink's sentence does not begin a new mixture ("the cooled cocoa mixture", "the cream cheese filling"), what a sentence of its reading chills, freezes or sets
        /// aside ("Place the scones in the freezer"), "chilled" after a chill, the dough or batter of a reading that mixes
        /// flour, or, when its reading marinates, soaks, coats or
        /// tosses, the meat or fish it holds, with its marinade, but not one it is only for) is read from that name on, or
        /// from the sentence's start when only a verb leads to the name ("Grill the chicken") (staging checkpoint 6, R14).
        /// When that reading again stops before any heat, the next sentence that names the part is read on the same way,
        /// and when it stops by chilling (the dough chilled once more) with a bake in the next three sentences, the sentence
        /// right after it is. "The mixture" names
        /// the part only before a separate step or a new part. Null when no later sentence names it, or when the first that does serves it
        /// ("Serve the shrimp with crackers").
        /// </summary>
        private static List<string>? Resume(List<string> sentences, int index, IReadOnlyList<string> read)
        {
            var text = sentences[index] + " " + string.Join(" ", read.Skip(1));
            var proteins = Coated.IsMatch(text) ? Protein.Matches(text).Select(m => m.Value.ToLowerInvariant()).ToList() : new List<string>();
            var names = PartNoun.Matches(text).Select(m => m.Value.ToLowerInvariant())
                .Concat(Mixture.Matches(text).Select(m => m.Value.ToLowerInvariant()))
                .Concat(OwnPart.Matches(text).Select(m => m.Value.ToLowerInvariant()))
                .Concat(DryHalf.IsMatch(text) ? new[] { "dough", "batter" } : Array.Empty<string>())
                .Concat(ChillPause.IsMatch(read[^1]) ? new[] { "chilled" } : Array.Empty<string>())
                .Concat(PauseStep.IsMatch(read[^1]) && !DryHalf.IsMatch(text) ? new[] { "dry ingredients" } : Array.Empty<string>())
                .Concat(read.Any(r => PauseStep.IsMatch(r) && TheMixture.IsMatch(r)) ? new[] { "the mixture" } : Array.Empty<string>())
                .Concat(read.Where(s => PauseStep.IsMatch(s)).SelectMany(s => PauseObject.Matches(s)).Select(m => m.Groups["n"].Value.ToLowerInvariant()))
                .Concat(sentences.Skip(index + read.Count).SelectMany(s => LaterPart.Matches(s)).Where(m => Regex.IsMatch((index > 0 && !NewMixture(sentences[index]) ? sentences[index - 1] + " " : string.Empty) + text, @"\b" + WordForms(m.Groups["w"].Value) + @"\b", Options)).Select(m => m.Value.ToLowerInvariant()))
                .Concat(proteins)
                .Concat(proteins.Count > 0 ? new[] { "marinade", "marinated" } : Array.Empty<string>())
                .Distinct()
                .ToList();
            if (names.Count == 0) return null;
            var name = new Regex(@"\b(?:" + string.Join('|', names.Select(Regex.Escape)) + @")\b", Options);
            var resumed = new List<string>();
            var chilled = false;
            var crossed = false;
            for (var i = index + read.Count; i < sentences.Count; i++)
            {
                crossed |= NewComponent.IsMatch(sentences[i]) || SeparateStep.IsMatch(sentences[i]);
                var m = name.Match(sentences[i]);
                if (!chilled && (!m.Success || (crossed && m.Value.Equals("the mixture", StringComparison.OrdinalIgnoreCase)))) continue;
                var lead = m.Success ? sentences[i][..m.Index] : string.Empty;
                if (Served.IsMatch(lead)) break;
                var start = chilled || !m.Success || LeadVerb.IsMatch(lead) ? 0 : m.Index;
                var segment = Segment(sentences[i][start..], sentences.Skip(i + 1).ToList(), sentences[i], false);
                if (AppliedPart.IsMatch(sentences[i]) && segment.FindIndex(1, s => NewMixture(s)) is var cut and > 0) segment = segment.Take(cut).ToList();
                resumed.AddRange(segment);
                if (Scan(segment) is { Stove: true } or { Oven: true } or { Flame: true }) break;
                chilled = !chilled && ChillPause.IsMatch(segment[^1]) && !Served.IsMatch(segment[^1])
                    && sentences.Skip(i + segment.Count).Take(3).Any(s => Oven.IsMatch(NotHeating.Replace(s, " ")));
                i += segment.Count - 1;
            }
            return resumed.Count == 0 ? null : resumed;
        }

        /// <summary>
        /// The method after a drink's dish is put in its baking vessel and chilled ("Transfer to the prepared dish, cover, and
        /// refrigerate overnight ... Cover the casserole with foil and bake"): the sentences after the chill, read as any
        /// other reading, when the drink's reading ends by chilling a mixture it put into a prepared pan, baking dish or
        /// baking sheet, or one of the next two sentences puts it there ("Slice into discs and place on the prepared baking
        /// sheet"); or, from the sentence that takes the dish out of the refrigerator, when one of the next five does
        /// ("Remove the casserole from the refrigerator"). Null otherwise.
        /// </summary>
        private static List<string>? MadeAhead(List<string> sentences, int index, IReadOnlyList<string> read)
        {
            var next = index + read.Count;
            if (next >= sentences.Count || !ChillPause.IsMatch(read[^1])) return null;
            if (!Vessel.IsMatch(string.Join(" ", read)) && !sentences.Skip(next).Take(2).Any(s => Vessel.IsMatch(s)) && !NextMorning.IsMatch(sentences[next]))
            {
                next = Enumerable.Range(next, Math.Min(5, sentences.Count - next)).FirstOrDefault(i => FromCold.IsMatch(sentences[i]), -1);
                if (next < 0) return null;
            }
            return Segment(sentences[next], sentences.Skip(next + 1).ToList(), sentences[next], false);
        }

        /// <summary>
        /// The bake of a dish the drink's mixture was spread in before a separate topping went over it ("Transfer to the
        /// prepared baking dish ... In a separate bowl, mix the butter, sugar and pecans; spoon this mixture over the
        /// filling. Bake for 30 minutes"): the first later sentence that bakes, read on as any other reading, when the
        /// drink's reading puts its mixture into a prepared pan or baking dish and stops at a separate step, a new part
        /// ("Prepare the topping") or by setting it aside. Null otherwise.
        /// </summary>
        private static List<string>? Assembled(List<string> sentences, int index, IReadOnlyList<string> read)
        {
            var next = index + read.Count;
            if (next >= sentences.Count || !(SeparateStep.IsMatch(sentences[next]) || NewComponent.IsMatch(sentences[next]) || NewVessel.IsMatch(sentences[next]) || PauseStep.IsMatch(read[^1]))
                || !Vessel.IsMatch(string.Join(" ", read))) return null;
            for (var i = next; i < sentences.Count; i++)
            {
                if (Served.IsMatch(sentences[i]) && !Oven.IsMatch(sentences[i])) return null;
                if (Oven.IsMatch(NotHeating.Replace(sentences[i], " "))) return Segment(sentences[i], sentences.Skip(i + 1).ToList(), sentences[i], false);
            }
            return null;
        }

        /// <summary>
        /// Where a sentence that starts a new preparation adds what it makes, in a later clause of its own, back into the
        /// run's mass: into "the bowl", "the mixture", "the batter" or "the dough", or into or with a food the run named
        /// ("Chop the apples and stir them into the bowl of oats"; "Beat the whites ..., and add them ... with the rest of
        /// the flour"), so the run goes on with it from there (staging checkpoint 9, U12: bounds too high where every part is
        /// baked); the heat of the preparation before that clause ("Melt the gelatine in boiling water and stir it into the
        /// cream") is not the drink's. -1 when it adds nothing back.
        /// </summary>
        private static int JoinAt(string sentence, string run) =>
            JoinedHere.Matches(sentence).FirstOrDefault(m => m.Index > 0
                && (m.Groups["w"].Value.ToLowerInvariant() is "bowl" or "mixture" or "batter" or "dough"
                    || Regex.IsMatch(run, @"\b" + WordForms(m.Groups["w"].Value) + @"\b", Options)))?.Index ?? -1;

        private static string WordForms(string word) =>
            "(?:" + Regex.Escape(word) + "(?:s|es)?" + (word.EndsWith('y') ? "|" + Regex.Escape(word[..^1]) + "ies" : string.Empty) + ")";

        private static bool JoinsBack(string next, IEnumerable<string> drinkPart)
        {
            var text = string.Join(" ", drinkPart);
            return WetHalf.IsMatch(next) || IntoTheMixture.IsMatch(next) || JoinMixture.Matches(next).Any(m => Regex.IsMatch(text, @"\b" + Regex.Escape(m.Groups["w"].Value), Options));
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
        private static HeatScan Scan(IReadOnlyList<string> sentences) => Scan(sentences, false);

        /// <summary><see cref="Scan(IReadOnlyList{string})"/>; with <paramref name="lowEnd"/> every range counts at its lower end, as the certain reading needs.</summary>
        private static HeatScan Scan(IReadOnlyList<string> sentences, bool lowEnd)
        {
            bool stove = false, oven = false, flame = false, timedStove = false, heatedBefore = false, stoveBefore = false, untimed = false;
            var minutes = 0;
            var lastTimed = 0;
            var altPending = false;
            for (var i = 0; i < sentences.Count; i++)
            {
                var text = NoHeatChoice.IsMatch(sentences[i]) || NotHeatingSentence.IsMatch(sentences[i]) || CoolsInOven.IsMatch(sentences[i]) ? " " : NotHeating.Replace(sentences[i], " ");
                var gas = GasFlame.IsMatch(text);
                var withoutGas = GasFlame.Replace(text, " ");
                var going = heatedBefore && Continued.IsMatch(withoutGas);
                var altBefore = altPending;
                altPending = AltMethod.IsMatch(sentences[i]) && TimesIn(sentences[i]) == null;
                var o = Oven.IsMatch(withoutGas) || (heatedBefore && !stoveBefore && !gas && !Stove.IsMatch(withoutGas) && (GenericHeat.IsMatch(withoutGas) || going));
                var s = gas || Stove.IsMatch(withoutGas) || (!o && (GenericHeat.IsMatch(withoutGas) || going)) || SauceMade.IsMatch(withoutGas) || (i > 0 && HotThrough.IsMatch(withoutGas));
                var f = Flame.IsMatch(withoutGas);
                stove |= s;
                oven |= o;
                flame |= f;
                if ((s || o) && HeatedTimes(text) is { } t && t.Low > 0)
                {
                    if ((AltMethod.IsMatch(text) || Alternative.IsMatch(text) || altBefore) && lastTimed > 0)
                    {
                        minutes -= lastTimed - Math.Min(lastTimed, t.Low);
                        lastTimed = Math.Min(lastTimed, t.Low);
                        heatedBefore = true;
                        stoveBefore = s;
                        continue;
                    }
                    if (Conditional.IsMatch(text) && minutes > 0)
                    {
                        heatedBefore = true;
                        stoveBefore = s;
                        continue;
                    }
                    lastTimed = untimed && !lowEnd && t.High is { } high ? high : t.Low;
                    minutes += lastTimed;
                    if (s) timedStove = true;
                }
                else if (!s && !o && heatedBefore && TimeOnly.IsMatch(text) && !NotCooking.IsMatch(text) && TimesIn(text) is { } only && only.Low > 0)
                {
                    if (Alternative.IsMatch(text) && lastTimed > 0)
                    {
                        minutes -= lastTimed - Math.Min(lastTimed, only.Low);
                        lastTimed = Math.Min(lastTimed, only.Low);
                        continue;
                    }
                    minutes += only.Low;
                    lastTimed = only.Low;
                    if (stoveBefore) timedStove = true;
                    continue;
                }
                else
                {
                    untimed |= s;
                    if ((s || o) && !altPending) lastTimed = 0;
                }
                heatedBefore = s || o;
                stoveBefore = s;
            }
            return new HeatScan(stove, oven, flame, minutes, minutes > 0 && !timedStove);
        }

        /// <summary>
        /// The cooking time of a heating sentence: the first time it gives, and the time of each later clause after "then" that
        /// heats again ("Bake for 10 minutes at 220°C, then turn the oven down to 180°C and bake 30 minutes more"),
        /// added up; null when it gives none. A later clause that only names a cooked food or the heat ("let the roast rest",
        /// "the baked cake", "from the heat"), or that lets the dish rest, stand, sit or cool, is not more cooking.
        /// </summary>
        private static (int Low, int? High)? HeatedTimes(string sentence)
        {
            var clauses = ThenClause.Split(sentence);
            if (TimesIn(sentence) is not { } first) return null;
            int low = 0, high = 0;
            bool counted = false, range = false;
            for (var i = 0; i < clauses.Length; i++)
            {
                var verbs = HeatNoun.Replace(clauses[i], " ");
                var heats = i == 0 || (!RestClause.IsMatch(clauses[i]) && (Oven.IsMatch(verbs) || Stove.IsMatch(verbs) || GenericHeat.IsMatch(verbs)));
                if (!heats || TimesIn(clauses[i]) is not { } t) continue;
                low += t.Low;
                high += t.High ?? t.Low;
                range |= t.High != null;
                counted = true;
            }
            return counted ? (low, range ? high : null) : first;
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
            for (var other = m.NextMatch(); other.Success; other = other.NextMatch())
            {
                if (!OrBetween.IsMatch(text[(m.Index + m.Length)..other.Index])) break;
                if (Minutes(other) < Minutes(m)) m = other;
            }
            var amount = NumberOf(m.Groups["a"].Value);
            if (amount == null) return null;
            var hours = m.Groups["u"].Value.StartsWith("h", StringComparison.OrdinalIgnoreCase);
            var total = hours ? amount.Value * 60m : amount.Value;
            if (hours && MoreMinutes.Match(text[(m.Index + m.Length)..]) is { Success: true } more && NumberOf(more.Groups["a"].Value) is { } extra)
                total += extra;
            int? high = m.Groups["b"].Success && NumberOf(m.Groups["b"].Value) is { } upper && upper > amount.Value ? (int)Math.Floor(hours ? upper * 60m : upper) : null;
            return ((int)Math.Floor(total), high);
        }

        /// <summary>Every time a sentence gives, in whole minutes, each range at its lower end.</summary>
        private static List<int> AllMinutes(string sentence) =>
            Duration.Matches(Normalize(sentence)).Select(m => (int)Math.Floor(Minutes(m))).Where(m => m > 0).ToList();

        private static decimal Minutes(Match duration) =>
            (NumberOf(duration.Groups["a"].Value) ?? 0m) * (duration.Groups["u"].Value.StartsWith("h", StringComparison.OrdinalIgnoreCase) ? 60m : 1m);

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
            if (Ratio.Match(text) is { Success: true, Index: > 0 } ratio) text = text[..ratio.Index];
            var words = new List<string>();
            var lineWords = LineWord.Matches(text).Select(w => w.Value).ToList();
            var bare = false;
            foreach (Match m in AlcoholLineContext.Trigger.Matches(text))
            {
                var word = m.Value.ToLowerInvariant();
                if (DrinkGeneric.IsMatch(word))
                {
                    bare |= word.StartsWith("extract", StringComparison.Ordinal);
                    var at = lineWords.FindIndex(w => w.Equals(m.Value, StringComparison.OrdinalIgnoreCase));
                    if (at > 0 && lineWords[at - 1].Length > 2) words.Add(lineWords[at - 1].ToLowerInvariant());
                    if (word.StartsWith("spirit", StringComparison.Ordinal) || word.StartsWith("liquor", StringComparison.Ordinal) || word.StartsWith("alcohol", StringComparison.Ordinal)) words.Add(word);
                    continue;
                }
                words.Add(word.EndsWith("s", StringComparison.Ordinal) && word.Length > 4 ? word[..^1] : word);
            }
            if (words.Count == 0 && lineWords.FirstOrDefault(w => w.Length > 2 && !NotMention.IsMatch(w)) is { } head) words.Add(head.ToLowerInvariant());
            if (words.Count == 0) return null;
            return new Regex(@"\b(?:" + string.Join('|', words.Distinct().Select(Regex.Escape)) + @")(?:s|es)?\b(?!\s+(?:beans?(?!\s+paste)|pods?|batters?|frostings?|icings?|glaze|ice\s+cream|cakes?|wafers?|puddings?|sugar|layers?|yogh?urt|cookies?|custard|sauce|buttercream|whipped|cupcakes?|glass|glasses|glassful|glassfuls)\b)" + (bare ? "|" + BareExtract : string.Empty), Options);
        }

        private static string Normalize(string text)
        {
            var s = text.Replace('’', '\'').Replace('–', '-').Replace('—', '-')
                .Replace("½", " 1/2").Replace("¼", " 1/4").Replace("¾", " 3/4").Replace("⅓", " 1/3").Replace("⅔", " 2/3");
            s = Regex.Replace(s, @"(\d+)\s+(\d/\d)", "$1 $2");
            s = Regex.Replace(s, @"(\d)-(?=\d/)", "$1 ");
            s = Regex.Replace(s, @"(?<=\p{L})-(?=\p{L})", " ");
            foreach (var (pattern, replacement) in Phrases) s = pattern.Replace(s, replacement);
            s = HalfHourRange.Replace(s, m => string.Create(CultureInfo.InvariantCulture, $"{int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * 60} to {int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) * 60 + 30} minutes"));
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
            foreach (var regex in new[] { SentenceSplit, NotHeating, OffHeat, GasFlame, TimeOnly, NotCooking, Served, NewComponent, HotInto, HotThrough, SauceMade, NewMixtureVerb, BackReference, WorkedCold, LastClause, PartNoun, Protein, Coated, LeadVerb, Stove, GenericHeat, Oven, Flame, OvernightWords, Duration, MoreMinutes, SeparateStep, PauseStep, Frozen, Cold, Storage, OwnPart, PauseObject, HotJoin, Alternative, WetHalf, ChillPause, Vessel, Ratio, TheMixture, ThenClause, HeatNoun, RestClause, NoHeatChoice, Sealed, SetAsideRest, Continued, BareFlavour, FlavourNoun, JoinMixture, LaterPart, OptionalPart, ShortOvernight, ColdAct, ForLater, OrBetween, AltMethod, CoolsInOven, NextMorning, AndThenCold, PlacedInPan, NewVessel, IntoTheMixture, NotHeatingSentence, NotPause, Conditional, FromCold, MeltedFat, UsedInstead, AppliedPart, HalfHourRange, Held, HeldList, PriorHeat, AddedAgain, ItsMixture, ClauseVerb, DryHalf, Mixture, Soak, ClauseBreak, AddVerb, DrinkGeneric, GenericMention, MixAll, DryIngredients, NotMention, EveryBefore, Marinade, Choice, PanVessel, DryJoin, HeatObject, DishNoun, ThisPart, NotServed, SameRest, SetAside, WetWords, WhippedCream, NewPrep, AppliedCold, OwnMass, MixOn, Strewn, SetFirm, Heading, IntoPan, TheMass, OntoDish, LineAmount, SidePart, ChoiceLead, ChoiceTime, OrMethod, Seconds, SplitUse, Portion, AddedTo, OverCrust, ThenGoesOn, WarmOnly, VesselFirst, HeatObjectAny, DryWord, LiquorWord, JoinedHere, LaterUse, AltPurpose, VesselHeated, Sufficient, AddedTime, SameHeat })
                text.Append('\n').Append(regex);
            text.Append('\n').Append(BareExtract);
            foreach (var (pattern, replacement) in Phrases) text.Append('\n').Append(pattern).Append('=').Append(replacement);
            foreach (var row in Rows) text.Append('\n').Append(row.Code).Append('=').Append(row.Percent.ToString(CultureInfo.InvariantCulture)).Append(':').Append(row.Minutes?.ToString(CultureInfo.InvariantCulture));
            text.Append("\nreasons:").Append(string.Join(',', Reasons));
            text.Append("\ntrigger:").Append(AlcoholLineContext.Trigger);
            text.Append("\nfamilies:").Append(AlcoholLineContext.Signature);
            return text.ToString();
        }
    }
}
