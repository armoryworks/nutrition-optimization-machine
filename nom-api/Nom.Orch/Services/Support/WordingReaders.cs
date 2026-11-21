using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Nom.Data.Recipe;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// One food a reader may choose, as the evidence pack shows it: its catalog name, its USDA
    /// description, its USDA category, its energy and macronutrients per 100 g when stored, and how
    /// many nutrient rows it has. The readers see the facts; no number they give is ever stored.
    /// </summary>
    public sealed record ReaderFood(
        long Id,
        string Name,
        string Text,
        int? CategoryId = null,
        int? NutrientRows = null,
        decimal? Kcal = null,
        decimal? Protein = null,
        decimal? Fat = null,
        decimal? Carbohydrate = null);

    /// <summary>One recipe line of a wording as the pack shows it: its raw text, its recipe's title, the era of its source (web or book) and its unit class.</summary>
    public sealed record ReaderLine(string Raw, string? Title, string Era, string UnitClass);

    /// <summary>
    /// The deterministic evidence one wording is read on: the wording, up to
    /// <see cref="WordingReaders.MaxLines"/> lines from different recipes (three for a small pack),
    /// the share of lines in each unit class, and up to <see cref="WordingReaders.MaxCandidates"/>
    /// candidate foods (eight for a small pack). A small pack is read six to a call.
    /// </summary>
    public sealed record WordingPack(string Wording, IReadOnlyList<ReaderLine> Lines, IReadOnlyDictionary<string, double> UnitProfile, IReadOnlyList<ReaderFood> Candidates, bool Small);

    /// <summary>
    /// One reader's answer on one pack: food (with the chosen food's id), none (the catalog has no
    /// such food) or unclear (it names no single food, or its lines mix foods), the reader's reason,
    /// and, when the wording's word is an old, regional or brand name of a common food, what it means.
    /// </summary>
    public sealed record ReaderAnswer(string Verdict, long? FoodId, string? Reason = null, string? Means = null)
    {
        public override string ToString() => Verdict == IngredientWordingVerdict.Food ? $"food #{FoodId}" : Verdict;
    }

    /// <summary>
    /// What the outcome table makes of the two blind answers: the verdict a row would carry (null
    /// for no row), its food, the rule of the table that gave it, and the detail.
    /// </summary>
    public sealed record ReaderOutcome(string? Verdict, long? FoodId, string Rule, string? Detail = null)
    {
        public bool Writes => Verdict != null;
    }

    /// <summary>
    /// The model readers of the one decision pipeline (plan C1 step 3; wordings design 2.1 to 2.5):
    /// the wordings the deterministic tiers left (no tier answer, or an answer the gate held) are read
    /// by two readers of different families on one evidence pack each, blind to each other and with
    /// the candidates in another order for each. Their answers are combined by a fixed outcome table:
    /// the same food goes to the link gate; two foods of the same lead word and USDA category within
    /// 10 % energy and 15 % per macronutrient are nutrition-equivalent and the one with fewer
    /// description words the wording lacks goes to the gate; anything else writes nothing; two
    /// "none" answers write a none row only when no candidate passes the gate on its own; two
    /// "unclear" answers write an unclear row only when a rule agrees. A model only classifies:
    /// it chooses among the catalog's foods or proposes a synonym, and never supplies a number.
    /// See docs/architecture/wording-readers.md.
    /// </summary>
    public static class WordingReaders
    {
        public const string Version = "wording-readers/1";

        public const string Read = "read";
        public const string Lexicon = "lexicon";
        public const string Alcohol = "alcohol";

        /// <summary>The frame tiers of lane <c>wording-readers</c>.</summary>
        public static readonly IReadOnlyList<string> Tiers = new[] { Read, Lexicon, Alcohol };

        public const string SourcePrefix = "ai:readers:";
        public const string BatchPrefix = "wr-";
        public const string EvidencePrefix = "wording-readers:lines-";

        public const int MaxLines = 40;
        public const int MaxCandidates = 12;
        public const int SmallLines = 3;
        public const int SmallCandidates = 8;
        public const int SmallPerCall = 6;

        /// <summary>A wording with fewer lines than this is read on a small pack.</summary>
        public const int SmallBelow = 3;

        /// <summary>A wording with more lines than this is written only when the sentinel finds none of 20 sampled lines a different food (wordings design 2.4 check 9).</summary>
        public const int SizeGateLines = 500;
        public const int SizeGateSample = 20;

        public const double EnergyTolerance = 0.10;
        public const double MacroTolerance = 0.15;

        /// <summary>Below this many grams per 100 g two macronutrient amounts count as equal whatever their ratio (0.2 g and 0.4 g of fat are both "none").</summary>
        public const decimal MacroFloorGrams = 1m;

        public const double SplitProfile = 0.70;
        public const double NotNamedShare = 0.20;

        public const string Agree = "agree";
        public const string Equivalent = "nutrition-equivalent";
        public const string Disagree = "readers disagree";
        public const string Unreadable = "no readable answer";
        public const string BothNone = "both none";
        public const string BothUnclear = "both unclear";
        public const string NoRuleAgrees = "unclear without a rule that agrees";
        public const string CandidatePasses = "none, but a candidate passes the gate on its own";

        /// <summary>Bare class words: a wording that is only one of these names no single food, so two "unclear" answers on it are confirmed by rule.</summary>
        public static readonly IReadOnlyList<string> ClassWords = new[]
        {
            "meat", "fish", "fruit", "fruits", "herbs", "herb", "spices", "spice", "stock", "gravy", "wine", "fat", "vegetables", "greens", "nuts", "berries",
            "seasoning", "sauce", "liquor", "game", "poultry",
        };

        /// <summary>The USDA food categories by FDC id, as the evidence pack names them.</summary>
        public static readonly IReadOnlyDictionary<int, string> CategoryNames = new Dictionary<int, string>
        {
            [1] = "Dairy and Egg Products", [2] = "Spices and Herbs", [3] = "Baby Foods", [4] = "Fats and Oils", [5] = "Poultry Products",
            [6] = "Soups, Sauces, and Gravies", [7] = "Sausages and Luncheon Meats", [8] = "Breakfast Cereals", [9] = "Fruits and Fruit Juices",
            [10] = "Pork Products", [11] = "Vegetables and Vegetable Products", [12] = "Nut and Seed Products", [13] = "Beef Products", [14] = "Beverages",
            [15] = "Finfish and Shellfish Products", [16] = "Legumes and Legume Products", [17] = "Lamb, Veal, and Game Products", [18] = "Baked Products",
            [19] = "Sweets", [20] = "Cereal Grains and Pasta", [21] = "Fast Foods", [22] = "Meals, Entrees, and Side Dishes", [23] = "Snacks",
            [25] = "Restaurant Foods", [28] = "Alcoholic Beverages",
        };

        public static string Source(string readersHash) => SourcePrefix + readersHash;

        public static string BatchName(string tier, DateTime at) => string.Create(CultureInfo.InvariantCulture, $"{BatchPrefix}{tier}-{at:yyyyMMddHHmmss}");

        /// <summary>The tier a batch name of <see cref="BatchName"/> was written by, or null for another name.</summary>
        public static string? TierOfBatch(string? batch)
        {
            if (batch == null || !batch.StartsWith(BatchPrefix, StringComparison.Ordinal)) return null;
            var rest = batch[BatchPrefix.Length..];
            var cut = rest.LastIndexOf('-');
            if (cut <= 0) return null;
            var tier = rest[..cut];
            return Tiers.Contains(tier) ? tier : null;
        }

        /// <summary>The evidence a row is loaded on: the wording's line count, so a retracted pair is written again only once its lines have doubled.</summary>
        public static string EvidenceKey(int lines) => string.Create(CultureInfo.InvariantCulture, $"{EvidencePrefix}{Math.Max(1, lines)}");

        /// <summary>
        /// Whether a retract on <paramref name="rejectedEvidence"/> still holds the pair for a wording
        /// that now has <paramref name="lines"/> lines: one with no evidence key for good, one on this
        /// lane's evidence until the lines reach twice the count it was loaded on, one on the tiers'
        /// evidence by the tiers' own rule, and one on any other key only on that key.
        /// </summary>
        public static bool StillRetracted(string? rejectedEvidence, int lines)
        {
            if (rejectedEvidence == null || rejectedEvidence == EvidenceKey(lines)) return true;
            if (rejectedEvidence.StartsWith(WordingTierLaneService.EvidencePrefix, StringComparison.Ordinal)) return WordingTierLaneService.StillRetracted(rejectedEvidence, lines);
            if (!rejectedEvidence.StartsWith(EvidencePrefix, StringComparison.Ordinal)
                || !int.TryParse(rejectedEvidence.AsSpan(EvidencePrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var loaded)) return false;
            return Math.Max(1, lines) < 2L * loaded;
        }

        /// <summary>The share of lines in each unit class.</summary>
        public static Dictionary<string, double> UnitProfile(IEnumerable<string> unitClasses)
        {
            var list = unitClasses.ToList();
            return list.Count == 0
                ? new Dictionary<string, double>(StringComparer.Ordinal)
                : list.GroupBy(c => c, StringComparer.Ordinal).ToDictionary(g => g.Key, g => Math.Round((double)g.Count() / list.Count, 4), StringComparer.Ordinal);
        }

        /// <summary>
        /// The pack of one wording: lines from different recipes first (up to the pack's size), the
        /// unit profile over every line given, and the candidates in the order given, cut to the
        /// pack's size. A wording with fewer than <see cref="SmallBelow"/> lines gets a small pack.
        /// </summary>
        public static WordingPack Pack(string wording, IReadOnlyList<(long RecipeId, ReaderLine Line)> lines, IReadOnlyList<ReaderFood> candidates)
        {
            var small = lines.Count < SmallBelow;
            var take = small ? SmallLines : MaxLines;
            var distinct = lines.GroupBy(l => l.RecipeId).Select(g => g.First().Line).ToList();
            var shown = distinct.Take(take).ToList();
            if (shown.Count < take) shown.AddRange(lines.Select(l => l.Line).Where(l => !shown.Contains(l)).Take(take - shown.Count));
            return new WordingPack(wording, shown, UnitProfile(lines.Select(l => l.Line.UnitClass)),
                candidates.GroupBy(c => c.Id).Select(g => g.First()).Take(small ? SmallCandidates : MaxCandidates).ToList(), small);
        }

        /// <summary>The pack with its candidates in the order one reader sees them: a shuffle seeded by the wording and the reader's role, so A and B never share an order and a rerun repeats it.</summary>
        public static WordingPack Shuffled(WordingPack pack, string role)
        {
            var seed = BitConverter.ToInt32(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(pack.Wording + "|" + role)), 0);
            var random = new Random(seed);
            var order = pack.Candidates.ToList();
            for (var i = order.Count - 1; i > 0; i--)
            {
                var j = random.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }
            return pack with { Candidates = order };
        }

        /// <summary>The model calls a list of packs takes: each full pack alone, small packs <see cref="SmallPerCall"/> to a call.</summary>
        public static IReadOnlyList<IReadOnlyList<WordingPack>> Calls(IEnumerable<WordingPack> packs)
        {
            var list = packs.ToList();
            var calls = list.Where(p => !p.Small).Select(p => (IReadOnlyList<WordingPack>)new[] { p }).ToList();
            calls.AddRange(list.Where(p => p.Small).Chunk(SmallPerCall).Select(c => (IReadOnlyList<WordingPack>)c.ToList()));
            return calls;
        }

        /// <summary>
        /// The outcome table (wordings design 2.3): the same food from both readers, or two
        /// nutrition-equivalent foods, go to the gate; two "none" or two "unclear" answers go to their
        /// own checks; any other pair, or an answer missing, writes nothing.
        /// </summary>
        public static ReaderOutcome Combine(ReaderAnswer? a, ReaderAnswer? b, WordingPack pack)
        {
            if (a == null || b == null) return new ReaderOutcome(null, null, Unreadable, a == null ? "reader A" : "reader B");
            var candidates = pack.Candidates.ToDictionary(c => c.Id);
            var aFood = a.Verdict == IngredientWordingVerdict.Food && a.FoodId is { } af && candidates.ContainsKey(af);
            var bFood = b.Verdict == IngredientWordingVerdict.Food && b.FoodId is { } bf && candidates.ContainsKey(bf);
            if (aFood && bFood)
            {
                if (a.FoodId == b.FoodId) return new ReaderOutcome(IngredientWordingVerdict.Food, a.FoodId, Agree);
                var f = candidates[a.FoodId!.Value];
                var g = candidates[b.FoodId!.Value];
                if (!NutritionEquivalent(f, g)) return new ReaderOutcome(null, null, Disagree, $"A #{f.Id}, B #{g.Id}");
                var chosen = Preferred(pack.Wording, f, g);
                return new ReaderOutcome(IngredientWordingVerdict.Food, chosen.Id, Equivalent, $"A #{f.Id}, B #{g.Id}, took #{chosen.Id}");
            }
            if (a.Verdict == IngredientWordingVerdict.None && b.Verdict == IngredientWordingVerdict.None) return new ReaderOutcome(IngredientWordingVerdict.None, null, BothNone);
            if (a.Verdict == IngredientWordingVerdict.Unclear && b.Verdict == IngredientWordingVerdict.Unclear) return new ReaderOutcome(IngredientWordingVerdict.Unclear, null, BothUnclear);
            return new ReaderOutcome(null, null, Disagree, $"A {a}, B {b}");
        }

        /// <summary>
        /// Whether two foods are nutrition-equivalent (owner choice 7.2): the same lead word, the same
        /// USDA category, energy within 10 % and protein, fat and carbohydrate each within 15 % of the
        /// larger (amounts both under 1 g per 100 g count as equal). A fact either food lacks makes them
        /// not equivalent.
        /// </summary>
        public static bool NutritionEquivalent(ReaderFood f, ReaderFood g)
        {
            if (f.Id == g.Id) return true;
            var lead = LinkGate.LeadWord(f.Text);
            if (lead == null || lead != LinkGate.LeadWord(g.Text)) return false;
            if (f.CategoryId == null || f.CategoryId != g.CategoryId) return false;
            return Within(f.Kcal, g.Kcal, EnergyTolerance, 0m) && Within(f.Protein, g.Protein, MacroTolerance, MacroFloorGrams)
                && Within(f.Fat, g.Fat, MacroTolerance, MacroFloorGrams) && Within(f.Carbohydrate, g.Carbohydrate, MacroTolerance, MacroFloorGrams);
        }

        private static bool Within(decimal? x, decimal? y, double tolerance, decimal floor)
        {
            if (x is not { } a || y is not { } b || a < 0 || b < 0) return false;
            if (a < floor && b < floor) return true;
            var larger = Math.Max(a, b);
            return larger == 0 || (double)(Math.Abs(a - b) / larger) <= tolerance + 1e-9;
        }

        /// <summary>Of two nutrition-equivalent foods, the one whose description has fewer qualifier words the wording lacks; a tie goes to more nutrient rows, then the lower id.</summary>
        public static ReaderFood Preferred(string wording, ReaderFood f, ReaderFood g) =>
            new[] { f, g }.OrderBy(x => Qualifiers(wording, x)).ThenByDescending(x => x.NutrientRows ?? 0).ThenBy(x => x.Id).First();

        /// <summary>The words of a food's description, after its lead word, that the wording does not hold.</summary>
        public static int Qualifiers(string wording, ReaderFood food)
        {
            var have = LinkGate.Words(wording).Select(LinkGate.Fold).ToHashSet(StringComparer.Ordinal);
            var lead = LinkGate.LeadWord(food.Text);
            return LinkGate.Words(food.Text).Select(LinkGate.Fold)
                .Where(w => w != lead && !have.Contains(w) && w.Length > 2 && !FoodNameMatcher.ClassPrefixes.Contains(w))
                .Distinct(StringComparer.Ordinal)
                .Count();
        }

        /// <summary>
        /// The rule that agrees with two "unclear" answers, or null: the unit profile is split (no
        /// class above 70 %), more than 20 % of the lines do not name the wording, or the wording is
        /// a bare class word.
        /// </summary>
        public static string? UnclearRule(string wording, IReadOnlyCollection<string> rawLines, IReadOnlyDictionary<string, double> unitProfile)
        {
            var key = IngredientNameNormalizer.TierKey(wording) ?? wording;
            if (ClassWords.Contains(key.Trim(), StringComparer.Ordinal)) return $"a bare class word ({key})";
            if (unitProfile.Count > 1 && unitProfile.Values.Max() <= SplitProfile)
                return string.Create(CultureInfo.InvariantCulture, $"a split unit profile (largest class {unitProfile.Values.Max():P0})");
            if (rawLines.Count > 0)
            {
                var notNamed = rawLines.Count(l => !LineRelinkService.NamesWording(l, wording));
                if ((double)notNamed / rawLines.Count > NotNamedShare)
                    return string.Create(CultureInfo.InvariantCulture, $"{notNamed} of {rawLines.Count} lines do not name the wording");
            }
            return null;
        }

        /// <summary>The single word a lexicon entry would be keyed by: the wording's harder key when it is one word of letters, else null.</summary>
        public static string? LexiconTerm(string wording)
        {
            var key = IngredientNameNormalizer.TierKey(wording);
            if (string.IsNullOrWhiteSpace(key)) return null;
            key = key.Trim();
            return key.Length >= 2 && key.All(char.IsLetter) ? key : null;
        }

        /// <summary>A proposed meaning in the form the lexicon keeps it: normalised and lower-cased, or null when empty, the term itself, or longer than four words.</summary>
        public static string? Meaning(string? means, string term)
        {
            if (string.IsNullOrWhiteSpace(means)) return null;
            var normalized = IngredientNameNormalizer.WordingKey(means).Trim();
            if (normalized.Length == 0 || string.Equals(normalized, term, StringComparison.Ordinal)) return null;
            var words = LinkGate.Words(normalized);
            return words.Count is >= 1 and <= 4 && !words.Select(LinkGate.Fold).Contains(LinkGate.Fold(term)) ? string.Join(' ', words) : null;
        }

        /// <summary>The meaning both readers proposed blind for the term, or null when either proposed none or they differ.</summary>
        public static string? AgreedMeaning(ReaderAnswer? a, ReaderAnswer? b, string term)
        {
            var ma = Meaning(a?.Means, term);
            var mb = Meaning(b?.Means, term);
            return ma != null && ma == mb ? ma : null;
        }

        /// <summary>The lexicon as the gate reads it: each term folded as the gate folds a wording's words, to what it means.</summary>
        public static Dictionary<string, string> GateLexicon(IEnumerable<(string Term, string Means)> entries)
        {
            var lexicon = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (term, means) in entries)
            {
                var words = LinkGate.Words(term);
                if (words.Count != 1) continue;
                lexicon.TryAdd(LinkGate.Fold(words[0]), means);
            }
            return lexicon;
        }

        /// <summary>A copy of the gate's catalog with more lexicon entries (a fixed reading's meaning, or this pass's agreed synonyms); the catalog's own entries win.</summary>
        public static GateCatalog WithLexicon(GateCatalog catalog, IReadOnlyDictionary<string, string> extra)
        {
            if (extra.Count == 0) return catalog;
            var lexicon = new Dictionary<string, string>(catalog.Lexicon, StringComparer.Ordinal);
            foreach (var (term, means) in extra) lexicon.TryAdd(term, means);
            return new GateCatalog
            {
                Curated = catalog.Curated,
                CuratedNames = catalog.CuratedNames,
                Siblings = catalog.Siblings,
                LeadWords = catalog.LeadWords,
                TrustedAliases = catalog.TrustedAliases,
                HeadDefaults = catalog.HeadDefaults,
                Lexicon = lexicon,
                GramBounds = catalog.GramBounds,
                Matcher = catalog.Matcher,
                Staples = catalog.Staples,
            };
        }

        /// <summary>A hash of <see cref="Version"/>, the pack sizes, the outcome table's tolerances, the rule tables, the gate's and the alcohol rules' signatures and the fixed readings.</summary>
        public static string Signature => SignatureValue.Value;

        private static readonly Lazy<string> SignatureValue = new(() => WordingTiers.Hash(SignatureSource()));

        internal static string SignatureSource()
        {
            var text = new StringBuilder(Version);
            text.Append("\nnumbers:").Append(string.Join(',', new object[]
            {
                MaxLines, MaxCandidates, SmallLines, SmallCandidates, SmallPerCall, SmallBelow, SizeGateLines, SizeGateSample,
                EnergyTolerance, MacroTolerance, MacroFloorGrams, SplitProfile, NotNamedShare,
            }.Select(o => Convert.ToString(o, CultureInfo.InvariantCulture))));
            text.Append("\nclass-words:").Append(string.Join(',', ClassWords));
            text.Append("\ncategories:").Append(string.Join(',', CategoryNames.Select(kv => kv.Key.ToString(CultureInfo.InvariantCulture) + "=" + kv.Value)));
            text.Append("\ngate:").Append(LinkGate.Signature);
            text.Append("\nalcohol:").Append(AlcoholLineContext.Signature);
            text.Append("\nfixed:").Append(AlcoholFixedReading.SignatureSource());
            return text.ToString();
        }
    }
}
