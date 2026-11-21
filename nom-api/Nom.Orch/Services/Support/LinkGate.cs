using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Nom.Data.Nutrition;
using Nom.Data.Recipe;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// What the link gate knows about a food a wording might be linked to: its catalog name, its
    /// USDA description, data type, curation status, USDA category, admission reason, nutrient row
    /// count, whether it holds the core nutrients and its FDC id. A fact left null is not checked.
    /// </summary>
    public sealed record GateFood(
        long Id,
        string Name,
        string? Description = null,
        string? DataType = null,
        long? Status = null,
        int? CategoryId = null,
        string? AdmissionReason = null,
        int? NutrientRows = null,
        bool? CoreNutrients = null,
        bool Deleted = false,
        string? FdcId = null)
    {
        public string Text => string.IsNullOrWhiteSpace(Description) ? Name : Description;
    }

    /// <summary>
    /// One recipe line of the wording as the units and alcohol checks read it: its raw text, its
    /// unit, its grams through the food's conversions (null when none), when the caller has it what
    /// the rest of its recipe says, and, for a line two blind readers gave one of the fixed readings
    /// of an unclear alcohol word, the alcohol sense that reading stands for, which the alcohol rule
    /// then reads in place of the line's own.
    /// </summary>
    public sealed record GateLine(string? Raw, string? Unit, decimal? Grams, AlcoholRecipeContext? Recipe = null, string? Sense = null);

    /// <summary>
    /// One candidate answer the gate judges: the wording, the ingredient it is on, the food it
    /// would be linked to, how the answer was reached (a name match, a single model pick, two blind
    /// readers that agreed) and, when the caller has them, the wording's lines.
    /// </summary>
    public sealed record GatePair(
        string Wording,
        long SourceId,
        GateFood Target,
        bool NameMatch = false,
        bool ModelPick = false,
        bool ReadersAgreed = false,
        IReadOnlyList<GateLine>? Lines = null);

    /// <summary>One rule that holds a pair: its stable code, the rule as a reason reads it, and the detail.</summary>
    public sealed record GateHold(string Code, string Rule, string? Detail = null)
    {
        public override string ToString() => Detail == null ? Rule : $"{Rule} ({Detail})";
    }

    /// <summary>
    /// The family check's verdict on one answer: the folded key its wording shares with others,
    /// and the food the family agrees on (null when two foods tie and neither may be written).
    /// </summary>
    public sealed record GateFamilyVerdict(string Wording, long TargetId, string Key, long? Winner)
    {
        public bool Keeps => Winner == TargetId;
    }

    /// <summary>What the gate reads about the catalog, built once per pass.</summary>
    public sealed class GateCatalog
    {
        /// <summary>Curated foods with nutrition, by name, as <see cref="LineRelinkService.TargetFor"/> reads them.</summary>
        public IReadOnlyDictionary<string, long> Curated { get; init; } = new Dictionary<string, long>();
        public IReadOnlyDictionary<long, string> CuratedNames { get; init; } = new Dictionary<long, string>();
        /// <summary>The foods that share each lead word (the description's first word after a class word such as "Spices,").</summary>
        public IReadOnlyDictionary<string, IReadOnlyList<GateFood>> Siblings { get; init; } = new Dictionary<string, IReadOnlyList<GateFood>>();
        /// <summary>How many foods carry each word in the lead of their description.</summary>
        public IReadOnlyDictionary<string, int> LeadWords { get; init; } = new Dictionary<string, int>();
        /// <summary>Foods trusted aliases point at, by wording key.</summary>
        public IReadOnlyDictionary<string, long> TrustedAliases { get; init; } = new Dictionary<string, long>();
        /// <summary>The data-given default food of a bare head word, beside the staple names.</summary>
        public IReadOnlyDictionary<string, long> HeadDefaults { get; init; } = new Dictionary<string, long>();
        /// <summary>Synonyms a pair with no shared word may rest on: term to what it means.</summary>
        public IReadOnlyDictionary<string, string> Lexicon { get; init; } = new Dictionary<string, string>();
        /// <summary>The 99th percentile of grams per line on curated lines, by USDA category.</summary>
        public IReadOnlyDictionary<int, decimal> GramBounds { get; init; } = new Dictionary<int, decimal>();
        public FoodNameMatcher? Matcher { get; init; }
        /// <summary>The USDA entry of each staple name, as <see cref="IngredientLinkService.StapleTargets"/> builds it.</summary>
        public IReadOnlyDictionary<string, FoodCandidate> Staples { get; init; } = new Dictionary<string, FoodCandidate>();

        /// <summary>
        /// A catalog from the foods a pass can link to: siblings and lead words from every live,
        /// non-branded food, and the curated name map from curated foods with nutrition other than
        /// prepared foods, which are never a line's food (first id wins).
        /// </summary>
        public static GateCatalog Build(
            IEnumerable<GateFood> foods,
            FoodNameMatcher? matcher = null,
            IReadOnlyDictionary<string, long>? trustedAliases = null,
            IReadOnlyDictionary<string, long>? headDefaults = null,
            IReadOnlyDictionary<string, string>? lexicon = null,
            IReadOnlyDictionary<int, decimal>? gramBounds = null,
            IReadOnlyDictionary<string, FoodCandidate>? staples = null)
        {
            var live = foods.Where(f => !f.Deleted && f.DataType != CatalogAdmissionRule.BrandedFood).OrderBy(f => f.Id).ToList();
            var siblings = live
                .Select(f => (Lead: LinkGate.LeadWord(f.Text), Food: f))
                .Where(x => x.Lead != null)
                .GroupBy(x => x.Lead!, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<GateFood>)g.Select(x => x.Food).ToList(), StringComparer.Ordinal);
            var curated = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in live.Where(f => f.Status == (long)CurationStatusEnum.Curated && (f.NutrientRows ?? 1) > 0 && !PreparedFoods.IsPrepared(f.CategoryId))) curated.TryAdd(f.Name.Trim(), f.Id);
            return new GateCatalog
            {
                Curated = curated,
                CuratedNames = curated.ToDictionary(kv => kv.Value, kv => kv.Key),
                Siblings = siblings,
                LeadWords = live
                    .SelectMany(f => LinkGate.LeadNounWords(f.Text).Distinct())
                    .GroupBy(w => w, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal),
                TrustedAliases = trustedAliases ?? new Dictionary<string, long>(),
                HeadDefaults = headDefaults ?? new Dictionary<string, long>(),
                Lexicon = lexicon ?? new Dictionary<string, string>(),
                GramBounds = gramBounds ?? new Dictionary<int, decimal>(),
                Matcher = matcher,
                Staples = staples ?? new Dictionary<string, FoodCandidate>(),
            };
        }
    }

    /// <summary>
    /// The one deterministic gate every link answer passes before it is written, whatever produced
    /// it: a name match, a staple, a verified wording, a trusted alias, a model pick or two blind
    /// readers. It holds a pair and says why; it never chooses a food. The rules come from the
    /// classes of wrong links found in the judged pairs (links design B3) and the wording pair
    /// checks (wordings design 2.4): a heading or a compound; a target that is retired, branded, a
    /// prepared dish, not curated or short of nutrients; a wording that is another food's name or
    /// alias, or that a name rule gives to another food; a target led by another noun; a wording
    /// word the food lacks; a food made from the wording's head noun ("pickle cucumbers" is not a
    /// pickle); a food whose own name ends in a variety word the wording lacks (chorizo for "pork
    /// sausage"); a state, form or part one side has and the other lacks (sprouted, roast before the
    /// food, green pods against mature seeds); a dried herb for
    /// a fresh one; a marked variant, a percent variant or a salted/sweetened polarity; one generic
    /// word for one of many varieties; a group word the target's USDA category contradicts; a line
    /// whose alcohol sense (<see cref="AlcoholLineContext"/>) refuses the food; and, with lines,
    /// units the food cannot weigh or amounts past its group's bound. <see cref="Family"/>
    /// makes wordings that fold to one key agree on one food. See docs/architecture/link-gate.md.
    /// </summary>
    public static class LinkGate
    {
        public const string Version = "link-gate/7";

        public const string Heading = "heading";
        public const string Compound = "compound";
        public const string Retired = "retired";
        public const string Branded = "branded";
        public const string Prepared = "prepared";
        public const string Uncurated = "uncurated";
        public const string Incomplete = "incomplete";
        public const string OtherFood = "other-food";
        public const string OtherAlias = "other-alias";
        public const string State = "state";
        public const string WordOrder = "word-order";
        public const string RuleOther = "rule-other";
        public const string LeadNoun = "lead-noun";
        public const string Containment = "containment";
        public const string HeadNoun = "head-noun";
        public const string HeadLead = "head-lead";
        public const string Variety = "variety";
        public const string Form = "form";
        public const string FreshHerb = "fresh-herb";
        public const string Part = "part";
        public const string Marked = "marked";
        public const string Percent = "percent";
        public const string Polarity = "polarity";
        public const string Generic = "generic";
        public const string Group = "group";
        public const string Shortlist = "shortlist";
        public const string Units = "units";
        public const string Grams = "grams";
        public const string AlcoholDrink = "alcohol-drink";
        public const string AlcoholNoFood = "alcohol-no-food";
        public const string AlcoholNotDrink = "alcohol-not-drink";

        public const int ShortlistSize = 12;
        public const int MinimumNutrientRows = 20;
        public const int SiblingNutrientSlack = 5;
        public const double UnitCoverage = 0.90;
        public const double LiquidOnDryShare = 0.05;
        public const double OverBoundShare = 0.02;

        private static readonly Regex Word = new(@"\p{L}+", RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex Parenthetical = new(@"\([^)]*\)", RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex Slash = new(@"\p{L}\s*/\s*\p{L}", RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex PercentPattern = new(@"\d+(?:\.\d+)?\s*%", RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex HeadingPattern = new(@"^\s*for\s+the\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex SaltAddedPattern = new(@"\bwith salt added\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex PodBeanPattern = new(@"\b(?:french|string|stringless|snap|green|runner|wax)\s+beans?\b|\bharicots?\s+verts?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        internal static readonly Dictionary<string, string> OldSpellings = new(StringComparer.Ordinal)
        {
            ["cocoanut"] = "coconut", ["shalot"] = "shallot", ["eschalot"] = "shallot", ["currans"] = "currant", ["apricock"] = "apricot",
            ["apricocks"] = "apricot", ["carraway"] = "caraway", ["paprica"] = "paprika", ["tumeric"] = "turmeric", ["cinamon"] = "cinnamon",
            ["flower"] = "flour", ["gelatine"] = "gelatin", ["catsup"] = "ketchup", ["maccaroni"] = "macaroni", ["chesnut"] = "chestnut",
            ["rind"] = "peel", ["zest"] = "peel",
        };

        internal static readonly Dictionary<string, string[]> States = new(StringComparer.Ordinal)
        {
            ["dried"] = new[] { "dried", "dry", "dehydrated" },
            ["dehydrated"] = new[] { "dried", "dry", "dehydrated" },
            ["canned"] = new[] { "canned", "can", "cans", "tinned", "tin", "jarred" },
            ["cooked"] = new[] { "cooked", "boiled", "baked", "roasted", "fried", "steamed", "braised", "stewed", "grilled", "leftover" },
            ["boiled"] = new[] { "cooked", "boiled" },
            ["frozen"] = new[] { "frozen" },
            ["ground"] = new[] { "ground", "minced" },
            ["powder"] = new[] { "powder", "powdered", "confectioners", "confectioner's", "icing" },
            ["powdered"] = new[] { "powder", "powdered", "confectioners", "confectioner's", "icing" },
            ["sweetened"] = new[] { "sweetened" },
            ["cured"] = new[] { "cured", "corned" },
            ["roasted"] = new[] { "roasted", "toasted", "roast" },
            ["smoked"] = new[] { "smoked" },
            ["pickled"] = new[] { "pickled", "pickle", "pickles" },
            ["dry"] = new[] { "dried", "dry", "dehydrated" },
            ["sprouted"] = new[] { "sprouted", "sprouts", "sprout" },
        };

        private static readonly string[] CookedWords = { "cooked", "boiled", "baked", "fried", "steamed", "braised", "stewed", "grilled", "broiled", "roasted", "poached", "microwaved", "simmered", "scrambled" };

        internal static readonly Dictionary<string, string[]> Forms = new(StringComparer.Ordinal)
        {
            ["dried"] = new[] { "dried", "dry", "dehydrated", "desiccated", "powder", "powdered", "mature" },
            ["dry"] = new[] { "dried", "dry", "dehydrated", "desiccated", "powder", "powdered", "mature" },
            ["dehydrated"] = new[] { "dried", "dry", "dehydrated", "desiccated", "powder", "powdered", "mature" },
            ["canned"] = new[] { "canned", "can", "tinned", "jarred", "bottled" },
            ["can"] = new[] { "canned", "can", "tinned", "jarred", "bottled" },
            ["cans"] = new[] { "canned", "can", "tinned", "jarred", "bottled" },
            ["tinned"] = new[] { "canned", "can", "tinned", "jarred", "bottled" },
            ["cooked"] = CookedWords,
            ["boiled"] = CookedWords,
            ["stewed"] = CookedWords,
            ["baked"] = CookedWords,
            ["fried"] = CookedWords,
            ["steamed"] = CookedWords,
            ["braised"] = CookedWords,
            ["grilled"] = CookedWords,
            ["broiled"] = CookedWords,
            ["poached"] = CookedWords,
            ["roasted"] = new[] { "roasted", "toasted", "browned", "roast" },
            ["toasted"] = new[] { "roasted", "toasted", "browned", "roast" },
            ["browned"] = new[] { "roasted", "toasted", "browned", "roast" },
            ["frozen"] = new[] { "frozen" },
            ["smoked"] = new[] { "smoked" },
            ["cured"] = new[] { "cured", "smoked", "corned" },
            ["pickled"] = new[] { "pickled", "pickle", "pickles" },
            ["ground"] = new[] { "ground", "powder", "powdered", "crushed", "flour", "meal" },
            ["powdered"] = new[] { "ground", "powder", "powdered", "crushed" },
            ["pulverized"] = new[] { "ground", "powder", "powdered", "crushed" },
            ["pounded"] = new[] { "ground", "powder", "powdered", "crushed" },
            ["powder"] = new[] { "ground", "powder", "powdered" },
            ["salted"] = new[] { "salted", "salt" },
        };

        private static readonly HashSet<string> FreshWords = new(StringComparer.Ordinal) { "fresh", "raw" };

        private static readonly HashSet<string> Processed = new(StringComparer.Ordinal)
        {
            "dried", "dry", "dehydrated", "desiccated", "canned", "cooked", "boiled", "baked", "fried", "steamed", "braised", "stewed", "grilled",
            "broiled", "roasted", "toasted", "poached", "frozen", "powder", "powdered", "smoked", "cured", "pickled",
        };

        private static readonly HashSet<string> SpiceNeutral = new(StringComparer.Ordinal) { "dried", "dry", "dehydrated", "ground", "powder", "powdered", "pulverized", "pounded" };
        private static readonly HashSet<string> FlourNeutral = new(StringComparer.Ordinal) { "browned", "dried", "dry", "roasted", "toasted" };
        private static readonly HashSet<string> DrinkNeutral = new(StringComparer.Ordinal) { "dry" };

        private static readonly HashSet<string> SeedWords = new(StringComparer.Ordinal) { "dried", "dry", "dehydrated", "mature" };

        private const string RoastWord = "roast";

        private static readonly HashSet<string> ProductLeads = new(StringComparer.Ordinal)
        {
            "pickle", "sauce", "soup", "pie", "jam", "jelly", "relish", "pudding", "salad", "dressing", "gravy", "stew",
        };

        private static readonly HashSet<string> DriedCues = new(StringComparer.Ordinal) { "dried", "dry", "ground", "grated", "powdered", "powder" };

        private static readonly HashSet<string> EggWhiteWords = new(StringComparer.Ordinal) { "white", "whites" };

        private const string OnlyWord = "only";

        private static readonly string[] UnsaltedPhrases = { "unsalted", "without salt", "no salt", "salt free" };
        private static readonly string[] SaltedWordingPhrases = { "salted" };
        private static readonly string[] SaltedFoodPhrases = { "salted", "with salt" };
        private static readonly string[] UnsweetenedWordingPhrases = { "unsweetened", "sugar free", "no sugar" };
        private static readonly string[] UnsweetenedFoodPhrases = { "unsweetened", "sugar free", "no sugar added" };
        private static readonly string[] SweetenedPhrases = { "sweetened" };

        private static readonly HashSet<string> FreshCues = new(StringComparer.Ordinal)
        {
            "fresh", "freshly", "sprig", "sprigs", "handful", "handfuls", "bunch", "bunches", "stalk", "stalks", "leaves", "chopped", "minced",
        };

        private static readonly Dictionary<string, string> WordingParts = new(StringComparer.Ordinal)
        {
            ["skin"] = "skin", ["skins"] = "skin", ["peel"] = "peel", ["peels"] = "peel", ["rind"] = "peel", ["rinds"] = "peel", ["zest"] = "peel",
            ["juice"] = "juice", ["juices"] = "juice", ["seeds"] = "seed", ["leaves"] = "leaf", ["kernels"] = "kernel", ["greens"] = "greens",
            ["bran"] = "bran", ["germ"] = "germ", ["roe"] = "roe", ["yolk"] = "yolk", ["yolks"] = "yolk",
        };

        private static readonly Dictionary<string, string> DescriptionParts = new(StringComparer.Ordinal)
        {
            ["skin"] = "skin", ["skins"] = "skin", ["peel"] = "peel", ["rind"] = "peel", ["zest"] = "peel", ["juice"] = "juice",
            ["seed"] = "seed", ["seeds"] = "seed", ["leaf"] = "leaf", ["leaves"] = "leaf", ["kernel"] = "kernel", ["kernels"] = "kernel",
            ["greens"] = "greens", ["bran"] = "bran", ["germ"] = "germ", ["roe"] = "roe", ["yolk"] = "yolk", ["flesh"] = "flesh",
            ["tops"] = "tops", ["stalks"] = "stalks",
        };

        internal static readonly HashSet<string> MarkedWords = new(StringComparer.Ordinal)
        {
            "light", "lite", "imitation", "substitute", "reduced", "low", "lowfat", "nonfat", "skim", "free", "non", "nondairy", "artificial",
            "dietetic", "diet", "decaffeinated", "instant", "mix", "flavored", "restaurant", "pressurized",
        };

        private static readonly HashSet<string> Filler = new(StringComparer.Ordinal)
        {
            "nice", "good", "best", "fine", "remains", "quality", "pure", "plain", "organic", "extra", "virgin", "little", "some", "few", "nearly",
            "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "twelve", "half", "halves", "quarter", "quarters",
            "dozen", "pound", "pounds", "pinch", "dash", "cupful", "cupfuls", "spoonful", "spoonfuls", "teaspoonful", "tablespoonful",
            "glassful", "wineglassful", "tb", "tbs", "tbsp", "tsp", "teaspn", "tablespn", "c", "t", "lb", "lbs", "oz", "no", "bunch",
            "bunches", "stalk", "stalks", "clove", "cloves", "head", "heads", "sprigs", "handfuls", "glass", "glasses", "ounces", "cups",
            "teaspoons", "tablespoons", "pints", "quarts",
        };

        private static readonly HashSet<string> GenericNeutral = new(StringComparer.Ordinal)
        {
            "raw", "fresh", "whole", "table", "regular", "plain", "commercial", "all", "purpose", "drinking", "tap", "homemade", "mature", "drained", "solid",
        };

        internal static readonly Dictionary<string, int[]> GroupWords = new(StringComparer.Ordinal)
        {
            ["wine"] = new[] { 14, 28 }, ["beer"] = new[] { 14, 28 }, ["ale"] = new[] { 14, 28 }, ["rum"] = new[] { 14, 28 },
            ["brandy"] = new[] { 14, 28 }, ["whiskey"] = new[] { 14, 28 }, ["whisky"] = new[] { 14, 28 }, ["sherry"] = new[] { 14, 28 },
            ["port"] = new[] { 14, 28 }, ["gin"] = new[] { 14, 28 }, ["vodka"] = new[] { 14, 28 }, ["liqueur"] = new[] { 14, 28 },
            ["champagne"] = new[] { 14, 28 }, ["madeira"] = new[] { 14, 28 }, ["claret"] = new[] { 14, 28 },
            ["cheese"] = new[] { 1 }, ["milk"] = new[] { 1, 14 }, ["cream"] = new[] { 1 }, ["butter"] = new[] { 1, 4 }, ["buttermilk"] = new[] { 1 },
            ["egg"] = new[] { 1 },
            ["flour"] = new[] { 20, 18, 16, 12, 11, 8 }, ["meal"] = new[] { 20, 18, 16, 12, 11, 8 }, ["starch"] = new[] { 20, 18, 16, 12, 11, 8 },
            ["oil"] = new[] { 4, 12 }, ["lard"] = new[] { 4 }, ["dripping"] = new[] { 4 }, ["shortening"] = new[] { 4 }, ["tallow"] = new[] { 4 },
            ["margarine"] = new[] { 4 },
            ["juice"] = new[] { 9, 14, 11 },
            ["sauce"] = new[] { 6, 2, 11, 16, 9, 19, 15, 1 }, ["ketchup"] = new[] { 6, 2, 11 },
            ["vinegar"] = new[] { 2, 6 },
            ["stock"] = new[] { 6, 5 }, ["broth"] = new[] { 6, 5 }, ["bouillon"] = new[] { 6 }, ["consomme"] = new[] { 6 }, ["gravy"] = new[] { 6 },
            ["syrup"] = new[] { 19, 9, 14 }, ["honey"] = new[] { 19 }, ["molasses"] = new[] { 19 }, ["sugar"] = new[] { 19 },
            ["gelatin"] = new[] { 19 },
        };

        private static readonly HashSet<string> GroupCompoundWords = new(StringComparer.Ordinal)
        {
            "peanut", "almond", "cashew", "coconut", "soy", "soya", "rice", "oat", "apple", "cocoa", "ice", "nut", "sunflower", "hazelnut",
            "pistachio", "macadamia", "sesame", "pumpkin", "apricot", "plum", "pear", "shea", "garlic", "herb", "chocolate", "caramel",
            "butterscotch", "fruit", "corn", "maple", "malt", "potato", "tapioca", "arrowroot",
        };

        private static readonly HashSet<string> LiquidUnits = new(StringComparer.Ordinal)
        {
            "gill", "gills", "pint", "pints", "pt", "quart", "quarts", "qt", "gallon", "gallons", "gal", "glass", "glasses", "wineglass",
            "wineglasses", "wineglassful", "tumbler", "tumblers", "ml", "milliliter", "milliliters", "millilitre", "liter", "liters", "litre",
            "litres", "l", "cl", "dl", "fluid",
        };

        private static readonly HashSet<string> LeadClasses = new(StringComparer.Ordinal) { "cheese", "distilled" };

        private static readonly HashSet<string> Connectives = new(StringComparer.Ordinal)
        {
            "and", "or", "with", "without", "in", "of", "from", "made", "includes", "include", "type", "style", "the", "a", "for", "as", "to", "added",
        };


        private static readonly HashSet<string> DryLeads = new(StringComparer.Ordinal) { "spice", "leavening", "flour", "sugar", "gelatin", "starch", "cornstarch" };

        private static readonly HashSet<string> AlcoholicWords = new(StringComparer.Ordinal) { "alcoholic", "wine", "beer", "liqueur", "spirits", "rum", "whiskey", "vodka", "gin", "brandy" };

        private static readonly Dictionary<string, string> AlcoholRules = new(StringComparer.Ordinal)
        {
            [AlcoholDrink] = "a drink line on a food outside its sense",
            [AlcoholNoFood] = "a drink with no USDA food, or one its line cannot tell",
            [AlcoholNotDrink] = "a line that is not a drink on an alcoholic food or a food not of its kind",
        };

        private static readonly Lazy<string> SignatureValue = new(() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(SignatureSource())))[..16].ToLowerInvariant());

        /// <summary>
        /// A hash of <see cref="Version"/> and every table the rules read (the gate's own word lists,
        /// phrases and patterns, the staple names, the prepared categories, the matcher and guard
        /// tables and the alcohol sense rules), so any change to a rule table shows as a new signature: the link-gate scorer
        /// records it and a tier's gate signature drops it back to shadow when it changes. A change
        /// to a rule's code rather than a table raises <see cref="Version"/>.
        /// </summary>
        public static string Signature => SignatureValue.Value;

        /// <summary>The first rule that holds the pair, as a reason, or null to let it through.</summary>
        public static string? Hold(GatePair pair, GateCatalog catalog) => Check(pair, catalog).FirstOrDefault()?.ToString();

        /// <summary>Every rule that holds the pair, in the gate's order; empty when it passes.</summary>
        public static IReadOnlyList<GateHold> Check(GatePair pair, GateCatalog catalog)
        {
            var holds = new List<GateHold>();
            var target = pair.Target;
            var raw = pair.Wording ?? string.Empty;
            var wording = IngredientNameNormalizer.Normalize(raw);
            var text = target.Text;
            var segments = Plain(text).Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            var textWords = Words(text).Select(Fold).ToHashSet(StringComparer.Ordinal);
            var foodWords = Words(target.Name).Select(Fold).Concat(textWords).ToHashSet(StringComparer.Ordinal);
            var foodSequence = Words(target.Name).Concat(Words(text)).Select(Fold).ToList();
            var rawWords = Words(raw).ToHashSet(StringComparer.Ordinal);
            var allWords = Words(wording).ToHashSet(StringComparer.Ordinal);
            allWords.UnionWith(rawWords);
            var folded = allWords.Select(Fold).ToHashSet(StringComparer.Ordinal);
            foreach (var word in allWords.Where(FoodNameMatcher.Synonyms.ContainsKey).ToList())
                folded.UnionWith(FoodNameMatcher.Synonyms[word].Split(' ').Select(Fold));
            var sequence = Words(wording).ToList();
            for (var i = 0; i + 1 < sequence.Count; i++) folded.Add(Fold(sequence[i] + sequence[i + 1]));
            var identity = IdentityWords(wording);
            var lead = LeadWord(text);
            var spice = target.CategoryId == CatalogAdmissionRule.SpicesAndHerbs || (segments.Count > 0 && Words(segments[0]).Select(Fold).Any(w => w == "spice"));
            var flour = textWords.Contains("flour");
            var drink = target.CategoryId is 14 or 28 || textWords.Overlaps(AlcoholicWords) || textWords.Contains("beverage");
            var freshTarget = textWords.Overlaps(FreshWords);
            var driedSpice = spice && !freshTarget && !textWords.Contains("canned")
                && (textWords.Contains("dried") || textWords.Contains("ground") || textWords.Contains("powder") || Words(segments.FirstOrDefault() ?? string.Empty).Select(Fold).Contains("spice"));
            var firstSegmentWords = Words(raw.Split(',')[0]).ToList();
            var plainWords = Words(Plain(text));
            var plainFolded = plainWords.Select(Fold).ToHashSet(StringComparer.Ordinal);

            bool FormMet(string form) =>
                (driedSpice && SpiceNeutral.Contains(form))
                || (flour && FlourNeutral.Contains(form))
                || (drink && DrinkNeutral.Contains(form))
                || Forms[form].Any(f => f == "mature" ? plainFolded.Contains(f) && !plainFolded.Overlaps(CookedWords) && !plainFolded.Contains("canned") : plainFolded.Contains(f) || plainWords.Contains(f));

            var formWords = firstSegmentWords.Select((w, i) => w == RoastWord && i + 1 < firstSegmentWords.Count ? "roasted" : w).ToList();
            var wordingForms = formWords.Where(Forms.ContainsKey).Distinct().ToList();
            var stateWords = allWords.Concat(formWords).ToHashSet(StringComparer.Ordinal);
            var residual = firstSegmentWords.Where(w => !Filler.Contains(w) && !FoodNameMatcher.Noise.Contains(w) && !(Forms.ContainsKey(w) && FormMet(w))
                && !identity.Contains(Fold(w)) && w is not ("of" or "a" or "an" or "the")).ToList();
            var headDefault = IsHeadDefault(wording, residual.Count == 0 ? identity : null, target, catalog) && wordingForms.All(FormMet);

            void Add(string code, string rule, string? detail = null) => holds.Add(new GateHold(code, rule, detail));

            if (HeadingPattern.IsMatch(raw) || raw.TrimEnd().EndsWith(':')) Add(Heading, "a heading, not a food");
            if (LinkAutoApplyGuard.IsCompound(wording) || Slash.IsMatch(wording)) Add(Compound, LinkAutoApplyGuard.CompoundRule);

            if (target.Deleted) Add(Retired, "target retired");
            if (target.DataType == CatalogAdmissionRule.BrandedFood) Add(Branded, "branded target");
            if (PreparedFoods.IsPrepared(target.CategoryId))
                Add(Prepared, "prepared food", CatalogAdmissionRule.PreparedCategories[target.CategoryId!.Value]);
            if (target.Status is { } status && status != (long)CurationStatusEnum.Curated
                && !(status == (long)CurationStatusEnum.PendingCuration && target.AdmissionReason == null
                     && target.DataType is CatalogAdmissionRule.SrLegacyFood or CatalogAdmissionRule.FoundationFood))
                Add(Uncurated, "target not curated", target.AdmissionReason ?? ((CurationStatusEnum)status).ToString());
            if (Incompleteness(target, lead, catalog) is { } incomplete) Add(Incomplete, "nutrients incomplete", incomplete);

            var readings = pair.Lines is { Count: > 0 } senseLines
                ? senseLines.Select(l => l.Sense is { } fixedSense ? new AlcoholReading(fixedSense, AlcoholLineContext.KindOf(fixedSense), "a fixed reading") : AlcoholLineContext.Read(l.Raw ?? raw, l.Recipe))
                : new[] { AlcoholLineContext.Read(raw) };
            foreach (var reading in readings)
            {
                if (AlcoholLineContext.Refuse(reading, target.FdcId, text) is not { } refusal) continue;
                Add(refusal.Code, AlcoholRules[refusal.Code], refusal.Detail);
                break;
            }

            if (LineRelinkService.TargetFor(wording, target.Id, target.Name.Trim(), catalog.Curated) is { } other && other != pair.SourceId)
                Add(OtherFood, LinkAutoApplyGuard.OtherFoodRule, catalog.CuratedNames.TryGetValue(other, out var otherName) ? otherName : "#" + other);
            if (catalog.TrustedAliases.TryGetValue(IngredientNameNormalizer.WordingKey(raw), out var aliasOwner) && aliasOwner != target.Id && aliasOwner != pair.SourceId)
                Add(OtherAlias, "trusted alias of another food", "#" + aliasOwner);

            if (!headDefault)
            {
                foreach (var state in Words(text).Where(States.ContainsKey).Distinct())
                {
                    if (States[state].Any(stateWords.Contains)) continue;
                    if (driedSpice && SpiceNeutral.Contains(state) && allWords.Overlaps(SpiceNeutral)) continue;
                    if (StateIsDefault(state, target, lead, catalog)) continue;
                    Add(State, LinkAutoApplyGuard.StateRule, state);
                    break;
                }
            }

            if (pair.NameMatch && FollowsDescriptionOrder(Words(wording), text)) Add(WordOrder, LinkAutoApplyGuard.WordOrderRule);

            if (catalog.Matcher is { } matcher)
            {
                var name = wording.ToLowerInvariant();
                var named = IngredientLinkService.DeterministicTarget(name, matcher, catalog.Staples)?.Food;
                if (named != null && named.IngredientId != target.Id && named.IngredientId != pair.SourceId)
                    Add(RuleOther, "a name rule gives another food", named.Name);
                if (pair.ModelPick && !pair.ReadersAgreed && named?.IngredientId != target.Id && matcher.Shortlist(name, ShortlistSize).All(c => c.IngredientId != target.Id))
                    Add(Shortlist, "not on the name matcher's shortlist");
            }

            var lexical = !pair.ReadersAgreed;
            var unmet = headDefault || !lexical ? new List<string>() : RequiredLeadWords(text).Where(w => !(spice && w == "seed") && !Meets(w, folded, catalog)).ToList();
            if (unmet.Count > 0) Add(LeadNoun, "food led by a word the wording lacks", string.Join(' ', unmet));

            if (lexical)
            {
                var missing = identity.Where((w, i) => !Contained(identity, i, foodWords, foodSequence, catalog)).ToList();
                if (missing.Count > 0) Add(Containment, "wording word not in the food", string.Join(' ', missing));
            }
            else if (identity.Count > 0 && !Contained(identity, identity.Count - 1, foodWords, foodSequence, catalog))
            {
                Add(HeadNoun, "the wording's head noun is not in the food", identity[^1]);
            }

            if (lexical && !headDefault && identity.Count >= 2 && lead != null && ProductLeads.Contains(lead))
            {
                var head = identity[^1];
                var leadWords = LeadNounWords(text).ToHashSet(StringComparer.Ordinal);
                var leadOnly = new HashSet<string>(StringComparer.Ordinal) { lead };
                if (head != lead && !Meets(head, leadWords, catalog) && identity.Take(identity.Count - 1).Any(w => Meets(w, leadOnly, catalog))
                    && catalog.Siblings.TryGetValue(head, out var own) && own.Any(f => f.Id != target.Id))
                    Add(HeadLead, "a food made from the wording's head noun, which leads foods of its own", $"{lead} of {head}; {head} leads {own.Count} foods");
            }

            if (lexical && !headDefault && !string.Equals(target.Name.Trim(), text.Trim(), StringComparison.OrdinalIgnoreCase)
                && Words(target.Name).Select(Fold).LastOrDefault() is { } nameHead
                && plainFolded.Contains(nameHead) && !LeadNounWords(text).Contains(nameHead) && !Meets(nameHead, folded, catalog)
                && !GenericNeutral.Contains(nameHead) && !States.ContainsKey(nameHead) && !Forms.ContainsKey(nameHead) && !Processed.Contains(nameHead) && !DescriptionParts.ContainsKey(nameHead))
                Add(Variety, "food is a variety the wording does not name", nameHead);

            if (wordingForms.FirstOrDefault(f => !FormMet(f)) is { } unmetForm) Add(Form, "wording form not in the food", unmetForm);
            if (firstSegmentWords.Any(FreshWords.Contains) && !spice)
            {
                var processed = Words(text).FirstOrDefault(Processed.Contains);
                if (processed != null) Add(Form, "wording form not in the food", $"fresh against {processed}");
            }

            if (PodBeanPattern.IsMatch(raw) && plainFolded.Contains("mature") && !allWords.Overlaps(SeedWords))
                Add(Form, "wording form not in the food", "green pods against mature seeds");

            if (driedSpice && (HasFreshCue(raw, textWords) || ShareOf(pair.Lines, l => HasFreshCue(l.Raw ?? string.Empty, textWords)) > LiquidOnDryShare))
                Add(FreshHerb, "a fresh herb is not the dried spice");

            var egg = lead == "egg" || folded.Contains("egg");
            foreach (var part in firstSegmentWords.Where(w => WordingParts.ContainsKey(w) || (egg && EggWhiteWords.Contains(w))).Distinct())
            {
                var kind = WordingParts.TryGetValue(part, out var k) ? k : "white";
                if (TextHasPart(text, kind) || (kind == "leaf" && spice)) continue;
                Add(Part, "wording part not in the food", part);
                break;
            }
            foreach (var segment in segments.Skip(1))
            {
                var words = Words(segment).Where(w => w != OnlyWord).ToList();
                var only = Words(segment).Contains(OnlyWord);
                var isPart = words.Count > 0 && words.All(w => DescriptionParts.ContainsKey(w) || (egg && EggWhiteWords.Contains(w)));
                if (!isPart && !only) continue;
                if (words.Count == 0 || words.Any(w => folded.Contains(Fold(w)) || (DescriptionParts.TryGetValue(w, out var kind) && WordingHasPart(firstSegmentWords, kind)))) continue;
                Add(Part, "food is a part the wording does not name", segment);
                break;
            }

            var marked = Words(text).FirstOrDefault(w => MarkedWords.Contains(w) && !allWords.Contains(w));
            if (marked != null) Add(Marked, "marked variant", marked);
            else if (SaltAddedPattern.IsMatch(text) && !allWords.Contains("salt") && !allWords.Contains("salted"))
                Add(Marked, "marked variant", "with salt added");

            if (lexical && !headDefault && PercentPattern.IsMatch(text) && !raw.Contains('%') && LeadCount(lead, catalog) > 1)
                Add(Percent, "percent variant", PercentPattern.Match(text).Value);
            else if (PercentPattern.Match(raw) is { Success: true } stated && !PercentPattern.Matches(text).Any(m => Number(m.Value) == Number(stated.Value)))
                Add(Percent, "percent variant", "wording states " + stated.Value.Replace(" ", string.Empty));

            if (PolarityOf(raw, text) is { } polarity) Add(Polarity, "salted or sweetened the other way", polarity);

            if (lexical && !headDefault && identity.Count == 1 && LeadCount(identity[0], catalog) is var count and >= 3)
            {
                var explained = wordingForms.Where(FormMet).SelectMany(f => Forms[f]).Concat(driedSpice && wordingForms.Any(SpiceNeutral.Contains) ? SpiceNeutral : Enumerable.Empty<string>())
                    .Select(Fold).ToHashSet(StringComparer.Ordinal);
                var qualifiers = Words(Plain(text)).Select(Fold)
                    .Where(w => !folded.Contains(w) && !explained.Contains(w) && !GenericNeutral.Contains(w) && !FoodNameMatcher.ClassPrefixes.Contains(w) && !LeadClasses.Contains(w)
                        && !Connectives.Contains(w) && !(driedSpice && w == "seed"))
                    .ToList();
                if (qualifiers.Count > 0) Add(Generic, "one generic word for one of many foods", $"{count} foods");
            }

            if (target.CategoryId is { } targetCategory && GroupHead(raw) is { } groupWord && GroupWords.TryGetValue(groupWord, out var groups) && !groups.Contains(targetCategory))
                Add(Group, "group word against the USDA category", $"{groupWord}: category {targetCategory}");

            if (pair.Lines is { Count: > 0 } lines)
            {
                var weighed = lines.Count(l => l.Grams != null) / (double)lines.Count;
                if (weighed < UnitCoverage) Add(Units, "lines the food cannot weigh", string.Create(CultureInfo.InvariantCulture, $"{weighed:P0} weighed"));
                var dry = target.CategoryId == CatalogAdmissionRule.SpicesAndHerbs || LeadNounWords(text).Concat(segments.Count > 0 ? Words(segments[0]).Select(Fold) : Enumerable.Empty<string>()).Any(DryLeads.Contains);
                if (dry && ShareOf(lines, l => IsLiquid(l.Unit)) > LiquidOnDryShare) Add(Units, "liquid measures on a dry food");
                if (target.CategoryId is { } c && catalog.GramBounds.TryGetValue(c, out var bound))
                {
                    var over = lines.Count(l => l.Grams > bound);
                    if (over / (double)lines.Count > OverBoundShare) Add(Grams, "amounts past the group's bound", string.Create(CultureInfo.InvariantCulture, $"{over} of {lines.Count} lines over {bound:0} g"));
                }
            }

            return holds;
        }

        /// <summary>
        /// Wordings that fold to one key (plural, hyphen or space, "X wine" against "X", a unit word
        /// in front) must point at one food. Within a key whose answers name more than one food, the
        /// food with no readiness fault and the most nutrient rows wins and the others are to be
        /// re-pointed to it; when the best two tie, no answer of that key may be written.
        /// </summary>
        public static IReadOnlyList<GateFamilyVerdict> Family(IEnumerable<(string Wording, GateFood Target)> answers, GateCatalog catalog)
        {
            var list = answers.Select(a => (a.Wording, a.Target, Key: FamilyKey(a.Wording))).ToList();
            var winners = new Dictionary<string, long?>(StringComparer.Ordinal);
            foreach (var family in list.GroupBy(a => a.Key, StringComparer.Ordinal))
            {
                var foods = family.Select(a => a.Target).GroupBy(t => t.Id).Select(g => g.First()).ToList();
                if (foods.Count == 1)
                {
                    winners[family.Key] = foods[0].Id;
                    continue;
                }
                var ranked = foods
                    .Select(f => (Food: f, Ready: Incompleteness(f, LeadWord(f.Text), catalog) == null && !f.Deleted, Rows: f.NutrientRows ?? 0))
                    .OrderByDescending(r => r.Ready).ThenByDescending(r => r.Rows)
                    .ToList();
                winners[family.Key] = ranked[0].Ready == ranked[1].Ready && ranked[0].Rows == ranked[1].Rows ? null : ranked[0].Food.Id;
            }
            return list.Select(a => new GateFamilyVerdict(a.Wording, a.Target.Id, a.Key, winners[a.Key])).ToList();
        }

        /// <summary>The key wordings of one family share: the wording's food words, folded, joined with no space, without a trailing "wine" after another word.</summary>
        public static string FamilyKey(string wording)
        {
            var words = Words(IngredientNameNormalizer.Normalize(wording))
                .Where(w => !Filler.Contains(w) && !FoodNameMatcher.Noise.Contains(w))
                .Select(Fold)
                .ToList();
            if (words.Count > 1 && words[^1] == "wine") words.RemoveAt(words.Count - 1);
            return string.Concat(words);
        }

        /// <summary>The food a description leads with: its first word, or the first word after a class word such as "Spices," or "Alcoholic beverage,".</summary>
        public static string? LeadWord(string text) => LeadNounWords(text).FirstOrDefault();

        /// <summary>
        /// True when the wording's first identifying word comes from the description's lead segment
        /// and a later word from one of its qualifying segments, the order USDA writes and a cook
        /// does not ("chocolate cake" reads right for "Cake, chocolate"; "cake chocolate" does not).
        /// </summary>
        internal static bool FollowsDescriptionOrder(IReadOnlyList<string> words, string description)
        {
            var segments = description.Split(',').Select(s => Words(s).Select(FoodNameMatcher.Singular).ToHashSet(StringComparer.Ordinal)).ToList();
            if (segments.Count < 2) return false;
            var lead = segments[0].Count > 0 && segments[0].All(w => FoodNameMatcher.ClassPrefixes.Contains(w) || w == "beverages" || w == "spices") && segments.Count > 2 ? 1 : 0;
            var head = segments[lead];
            var later = segments.Skip(lead + 1).SelectMany(s => s).Where(w => !head.Contains(w)).ToHashSet(StringComparer.Ordinal);
            var singular = words.Select(FoodNameMatcher.Singular).ToList();
            var headAt = singular.FindIndex(head.Contains);
            var laterAt = singular.FindLastIndex(later.Contains);
            return headAt >= 0 && laterAt > headAt;
        }

        private static string Plain(string text) => Parenthetical.Replace(text, " ");

        internal static List<string> Words(string text) =>
            Word.Matches(Unaccented(text.ToLowerInvariant().Replace("'", string.Empty).Replace("’", string.Empty))).Select(m => m.Value).ToList();

        private static string Unaccented(string text)
        {
            if (text.All(c => c < 128)) return text;
            var decomposed = text.Normalize(NormalizationForm.FormD);
            return new string(decomposed.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).Normalize(NormalizationForm.FormC);
        }

        internal static string Fold(string word)
        {
            var w = OldSpellings.TryGetValue(word, out var modern) ? modern : word;
            if (w is "leaves") return "leaf";
            if (w is "halves") return "half";
            if (w is "loaves") return "loaf";
            var singular = FoodNameMatcher.Singular(w);
            return OldSpellings.TryGetValue(singular, out var modernSingular) ? modernSingular : singular;
        }

        internal static List<string> LeadNounWords(string text)
        {
            var (lead, classes) = LeadSegment(text);
            return lead.Concat(classes.Where(w => FoodNameMatcher.IdentityClasses.Contains(w) || LeadClasses.Contains(w))).Where(w => !Connectives.Contains(w)).ToList();
        }

        /// <summary>
        /// The description's lead segment after any segments of class words ("Spices,",
        /// "Alcoholic beverage, distilled,"), and the class words skipped to reach it.
        /// </summary>
        private static (List<string> Lead, List<string> Classes) LeadSegment(string text)
        {
            var segments = Plain(text).Split(',').Select(s => Words(s).Select(Fold).ToList()).Where(s => s.Count > 0).ToList();
            var classes = new List<string>();
            var at = 0;
            while (at < segments.Count - 1 && segments[at].All(w => FoodNameMatcher.ClassPrefixes.Contains(w) || LeadClasses.Contains(w)))
            {
                classes.AddRange(segments[at]);
                at++;
            }
            return (segments.Count == 0 ? new List<string>() : segments[at], classes);
        }

        private static List<string> IdentityWords(string wording)
        {
            var first = wording.Split(',')[0];
            var words = new List<string>();
            foreach (var word in Words(first))
            {
                if (FoodNameMatcher.Synonyms.TryGetValue(word, out var mapped))
                {
                    words.AddRange(mapped.Split(' ').Where(w => !Forms.ContainsKey(w) && w is not ("without" or "salt")));
                    continue;
                }
                if (LinkAutoApplyGuard.Notes.Contains(word) && !WordingParts.ContainsKey(word)) continue;
                if (Filler.Contains(word) || FoodNameMatcher.Noise.Contains(word)) continue;
                if (Forms.ContainsKey(word) || States.ContainsKey(word) || WordingParts.ContainsKey(word) || FreshWords.Contains(word)) continue;
                words.Add(Fold(word));
            }
            return words;
        }

        private static bool Contained(IReadOnlyList<string> identity, int index, HashSet<string> foodWords, IReadOnlyList<string> sequence, GateCatalog catalog)
        {
            var word = identity[index];
            if (Meets(word, foodWords, catalog)) return true;
            if (index + 1 < identity.Count && foodWords.Contains(word + identity[index + 1])) return true;
            if (index > 0 && foodWords.Contains(identity[index - 1] + word)) return true;
            for (var i = 0; i + 1 < sequence.Count; i++)
                if (sequence[i] + sequence[i + 1] == word) return true;
            return false;
        }

        private static bool Meets(string word, IReadOnlySet<string> words, GateCatalog catalog)
        {
            if (words.Contains(word)) return true;
            if (word.Length >= 6 && words.Any(w => w.Length >= 6 && OneEdit(word, w))) return true;
            if (catalog.Lexicon.TryGetValue(word, out var means))
            {
                var meant = Words(means).Select(Fold).ToList();
                if (meant.Count > 0 && meant.All(words.Contains)) return true;
            }
            return false;
        }

        private static bool OneEdit(string a, string b)
        {
            if (a == b) return true;
            if (Math.Abs(a.Length - b.Length) > 1) return false;
            if (a.Length == b.Length)
            {
                var diff = a.Where((c, i) => c != b[i]).Count();
                if (diff == 1) return true;
                if (diff == 2)
                {
                    var at = Enumerable.Range(0, a.Length).Where(i => a[i] != b[i]).ToList();
                    return at[1] == at[0] + 1 && a[at[0]] == b[at[1]] && a[at[1]] == b[at[0]];
                }
                return false;
            }
            var (shorter, longer) = a.Length < b.Length ? (a, b) : (b, a);
            for (var i = 0; i < longer.Length; i++)
                if (longer.Remove(i, 1) == shorter) return true;
            return false;
        }

        /// <summary>
        /// Whether the target is the default food of the wording: the staple entry of the wording,
        /// or, when <paramref name="bare"/> holds the wording's food words with nothing else but
        /// filler and forms the food meets, the staple entry or data-given head default of those words.
        /// </summary>
        private static bool IsHeadDefault(string wording, IReadOnlyList<string>? bare, GateFood target, GateCatalog catalog)
        {
            if (StapleFoods.UsdaNameFor(wording) is { } staple && string.Equals(staple, target.Text, StringComparison.OrdinalIgnoreCase)) return true;
            if (bare == null || bare.Count == 0) return false;
            if (StapleFoods.UsdaNameFor(string.Join(' ', bare)) is { } bareStaple && string.Equals(bareStaple, target.Text, StringComparison.OrdinalIgnoreCase)) return true;
            return bare.Count == 1 && catalog.HeadDefaults.TryGetValue(bare[0], out var id) && id == target.Id;
        }

        private static int SiblingCount(string? lead, GateCatalog catalog) =>
            lead == null ? 0
            : catalog.Siblings.TryGetValue(lead, out var siblings) ? siblings.Count
            : catalog.LeadWords.TryGetValue(lead, out var count) ? count
            : 0;

        private static int LeadCount(string? word, GateCatalog catalog) =>
            word == null ? 0
            : catalog.LeadWords.TryGetValue(word, out var count) ? count
            : catalog.Siblings.TryGetValue(word, out var siblings) ? siblings.Count
            : 0;

        private static bool StateIsDefault(string state, GateFood target, string? lead, GateCatalog catalog)
        {
            if (lead != null && catalog.Siblings.TryGetValue(lead, out var siblings))
                return !siblings.Any(s => s.Id != target.Id && !States[state].Any(Words(s.Text).Contains));
            return SiblingCount(lead, catalog) <= 1;
        }

        private static string? Incompleteness(GateFood target, string? lead, GateCatalog catalog)
        {
            if (target.CoreNutrients == false) return "core nutrients missing";
            if (target.NutrientRows is not { } rows || rows >= MinimumNutrientRows) return null;
            if (lead == null || !catalog.Siblings.TryGetValue(lead, out var siblings)) return null;
            var best = siblings.Where(s => s.Id != target.Id && s.NutrientRows != null && !s.Deleted && s.DataType != CatalogAdmissionRule.BrandedFood)
                .OrderByDescending(s => s.NutrientRows).FirstOrDefault();
            return best != null && best.NutrientRows > rows + SiblingNutrientSlack
                ? $"{rows} rows; #{best.Id} {best.Name} has {best.NutrientRows}"
                : null;
        }

        private static bool TextHasPart(string text, string kind)
        {
            var words = Words(text).Select(Fold).ToHashSet(StringComparer.Ordinal);
            return kind switch
            {
                "peel" => words.Contains("peel") || words.Contains("rind") || words.Contains("zest"),
                "skin" => words.Contains("skin"),
                "leaf" => words.Contains("leaf"),
                "seed" => words.Contains("seed"),
                "kernel" => words.Contains("kernel"),
                "greens" => Words(text).Contains("greens") || words.Contains("leaf"),
                "white" => words.Contains("white"),
                _ => words.Contains(kind),
            };
        }

        private static bool WordingHasPart(IEnumerable<string> words, string kind) =>
            words.Any(w => WordingParts.TryGetValue(w, out var k) && k == kind);

        private static string? PolarityOf(string wording, string text)
        {
            var w = " " + string.Join(' ', Words(wording)) + " ";
            var t = " " + string.Join(' ', Words(text)) + " ";
            static bool Has(string padded, IEnumerable<string> phrases) => phrases.Any(p => padded.Contains(" " + p + " ", StringComparison.Ordinal));
            var wUnsalted = Has(w, UnsaltedPhrases);
            var wSalted = !wUnsalted && Has(w, SaltedWordingPhrases);
            var tUnsalted = Has(t, UnsaltedPhrases);
            var tSalted = !tUnsalted && Has(t, SaltedFoodPhrases);
            if (wUnsalted && tSalted) return "unsalted against salted";
            if (wSalted && tUnsalted) return "salted against unsalted";
            var wUnsweet = Has(w, UnsweetenedWordingPhrases);
            var wSweet = !wUnsweet && Has(w, SweetenedPhrases);
            var tUnsweet = Has(t, UnsweetenedFoodPhrases);
            var tSweet = !tUnsweet && Has(t, SweetenedPhrases);
            if (wUnsweet && tSweet) return "unsweetened against sweetened";
            if (wSweet && tUnsweet) return "sweetened against unsweetened";
            return null;
        }

        private static string? GroupHead(string wording)
        {
            var first = IngredientNameNormalizer.Normalize(wording).Split(',')[0];
            var words = Words(first).Where(w => !LinkAutoApplyGuard.Notes.Contains(w) || w == "of").Select(Fold).ToList();
            var of = words.LastIndexOf("of");
            if (of >= 0) words = words.Skip(of + 1).ToList();
            words.RemoveAll(w => w == "of");
            if (words.Count == 0 || words.Any(GroupCompoundWords.Contains)) return null;
            return GroupWords.ContainsKey(words[^1]) ? words[^1] : null;
        }

        /// <summary>
        /// Whether a wording or line asks for the fresh herb: a fresh cue with no dried, dry or
        /// ground word beside it ("freshly ground pepper" is the spice), and "leaves" only when the
        /// spice is not itself the dried leaf ("bay leaf").
        /// </summary>
        private static bool HasFreshCue(string text, IReadOnlySet<string> target)
        {
            var words = Words(text);
            if (words.Any(DriedCues.Contains)) return false;
            return words.Any(w => FreshCues.Contains(w) && !(w == "leaves" && target.Contains("leaf")));
        }

        private static decimal Number(string percent) =>
            decimal.TryParse(percent.TrimEnd('%', ' '), NumberStyles.Number, CultureInfo.InvariantCulture, out var n) ? n : -1;

        /// <summary>
        /// The words of the description's lead the wording must name: the lead segment after a
        /// class word, with an identity class such as "Oil," or "Vinegar," kept and a plain class
        /// such as "Spices," or "Cheese," left out, less connectives, neutral and state words.
        /// </summary>
        internal static List<string> RequiredLeadWords(string text)
        {
            var (segment, classes) = LeadSegment(text);
            var lead = segment.Concat(classes.Where(FoodNameMatcher.IdentityClasses.Contains)).ToList();
            return lead.Where(w => !Connectives.Contains(w) && !GenericNeutral.Contains(w) && !States.ContainsKey(w) && !Forms.ContainsKey(w) && !LeadClasses.Contains(w)
                && !(FoodNameMatcher.ClassPrefixes.Contains(w) && !FoodNameMatcher.IdentityClasses.Contains(w))).Distinct().ToList();
        }

        private static bool IsLiquid(string? unit)
        {
            if (string.IsNullOrWhiteSpace(unit)) return false;
            var words = Words(unit);
            return words.Any(LiquidUnits.Contains) || (words.Contains("fl") && words.Contains("oz"));
        }

        private static double ShareOf(IReadOnlyList<GateLine>? lines, Func<GateLine, bool> test) =>
            lines is not { Count: > 0 } ? 0 : lines.Count(test) / (double)lines.Count;

        /// <summary>The text <see cref="Signature"/> hashes: <see cref="Version"/> and every table the rules read, each sorted.</summary>
        internal static string SignatureSource()
        {
            var text = new StringBuilder(Version);
            void Table(string name, IEnumerable<string> rows)
            {
                text.Append('\n').Append(name).Append(':');
                foreach (var row in rows.OrderBy(r => r, StringComparer.Ordinal)) text.Append(row).Append('|');
            }
            Table("states", States.Select(kv => kv.Key + "=" + string.Join(',', kv.Value)));
            Table("forms", Forms.Select(kv => kv.Key + "=" + string.Join(',', kv.Value)));
            Table("marked", MarkedWords);
            Table("filler", Filler);
            Table("neutral", GenericNeutral);
            Table("groups", GroupWords.Select(kv => kv.Key + "=" + string.Join(',', kv.Value)));
            Table("group-compounds", GroupCompoundWords);
            Table("cues", FreshCues);
            Table("parts", WordingParts.Select(kv => kv.Key + "=" + kv.Value).Concat(DescriptionParts.Select(kv => "d:" + kv.Key + "=" + kv.Value)));
            Table("spellings", OldSpellings.Select(kv => kv.Key + "=" + kv.Value));
            Table("liquid", LiquidUnits);
            Table("matcher-parts", FoodNameMatcher.PartWords);
            Table("notes", LinkAutoApplyGuard.Notes);
            Table("connectives", Connectives);
            Table("lead-classes", LeadClasses);
            Table("processed", Processed);
            Table("cooked", CookedWords);
            Table("fresh", FreshWords);
            Table("neutral-spice", SpiceNeutral);
            Table("neutral-flour", FlourNeutral);
            Table("neutral-drink", DrinkNeutral);
            Table("dry-leads", DryLeads);
            Table("alcoholic", AlcoholicWords);
            Table("synonyms", FoodNameMatcher.Synonyms.Select(kv => kv.Key + "=" + kv.Value));
            Table("matcher-noise", FoodNameMatcher.Noise);
            Table("matcher-classes", FoodNameMatcher.ClassPrefixes.Concat(FoodNameMatcher.IdentityClasses.Select(c => "identity:" + c)));
            Table("dried-cues", DriedCues);
            Table("seed-words", SeedWords.Append("roast=" + RoastWord));
            Table("product-leads", ProductLeads);
            Table("egg-white", EggWhiteWords.Append("only=" + OnlyWord));
            Table("polarity", UnsaltedPhrases.Select(p => "unsalted=" + p)
                .Concat(SaltedWordingPhrases.Select(p => "salted-wording=" + p))
                .Concat(SaltedFoodPhrases.Select(p => "salted-food=" + p))
                .Concat(UnsweetenedWordingPhrases.Select(p => "unsweetened-wording=" + p))
                .Concat(UnsweetenedFoodPhrases.Select(p => "unsweetened-food=" + p))
                .Concat(SweetenedPhrases.Select(p => "sweetened=" + p)));
            Table("patterns", new[] { Word, Parenthetical, Slash, PercentPattern, HeadingPattern, SaltAddedPattern, PodBeanPattern }.Select(r => r.ToString()));
            Table("guard-joiners", LinkAutoApplyGuard.Joiners);
            Table("alcohol-context", new[] { AlcoholLineContext.SignatureSource() });
            Table("staples", StapleFoods.Entries.Select(kv => kv.Key + "=" + kv.Value));
            Table("staple-filler", StapleFoods.FillerWords);
            Table("prepared", CatalogAdmissionRule.PreparedCategories.Select(kv => kv.Key.ToString(CultureInfo.InvariantCulture) + "=" + kv.Value));
            Table("numbers", new[]
            {
                ShortlistSize.ToString(CultureInfo.InvariantCulture), MinimumNutrientRows.ToString(CultureInfo.InvariantCulture),
                SiblingNutrientSlack.ToString(CultureInfo.InvariantCulture), UnitCoverage.ToString(CultureInfo.InvariantCulture),
                LiquidOnDryShare.ToString(CultureInfo.InvariantCulture), OverBoundShare.ToString(CultureInfo.InvariantCulture),
            });
            return text.ToString();
        }
    }
}
