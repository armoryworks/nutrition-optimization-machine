using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
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
    /// the rule that decided and the gate's holds on the food they are on. <see cref="Class"/> is
    /// the re-check class of the refuting hold; when that class is not proven
    /// (<see cref="LinkRecheckRules.ProvenClasses"/>) the outcome is not proven and
    /// <see cref="Candidate"/> holds the move or take-back it would make, handed to the model audit.
    /// <see cref="Classes"/> holds every class whose hold refutes the food, whichever decided: each
    /// of them would make this move or take-back once proven, so each is scored on it.
    /// </summary>
    public sealed record RecheckVerdict(string Outcome, string? Reason, RecheckCandidate? Target, IReadOnlyList<GateHold> Holds, string? Class = null, string? Candidate = null, IReadOnlyList<string>? Classes = null);

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
        public const string Version = "link-recheck/8";

        public const string Alcohol = "alcohol";
        public const string PartHerb = "part-herb";
        public const string Compound = "compound";
        public const string ContainerForm = "container-form";
        public const string Prepared = "prepared";
        public const string FreeOf = "free-of";
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

        /// <summary>The re-check class of the three alcohol sense rules, which the alcohol set reads as one.</summary>
        public const string AlcoholClass = "alcohol";

        /// <summary>The re-check class of the container form line rule.</summary>
        public const string ContainerClass = "container-form";

        /// <summary>The precision a re-check class must hold on its judged golden lines (link-recheck.tsv) before it may move or take back a line by rule.</summary>
        public const double ClassPrecisionBar = 0.95;

        /// <summary>The judged golden lines a re-check class needs before its precision counts.</summary>
        public const int ClassMinimumJudged = 40;

        /// <summary>
        /// The re-check classes that may move or take back a line by rule: each held at least
        /// <see cref="ClassPrecisionBar"/> of its checkpoint-judged golden lines right over at least
        /// <see cref="ClassMinimumJudged"/> of them (link-recheck.tsv rows with status agent: the lines
        /// both staging checkpoints judged; rule-given rows must be right too but do not count toward
        /// the minimum). The set is read from the committed per-class record
        /// (<see cref="LinkRecheckClassRecord"/>), never written by hand: the golden tests re-score the
        /// judged lines, refuse a record that differs and refuse a class here that is below the bar, and
        /// a record scored under other rules proves nothing (<see cref="ProvenProblem"/>). Every other
        /// class refutes nothing by rule: its lines stay, counted as candidates, recorded on its own tier
        /// of the lane frame (<see cref="CandidateTier"/>), where the link auditor decides them in
        /// shadow, and they are written only while that tier is live.
        /// </summary>
        public static IReadOnlySet<string> ProvenClasses => ProvenOverride.Value ?? ProvenValue.Value.Proven;

        /// <summary>Why the committed per-class record proves no class on this build, or null when it is current.</summary>
        public static string? ProvenProblem => ProvenValue.Value.Problem;

        private static readonly Lazy<(IReadOnlySet<string> Proven, string? Problem)> ProvenValue = new(() =>
            LinkRecheckClassRecord.Evaluate(LinkRecheckClassRecord.Embedded(), Version, DecisionSignature));

        private static readonly AsyncLocal<IReadOnlySet<string>?> ProvenOverride = new();

        /// <summary>For tests of the machinery alone: the classes taken as proven in this async flow until the result is disposed, whatever the record proves. The signature still follows the record.</summary>
        internal static IDisposable AssumeProven(params string[] classes)
        {
            var before = ProvenOverride.Value;
            ProvenOverride.Value = new HashSet<string>(classes, StringComparer.Ordinal);
            return new Restore(() => ProvenOverride.Value = before);
        }

        private sealed class Restore(Action undo) : IDisposable
        {
            public void Dispose() => undo();
        }

        /// <summary>The prefix of the lane frame tier a re-check class that is not proven decides its candidates on, apart from the set they came from.</summary>
        public const string CandidateTierPrefix = "candidate:";

        /// <summary>The lane frame tier on which a class's candidates are recorded, audited, qualified, paused and undone.</summary>
        public static string CandidateTier(string reClass) => CandidateTierPrefix + reClass;

        /// <summary>The class of a candidate tier, or null for the tier of a set.</summary>
        public static string? ClassOfTier(string tier) => tier.StartsWith(CandidateTierPrefix, StringComparison.Ordinal) ? tier[CandidateTierPrefix.Length..] : null;

        /// <summary>The signature of a tier's decisions on the lane frame: a set's (<see cref="SetSignature"/>) or a candidate tier's.</summary>
        public static string TierSignature(string tier) =>
            ClassOfTier(tier) == null ? SetSignature(tier) : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Signature + "|" + tier)))[..16].ToLowerInvariant();

        /// <summary>Every re-check class that is not proven, each of which decides its candidates on a tier of its own.</summary>
        public static IReadOnlyList<string> CandidateClasses =>
            Refuting.Select(code => AlcoholCodes.Contains(code) ? AlcoholClass : code).Append(ContainerClass)
                .Distinct(StringComparer.Ordinal).Where(c => !Proven(c)).OrderBy(c => c, StringComparer.Ordinal).ToList();

        /// <summary>The re-check class of a refuting hold: the three alcohol rules are one class, every other rule its own.</summary>
        public static string ClassOf(GateHold hold) => AlcoholCodes.Contains(hold.Code) ? AlcoholClass : hold.Code;

        /// <summary>Whether a re-check class may move or take back a line by rule.</summary>
        public static bool Proven(string? reClass) => reClass != null && ProvenClasses.Contains(reClass);

        /// <summary>The day the unguarded model auto-apply ran (links design 1); its links are re-checked first.</summary>
        public static readonly DateTime NovemberThird = new(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>The rule sets that write, in the order the plan runs them (plan 3.3 step 8), with prepared foods before the applied links.</summary>
        public static readonly IReadOnlyList<string> Sets = new[] { Alcohol, PartHerb, Compound, ContainerForm, Prepared, FreeOf, AppliedNovember, Applied, Aliases };

        /// <summary>Every set the re-check knows, the report-only <see cref="Wordings"/> last.</summary>
        public static readonly IReadOnlyList<string> AllSets = Sets.Append(Wordings).ToList();

        /// <summary>
        /// Holds that are positive evidence that a link's food is wrong, and so move its lines or take
        /// them off their food when no tier names another: the wording is a heading or more than one
        /// food, the food is retired, branded or a prepared dish, the line's alcohol sense refuses
        /// it, the wording names a part or form the food contradicts, a fresh herb on the dried
        /// spice, a food made from the wording's head noun or only topped with it, a salted or
        /// sweetened polarity, a stated percent the food lacks, a group word its category
        /// contradicts, another curated food's very name, or a wording marked free of what the food's own name holds
        /// (<see cref="FreeOfHold"/>: "vegan butter" on Butter). The rest (a word the food lacks, a
        /// state or marked variant the wording does not name, a variety, an incomplete food ...)
        /// only fail to prove the food right: such lines stay and are counted as not proven.
        /// </summary>
        public static readonly IReadOnlySet<string> Refuting = new HashSet<string>(StringComparer.Ordinal)
        {
            LinkGate.Heading, LinkGate.Compound, LinkGate.Retired, LinkGate.Branded, LinkGate.Prepared,
            LinkGate.AlcoholDrink, LinkGate.AlcoholNoFood, LinkGate.AlcoholNotDrink,
            LinkGate.Part, LinkGate.FreshHerb, LinkGate.Form, LinkGate.HeadLead, LinkGate.HeadNoun, LinkGate.Polarity,
            LinkGate.Percent, LinkGate.Group, LinkGate.OtherFood, FreeOf,
        };

        /// <summary>The alcohol rules: read by the line in the alcohol set, and elsewhere only when the wording itself refuses the food.</summary>
        public static readonly IReadOnlySet<string> AlcoholCodes = new HashSet<string>(StringComparer.Ordinal) { LinkGate.AlcoholDrink, LinkGate.AlcoholNoFood, LinkGate.AlcoholNotDrink };

        /// <summary>The part segments that are the ordinary eaten part of a food named whole ("Potatoes, baked, flesh", "Goose, meat only"): never proof that a link is wrong.</summary>
        public static readonly IReadOnlySet<string> EdibleParts = new HashSet<string>(StringComparer.Ordinal) { "flesh", "flesh only", "meat", "meat only", "leaf", "leaves", "separable lean only", "lean only" };

        /// <summary>The forms a cook gives a food at home (cooked, roasted, toasted, grilled, browned ...) or keeps it in (frozen): a line asking for one ("toasted pine nuts", "roasted garlic", "frozen mango chunks") is the same food prepared or kept, so the form rule's hold on it is no proof that its food is wrong.</summary>
        public static readonly IReadOnlySet<string> PreparationForms = new HashSet<string>(StringComparer.Ordinal)
        {
            "cooked", "boiled", "stewed", "baked", "fried", "steamed", "braised", "grilled", "broiled", "poached", "roasted", "toasted", "browned", "frozen",
        };

        /// <summary>The classes whose hold a later alternative of the wording can raise, and which are no proof when the wording names its food first.</summary>
        public static readonly IReadOnlySet<string> AlternativeCodes = new HashSet<string>(StringComparer.Ordinal)
        {
            LinkGate.Part, LinkGate.Group, LinkGate.Form, LinkGate.Percent, LinkGate.Polarity, FreeOf,
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
            LinkGate.Percent, LinkGate.RuleOther, LinkGate.OtherAlias, LinkGate.Variety,
        };

        private static readonly Dictionary<string, string[]> Acting = new(StringComparer.Ordinal)
        {
            [Alcohol] = new[] { LinkGate.AlcoholDrink, LinkGate.AlcoholNoFood, LinkGate.AlcoholNotDrink },
            [PartHerb] = new[] { LinkGate.Part, LinkGate.FreshHerb },
            [Compound] = new[] { LinkGate.Compound, LinkGate.Heading },
            [Prepared] = new[] { LinkGate.Prepared },
            [FreeOf] = new[] { FreeOf },
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

        /// <summary>The signature of the rules alone, without <see cref="ProvenClasses"/>: what the per-class record is scored under.</summary>
        public static string DecisionSignature => DecisionSignatureValue.Value;

        private static readonly Lazy<string> DecisionSignatureValue = new(() =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(SignatureSource(withProven: false))))[..16].ToLowerInvariant());

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
        /// addition, a part the wording names that the food lacks always, and the food's own part
        /// segment only when it is not the ordinary eaten part (<see cref="EdibleParts"/>),
        /// "more than one food" not when the wording gives alternatives and the food is one of
        /// them (<paramref name="alternativeNamed"/>): a line of alternatives counts its first by the
        /// compound lane's own rule, so the food of any one of them is not proven wrong; and another
        /// curated food's name only when a word of the wording is also missing from this food
        /// (<paramref name="all"/> holds a containment or head-noun hold): "black beans" on canned
        /// black beans names a less exact food, "olive oil" on canola oil another one. A part, group,
        /// form, percent or polarity hold is no proof when the wording names the food as its first
        /// alternative (<paramref name="firstNamed"/>; "butter or margarine" on Butter), and a form
        /// hold none when the form is one a cook gives the food at home (<see cref="PreparationForms"/>).
        /// "More than one food" is proof only when the wording names another food
        /// (<paramref name="namesOtherFood"/>, <see cref="LinkGate.NamesOtherFood"/>): "heavy cream,
        /// whipped" and "dates stoned and sliced" are one food with a preparation.
        /// </summary>
        public static bool Refutes(GateHold hold, IReadOnlyCollection<GateHold> all, bool alternativeNamed = false, bool firstNamed = false, bool namesOtherFood = true)
        {
            if (!Refuting.Contains(hold.Code)) return false;
            if (firstNamed && AlternativeCodes.Contains(hold.Code)) return false;
            return hold.Code switch
            {
                LinkGate.OtherFood => all.Any(h => h.Code is LinkGate.Containment or LinkGate.HeadNoun),
                LinkGate.Percent => hold.Detail?.StartsWith("wording states", StringComparison.Ordinal) == true,
                LinkGate.HeadNoun => hold.Detail?.StartsWith("with ", StringComparison.Ordinal) == true,
                LinkGate.Part => hold.Rule != LinkGate.FoodPartRule || !EdibleParts.Contains((hold.Detail ?? string.Empty).Trim().ToLowerInvariant()),
                LinkGate.Compound => !alternativeNamed && namesOtherFood,
                LinkGate.Form => !PreparationForms.Contains(hold.Detail ?? string.Empty),
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
                {
                    if (Blocking(candidate.Tier, candidate.Holds).Count > 0) continue;
                    return Proven(ContainerClass)
                        ? new RecheckVerdict(Move, "container form", candidate, read, ContainerClass, Classes: new[] { ContainerClass })
                        : new RecheckVerdict(Unproven, "container form", candidate, read, ContainerClass, Move, new[] { ContainerClass });
                }
                return new RecheckVerdict(Held, "container form, no canned sister", null, read);
            }

            var acting = ActingHolds(set, read);
            if (acting.Count == 0) return new RecheckVerdict(Keep, null, null, read);
            var refuting = acting.Where(refutes ?? (h => Refutes(h, read))).ToList();
            if (refuting.Count == 0) return new RecheckVerdict(Unproven, acting[0].ToString(), null, read);

            var proven = refuting.FirstOrDefault(h => Proven(ClassOf(h)));
            var decisive = proven ?? refuting[0];
            var target = candidates().FirstOrDefault(c => Blocking(c.Tier, c.Holds).Count == 0);
            var outcome = target != null ? Move : Unlink;
            var classes = refuting.Select(ClassOf).Distinct(StringComparer.Ordinal).ToList();
            return proven != null
                ? new RecheckVerdict(outcome, decisive.ToString(), target, read, ClassOf(decisive), Classes: classes)
                : new RecheckVerdict(Unproven, decisive.ToString(), target, read, ClassOf(decisive), outcome, classes);
        }

        /// <summary>
        /// The verdict on one wording's lines on one food, as a run decides it: the gate's holds on
        /// the pair with its lines, read as the set reads them (<see cref="ReadHolds"/>), each tier's
        /// food asked the same way, and <see cref="Decide"/> with <see cref="Refutes"/> (a line of
        /// alternatives one of which is the food is no proof). A fresh-herb hold is no proof when every
        /// fresh cue qualifies a later item of the wording or its lines (<see cref="FreshOfLaterItem"/>),
        /// and no hold but an alcohol, fresh-herb, prepared or free-of one is proof when only the part after a ratio phrase, or the amount
        /// before it, raises it (<see cref="RatioCut"/>; the lines are read cut the same way). The wording's <see cref="FreeOfHold"/> is read
        /// beside the gate's holds, on the food and on every tier's food. A run and the class scorer both decide through this, so the
        /// judged golden lines measure what a run writes.
        /// </summary>
        public static RecheckVerdict Judge(string set, string wording, GateFood food, IReadOnlyList<GateLine>? lines, GateCatalog catalog, IEnumerable<(string Tier, GateFood Food)> candidates)
        {
            var holds = WithFreeOf(ReadHolds(set, wording, food, LinkGate.Check(new GatePair(wording, food.Id, food, Lines: lines), catalog)), wording, food);
            var alternative = new Lazy<bool>(() => LinkGate.NamesAlternative(wording, food, catalog));
            var first = new Lazy<bool>(() => LinkGate.NamesFirstAlternative(wording, food, catalog));
            var other = new Lazy<bool>(() => LinkGate.NamesOtherFood(wording, food, catalog));
            var freshLater = new Lazy<bool>(() =>
            {
                var cued = new[] { wording }.Concat((lines ?? Array.Empty<GateLine>()).Select(l => l.Raw ?? string.Empty)).Where(HasFreshCue).ToList();
                return cued.Count > 0 && cued.All(t => FreshOfLaterItem(t, food));
            });
            var beforeRatio = new Lazy<IReadOnlyList<GateHold>?>(() =>
                RatioCut(wording) is { } cut ? LinkGate.Check(new GatePair(cut, food.Id, food, Lines: lines?.Select(l => l with { Raw = RatioCut(l.Raw) ?? l.Raw }).ToList()), catalog) : null);
            bool Proof(GateHold h) =>
                Refutes(h, holds, alternative.Value, AlternativeCodes.Contains(h.Code) && first.Value, h.Code != LinkGate.Compound || other.Value)
                && !(h.Code == LinkGate.FreshHerb && freshLater.Value)
                && !(!AlcoholCodes.Contains(h.Code) && h.Code is not (LinkGate.FreshHerb or LinkGate.Prepared or FreeOf) && beforeRatio.Value is { } cutHolds && cutHolds.All(c => c.Code != h.Code));
            return Decide(set, holds,
                () => candidates.Select(c => new RecheckCandidate(c.Tier, c.Food, WithFreeOf(ReadHolds(set, wording, c.Food, LinkGate.Check(new GatePair(wording, food.Id, c.Food, Lines: lines), catalog)), wording, c.Food))),
                set == ContainerForm, Proof);
        }

        /// <summary>
        /// The phrases that measure a food against another ("sugar per quart of juice", "sugar, 10 oz. for each pound of juice",
        /// "to every pint", "sugar to a gallon of vinegar"): what follows names the measure, never a second food or the food's
        /// group (staging checkpoint 5, Q15; the gate reads its part rule the same way). A bare "each" or "every" is none: "1 cup
        /// each sugar and flour" spreads one amount over two foods.
        /// </summary>
        private static readonly Regex RatioPhrase = new(@"\s(?:per|(?:for|to)\s+(?:each|every)|to\s+an?\s+(?:\p{L}+\s+)?of)\s", Options);

        /// <summary>The wording before its first ratio phrase (<see cref="RatioPhrase"/>), or null when it has none after a word.</summary>
        public static string? BeforeRatio(string wording)
        {
            var match = RatioPhrase.Match(wording ?? string.Empty);
            if (!match.Success) return null;
            var cut = wording![..match.Index].TrimEnd(' ', ',', ';');
            return LinkGate.Words(cut).Any(w => w.Any(char.IsLetter)) ? cut : null;
        }

        private static readonly Regex AmountSegment = new(
            @"^\s*(?:in\s+the\s+proportion\s+of\s+|about\s+|nearly\s+|scant\s+|full\s+)?(?:(?:\d+(?:[./]\d+)?|[½¼¾⅓⅔⅛]|a|an|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|fourteen|fifteen|sixteen|twenty|thirty|half|quarter|and|or|to|-)[\s.]*)+(?:(?:heaping|level|rounded|scant|large|small)\s+)?(?:cups?|cupfuls?|teacups?|teacupfuls?|spoons?|spoonfuls?|teaspoons?|teaspoonfuls?|tablespoons?|tablespoonfuls?|dessertspoons?|dessertspoonfuls?|tsps?|tbsps?|gallons?|quarts?|pints?|gills?|pounds?|lbs?|ounces?|oz|drachms?|grams?|g|kilos?|kg|dozen|handfuls?|pinch(?:es)?|dash(?:es)?|drops?|liters?|litres?|ml)?[\s.]*$",
            Options);

        /// <summary>
        /// The wording a ratio phrase measures (<see cref="BeforeRatio"/>) with its amount left out: a segment after a comma
        /// that only states how much ("sugar, fourteen ounces for each pound of fruit", "flour, in the proportion of 1/2 oz. to
        /// every 1/2 pint of gravy") names no second food (staging checkpoint 6, R18). Null when the wording has no ratio phrase.
        /// </summary>
        public static string? RatioCut(string? wording)
        {
            if (wording == null || BeforeRatio(wording) is not { } cut) return null;
            var segments = cut.Split(',').ToList();
            while (segments.Count > 1 && AmountSegment.IsMatch(segments[^1])) segments.RemoveAt(segments.Count - 1);
            return string.Join(",", segments).TrimEnd(' ', ',', ';');
        }

        private static readonly Regex ItemBreak = new(@"[,;:()]|\s(?:or|and/or)\s", Options);

        private static readonly HashSet<string> ItemFiller = new(StringComparer.Ordinal)
        {
            "of", "the", "a", "an", "and", "or", "some", "few", "about", "optional", "inch", "piece", "small", "large", "medium", "teaspoon",
            "tablespoon", "cup", "tsp", "tbsp", "oz", "ounce", "pound", "lb", "pinch", "dash", "stick", "sprig", "leaf", "handful", "bunch", "stalk",
        };

        private static bool HasFreshCue(string text) => LinkGate.Words(text).Any(LinkGate.FreshCues.Contains);

        /// <summary>
        /// Whether every fresh cue of a text qualifies a later item, not the food named first: the cue stands in an item after
        /// the first one that names a word of the food, and a word follows it there that the first item does not hold
        /// ("1 cinnamon stick, 1-inch piece of fresh turmeric" on Cinnamon; "cayenne pepper, 1/2 teaspoon, or chopped sweet
        /// pepper" on Cayenne Pepper: staging checkpoint 5, Q14). A cue after its food ("rosemary, chopped") or before the
        /// food again ("rosemary, 2 sprigs fresh rosemary") qualifies the food.
        /// </summary>
        public static bool FreshOfLaterItem(string text, GateFood food)
        {
            var foodWords = LinkGate.Words(food.Name).Select(LinkGate.Fold).Where(w => !ItemFiller.Contains(w)).ToHashSet(StringComparer.Ordinal);
            var items = ItemBreak.Split(text ?? string.Empty).Select(i => LinkGate.Words(i).ToList()).ToList();
            var firstItem = items.FindIndex(i => i.Select(LinkGate.Fold).Any(foodWords.Contains));
            if (firstItem < 0) return false;
            static IEnumerable<string> Content(IEnumerable<string> words) =>
                words.Where(w => w.Any(char.IsLetter) && !LinkGate.FreshCues.Contains(w)).Select(LinkGate.Fold).Where(w => !ItemFiller.Contains(w));
            var first = Content(items[firstItem]).ToHashSet(StringComparer.Ordinal);
            var cues = 0;
            for (var i = 0; i < items.Count; i++)
            {
                for (var j = 0; j < items[i].Count; j++)
                {
                    if (!LinkGate.FreshCues.Contains(items[i][j])) continue;
                    cues++;
                    if (i <= firstItem) return false;
                    var after = Content(items[i].Skip(j + 1)).ToList();
                    if (after.Count == 0 || after.All(first.Contains)) return false;
                }
            }
            return cues > 0;
        }

        /// <summary>
        /// The markers that say a food is made without what a default restriction type holds, each with the types whose
        /// name patterns read what it leaves out (staging checkpoint 6, R6 and the diet coverage report's link errors:
        /// "vegan chocolate ice cream" on Chocolate Ice Cream, "gluten-free rolled oats" on Whole Rolled Oats).
        /// </summary>
        public static readonly IReadOnlyList<(string Marker, Regex Pattern, string[] Types)> FreeMarkers = new (string, Regex, string[])[]
        {
            ("vegan", new(@"\b(?:vegan|plant[- ]based)\b", Options), new[] { "Vegan" }),
            ("dairy-free", new(@"\b(?:dairy[- ]?free|non[- ]?dairy|milk[- ]?free)\b", Options), new[] { "Milk Allergy" }),
            ("lactose-free", new(@"\blactose[- ]?free\b", Options), new[] { "Lactose Intolerance" }),
            ("gluten-free", new(@"\bgluten[- ]?free\b", Options), new[] { "Celiac Disease" }),
            ("egg-free", new(@"\b(?:egg[- ]?free|eggless)\b", Options), new[] { "Egg Allergy" }),
            ("nut-free", new(@"\bnut[- ]?free\b", Options), new[] { "Tree Nut Allergy", "Peanut Allergy" }),
            ("soy-free", new(@"\bsoy[- ]?free\b", Options), new[] { "Soy Allergy" }),
            ("meatless", new(@"\b(?:meatless|meat[- ]?free)\b", Options), new[] { "Vegetarian" }),
        };

        private static readonly Regex FirstItemEnd = new(@"[(\[,;:]|\s(?:or|and/or)\s|/|\s-+\s", Options);

        private static readonly Regex FreeFood = new(@"\b(?:imitation|substitutes?|meatless|vegetarian|tofu|non[- ]?soy|(?:soy|almond|oat|rice|coconut|cashew|hemp)[- ]?(?:milk|beverage|drink|creamer)s?)\b", Options);

        /// <summary>The stems the free-of set's line query looks for before <see cref="FreeOfHold"/> reads a line.</summary>
        public static readonly IReadOnlyList<string> FreeOfStems = new[] { "vegan", "plant", "free", "dairy", "eggless", "meatless" };

        /// <summary>Whether a text carries a marker of <see cref="FreeMarkers"/>.</summary>
        public static bool HasFreeMarker(string? text) => !string.IsNullOrEmpty(text) && FreeMarkers.Any(m => m.Pattern.IsMatch(text));

        /// <summary>The restriction types the markers of <see cref="FreeMarkers"/> in a wording's first item name, the item cut as <see cref="FreeOfHold"/> cuts it ("vegan butter": Vegan; "butter or vegan butter": none).</summary>
        public static IReadOnlyList<string> FreeMarkedTypes(string? wording)
        {
            var first = FirstItemEnd.Split(wording ?? string.Empty)[0];
            return FreeMarkers.Where(m => m.Pattern.IsMatch(first)).SelectMany(m => m.Types).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// The <see cref="FreeOf"/> hold of a wording on a food: the wording's first item (before a bracket, a comma, a
        /// semicolon or colon, an "or", a slash or a spaced dash) carries a marker of <see cref="FreeMarkers"/>, the food's own
        /// name and USDA description carry no marker and do not name the food as a stand-in (imitation, substitute, meatless,
        /// vegetarian, tofu, a plant milk), and a name pattern of a type the marker names reads the food's name or description, so the
        /// food holds what the wording says the cook's food is made without. Null when it does not hold.
        /// </summary>
        public static GateHold? FreeOfHold(string wording, GateFood food)
        {
            var first = FirstItemEnd.Split(wording ?? string.Empty)[0];
            var own = food.Name + " " + food.Description;
            if (FreeFood.IsMatch(own) || HasFreeMarker(own)) return null;
            foreach (var (marker, pattern, types) in FreeMarkers)
            {
                if (!pattern.IsMatch(first)) continue;
                foreach (var type in types)
                {
                    if (!DefaultRestrictionCriteria.ByName.TryGetValue(type, out var criteria)) continue;
                    var hit = criteria.Patterns.Where(p => !DietCriteriaMatcher.IsWordPattern(p)).Select(p => DietPatternRule.For(p, type)).FirstOrDefault(r => Names(r, food));
                    if (hit != null) return new GateHold(FreeOf, "wording marked free of what the food holds", $"{marker} against {type}: {hit.Pattern}");
                }
            }
            return null;
        }

        private static IReadOnlyList<GateHold> WithFreeOf(IReadOnlyList<GateHold> holds, string wording, GateFood food) =>
            FreeOfHold(wording, food) is { } hold ? holds.Append(hold).ToList() : holds;

        /// <summary>The holds of a pair as the set reads them: the alcohol rules only where the set reads them (<see cref="ForSet"/>).</summary>
        public static IReadOnlyList<GateHold> ReadHolds(string set, string wording, GateFood food, IReadOnlyList<GateHold> holds) =>
            ForSet(set, holds, AlcoholLineContext.Refuse(AlcoholLineContext.Read(wording), food.FdcId, food.Text) != null);

        /// <summary>
        /// The foods the deterministic tiers name for a wording, in the pipeline's order: its
        /// verified wording row's food (<paramref name="verifiedFood"/>), a trusted alias, a staple or
        /// the name matcher, a curated food of its name and the drink its lines' alcohol sense allows
        /// (<paramref name="byFdc"/>: the allowed USDA foods by FDC id); for the container set only
        /// the one canned sister. The food the lines are on is never a candidate.
        /// </summary>
        public static List<(string Tier, long Id)> CandidateIds(string set, string wording, string? sense, bool drained, GateFood food, IReadOnlyList<GateLine>? lines, GateCatalog catalog,
            long? verifiedFood, IReadOnlyDictionary<string, GateFood> byFdc)
        {
            var list = new List<(string Tier, long Id)>();
            void Add(string tier, long? id)
            {
                if (id is { } value && value != food.Id && list.All(c => c.Id != value)) list.Add((tier, value));
            }

            if (set == ContainerForm)
            {
                Add(CannedSisterTier, CannedSister(food, wording, drained, catalog)?.Id);
                return list;
            }

            var normalized = IngredientNameNormalizer.Normalize(wording);
            Add(VerifiedWordingTier, verifiedFood);
            if (catalog.TrustedAliases.TryGetValue(IngredientNameNormalizer.WordingKey(wording), out var owner)) Add(TrustedAliasTier, owner);
            if (catalog.Matcher != null && IngredientLinkService.DeterministicTarget(normalized.ToLowerInvariant(), catalog.Matcher, catalog.Staples) is { } rule)
                Add(NameRuleTier, rule.Food.IngredientId);
            Add(CuratedNameTier, LineRelinkService.TargetFor(normalized, food.Id, food.Name.Trim(), catalog.Curated));

            sense ??= lines == null ? null : AlcoholLineContext.Shared(lines.Select(l => AlcoholLineContext.Read(l.Raw, l.Recipe)))?.Sense;
            if (sense != null && AlcoholLineContext.KindOf(sense) == AlcoholSenseKind.Drink)
            {
                foreach (var fdc in AlcoholLineContext.Allowed(sense))
                    if (byFdc.TryGetValue(fdc, out var drink)) Add(AlcoholSenseTier, drink.Id);
            }
            return list;
        }

        /// <summary>
        /// Whether a move may give the wording a trusted alias on the food its lines moved to: only
        /// when the gate, read as for any wording and not as the tier that vouched for the lines,
        /// lets the wording through on that food, and the wording names no restricted food that the
        /// food's own name and USDA entry do not (<see cref="RestrictedWords"/>). The lines move
        /// either way; only the alias, which every later import and the diet gate read, waits for
        /// the gate ("corn brandy" on distilled spirits made Corn Allergy keep out every brandy recipe).
        /// </summary>
        public static bool MayAlias(string wording, GateFood target, GateCatalog catalog) =>
            RestrictedWords(wording, target).Count == 0
            && LinkGate.Check(new GatePair(wording, 0, target), catalog).All(h => Unread.Contains(h.Code));

        /// <summary>
        /// The default restriction patterns the wording names and the food's own name and USDA description do not: the allergen or diet words an alias would add to the food.
        /// A companion pattern adds nothing when every default type that holds it already reads the food by another pattern ("dry sherry" on a dessert wine: %sherry%
        /// accompanies %wine%, which the description names).
        /// </summary>
        public static IReadOnlyList<string> RestrictedWords(string wording, GateFood target) =>
            RestrictionPatterns.Value.Where(p => p.IsMatch(wording) && !Names(p, target) && !CompanionAddsNoFlag(p.Pattern, target)).Select(p => p.Pattern).ToList();

        private static bool Names(DietPatternRule rule, GateFood target) => rule.IsMatch(target.Name) || rule.IsMatch(target.Description);

        private static bool CompanionAddsNoFlag(string pattern, GateFood target) =>
            DefaultRestrictionCriteria.Companions.Any(c => string.Equals(c.Companion, pattern, StringComparison.OrdinalIgnoreCase))
            && DefaultRestrictionCriteria.ByName.Where(kv => kv.Value.Patterns.Contains(pattern, StringComparer.OrdinalIgnoreCase))
                .All(kv => kv.Value.Patterns.Where(p => !string.Equals(p, pattern, StringComparison.OrdinalIgnoreCase) && !DietCriteriaMatcher.IsWordPattern(p))
                    .Any(p => Names(DietPatternRule.For(p, kv.Key), target)));

        private static readonly Lazy<IReadOnlyList<DietPatternRule>> RestrictionPatterns = new(() =>
            DefaultRestrictionCriteria.ByName.Values.SelectMany(v => v.Patterns).Where(p => p.Contains('%')).Distinct(StringComparer.Ordinal).Select(p => DietPatternRule.For(p)).ToList());

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

        internal static string SignatureSource(bool withProven = true)
        {
            var parts = new List<string> { Version, LinkGate.Signature, AlcoholLineContext.Signature };
            parts.AddRange(Sets);
            parts.Add(string.Join(",", Refuting.OrderBy(c => c, StringComparer.Ordinal)));
            if (withProven) parts.Add(string.Join(",", ProvenValue.Value.Proven.OrderBy(c => c, StringComparer.Ordinal)));
            parts.Add(string.Join(",", RestrictionPatterns.Value.Select(p => p.Pattern)));
            parts.Add(DietPatternRule.Version);
            parts.Add(StatementLabelTerms.Version + ":" + RecheckDietGuard.HeldReason + ":" + RecheckDietGuard.Version);
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{ClassPrecisionBar}:{ClassMinimumJudged}"));
            parts.Add(string.Join(",", EdibleParts.OrderBy(c => c, StringComparer.Ordinal)));
            parts.Add(string.Join(",", PreparationForms.OrderBy(c => c, StringComparer.Ordinal)));
            parts.Add(string.Join(",", AlternativeCodes.OrderBy(c => c, StringComparer.Ordinal)));
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
            parts.Add(RatioPhrase.ToString());
            parts.AddRange(FreeMarkers.Select(m => m.Marker + "=" + m.Pattern + "=" + string.Join(",", m.Types)));
            parts.Add(FirstItemEnd.ToString());
            parts.Add(FreeFood.ToString());
            parts.Add(AmountSegment.ToString());
            parts.Add(ItemBreak.ToString());
            parts.Add(string.Join(",", ItemFiller.OrderBy(w => w, StringComparer.Ordinal)));
            parts.Add(string.Join(",", LinkGate.FreshCues.OrderBy(w => w, StringComparer.Ordinal)));
            return string.Join("\n", parts);
        }
    }
}
