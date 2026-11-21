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
    /// What the re-check does with the lines of one wording on one food: keep them, move them to
    /// the food a tier names (<see cref="Target"/>), or move them back to an uncurated food of their
    /// wording; with the rule that decided and the gate's holds on the food they are on.
    /// </summary>
    public sealed record RecheckVerdict(string Outcome, string? Reason, RecheckCandidate? Target, IReadOnlyList<GateHold> Holds);

    /// <summary>
    /// The rules of the applied-link re-check (plan C3, alcohol A4, wordings 2.7, containers 2.6):
    /// which link gate holds each rule set acts on, which of them are hard enough to take a line off
    /// its food when no tier names another, the container form line rule and its canned sister, and
    /// the catalog-name check. No model; every decision is a rule. See docs/architecture/link-recheck.md.
    /// </summary>
    public static class LinkRecheckRules
    {
        public const string Version = "link-recheck/1";

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
        /// Holds that take a line off its food when no tier names another: the wording is a heading or
        /// more than one food, the food is retired, branded or a prepared dish, the line's alcohol
        /// sense refuses it, or the food is a part, a dried herb for a fresh one, another form or a
        /// marked variant. No food is better than a wrong one.
        /// </summary>
        public static readonly IReadOnlySet<string> Hard = new HashSet<string>(StringComparer.Ordinal)
        {
            LinkGate.Heading, LinkGate.Compound, LinkGate.Retired, LinkGate.Branded, LinkGate.Prepared,
            LinkGate.AlcoholDrink, LinkGate.AlcoholNoFood, LinkGate.AlcoholNotDrink,
            LinkGate.Part, LinkGate.FreshHerb, LinkGate.Form, LinkGate.Marked,
        };

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
            holds.Where(h => !Unread.Contains(h.Code) && !(tier is AlcoholSenseTier or CannedSisterTier && IdentityCodes.Contains(h.Code))).ToList();

        /// <summary>
        /// The verdict on one wording's lines on one food. A set acts only when the gate holds the pair
        /// with one of its own rules (any rule for the applied links and aliases), or, for the
        /// container set, when the line rule finds the wrong form. Acting, the lines move to the first
        /// food a tier names that the gate lets through; failing that, a hard hold takes them back to
        /// an uncurated food of their wording, and anything else keeps them where they are (held).
        /// The container rule never takes a line off its food.
        /// </summary>
        public static RecheckVerdict Decide(string set, IReadOnlyList<GateHold> holds, Func<IEnumerable<RecheckCandidate>> candidates, bool containerWrong = false)
        {
            var read = holds.Where(h => !Unread.Contains(h.Code)).ToList();
            var acting = set == ContainerForm ? new List<GateHold>() : ActingHolds(set, read);
            if (acting.Count == 0 && !containerWrong) return new RecheckVerdict(Keep, null, null, read);

            foreach (var candidate in candidates())
            {
                if (Blocking(candidate.Tier, candidate.Holds).Count == 0)
                    return new RecheckVerdict(Move, containerWrong ? "container form" : acting[0].ToString(), candidate, read);
            }

            var hard = acting.FirstOrDefault(h => Hard.Contains(h.Code));
            if (hard != null) return new RecheckVerdict(Unlink, hard.ToString(), null, read);
            return new RecheckVerdict(Held, containerWrong ? "container form, no canned sister" : acting[0].ToString(), null, read);
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
            return IsDrainedLine(raw) && IsCanned(food.Description) && !IsDrainedFood(food.Description);
        }

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
                    return words.All(text.Contains);
                })
                .ToList();
            return sisters.Count == 1 ? sisters[0] : null;
        }

        private static readonly HashSet<string> ContainerWords = new(StringComparer.Ordinal) { "can", "cans", "tin", "tins", "jar", "jars", "drained", "rinsed", "undrained" };

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
            parts.Add(string.Join(",", Hard.OrderBy(c => c, StringComparer.Ordinal)));
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
