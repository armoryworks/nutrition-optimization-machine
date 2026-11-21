using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Nom.Data.Nutrition;
using Nom.Data.Recipe;

namespace Nom.Orch.Services.Support
{
    /// <summary>A food a deterministic tier names for a wording, with what the link gate holds against it.</summary>
    public sealed record RecheckCandidate(string Tier, GateFood Food, IReadOnlyList<GateHold> Holds);

    /// <summary>
    /// What the re-check does with the lines of one wording on one food: keep them (no hold, or
    /// holds that do not prove the food wrong, counted as not proven), move them to the food a tier
    /// names (<see cref="Target"/>), or move them back to an uncurated food of their wording; with
    /// the rule that decided and the gate's holds on the food they are on.
    /// </summary>
    public sealed record RecheckVerdict(string Outcome, string? Reason, RecheckCandidate? Target, IReadOnlyList<GateHold> Holds);

    /// <summary>
    /// The rules of the applied-link re-check (plan C3, alcohol A4, wordings 2.7, containers 2.6):
    /// which link gate holds each rule set acts on, which of them are hard enough to take a line off
    /// its food when no tier names another, the container form line rule and its canned sister, and
    /// the catalog-name check. A link already made is refused only on positive evidence that its
    /// food is wrong (<see cref="Refutes"/>); a hold that only fails to prove the food right keeps
    /// the lines and is counted as not proven. No model; every decision is a rule. See
    /// docs/architecture/link-recheck.md.
    /// </summary>
    public static class LinkRecheckRules
    {
        public const string Version = "link-recheck/2";

        public const string Alcohol = "alcohol";
        public const string PartHerb = "part-herb";
        public const string Compound = "compound";
        public const string ContainerForm = "container-form";
        public const string Prepared = "prepared";
        public const string AppliedNovember = "applied-1103";
        public const string Applied = "applied";
        public const string Aliases = "aliases";
        public const string Wordings = "wordings";

        public const string Keep = "keep";
        public const string Held = "held";
        public const string Unproven = "unproven";
        public const string Move = "move";
        public const string Unlink = "unlink";
        public const string Verified = "verified";
        public const string Alternative = "alternative";

        public const string VerifiedWordingTier = "verified-wording";
        public const string TrustedAliasTier = "trusted-alias";
        public const string NameRuleTier = "name-rule";
        public const string CuratedNameTier = "curated-name";
        public const string AlcoholSenseTier = "alcohol-sense";
        public const string CannedSisterTier = "canned-sister";

        /// <summary>The day the unguarded model auto-apply ran (links design 1); its links are re-checked first.</summary>
        public static readonly DateTime NovemberThird = new(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>The rule sets that write, in the order the plan runs them (plan 3.3 step 8), with prepared foods before the applied links.</summary>
        public static readonly IReadOnlyList<string> Sets = new[] { Alcohol, PartHerb, Compound, ContainerForm, Prepared, AppliedNovember, Applied, Aliases };

        /// <summary>Every set the re-check knows, the report-only <see cref="Wordings"/> last.</summary>
        public static readonly IReadOnlyList<string> AllSets = Sets.Append(Wordings).ToList();

        /// <summary>
        /// Holds that are positive evidence that a link's food is wrong, and so move its lines or take
        /// them off their food when no tier names another: the wording is a heading or more than one
        /// food, the food is retired, branded or a prepared dish, the line's alcohol sense refuses
        /// it, the wording names a part or form the food contradicts, a fresh herb on the dried
        /// spice, a food made from the wording's head noun or only topped with it, a salted or
        /// sweetened polarity, a stated percent the food lacks, a group word its category
        /// contradicts, or another curated food's very name. The rest (a word the food lacks, a
        /// state or marked variant the wording does not name, a variety, an incomplete food ...)
        /// only fail to prove the food right: such lines stay and are counted as not proven.
        /// </summary>
        public static readonly IReadOnlySet<string> Refuting = new HashSet<string>(StringComparer.Ordinal)
        {
            LinkGate.Heading, LinkGate.Compound, LinkGate.Retired, LinkGate.Branded, LinkGate.Prepared,
            LinkGate.AlcoholDrink, LinkGate.AlcoholNoFood, LinkGate.AlcoholNotDrink,
            LinkGate.Part, LinkGate.FreshHerb, LinkGate.Form, LinkGate.HeadLead, LinkGate.HeadNoun, LinkGate.Polarity,
            LinkGate.Percent, LinkGate.Group, LinkGate.OtherFood,
        };

        /// <summary>The alcohol rules: read by the line in the alcohol set, and elsewhere only when the wording itself refuses the food.</summary>
        public static readonly IReadOnlySet<string> AlcoholCodes = new HashSet<string>(StringComparer.Ordinal) { LinkGate.AlcoholDrink, LinkGate.AlcoholNoFood, LinkGate.AlcoholNotDrink };

        /// <summary>The part segments that are the ordinary eaten part of a food named whole ("Potatoes, baked, flesh", "Goose, meat only"): never proof that a link is wrong.</summary>
        public static readonly IReadOnlySet<string> EdibleParts = new HashSet<string>(StringComparer.Ordinal) { "flesh", "flesh only", "meat", "meat only", "leaf", "leaves" };

        /// <summary>Holds the re-check does not read: it moves lines without their amounts, so grams are not computed for it.</summary>
        public static readonly IReadOnlySet<string> Unread = new HashSet<string>(StringComparer.Ordinal) { LinkGate.Units, LinkGate.Grams };

        /// <summary>
        /// Word rules a tier whose own rule vouches for the food's identity does not need: the
        /// alcohol sense table names the drink, and a canned sister shares the food's lead word and
        /// every word of the wording.
        /// </summary>
        public static readonly IReadOnlySet<string> IdentityCodes = new HashSet<string>(StringComparer.Ordinal)
        {
            LinkGate.Containment, LinkGate.LeadNoun, LinkGate.HeadNoun, LinkGate.Generic, LinkGate.State, LinkGate.WordOrder,
            LinkGate.Percent, LinkGate.RuleOther, LinkGate.OtherAlias,
        };

        private static readonly Dictionary<string, string[]> Acting = new(StringComparer.Ordinal)
        {
            [Alcohol] = new[] { LinkGate.AlcoholDrink, LinkGate.AlcoholNoFood, LinkGate.AlcoholNotDrink },
            [PartHerb] = new[] { LinkGate.Part, LinkGate.FreshHerb },
            [Compound] = new[] { LinkGate.Compound, LinkGate.Heading },
            [Prepared] = new[] { LinkGate.Prepared },
            [ContainerForm] = Array.Empty<string>(),
        };

        private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

        private static readonly Regex ContainerWord = new(@"\b(?:cans?|tins?|jars?)\b(?!\s+(?:be|you|also|easily|use|substitute|opener)\b)", Options);
        private static readonly Regex VerbBefore = new(@"\b(?:you|i|we|it|if|they)\s+$", Options);
        private static readonly Regex DrainedLine = new(@"\bdrained\b", Options);
        private static readonly Regex NotDrained = new(@"\bundrained\b|\b(?:not|don'?t|do not)\s+drain(?:ed)?\b|\bwith (?:its|their|the) (?:juice|liquid)s?\b", Options);
        private static readonly Regex CannedFood = new(@"\b(?:canned|condensed|evaporated|jarred|bottled)\b", Options);
        private static readonly Regex DrainedFood = new(@"\bdrained\b", Options);

        /// <summary>The trigger stems the alcohol set's line query looks for before the sense rules read a line.</summary>
        public static readonly IReadOnlyList<string> AlcoholStems = new[]
        {
            "wine", "sherry", "brand", "rum", "whisk", "bourbon", "scotch", "beer", "ale", "lager", "stout", "porter", "cider", "perry", "port",
            "sack", "madeira", "marsala", "vermouth", "liqueur", "liquor", "spirit", "alcohol", "bitters", "extract", "essence", "flavo",
            "sake", "mirin", "shaoxing", "gin", "vodka", "tequila", "cognac", "armagnac", "calvados", "kirsch", "grappa", "champagne",
            "prosecco", "claret", "burgundy", "chablis", "hock", "rhenish", "kahlua", "baileys", "curacao", "cointreau", "marnier", "amaretto",
            "creme de", "schnapps", "malaga", "tokay", "muscatel", "riesling", "chardonnay", "sauvignon", "merlot", "cabernet", "chianti",
            "pinot", "sauternes", "mead", "barm", "hollands", "arrack",
        };

        /// <summary>The stems the container set's line query looks for.</summary>
        public static readonly IReadOnlyList<string> ContainerStems = new[] { "can", "tin", "jar", "drained" };

        /// <summary>
        /// Catalog names known to contradict their USDA entry, by FDC id: the name the food is to have
        /// and a word its present name carries that the entry does not (wordings 1.4, item 10).
        /// </summary>
        public static readonly IReadOnlyDictionary<string, (string Name, string WrongWord)> NameFixes = new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            ["170929"] = ("Ground Mustard", "seed"),
        };

        private static readonly HashSet<string> NameColourWords = new(StringComparer.Ordinal) { "black", "white", "red", "green", "yellow", "golden", "brown", "whole", "seed" };
        private static readonly HashSet<string> DescriptionFormWords = new(StringComparer.Ordinal) { "ground", "powder", "powdered", "dried", "flakes" };

        private static readonly Lazy<string> SignatureValue = new(() =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(SignatureSource())))[..16].ToLowerInvariant());

        /// <summary>A hash of <see cref="Version"/>, the gate's and the alcohol rules' signatures and every table here, so a change to any of them is a new signature.</summary>
        public static string Signature => SignatureValue.Value;

        /// <summary>The signature of one rule set's decisions on the lane frame.</summary>
        public static string SetSignature(string set) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Signature + "|" + set)))[..16].ToLowerInvariant();

        /// <summary>Whether a set takes lines from applied model links and trusted aliases (as against every line of a kind).</summary>
        public static bool FromLinks(string set) => set is PartHerb or Compound or AppliedNovember or Applied or Aliases or Wordings;

        /// <summary>Whether a set reads every hold of the gate (the applied links and aliases) rather than its own rule's codes.</summary>
        public static bool ReadsEveryHold(string set) => set is AppliedNovember or Applied or Aliases or Wordings;

        /// <summary>The gate holds a set acts on, of the holds given; for the applied links and aliases every hold.</summary>
        public static IReadOnlyList<GateHold> ActingHolds(string set, IEnumerable<GateHold> holds) =>
            ReadsEveryHold(set) ? holds.ToList()
            : Acting.TryGetValue(set, out var codes) ? holds.Where(h => codes.Contains(h.Code)).ToList()
            : new List<GateHold>();

        /// <summary>The holds a tier's food must be free of: all for a name tier, the non-word rules for a tier that vouches for identity.</summary>
        public static IReadOnlyList<GateHold> Blocking(string tier, IEnumerable<GateHold> holds) =>
            holds.Where(h => !Unread.Contains(h.Code) && !(tier is AlcoholSenseTier or CannedSisterTier && IdentityCodes.Contains(h.Code)
                && !(tier == CannedSisterTier && h.Code == LinkGate.State && h.Detail != "canned"))).ToList();

        /// <summary>
        /// Whether a hold is positive evidence that the food is wrong (<see cref="Refuting"/>): a stated
        /// percent only when the wording states it, a head noun only when the food has it as an
        /// addition, a part only when it is not the ordinary eaten part (<see cref="EdibleParts"/>),
        /// "more than one food" not when the wording gives alternatives and the food is one of
        /// them (<paramref name="alternativeNamed"/>): a line of alternatives counts its first by the
        /// compound lane's own rule, so the food of any one of them is not proven wrong; and another
        /// curated food's name only when a word of the wording is also missing from this food
        /// (<paramref name="all"/> holds a containment or head-noun hold): "black beans" on canned
        /// black beans names a less exact food, "olive oil" on canola oil another one.
        /// </summary>
        public static bool Refutes(GateHold hold, IReadOnlyCollection<GateHold> all, bool alternativeNamed = false)
        {
            if (!Refuting.Contains(hold.Code)) return false;
            return hold.Code switch
            {
                LinkGate.OtherFood => all.Any(h => h.Code is LinkGate.Containment or LinkGate.HeadNoun),
                LinkGate.Percent => hold.Detail?.StartsWith("wording states", StringComparison.Ordinal) == true,
                LinkGate.HeadNoun => hold.Detail?.StartsWith("with ", StringComparison.Ordinal) == true,
                LinkGate.Part => !EdibleParts.Contains((hold.Detail ?? string.Empty).Trim().ToLowerInvariant()),
                LinkGate.Compound => !alternativeNamed,
                _ => true,
            };
        }

        /// <summary>
        /// The holds a set reads: the alcohol rules only in the alcohol set, which reads each line by
        /// its own sense, or where the wording itself is refused (<paramref name="wordingRefused"/>);
        /// one line of a wording that reads as a drink or a flavouring never decides for all of its
        /// lines elsewhere.
        /// </summary>
        public static IReadOnlyList<GateHold> ForSet(string set, IReadOnlyList<GateHold> holds, bool wordingRefused) =>
            set == Alcohol || wordingRefused ? holds : holds.Where(h => !AlcoholCodes.Contains(h.Code)).ToList();

        /// <summary>
        /// The verdict on one wording's lines on one food. A set acts only when the gate holds the pair
        /// with one of its own rules (any rule for the applied links and aliases), or, for the
        /// container set, when the line rule finds the wrong form. Acting, a hold that refutes the food
        /// (<paramref name="refutes"/>, by default <see cref="Refutes"/>) moves the lines to the first
        /// food a tier names that the gate lets through, or failing that takes them back to an
        /// uncurated food of their wording; holds that refute nothing keep the lines where they are,
        /// counted as not proven. The container rule moves a line to its one canned sister and never
        /// takes it off its food (held).
        /// </summary>
        public static RecheckVerdict Decide(string set, IReadOnlyList<GateHold> holds, Func<IEnumerable<RecheckCandidate>> candidates, bool containerWrong = false, Func<GateHold, bool>? refutes = null)
        {
            var read = holds.Where(h => !Unread.Contains(h.Code)).ToList();
            if (set == ContainerForm)
            {
                if (!containerWrong) return new RecheckVerdict(Keep, null, null, read);
                foreach (var candidate in candidates())
                    if (Blocking(candidate.Tier, candidate.Holds).Count == 0) return new RecheckVerdict(Move, "container form", candidate, read);
                return new RecheckVerdict(Held, "container form, no canned sister", null, read);
            }

            var acting = ActingHolds(set, read);
            if (acting.Count == 0) return new RecheckVerdict(Keep, null, null, read);
            var refuting = acting.Where(refutes ?? (h => Refutes(h, read))).ToList();
            if (refuting.Count == 0) return new RecheckVerdict(Unproven, acting[0].ToString(), null, read);

            foreach (var candidate in candidates())
            {
                if (Blocking(candidate.Tier, candidate.Holds).Count == 0)
                    return new RecheckVerdict(Move, refuting[0].ToString(), candidate, read);
            }
            return new RecheckVerdict(Unlink, refuting[0].ToString(), null, read);
        }

        /// <summary>Whether a recipe line counts out a can, tin or jar ("1 can tomatoes", "2 (15 oz) cans beans"), not the verb ("you can use").</summary>
        public static bool IsContainerLine(string? raw)
        {
            var text = raw ?? string.Empty;
            foreach (Match match in ContainerWord.Matches(text))
            {
                if (!VerbBefore.IsMatch(text[..match.Index])) return true;
            }
            return false;
        }

        /// <summary>Whether a line says its food is drained and not that it is not.</summary>
        public static bool IsDrainedLine(string? raw) => raw != null && DrainedLine.IsMatch(raw) && !NotDrained.IsMatch(raw);

        /// <summary>Whether a USDA description names a canned form (canned, condensed, evaporated, jarred, bottled).</summary>
        public static bool IsCanned(string? text) => text != null && CannedFood.IsMatch(text);

        /// <summary>Whether a USDA description names drained solids.</summary>
        public static bool IsDrainedFood(string? text) => text != null && DrainedFood.IsMatch(text);

        /// <summary>
        /// Whether a line is on a food of the wrong form for it (containers 2.6): a can or jar line on
        /// a food with a USDA description that names no canned form, or a drained line on a canned
        /// food that is not the drained solids. A food with no USDA description is not judged.
        /// </summary>
        public static bool WrongForm(string? raw, GateFood food)
        {
            if (string.IsNullOrWhiteSpace(food.Description)) return false;
            if (IsContainerLine(raw) && !IsCanned(food.Description)) return true;
            return IsDrainedLine(raw) && IsCanned(food.Description) && !IsDrainedFood(food.Description) && !LiquidWording.IsMatch(IngredientNameNormalizer.Normalize(raw ?? string.Empty));
        }

        private static readonly Regex LiquidWording = new(@"\b(?:juices?|liquid|liquor|syrup|brine|water|milk|cream|stock|broth)\b", Options);

        /// <summary>
        /// The one curated canned food that shares the food's lead word and holds every word of the
        /// wording: drained when the line is drained, otherwise not; null when there is none or more
        /// than one.
        /// </summary>
        public static GateFood? CannedSister(GateFood food, string wording, bool drained, GateCatalog catalog)
        {
            if (LinkGate.LeadWord(food.Text) is not { } lead || !catalog.Siblings.TryGetValue(lead, out var siblings)) return null;
            var words = LinkGate.Words(IngredientNameNormalizer.Normalize(wording)).Select(LinkGate.Fold)
                .Where(w => !ContainerWords.Contains(w)).ToList();
            var sisters = siblings
                .Where(f => f.Id != food.Id && !f.Deleted && f.Status == (long)CurationStatusEnum.Curated && !PreparedFoods.IsPrepared(f.CategoryId)
                    && f.DataType != CatalogAdmissionRule.BrandedFood && (f.NutrientRows ?? 1) > 0
                    && IsCanned(f.Description) && IsDrainedFood(f.Description) == drained)
                .Where(f =>
                {
                    var text = LinkGate.Words(f.Text).Concat(LinkGate.Words(f.Name)).Select(LinkGate.Fold).ToHashSet(StringComparer.Ordinal);
                    return words.All(text.Contains) && Additions(f.Text).All(words.Contains)
                        && LinkGate.Words(f.Text).Where(w => LinkGate.States.ContainsKey(w) && w is not ("canned" or "drained")).All(w => LinkGate.States[w].Any(words.Contains));
                })
                .ToList();
            return sisters.Count == 1 ? sisters[0] : null;
        }

        private static readonly HashSet<string> ContainerWords = new(StringComparer.Ordinal) { "can", "cans", "tin", "tins", "jar", "jars", "drained", "rinsed", "undrained" };

        private static readonly HashSet<string> AdditionNeutral = new(StringComparer.Ordinal) { "and", "salt", "added", "sodium", "liquid", "juice", "water", "syrup", "light", "heavy", "packed", "pack" };

        /// <summary>The food words a description adds after "with" ("Corn with red and green peppers, canned"): a canned sister must not add a food the wording does not name.</summary>
        private static IEnumerable<string> Additions(string text) =>
            text.Split(',').SelectMany(segment =>
            {
                var words = LinkGate.Words(segment).Select(LinkGate.Fold).ToList();
                var with = words.IndexOf("with");
                return with < 0 ? Enumerable.Empty<string>() : words.Skip(with + 1).Where(w => !AdditionNeutral.Contains(w));
            });

        /// <summary>
        /// Whether a curated food's name says something its USDA entry contradicts: a colour, "whole"
        /// or "seed" the entry lacks while the entry names a form ("ground", "powder", "dried",
        /// "flakes") the name lacks. "Black Mustard Seed" for "Spices, mustard seed, ground" is listed;
        /// "Fresh Squid" is not.
        /// </summary>
        public static bool NameContradicts(string name, string? description)
        {
            if (string.IsNullOrWhiteSpace(description)) return false;
            var nameWords = LinkGate.Words(name).Select(LinkGate.Fold).ToHashSet(StringComparer.Ordinal);
            var entry = LinkGate.Words(description).Select(LinkGate.Fold).ToHashSet(StringComparer.Ordinal);
            var claims = nameWords.Where(w => NameColourWords.Contains(w) && !entry.Contains(w)).ToList();
            var forms = entry.Where(w => DescriptionFormWords.Contains(w) && !nameWords.Contains(w)).ToList();
            return claims.Count > 0 && forms.Count > 0;
        }

        /// <summary>The name a food is to get from <see cref="NameFixes"/>, or null when it has it or its name no longer carries the wrong word.</summary>
        public static string? NameFix(string? fdcId, string name)
        {
            if (fdcId == null || !NameFixes.TryGetValue(fdcId, out var fix) || string.Equals(name, fix.Name, StringComparison.Ordinal)) return null;
            return LinkGate.Words(name).Select(LinkGate.Fold).Contains(LinkGate.Fold(fix.WrongWord)) ? fix.Name : null;
        }

        internal static string SignatureSource()
        {
            var parts = new List<string> { Version, LinkGate.Signature, AlcoholLineContext.Signature };
            parts.AddRange(Sets);
            parts.Add(string.Join(",", Refuting.OrderBy(c => c, StringComparer.Ordinal)));
            parts.Add(string.Join(",", EdibleParts.OrderBy(c => c, StringComparer.Ordinal)));
            parts.Add(string.Join(",", AdditionNeutral.OrderBy(c => c, StringComparer.Ordinal)));
            parts.Add(LiquidWording.ToString());
            parts.Add(string.Join(",", Unread.OrderBy(c => c, StringComparer.Ordinal)));
            parts.Add(string.Join(",", IdentityCodes.OrderBy(c => c, StringComparer.Ordinal)));
            parts.AddRange(Acting.OrderBy(a => a.Key, StringComparer.Ordinal).Select(a => a.Key + "=" + string.Join(",", a.Value)));
            parts.Add(ContainerWord.ToString());
            parts.Add(VerbBefore.ToString());
            parts.Add(DrainedLine.ToString());
            parts.Add(NotDrained.ToString());
            parts.Add(CannedFood.ToString());
            parts.Add(string.Join(",", ContainerWords.OrderBy(w => w, StringComparer.Ordinal)));
            parts.Add(NovemberThird.ToString("O"));
            return string.Join("\n", parts);
        }
    }
}
