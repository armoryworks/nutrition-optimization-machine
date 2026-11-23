using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Nom.Data.Nutrition;
using Nom.Data.Recipe;

namespace Nom.Orch.Services.Support
{
    /// <summary>One line of a wording as the tiers read it: its raw text, its unit class and, when the caller has it, what the rest of its recipe says.</summary>
    public sealed record TierLine(string Raw, string UnitClass, AlcoholRecipeContext? Recipe = null);

    /// <summary>A verdict of the verified wording table as the verified-wording tier reads it.</summary>
    public sealed record TierVerdict(string Verdict, long? FoodId);

    /// <summary>What one tier says about a wording: the food, the name the gate reads the pair under (the harder key), and why.</summary>
    public sealed record TierAnswer(string Tier, long FoodId, string GateWording, string Detail);

    /// <summary>
    /// The tiers' decision on one wording: its key, the tier that answered first and its food, every
    /// tier's answer, the rules that hold the pair (empty when it may be written) and, when no tier
    /// answered, why.
    /// </summary>
    public sealed record WordingTierDecision(string Wording, string? Key, TierAnswer? Answer, IReadOnlyList<TierAnswer> Answers, IReadOnlyList<GateHold> Holds, string? Abstain)
    {
        public bool Writes => Answer != null && Holds.Count == 0;
        public string? Hold => Holds.Count == 0 ? null : string.Join("; ", Holds.Select(h => h.Code + ": " + h));
    }

    /// <summary>What the tiers read about the catalog, built once per pass.</summary>
    public sealed class WordingTierCatalog
    {
        public GateCatalog Gate { get; init; } = new();
        public IReadOnlyDictionary<long, GateFood> Foods { get; init; } = new Dictionary<long, GateFood>();
        public IReadOnlyDictionary<string, IReadOnlyList<GateFood>> ByFdcId { get; init; } = new Dictionary<string, IReadOnlyList<GateFood>>();
        /// <summary>The curated food whose name has this harder key (or its singular or plural); a key two foods share is left out.</summary>
        public IReadOnlyDictionary<string, long> NameKeys { get; init; } = new Dictionary<string, long>();
        /// <summary>The food a trusted alias with this harder key points at; a key two foods share is left out.</summary>
        public IReadOnlyDictionary<string, long> AliasKeys { get; init; } = new Dictionary<string, long>();
        /// <summary>The verdict of the verified wording table for this harder key; a key two rows judge differently is left out.</summary>
        public IReadOnlyDictionary<string, TierVerdict> Verified { get; init; } = new Dictionary<string, TierVerdict>();
        public WordingHeadDefaults HeadDefaults { get; init; } = WordingHeadDefaults.Empty;

        /// <summary>
        /// The tiers' catalog from the gate's foods (live, the curated ones and the USDA foods a link may
        /// target), the trusted aliases (name and owner, the contexts the gate trusts), the live rows of
        /// the verified wording table and the head-default table. The gate's
        /// own catalog is built with the trusted aliases by wording key, the head defaults over all unit
        /// classes and the grams bounds.
        /// </summary>
        public static WordingTierCatalog Build(
            IReadOnlyCollection<GateFood> foods,
            FoodNameMatcher? matcher,
            IReadOnlyDictionary<string, FoodCandidate>? staples,
            IEnumerable<(string Name, long Owner)>? trustedAliases = null,
            IEnumerable<(string Wording, string Verdict, long? FoodId)>? verified = null,
            WordingHeadDefaults? headDefaults = null,
            IReadOnlyDictionary<int, decimal>? gramBounds = null)
        {
            var aliases = (trustedAliases ?? Enumerable.Empty<(string, long)>()).ToList();
            var byWordingKey = aliases
                .GroupBy(a => IngredientNameNormalizer.WordingKey(a.Name), StringComparer.Ordinal)
                .Where(g => g.Select(a => a.Owner).Distinct().Count() == 1)
                .ToDictionary(g => g.Key, g => g.First().Owner, StringComparer.Ordinal);
            headDefaults ??= WordingHeadDefaults.Empty;
            var gate = GateCatalog.Build(foods, matcher, byWordingKey, headDefaults.Overall(), gramBounds: gramBounds, staples: staples);

            return new WordingTierCatalog
            {
                Gate = gate,
                Foods = foods.Where(f => !f.Deleted).GroupBy(f => f.Id).ToDictionary(g => g.Key, g => g.First()),
                ByFdcId = foods.Where(f => !f.Deleted && f.FdcId != null).GroupBy(f => f.FdcId!, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => (IReadOnlyList<GateFood>)g.OrderBy(f => f.Id).ToList(), StringComparer.Ordinal),
                NameKeys = Unique(gate.Curated.Select(kv => (kv.Key, kv.Value)), variants: true),
                AliasKeys = Unique(aliases.Select(a => (a.Name, a.Owner)), variants: false),
                Verified = (verified ?? Enumerable.Empty<(string, string, long?)>())
                    .Select(v => (Key: IngredientNameNormalizer.TierKey(v.Wording), Verdict: new TierVerdict(v.Verdict, v.Verdict == IngredientWordingVerdict.Food ? v.FoodId : null)))
                    .Where(v => v.Key != null)
                    .GroupBy(v => v.Key!, StringComparer.Ordinal)
                    .Where(g => g.Select(v => v.Verdict).Distinct().Count() == 1)
                    .ToDictionary(g => g.Key, g => g.First().Verdict, StringComparer.Ordinal),
                HeadDefaults = headDefaults,
            };
        }

        private static Dictionary<string, long> Unique(IEnumerable<(string Name, long Id)> names, bool variants) => WordingTiers.KeyMap(names, variants);
    }

    /// <summary>
    /// The deterministic wording tiers of the one decision pipeline (plan C1 step 2; links design
    /// B4 and B5, alcohol design A3): a wording is keyed by
    /// <see cref="IngredientNameNormalizer.TierKey"/> and asked, in this order, of the verified
    /// wording table (another wording with the same key), a trusted alias, a staple name, the name
    /// key of exactly one curated food, the alcohol sense its lines share when exactly one ready
    /// USDA food is allowed for it, and the data-given default of a bare head word in its lines' unit
    /// classes. A wording that names more than one food gets no row: it is left to the compound line
    /// rules (<see cref="CompoundLineSplitter"/>, links B11), which decide each line on its own.
    /// The first tier that answers decides; a later tier that names another food holds the pair.
    /// Every answer then passes <see cref="LinkGate"/> with the wording's lines. No model is asked,
    /// and no tier ever writes a number. See docs/architecture/wording-tiers.md.
    /// </summary>
    public static class WordingTiers
    {
        public const string Version = "wording-tiers/3";

        public const string VerifiedWording = "verified-wording";
        public const string Alias = "alias";
        public const string Staple = "staple";
        public const string NameKey = "name-key";
        public const string AlcoholContext = "alcohol-context";
        public const string HeadDefault = "head-default";

        /// <summary>Every tier, in the order the pipeline asks them.</summary>
        public static readonly IReadOnlyList<string> All = new[] { VerifiedWording, Alias, Staple, NameKey, AlcoholContext, HeadDefault };

        /// <summary>The source a row a tier writes carries in the verified wording table.</summary>
        public static string Source(string tier) => "deterministic:" + tier;

        /// <summary>
        /// Every source a tier writes. The verified-wording tier never reads a row with one of these:
        /// such a row is one tier's answer, not two blind readers', and only that tier's undo reaches it.
        /// </summary>
        public static readonly IReadOnlyList<string> Sources = All.Select(Source).ToArray();

        /// <summary>The tier that wrote a row with this source, or null when no tier did.</summary>
        public static string? TierOf(string? source) => All.FirstOrDefault(t => string.Equals(Source(t), source, StringComparison.Ordinal));

        public const string HeadingAbstain = "a heading, not a food";
        public const string CompoundAbstain = "names more than one food: left to the compound line rules";
        public const string NoTier = "no tier answers";
        public const string TiersDisagree = "tiers-disagree";

        private static readonly Lazy<string> SignatureValue = new(() => Hash(SignatureSource()));

        /// <summary>A hash of the version, the gate's signature, the alcohol rules, the head-default thresholds and the tier order.</summary>
        public static string Signature => SignatureValue.Value;

        internal static string SignatureSource() =>
            string.Join('\n', Version, LinkGate.Signature, AlcoholLineContext.Signature, WordingHeadDefaults.SignatureSource(),
                string.Join(',', LinkAutoApplyGuard.Joiners), string.Join(',', All), string.Join(',', CueRules),
                string.Join(',', Species.OrderBy(w => w, StringComparer.Ordinal)), NoteForm.ToString(), string.Join(',', NoteForms.OrderBy(w => w, StringComparer.Ordinal)),
                string.Join(',', NoteParts.OrderBy(w => w, StringComparer.Ordinal)), "note-form:majority");

        /// <summary>
        /// Harder keys to the one food each names. A name that is its own key ("Ham") owns that key
        /// before any name that only comes to it once words are taken off ("Ham, minced"); two foods
        /// of the same rank on one key leave it to neither. With <paramref name="variants"/> the
        /// singular and plural of each key count too.
        /// </summary>
        public static Dictionary<string, long> KeyMap(IEnumerable<(string Name, long Id)> names, bool variants)
        {
            var exact = new Dictionary<string, long>(StringComparer.Ordinal);
            var derived = new Dictionary<string, long>(StringComparer.Ordinal);
            var shared = new HashSet<(bool, string)>();
            foreach (var (name, id) in names)
            {
                if (IngredientNameNormalizer.TierKey(name) is not { } key) continue;
                var own = string.Equals(key, name.Trim().ToLowerInvariant(), StringComparison.Ordinal);
                var map = own ? exact : derived;
                foreach (var variant in variants ? IngredientNameNormalizer.ExactKeys(key).Select(k => k.ToLowerInvariant()) : new[] { key })
                {
                    if (map.TryGetValue(variant, out var other) && other != id) shared.Add((own, variant));
                    else map[variant] = id;
                }
            }
            foreach (var (own, key) in shared) (own ? exact : derived).Remove(key);
            foreach (var key in shared.Where(s => s.Item1).Select(s => s.Item2)) derived.Remove(key);
            foreach (var (key, id) in derived) exact.TryAdd(key, id);
            return exact;
        }

        internal static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16].ToLowerInvariant();

        /// <summary>
        /// What each tier says about the wording, in pipeline order, and why none answered. A
        /// verdict of the verified wording table that is not a food stops the pipeline with no answer.
        /// </summary>
        public static (string? Key, IReadOnlyList<TierAnswer> Answers, string? Abstain) Ask(string wording, IReadOnlyList<TierLine> lines, WordingTierCatalog catalog)
        {
            var key = IngredientNameNormalizer.TierKey(wording);
            var answers = new List<TierAnswer>();
            if (key == null) return (null, answers, HeadingAbstain);
            if (LinkAutoApplyGuard.IsCompound(key)) return (key, answers, CompoundAbstain);

            void Add(string tier, long? food, string detail)
            {
                if (food is { } id && catalog.Foods.ContainsKey(id)) answers.Add(new TierAnswer(tier, id, key, detail));
            }

            var variants = IngredientNameNormalizer.ExactKeys(key).Select(k => k.ToLowerInvariant()).ToList();
            if (catalog.Verified.TryGetValue(key, out var verdict))
            {
                if (verdict.Verdict != IngredientWordingVerdict.Food) return (key, answers, $"the verified wording table says {verdict.Verdict} for \"{key}\"");
                Add(VerifiedWording, verdict.FoodId, $"verified wording \"{key}\"");
            }

            if (catalog.AliasKeys.TryGetValue(key, out var owner)) Add(Alias, owner, $"trusted alias \"{key}\"");

            if (StapleFoods.UsdaNameFor(key) is { } stapleName && catalog.Gate.Staples.TryGetValue(stapleName, out var staple))
                Add(Staple, staple.IngredientId, $"staple: {stapleName}");

            if (variants.Select(v => catalog.NameKeys.TryGetValue(v, out var id) ? id : (long?)null).FirstOrDefault(id => id != null) is { } named)
                Add(NameKey, named, $"name key \"{key}\"");

            if (AlcoholTarget(wording, lines, catalog) is { } drink) Add(AlcoholContext, drink.Food, drink.Detail);

            if (WordingHeadDefaults.HeadOf(key) is { } head)
            {
                var (chosen, _) = catalog.HeadDefaults.Decide(head, lines.Select(l => l.UnitClass).ToList());
                if (chosen != null)
                    Add(HeadDefault, chosen.FoodId, $"\"{head}\" by {chosen.UnitClass}: {chosen.Lines} of {chosen.Total} curated lines");
            }

            return (key, answers, answers.Count == 0 ? NoTier : null);
        }

        /// <summary>
        /// The gate rules that read words of the wording itself (a fresh cue, a part, a form, a
        /// salted or sweetened polarity, a stated percent, a marked variant). The tiers decide on the
        /// harder key, which drops preparation words and notes; these rules are also read on the
        /// wording as written, so a word the key dropped ("chopped" tarragon) still holds the pair.
        /// </summary>
        public static readonly IReadOnlyList<string> CueRules = new[] { LinkGate.FreshHerb, LinkGate.Part, LinkGate.Form, LinkGate.Polarity, LinkGate.Percent, LinkGate.Marked };

        /// <summary>
        /// The tiers' decision on one wording: the first answer, held when a later tier names another
        /// food, then put through the gate under the harder key with the lines
        /// <paramref name="gateLines"/> gives for its food (null for none: the units and grams checks
        /// are then not read), and through the <see cref="CueRules"/> on the wording as written. An
        /// answer of the verified wording table rests on a row two blind readers agreed on, so the
        /// gate reads it as readers' answer (head noun in place of containment and lead noun).
        /// </summary>
        public static WordingTierDecision Decide(string wording, long sourceId, IReadOnlyList<TierLine> lines, WordingTierCatalog catalog, Func<GateFood, IReadOnlyList<GateLine>?>? gateLines = null)
        {
            var (key, answers, abstain) = Ask(wording, lines, catalog);
            if (answers.Count == 0) return new WordingTierDecision(wording, key, null, answers, Array.Empty<GateHold>(), abstain);

            var answer = answers[0];
            var holds = new List<GateHold>();
            var others = answers.Where(a => a.FoodId != answer.FoodId).ToList();
            if (others.Count > 0)
                holds.Add(new GateHold(TiersDisagree, "tiers name different foods", string.Join(", ", others.Select(o => $"{o.Tier} #{o.FoodId}"))));

            var target = catalog.Foods[answer.FoodId];
            if (answer.Tier == HeadDefault && HeadDefaultHold(key!, target) is { } plain) holds.Add(plain);
            if (answer.Tier is NameKey or HeadDefault && NoteFormHold(lines, target) is { } note) holds.Add(note);
            var weighed = gateLines?.Invoke(target);
            var readers = answer.Tier == VerifiedWording;
            holds.AddRange(LinkGate.Check(new GatePair(answer.GateWording, sourceId, target, ReadersAgreed: readers, Lines: weighed), catalog.Gate));
            if (!string.Equals(IngredientNameNormalizer.WordingKey(wording), answer.GateWording, StringComparison.Ordinal))
            {
                holds.AddRange(LinkGate.Check(new GatePair(wording, sourceId, target, ReadersAgreed: readers, Lines: weighed), catalog.Gate)
                    .Where(h => CueRules.Contains(h.Code) && holds.All(held => held.Code != h.Code)));
            }
            return new WordingTierDecision(wording, key, answer, answers, holds, null);
        }

        /// <summary>Animal species a food's name may carry: a head default never picks one its bare head word does not name ("fowl" is not Guinea Fowls).</summary>
        public static readonly IReadOnlySet<string> Species = new HashSet<string>(StringComparer.Ordinal)
        {
            "turkey", "chicken", "pork", "beef", "veal", "lamb", "mutton", "duck", "goose", "guinea", "pheasant", "quail", "venison", "rabbit", "goat", "bison",
        };

        private static readonly System.Text.RegularExpressions.Regex NoteForm = new(@"\(([^()]*)\)", System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>Forms a line's bracketed note may give its wording ("3/4 cup cranberry (blood orange or pomegranate juice)" is a juice).</summary>
        private static readonly HashSet<string> NoteForms = new(StringComparer.Ordinal) { "juice", "juices", "nectar", "syrup", "cocktail" };

        /// <summary>
        /// Why a head default may not stand for a bare head word: the default must be the plain food
        /// of the head (its name holds the head word) and may not be a species the head does not
        /// name, however many curated lines it holds (22 of 22 "fowl" lines on Guinea Fowls).
        /// </summary>
        internal static GateHold? HeadDefaultHold(string key, GateFood target)
        {
            var head = LinkGate.Fold(key.Trim());
            var name = LinkGate.Words(target.Name).Select(LinkGate.Fold).ToList();
            if (!name.Contains(head)) return new GateHold(LinkGate.Variety, "a head default must be the plain food of its head", $"{head} not in {target.Name}");
            var species = name.FirstOrDefault(w => w != head && Species.Contains(w));
            return species == null ? null : new GateHold(LinkGate.Variety, "a head default may not pick a species the wording does not name", species);
        }

        /// <summary>Parts of a fruit a note may name beside its juice ("1 lemon (zest and juice)"): such a note says what is taken from the whole fruit, not that the wording is a juice.</summary>
        private static readonly HashSet<string> NoteParts = new(StringComparer.Ordinal) { "zest", "rind", "peel", "flesh", "pulp", "segments" };

        /// <summary>
        /// A hold when the bracketed notes of more than half of the wording's lines give it a form the
        /// food is not (a juice for the fruit), which the name key and the head default cannot read. One
        /// line's note never holds every line of a common wording, and a note that names a part of the
        /// fruit beside the form ("zest and juice") reads no form.
        /// </summary>
        internal static GateHold? NoteFormHold(IReadOnlyList<TierLine> lines, GateFood target)
        {
            if (lines.Count == 0) return null;
            var food = LinkGate.Words(target.Name + " " + target.Text).Select(LinkGate.Fold).ToHashSet(StringComparer.Ordinal);
            string? first = null;
            var reading = 0;
            foreach (var line in lines)
            {
                foreach (System.Text.RegularExpressions.Match note in NoteForm.Matches(line.Raw))
                {
                    var words = LinkGate.Words(note.Groups[1].Value).ToList();
                    var form = words.FirstOrDefault(NoteForms.Contains);
                    if (form == null || food.Contains(LinkGate.Fold(form)) || words.Any(NoteParts.Contains)) continue;
                    first ??= form;
                    reading++;
                    break;
                }
            }
            return reading * 2 > lines.Count ? new GateHold(LinkGate.Form, "a line's note reads a form the food is not", $"{first} ({reading} of {lines.Count} lines)") : null;
        }

        /// <summary>
        /// The ready USDA food the alcohol sense shared by every line (or the wording, with no lines)
        /// allows: the first of the sense's allowed foods that is ready (live, not branded or a
        /// prepared food, curated or a pending SR Legacy or Foundation food with no admission
        /// reason, holding the core nutrients and at least <see cref="LinkGate.MinimumNutrientRows"/>
        /// rows). The sense table lists the food of the drink's own kind first and after it only
        /// foods the alcohol design holds nutrition-equivalent (every 80-proof spirit, the table
        /// wines, the two dessert wines). Null when a line has no alcohol word, the lines share no
        /// drink sense, or the sense allows no ready food.
        /// </summary>
        internal static (long Food, string Detail)? AlcoholTarget(string wording, IReadOnlyList<TierLine> lines, WordingTierCatalog catalog)
        {
            var readings = lines.Count == 0 ? new[] { AlcoholLineContext.Read(wording) } : lines.Select(l => AlcoholLineContext.Read(l.Raw, l.Recipe)).ToArray();
            if (readings.Any(r => r.Kind == AlcoholSenseKind.None)) return null;
            if (AlcoholLineContext.Shared(readings) is not { Kind: AlcoholSenseKind.Drink } shared) return null;
            foreach (var fdc in AlcoholLineContext.Allowed(shared.Sense))
            {
                if (!catalog.ByFdcId.TryGetValue(fdc, out var foods)) continue;
                var ready = foods.Where(Ready).OrderByDescending(f => f.Status == (long)CurationStatusEnum.Curated).ThenBy(f => f.Id).FirstOrDefault();
                if (ready != null) return (ready.Id, $"alcohol sense {shared.Sense}: FDC {fdc}");
            }
            return null;
        }

        /// <summary>Whether a food may be a deterministic target by itself (alcohol design 2.4).</summary>
        public static bool Ready(GateFood food) =>
            !food.Deleted
            && food.DataType != CatalogAdmissionRule.BrandedFood
            && !PreparedFoods.IsPrepared(food.CategoryId)
            && (food.Status == (long)CurationStatusEnum.Curated
                || (food.Status == (long)CurationStatusEnum.PendingCuration && food.AdmissionReason == null
                    && food.DataType is CatalogAdmissionRule.SrLegacyFood or CatalogAdmissionRule.FoundationFood))
            && food.CoreNutrients != false
            && (food.NutrientRows ?? LinkGate.MinimumNutrientRows) >= LinkGate.MinimumNutrientRows;
    }
}
