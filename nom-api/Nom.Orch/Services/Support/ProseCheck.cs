using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// Check v5 of a recipe's prose in our own words against its source, the gate the re-check and
    /// the rewrite lane share. It answers two questions apart and has three outcomes
    /// (<see cref="Finding.Failed"/>, <see cref="Finding.Unsure"/>, <see cref="Finding.Passed"/>):
    /// <list type="number">
    /// <item>Copy, a rule of its own that fails the rewrite, measured on the whole text and never
    /// step by step: the rewrite's description and steps are one stream of words and the source's
    /// description and method another (<see cref="CopyTokens"/>: every amount one token however it
    /// is written), every word counts in full, ingredient names and amounts included, and stock
    /// phrases count like any other words (the <see cref="ProseStockIndex"/> version is only
    /// recorded). Each test finds passages anywhere in the streams, so it depends on neither step
    /// boundaries nor the order of the sentences: no common run of <see cref="CopiedRun"/> words or
    /// more; no stretch following the source in order with a score of <see cref="CloseStretchLimit"/>
    /// or more, or of <see cref="MinimumStretchForShare"/> or more that reaches
    /// <see cref="CloseStretchShare"/> of the rewrite's word count, each word kept adding 1 and each
    /// word changed, added or dropped costing <see cref="StretchPenalty"/> (a close paraphrase that
    /// changes a word every few words); the
    /// words inside common runs of <see cref="SharedRun"/> words or more under
    /// <see cref="MaximumCopiedShare"/> of a rewrite of <see cref="MinimumWordsForShare"/> words or
    /// more; no run of <see cref="DescriptionRun"/> words shared by the two descriptions; and no
    /// sentence of <see cref="SentenceWords"/> words or more matching a source sentence at
    /// <see cref="SentenceSimilarity"/> of its words in order.</item>
    /// <item>Facts, decided by rule, each reason a class (<see cref="FactClasses"/>) that fails the
    /// rewrite only while it is proven on its judged rows (<see cref="ProvenClasses"/>) and otherwise
    /// marks it unsure: every
    /// amount, temperature and time of the source method is stated with its unit and range
    /// (<see cref="ProseQuantities"/>), once for each time the source states it (an oven temperature
    /// restated counts once, an amount "on each side" twice), except a bracketed conversion and an
    /// amount in a cook's first-person note in brackets ("(I used two)", "(I made 6.)"); an amount within
    /// <see cref="IngredientWindow"/> words before an ingredient's name equals the source method's
    /// amount for it, or else that line's amount, the sum of its lines or a method amount next to no
    /// other ingredient (one that conflicts is <see cref="WrongAmount"/>; an amount of the rewrite's
    /// description the source's description or title states is never wrong); and every ingredient the
    /// source method names is named somewhere in the rewrite (under its own name or a listed other
    /// name), else <see cref="LostIngredient"/>.</item>
    /// <item>Weak signals, which never fail a rewrite by themselves and mark it unsure
    /// (<see cref="UnsureReasons"/>): a cooking verb of the source (<see cref="ProseVerbLexicon"/>)
    /// not kept, the source steps' amounts out of their order, an amount the source, its title and
    /// its lines lack, and a method of <see cref="MinimumWordsForLength"/> words or more rewritten
    /// outside <see cref="MinimumLengthRatio"/> to <see cref="MaximumLengthRatio"/> times its length.
    /// An unsure rewrite is shown and counted, and is the prose judge's to read.</item>
    /// <item>Form, failing the rewrite: no step marker and no empty step.</item>
    /// </list>
    /// <see cref="Version"/> changes whenever a rule or a limit does. Text only: no database and no
    /// model.
    /// </summary>
    public static class ProseCheck
    {
        public const int Version = 5;

        public const int CopiedRun = 16;
        public const int SharedRun = 7;
        public const decimal MaximumCopiedShare = 0.42m;
        public const int MinimumWordsForShare = 40;
        public const int SentenceWords = 15;
        public const int DescriptionRun = 11;
        public const decimal SentenceSimilarity = 0.92m;
        public const decimal CloseStretchLimit = 44.5m;
        public const decimal CloseStretchShare = 0.4m;
        public const decimal MinimumStretchForShare = 15m;
        public const decimal StretchPenalty = 1.5m;
        public const decimal MinimumLengthRatio = 0.4m;
        public const decimal MaximumLengthRatio = 1.3m;
        public const int MinimumWordsForLength = 25;
        public const int IngredientWindow = 4;

        public const string WrongAmount = "wrong-amount";
        public const string ChangedUnit = "changed-unit";
        public const string ChangedRange = "changed-range";
        public const string LostQuantity = "lost-quantity";
        public const string AddedQuantity = "added-quantity";
        public const string LostStep = "lost-step";
        public const string LostIngredient = "lost-ingredient";
        public const string OutOfOrder = "out-of-order";
        public const string TooShort = "too-short";
        public const string TooLong = "too-long";
        public const string CopiedText = "copied-text";
        public const string Unchanged = "unchanged";
        public const string StepMarker = "step-marker";
        public const string EmptyStep = "empty-step";

        /// <summary>The reasons that concern amounts, most serious first.</summary>
        public static readonly IReadOnlyList<string> QuantityReasons = new[] { WrongAmount, ChangedUnit, ChangedRange, LostQuantity, AddedQuantity };

        /// <summary>The weak signals: a fault with one of these reasons marks a rewrite unsure and never fails it.</summary>
        public static readonly IReadOnlySet<string> UnsureReasons = new HashSet<string>(StringComparer.Ordinal) { LostStep, OutOfOrder, AddedQuantity, TooShort, TooLong };

        /// <summary>The reasons of the copy rule.</summary>
        public static readonly IReadOnlySet<string> CopyReasons = new HashSet<string>(StringComparer.Ordinal) { CopiedText, Unchanged };

        /// <summary>The reasons of the form rules.</summary>
        public static readonly IReadOnlySet<string> FormReasons = new HashSet<string>(StringComparer.Ordinal) { StepMarker, EmptyStep };

        /// <summary>The fact rules, each a class that fails a rewrite only while it is proven (<see cref="ProvenClasses"/>) and otherwise marks it unsure.</summary>
        public static readonly IReadOnlySet<string> FactClasses = new HashSet<string>(StringComparer.Ordinal) { LostQuantity, WrongAmount, ChangedUnit, ChangedRange, LostIngredient };

        /// <summary>The precision a fact class must hold on its judged rows of prose-check.tsv before it may fail a recipe.</summary>
        public const double ClassPrecisionBar = 0.95;

        /// <summary>The checkpoint-judged rows of prose-check.tsv a fact class needs before its precision counts.</summary>
        public const int ClassMinimumJudged = 40;

        /// <summary>
        /// The fact classes that may fail a rewrite: each held at least <see cref="ClassPrecisionBar"/>
        /// of its judged rows of prose-check.tsv right over at least <see cref="ClassMinimumJudged"/>
        /// rows the staging checkpoints read (status agent; rule-given rows must be right too but do not
        /// count toward the minimum). The set is read from the committed per-class record
        /// (<see cref="ProseCheckClassRecord"/>), never written by hand, and a record scored under another
        /// check version proves nothing (<see cref="ProvenProblem"/>). A fault of a class not proven marks
        /// the rewrite unsure, which withholds nothing and is the prose judge's and the prose audit's to read.
        /// </summary>
        public static IReadOnlySet<string> ProvenClasses => ProvenOverride.Value ?? ProvenValue.Value.Proven;

        /// <summary>
        /// Whether a fact class with these figures over the judged rows of prose-check.tsv may fail a
        /// rewrite: at least <see cref="ClassMinimumJudged"/> checkpoint-judged rows, at least
        /// <see cref="ClassPrecisionBar"/> of them right and of all its rows right, every rule-given row
        /// right, and figures that add up (a doubtful row counts as judged and not right).
        /// </summary>
        public static bool ClassMeetsBar(int judged, int right, int agentJudged, int agentRight, int ruleJudged, int ruleRight) =>
            agentJudged >= 0 && ruleJudged >= 0 && agentRight >= 0 && ruleRight >= 0
            && agentRight <= agentJudged && ruleRight <= ruleJudged
            && judged == agentJudged + ruleJudged && right == agentRight + ruleRight
            && agentJudged >= ClassMinimumJudged
            && (double)agentRight / agentJudged >= ClassPrecisionBar
            && (double)right / judged >= ClassPrecisionBar
            && ruleRight == ruleJudged;

        /// <summary>Why the committed per-class record proves no class on this build, or null when it is current.</summary>
        public static string? ProvenProblem => ProvenValue.Value.Problem;

        /// <summary>Whether a fault with <paramref name="reason"/> fails a rewrite while <paramref name="proven"/> are the proven fact classes: every reason but a weak signal and a fact class not proven.</summary>
        public static bool Fails(string reason, IReadOnlySet<string> proven) =>
            !UnsureReasons.Contains(reason) && (!FactClasses.Contains(reason) || proven.Contains(reason));

        private static readonly Lazy<(IReadOnlySet<string> Proven, string? Problem)> ProvenValue = new(() =>
            ProseCheckClassRecord.Evaluate(ProseCheckClassRecord.Embedded(), Version));

        private static readonly AsyncLocal<IReadOnlySet<string>?> ProvenOverride = new();

        /// <summary>For tests of the machinery alone: the fact classes taken as proven in this async flow until the result is disposed, whatever the record proves.</summary>
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

        private static readonly HashSet<string> FirstPerson = new(StringComparer.Ordinal) { "I", "I'd", "I'll", "I'm", "I've", "my", "My", "me", "mine" };

        private static readonly HashSet<string> LineStopWords = new(StringComparer.Ordinal)
        {
            "of", "the", "a", "an", "and", "or", "for", "to", "taste", "plus", "more", "about", "optional", "divided", "fresh", "freshly",
            "chopped", "minced", "diced", "sliced", "ground", "large", "small", "medium", "whole", "finely", "roughly", "coarsely",
            "thinly", "packed", "softened", "melted", "room", "temperature", "cold", "warm", "hot", "extra", "virgin", "kosher", "sea",
            "unsalted", "salted", "peeled", "grated", "shredded", "cut", "into", "pieces", "inch", "cubes", "beaten", "lightly", "well",
            "at", "in", "x", "heaping", "level", "scant", "generous", "good", "few", "some", "ref",
        };

        private static readonly Dictionary<string, string[]> IngredientSynonyms = new(StringComparer.Ordinal)
        {
            ["onion"] = new[] { "scallion" }, ["onions"] = new[] { "scallion" }, ["scallion"] = new[] { "onion" }, ["scallions"] = new[] { "onion" },
            ["cilantro"] = new[] { "coriander" }, ["coriander"] = new[] { "cilantro" },
            ["chickpeas"] = new[] { "garbanzo" }, ["chickpea"] = new[] { "garbanzo" }, ["garbanzo"] = new[] { "chickpea" }, ["garbanzos"] = new[] { "chickpea" },
            ["zucchini"] = new[] { "courgette" }, ["courgette"] = new[] { "zucchini" }, ["eggplant"] = new[] { "aubergine" }, ["aubergine"] = new[] { "eggplant" },
            ["shrimp"] = new[] { "prawn" }, ["prawn"] = new[] { "shrimp" }, ["prawns"] = new[] { "shrimp" },
        };

        private static readonly Regex WordToken = new(@"\p{L}+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex CopyWord = new(@"[\p{L}\p{N}¼½¾⅓⅔⅕⅖⅗⅘⅙⅚⅛⅜⅝⅞]+(?:['’]\p{L}+)*", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex Apostrophe = new(@"['’]", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex Digit = new(@"[\p{N}¼½¾⅓⅔⅕⅖⅗⅘⅙⅚⅛⅜⅝⅞]", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex BracketOwner = new(@"(?<word>\p{L}+)\s*\(\s*(?:about|approximately|approx\.?|around|roughly|or)?\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex SentenceEnd = new(@"(?<=[.!?;])\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex Brackets = new(@"\([^()]*\)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex NoteWord = new(@"[\p{L}'’]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex Restated = new(@"\bthe\s+(?:\p{L}+\s+)?$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex Purpose = new(@"\bfor\b.*$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex ClauseBreak = new(@"[.;:!?()]", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex WindowBreak = new(@"[.,;:!?()]", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex EachOf = new(@"^\s*each\b(?:\s+of\b)?", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex ListItems = new(@"\s*(?:,|\band\b|\bor\b|&)\s*", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly HashSet<string> CookingVessels = new(StringComparer.Ordinal)
        {
            "pan", "pans", "dish", "dishes", "skillet", "skillets", "tin", "tins", "pot", "pots", "saucepan", "saucepans", "mold", "molds", "mould", "moulds",
            "ramekin", "ramekins", "casserole", "casseroles", "sheet", "sheets", "tray", "trays", "cutter", "cutters", "crock", "slow", "cooker", "oven",
        };

        private static readonly HashSet<string> CapacityVessels = new(StringComparer.Ordinal)
        {
            "pot", "pots", "saucepan", "saucepans", "casserole", "casseroles", "dish", "dishes", "ramekin", "ramekins", "mold", "molds", "mould", "moulds", "crock", "slow", "cooker", "oven",
        };

        private static readonly HashSet<string> HoldingVessels = new(StringComparer.Ordinal)
        {
            "measuring", "bowl", "bowls", "jug", "jugs", "pitcher", "pitchers", "mug", "mugs", "plate", "plates", "platter", "platters", "container", "containers",
        };

        private static readonly HashSet<string> VesselWords = new(StringComparer.Ordinal)
        {
            "round", "square", "rectangular", "deep", "shallow", "nonstick", "non", "stick", "cast", "iron", "metal", "glass", "ceramic", "baking", "cake", "pie", "loaf",
            "springform", "tart", "bundt", "muffin", "rimmed", "heavy", "bottomed", "large", "medium", "small", "dutch", "sauce", "jelly", "roll", "quiche", "tube",
            "souffle", "soufflé", "gratin", "roasting", "sheet", "oven", "proof", "safe", "x",
        };

        private static readonly HashSet<string> NotTheIngredient = new(StringComparer.Ordinal) { "water", "liquid" };

        private static readonly HashSet<string> NamedByQualifier = new(StringComparer.Ordinal) { "extract", "extracts", "essence", "leaves", "leaf" };

        private static readonly Regex EachSide = new(@"^\W*(?:\p{L}+\W+){0,2}?(?:on\s+each\s+side|per\s+side|each\s+side|on\s+both\s+sides)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>One rule a rewrite broke, and what broke it.</summary>
        public sealed record Fault(string Reason, string Detail)
        {
            public override string ToString() => $"{Reason}: {Detail}";
        }

        /// <summary>What the check found, with the copy measures it computed (the share of the rewrite's words in copied runs, the sentences kept nearly word for word, the longest copied run) and the check and stock-index versions it ran with. It failed when a fault's reason fails it (<see cref="Fails"/>: any reason but a weak signal and a fact class not among <see cref="Proven"/>, the classes proven when it was made), is unsure when it has faults and none of them fails it, and passed when it has none.</summary>
        public sealed record Finding(
            IReadOnlyList<Fault> Faults,
            decimal CopiedShare,
            int CopiedSentences,
            int LongestRun,
            decimal LengthRatio,
            int StockVersion)
        {
            public int CheckVersion { get; init; } = Version;

            public decimal CloseStretch { get; init; }

            /// <summary>The fact classes that fail this finding: <see cref="ProvenClasses"/> when it was made.</summary>
            public IReadOnlySet<string> Proven { get; init; } = ProvenClasses;

            public bool Passed => Faults.Count == 0;

            public bool Failed => Faults.Any(f => Fails(f.Reason, Proven));

            public bool Unsure => Faults.Count > 0 && !Failed;

            /// <summary>"passed", "unsure" or "failed".</summary>
            public string Outcome => Passed ? "passed" : Failed ? "failed" : "unsure";

            public IReadOnlyList<string> Reasons => Faults.Select(f => f.Reason).Distinct().ToList();

            /// <summary>The reasons that fail the rewrite, without the weak signals and the fact classes not proven.</summary>
            public IReadOnlyList<string> FailReasons => Reasons.Where(r => Fails(r, Proven)).ToList();

            /// <summary>The fact classes whose faults this finding holds but which did not fail it, not being proven.</summary>
            public IReadOnlyList<string> UnprovenReasons => Reasons.Where(r => FactClasses.Contains(r) && !Proven.Contains(r)).ToList();

            public bool Copied => Faults.Any(f => CopyReasons.Contains(f.Reason));

            public override string ToString() => Passed ? "passed" : (Failed ? string.Empty : "unsure: ") + string.Join("; ", Faults);
        }

        private sealed record Ingredient(string Head, IReadOnlyList<ProseQuantity> Amounts, string? Qualifier = null);

        private enum Utensil { None, Cooking, Holding }

        private sealed class Located
        {
            public Located(ProseQuantity quantity, string text, int step, int limit, Utensil utensil)
            {
                Quantity = quantity;
                Text = text;
                Step = step;
                Limit = limit;
                Utensil = utensil;
            }

            public ProseQuantity Quantity { get; }

            public string Text { get; }

            public int Step { get; }

            public int Limit { get; }

            public Utensil Utensil { get; }
        }

        private static IEnumerable<Located> Locate(string text, int step)
        {
            var quantities = ProseQuantities.ReadPrepared(text);
            foreach (var q in quantities)
            {
                var from = q.End;
                var opening = text.IndexOf('(', q.End);
                if (opening >= 0 && text[q.End..opening].Trim().Length == 0 && text.IndexOf(')', opening) is var closing and >= 0) from = closing + 1;
                var next = quantities.Where(o => o.Start >= from && !o.Conversion).Select(o => o.Start).DefaultIfEmpty(text.Length).Min();
                var clause = WindowBreak.Match(text, from);
                var limit = Math.Min(next, clause.Success ? clause.Index : text.Length);
                yield return new Located(q, text, step, limit, UtensilOf(q, text));
            }
        }

        private static Utensil UtensilOf(ProseQuantity q, string text)
        {
            if (q.Family is not (ProseQuantities.Length or ProseQuantities.Volume)) return Utensil.None;
            foreach (Match word in WordToken.Matches(text, q.End))
            {
                var w = word.Value.ToLowerInvariant();
                if (word.Index > q.End && text[q.End..word.Index].Any(c => c is '.' or ',' or ';' or ':' or '(' or ')')) return Utensil.None;
                if (CookingVessels.Contains(w)) return q.Family == ProseQuantities.Volume && !CapacityVessels.Contains(w) ? Utensil.None : Utensil.Cooking;
                if (HoldingVessels.Contains(w)) return Utensil.Holding;
                if (w == "inch" || w == "inches" || VesselWords.Contains(w)) continue;
                return Utensil.None;
            }

            return Utensil.None;
        }

        /// <summary>The ingredient lines (<c>recipeIngredient</c>) of a schema.org Recipe node; empty when it states none or cannot be read.</summary>
        public static IReadOnlyList<string> SourceLines(string? rawJsonLd)
        {
            if (string.IsNullOrWhiteSpace(rawJsonLd)) return Array.Empty<string>();
            try
            {
                using var document = JsonDocument.Parse(rawJsonLd);
                if (document.RootElement.ValueKind != JsonValueKind.Object
                    || !document.RootElement.TryGetProperty("recipeIngredient", out var lines)
                    || lines.ValueKind != JsonValueKind.Array)
                    return Array.Empty<string>();
                return lines.EnumerateArray()
                    .Where(l => l.ValueKind == JsonValueKind.String)
                    .Select(l => ProseQuantities.Prepare(l.GetString()))
                    .Where(l => l.Length > 0)
                    .ToList();
            }
            catch (JsonException)
            {
                return Array.Empty<string>();
            }
        }

        /// <summary>The head word of each ingredient line the check reads (the last word of its name once amounts, brackets, what follows a comma and stop words are gone), in line order; a line with no such word is left out.</summary>
        public static IReadOnlyList<(string Line, string Head)> LineHeads(IReadOnlyList<string> lines) =>
            lines.Select(l => (Line: l, Ingredients: Ingredients(new[] { l })))
                .Where(x => x.Ingredients.Count == 1)
                .Select(x => (x.Line, x.Ingredients[0].Head))
                .ToList();

        /// <summary>Whether <paramref name="word"/> (lower-case) names the ingredient whose head word is <paramref name="head"/>, singular or plural.</summary>
        public static bool NamesHead(string word, string head) => Names(word, head);

        public static Finding Check(
            RewriteFidelity.SourceText source,
            RewriteFidelity.SourceText rewrite,
            IReadOnlyList<string>? ingredientLines = null,
            ProseStockIndex? stock = null,
            ProseVerbLexicon? verbs = null)
        {
            stock ??= ProseStockIndex.Empty;
            verbs ??= ProseVerbLexicon.Default;
            var faults = new List<Fault>();

            var method = source.Steps.Select(ProseQuantities.Prepare).Where(s => s.Length > 0).ToList();
            var sourceDescription = ProseQuantities.Prepare(source.Description);
            var steps = rewrite.Steps.Select(ProseQuantities.Prepare).ToList();
            var description = ProseQuantities.Prepare(rewrite.Description);
            var ingredients = Ingredients(ingredientLines ?? Array.Empty<string>());

            for (var i = 0; i < rewrite.Steps.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(rewrite.Steps[i]) || steps[i].Length == 0) faults.Add(new Fault(EmptyStep, $"step {i + 1}"));
                else if (ProseQuantities.HasStepMarker(rewrite.Steps[i])) faults.Add(new Fault(StepMarker, $"step {i + 1}: \"{Excerpt(rewrite.Steps[i])}\""));
            }

            CheckQuantities(method, sourceDescription, source.Title, steps.Where(s => s.Length > 0).ToList(), description, rewrite.Title, ingredients, faults);
            var lengthRatio = CheckCoverage(method, steps.Where(s => s.Length > 0).ToList(), description, ingredients, verbs, faults);
            var (share, sentences, run, stretch) = CheckCopy(method, sourceDescription, steps.Where(s => s.Length > 0).ToList(), description, faults);

            return new Finding(faults, share, sentences, run, lengthRatio, stock.Version) { CloseStretch = stretch };
        }

        private static void CheckQuantities(
            List<string> method, string sourceDescription, string sourceTitle,
            List<string> steps, string description, string title,
            List<Ingredient> ingredients, List<Fault> faults)
        {
            var stated = method.SelectMany((text, i) => Locate(text, i)).ToList();
            var notes = stated.Where(InNote).ToList();
            var sourceQuantities = stated.Except(notes).ToList();
            var described = ProseQuantities.Read(sourceDescription).Concat(ProseQuantities.Read(sourceTitle)).ToList();
            var optional = described.Concat(ProseQuantities.Read(title)).Concat(notes.Select(n => n.Quantity)).ToList();
            var ours = steps.Select((text, i) => (Text: text, Step: i)).Prepend((Text: description, Step: -1))
                .SelectMany(s => Locate(s.Text, s.Step))
                .ToList();

            var softContexts = sourceQuantities.Where(s => s.Quantity.Soft).Select(SoftWord)
                .Intersect(ours.Where(o => o.Quantity.Soft).Select(SoftWord), StringComparer.Ordinal)
                .Where(w => w.Length > 0)
                .ToHashSet(StringComparer.Ordinal);
            bool Compared(Located q) => !q.Quantity.Soft || softContexts.Contains(SoftWord(q));

            var required = sourceQuantities.Where(s => !s.Quantity.Conversion && s.Utensil != Utensil.Holding && Compared(s)).ToList();
            var used = new Dictionary<Located, int>();
            var lost = new List<Located>();
            for (var k = 0; k < required.Count; k++)
            {
                var s = required[k];
                var again = s.Quantity.Family == ProseQuantities.Temperature || s.Utensil == Utensil.Cooking
                    || (Restated.IsMatch(s.Text[..s.Quantity.Start]) && required.Take(k).Any(e => ProseQuantities.Equal(e.Quantity, s.Quantity)));
                var match = ours.FirstOrDefault(o => (again || used.GetValueOrDefault(o) < Uses(o)) && ProseQuantities.Equal(s.Quantity, o.Quantity));
                if (match == null) lost.Add(s);
                else used[match] = used.GetValueOrDefault(match) + 1;
            }

            lost.RemoveAll(s => s.Quantity.Family == ProseQuantities.Count && !s.Quantity.IsRange
                && ours.Any(o => o.Quantity.Alternatives && (o.Quantity.Low == s.Quantity.Low || o.Quantity.High == s.Quantity.Low)
                    && sourceQuantities.Any(e => e.Quantity.Family == ProseQuantities.Count && e.Quantity.Low == o.Quantity.Low)
                    && sourceQuantities.Any(e => e.Quantity.Family == ProseQuantities.Count && e.Quantity.Low == o.Quantity.High)));

            var unmatched = new List<Located>();
            var conflicting = new List<Located>();
            foreach (var o in ours)
            {
                if (!Compared(o) || o.Utensil == Utensil.Holding) continue;
                if (o.Step < 0 && described.Any(q => ProseQuantities.Equal(q, o.Quantity))) continue;

                var ingredient = IngredientAfter(o, ingredients);
                if (ingredient == null)
                {
                    if (sourceQuantities.Any(s => ProseQuantities.Equal(s.Quantity, o.Quantity))) continue;
                    if (optional.Any(q => ProseQuantities.Equal(q, o.Quantity))) continue;
                    unmatched.Add(o);
                    continue;
                }

                var methodAmounts = sourceQuantities
                    .Where(s => s.Quantity.Family == o.Quantity.Family && IngredientAfter(s, ingredients)?.Head == ingredient.Head)
                    .ToList();
                if (methodAmounts.Count > 0)
                {
                    if (methodAmounts.Any(s => ProseQuantities.Equal(s.Quantity, o.Quantity))) continue;
                    faults.Add(new Fault(WrongAmount, $"\"{o.Quantity.Text}\" {ingredient.Head}, the method says \"{methodAmounts[0].Quantity.Text}\""));
                    conflicting.Add(o);
                    continue;
                }

                if (LineAllows(o.Quantity, ingredient, ingredients)) continue;
                if (sourceQuantities.Any(s => ProseQuantities.Equal(s.Quantity, o.Quantity) && IngredientAfter(s, ingredients) == null)) continue;
                if (optional.Any(q => ProseQuantities.Equal(q, o.Quantity))) continue;

                var lineAmount = ingredient.Amounts.FirstOrDefault(a => a.Family == o.Quantity.Family && !a.Soft);
                if (lineAmount != null)
                {
                    faults.Add(new Fault(WrongAmount, $"\"{o.Quantity.Text}\" {ingredient.Head}, the line says \"{lineAmount.Text}\""));
                    conflicting.Add(o);
                    continue;
                }

                unmatched.Add(o);
            }

            var pairedAdded = new HashSet<Located>();
            foreach (var s in lost)
            {
                var unit = unmatched.Concat(conflicting).FirstOrDefault(o => !pairedAdded.Contains(o) && ProseQuantities.SameNumbersOtherUnit(s.Quantity, o.Quantity));
                if (unit != null)
                {
                    faults.Add(new Fault(ChangedUnit, $"\"{s.Quantity.Text}\" became \"{unit.Quantity.Text}\""));
                    pairedAdded.Add(unit);
                    continue;
                }

                var range = unmatched.Concat(conflicting).FirstOrDefault(o => !pairedAdded.Contains(o) && ProseQuantities.RangeChanged(s.Quantity, o.Quantity));
                if (range != null)
                {
                    faults.Add(new Fault(ChangedRange, $"\"{s.Quantity.Text}\" became \"{range.Quantity.Text}\""));
                    pairedAdded.Add(range);
                }
            }

            foreach (var s in lost) faults.Add(new Fault(LostQuantity, $"\"{s.Quantity.Text}\" in step {s.Step + 1}"));
            foreach (var o in unmatched.Where(o => !pairedAdded.Contains(o)))
                faults.Add(new Fault(AddedQuantity, $"\"{o.Quantity.Text}\" in {(o.Step < 0 ? "the description" : $"step {o.Step + 1}")}"));

            var cursor = -1;
            var offset = 0;
            var ourSteps = ours.Where(o => o.Step >= 0).ToList();
            var stepStarts = new Dictionary<int, int>();
            for (var i = 0; i < steps.Count; i++)
            {
                stepStarts[i] = offset;
                offset += steps[i].Length + 1;
            }

            foreach (var group in required.Except(lost).GroupBy(s => s.Step).OrderBy(g => g.Key))
            {
                var chosen = new List<int>();
                var backwards = false;
                foreach (var s in group)
                {
                    var at = ourSteps.Where(o => ProseQuantities.Equal(s.Quantity, o.Quantity)).Select(o => stepStarts[o.Step] + o.Quantity.Start).OrderBy(p => p).ToList();
                    var forward = at.Where(p => p >= cursor).ToList();
                    if (forward.Count > 0) chosen.Add(forward[0]);
                    else if (at.Count > 0) backwards = true;
                }

                if (backwards)
                {
                    faults.Add(new Fault(OutOfOrder, $"an amount of source step {group.Key + 1} comes before an earlier step's"));
                    break;
                }

                if (chosen.Count > 0) cursor = chosen.Min();
            }
        }

        private static int Uses(Located located)
        {
            var after = located.Text[located.Quantity.End..];
            if (EachSide.IsMatch(after)) return 2;
            var each = EachOf.Match(after);
            if (!each.Success) return 1;
            var rest = after[each.Length..];
            var listed = rest.Length > 0 && !ClauseBreak.IsMatch(rest[..1]) && WordToken.IsMatch(rest[..Math.Min(rest.Length, 3)])
                ? ClauseBreak.Split(rest)[0]
                : ClauseBreak.Split(located.Text[..located.Quantity.Start]).Last();
            return Math.Max(1, ListItems.Split(listed).Count(i => WordToken.IsMatch(i)));
        }

        private static bool InNote(Located located)
        {
            var text = located.Text;
            var start = located.Quantity.Start;
            var open = start > 0 ? text.LastIndexOf('(', start - 1) : -1;
            if (open < 0) return false;
            var close = text.IndexOf(')', open);
            return (close < 0 || close >= start) && NoteWord.Matches(text[(open + 1)..(close < 0 ? text.Length : close)]).Any(m => FirstPerson.Contains(m.Value));
        }

        private static string SoftWord(Located located)
        {
            if (located.Quantity.Unit.Length > 0) return located.Quantity.Unit;
            var next = WordToken.Match(located.Text, located.Quantity.End);
            return next.Success ? next.Value.ToLowerInvariant().TrimEnd('s') : string.Empty;
        }

        private static bool LineAllows(ProseQuantity q, Ingredient ingredient, List<Ingredient> ingredients)
        {
            if (ingredient.Amounts.Any(a => ProseQuantities.Equal(a, q))) return true;
            var same = ingredients.Where(i => i.Head == ingredient.Head).Select(i => i.Amounts.FirstOrDefault(a => a.Family == q.Family && !a.IsRange)).OfType<ProseQuantity>().ToList();
            if (same.Count < 2 || q.IsRange) return false;
            var total = same.Sum(a => ProseQuantities.Base(a.Low, a));
            var sum = new ProseQuantity(total, total, q.Family, Base(q.Family), 0, 0, "sum", false, false);
            return ProseQuantities.Equal(sum, q);
        }

        private static string Base(string family) => family switch
        {
            ProseQuantities.Time => "minute",
            ProseQuantities.Length => "inch",
            ProseQuantities.Volume => "teaspoon",
            ProseQuantities.Mass => "ounce",
            ProseQuantities.Temperature => "F",
            _ => string.Empty,
        };

        private static Ingredient? IngredientAfter(Located located, List<Ingredient> ingredients)
        {
            if (ingredients.Count == 0 || located.Utensil != Utensil.None) return null;
            if (located.Quantity.Family is ProseQuantities.Time or ProseQuantities.Temperature) return null;
            if (BracketOwner.Match(located.Text[..located.Quantity.Start]) is { Success: true } owner
                && ingredients.FirstOrDefault(i => Names(owner.Groups["word"].Value.ToLowerInvariant(), i.Head)) is { } bracketed)
                return bracketed;
            var after = WordToken.Matches(located.Text[located.Quantity.End..Math.Max(located.Quantity.End, located.Limit)]).Select(m => m.Value.ToLowerInvariant()).Take(IngredientWindow + 1).ToList();
            for (var k = 0; k < Math.Min(after.Count, IngredientWindow); k++)
            {
                var match = ingredients.FirstOrDefault(i => Names(after[k], i.Head));
                if (match == null) continue;
                if (k + 1 < after.Count && NotTheIngredient.Contains(after[k + 1])) return null;
                return match;
            }

            return null;
        }

        private static bool Names(string word, string head) =>
            word == head || word == head + "s" || word == head + "es" || (head.EndsWith('s') && word == head[..^1]) || (head.EndsWith('y') && word == head[..^1] + "ies");

        private static List<Ingredient> Ingredients(IReadOnlyList<string> lines)
        {
            var ingredients = new List<Ingredient>();
            foreach (var raw in lines)
            {
                var line = ProseQuantities.Prepare(raw);
                if (line.Length == 0) continue;
                var amounts = ProseQuantities.ReadPrepared(line);
                var name = Brackets.Replace(ProseQuantities.WithoutAmounts(line, amounts), " ");
                var comma = name.IndexOf(',');
                if (comma > 0) name = name[..comma];
                name = Purpose.Replace(name, " ");
                var words = WordToken.Matches(name.ToLowerInvariant()).Select(m => m.Value)
                    .Where(w => w.Length > 1 && !LineStopWords.Contains(w) && !IsUnitWord(w))
                    .ToList();
                if (words.Count == 0) continue;
                ingredients.Add(new Ingredient(words[^1], amounts, words.Count > 1 ? words[^2] : null));
            }

            return ingredients;
        }

        private static IEnumerable<string> Qualifiers(string head, List<Ingredient> ingredients) =>
            ingredients.Where(i => i.Head == head && i.Qualifier != null).Select(i => i.Qualifier!);

        private static bool IsUnitWord(string word) => ProseQuantities.ReadPrepared("2 " + word) is [{ Unit.Length: > 0 }];

        private static decimal CheckCoverage(
            List<string> method, List<string> steps, string description,
            List<Ingredient> ingredients, ProseVerbLexicon verbs, List<Fault> faults)
        {
            var ourWords = steps.Prepend(description).SelectMany(ProsePhrases.WordsOfPrepared).ToList();
            var ourGroups = verbs.GroupsIn(steps.Prepend(description).SelectMany(s => ProsePhrases.WordsOfPrepared(ProseVerbLexicon.ActionText(s, source: false))).ToList());
            var ourWordSet = ourWords.ToHashSet(StringComparer.Ordinal);

            var missingVerbs = new List<string>();
            var missingIngredients = new List<string>();
            for (var i = 0; i < method.Count; i++)
            {
                var words = ProsePhrases.WordsOfPrepared(Brackets.Replace(method[i], " "));
                foreach (var verb in verbs.VerbsIn(ProsePhrases.WordsOfPrepared(ProseVerbLexicon.ActionText(method[i], source: true))))
                {
                    if (verb.Overlaps(ourGroups)) continue;
                    var name = string.Join("/", verb.OrderBy(g => g, StringComparer.Ordinal));
                    if (!missingVerbs.Contains($"{name} (step {i + 1})")) missingVerbs.Add($"{name} (step {i + 1})");
                }

                foreach (var ingredient in ingredients.Select(x => x.Head).Distinct())
                {
                    if (!words.Any(w => Names(w, ingredient))) continue;
                    if (ourWordSet.Any(w => Names(w, ingredient))) continue;
                    if (NamedByQualifier.Contains(ingredient) && Qualifiers(ingredient, ingredients).Any(q => ourWordSet.Contains(q))) continue;
                    if (IngredientSynonyms.TryGetValue(ingredient, out var others) && ourWordSet.Any(w => others.Any(o => Names(w, o)))) continue;
                    if (!missingIngredients.Contains($"{ingredient} (step {i + 1})")) missingIngredients.Add($"{ingredient} (step {i + 1})");
                }
            }

            if (missingVerbs.Count > 0) faults.Add(new Fault(LostStep, $"no {string.Join(", ", missingVerbs)}"));
            if (missingIngredients.Count > 0) faults.Add(new Fault(LostIngredient, $"no {string.Join(", ", missingIngredients)}"));

            var sourceCount = method.Sum(m => ProsePhrases.WordsOfPrepared(m).Count);
            var ourCount = steps.Sum(s => ProsePhrases.WordsOfPrepared(s).Count);
            if (sourceCount == 0) return 0m;
            var ratio = Math.Round((decimal)ourCount / sourceCount, 3);
            if (sourceCount >= MinimumWordsForLength && ratio < MinimumLengthRatio)
                faults.Add(new Fault(TooShort, $"{ourCount} words for the source's {sourceCount}"));
            else if (sourceCount >= MinimumWordsForLength && ratio > MaximumLengthRatio)
                faults.Add(new Fault(TooLong, $"{ourCount} words for the source's {sourceCount}"));
            return ratio;
        }

        private static (decimal Share, int Sentences, int Run, decimal Stretch) CheckCopy(
            List<string> method, string sourceDescription, List<string> steps, string description, List<Fault> faults)
        {
            var methodWords = string.Join(" ", method.SelectMany(ProsePhrases.WordsOfPrepared));
            var stepWords = string.Join(" ", steps.SelectMany(ProsePhrases.WordsOfPrepared));
            if (methodWords.Length > 0 && methodWords == stepWords) faults.Add(new Fault(Unchanged, "the steps are the source method word for word"));

            var sourceTexts = method.Prepend(sourceDescription).Where(t => t.Length > 0).ToList();
            var ourTexts = steps.Prepend(description).Where(t => t.Length > 0).ToList();
            var theirs = sourceTexts.SelectMany(CopyTokens).ToList();
            var ours = InSourceOrder(ourTexts.SelectMany(t => SentenceEnd.Split(t)).Select(CopyTokens).Where(t => t.Count > 0).ToList(), theirs);

            var cover = CommonRuns(ours, theirs);
            var copied = new bool[ours.Count];
            var longest = 0;
            var longestEnd = -1;
            for (var i = 0; i < ours.Count; i++)
            {
                if (cover[i] >= SharedRun)
                    for (var j = i - cover[i] + 1; j <= i; j++) copied[j] = true;
                if (cover[i] <= longest) continue;
                longest = cover[i];
                longestEnd = i;
            }

            if (longest >= CopiedRun)
                faults.Add(new Fault(CopiedText, RunDetail(ours, longestEnd, longest)));

            var stretch = CloseStretch(ours, theirs);
            if (stretch.Score >= CloseStretchLimit || (stretch.Score >= MinimumStretchForShare && ours.Count > 0 && stretch.Score / ours.Count >= CloseStretchShare))
                faults.Add(new Fault(CopiedText, $"a stretch of {stretch.End - stretch.Start + 1} words follows the source in order with a few words changed (score {stretch.Score.ToString("0.#", CultureInfo.InvariantCulture)}): \"{Excerpt(string.Join(" ", ours.Skip(stretch.Start).Take(stretch.End - stretch.Start + 1)))}\""));

            var ourDescription = CopyTokens(description);
            var descriptionCover = CommonRuns(ourDescription, CopyTokens(sourceDescription));
            var descriptionRun = 0;
            var descriptionEnd = -1;
            for (var i = 0; i < ourDescription.Count; i++)
            {
                if (descriptionCover[i] <= descriptionRun) continue;
                (descriptionRun, descriptionEnd) = (descriptionCover[i], i);
            }

            if (descriptionRun >= DescriptionRun)
                faults.Add(new Fault(CopiedText, "the description keeps " + RunDetail(ourDescription, descriptionEnd, descriptionRun) + " of the source's"));

            var share = ours.Count == 0 ? 0m : Math.Round((decimal)copied.Count(c => c) / ours.Count, 3);
            if (ours.Count >= MinimumWordsForShare && share >= MaximumCopiedShare)
                faults.Add(new Fault(CopiedText, $"{copied.Count(c => c)} of the rewrite's {ours.Count} words sit in runs of {SharedRun} or more words of the source ({share.ToString("0.###", CultureInfo.InvariantCulture)})"));

            var sourceSentences = sourceTexts.SelectMany(t => SentenceEnd.Split(t)).Select(s => (Text: s, Tokens: CopyTokens(s))).Where(s => ProsePhrases.WordsOfPrepared(s.Text).Count >= SentenceWords).ToList();
            var sentences = 0;
            string? first = null;
            foreach (var sentence in ourTexts.SelectMany(t => SentenceEnd.Split(t)).Where(s => ProsePhrases.WordsOfPrepared(s).Count >= SentenceWords).Select(CopyTokens))
            {
                var match = sourceSentences.FirstOrDefault(s =>
                    Lcs(sentence, s.Tokens) >= SentenceSimilarity * Math.Max(sentence.Count, s.Tokens.Count));
                if (match.Tokens == null) continue;
                sentences++;
                first ??= match.Text;
            }

            if (first != null)
                faults.Add(new Fault(CopiedText, $"{sentences} sentence{(sentences == 1 ? string.Empty : "s")} kept nearly word for word, such as \"{Excerpt(first)}\""));

            return (share, sentences, longest, stretch.Score);
        }

        /// <summary>
        /// The words the copy rules compare: lower-cased, apostrophes dropped, each amount the
        /// quantity reader finds one <c>#</c> however it is written ("½ cup", "0.5 cup", "half a
        /// cup", "1/2 c."), a bracketed conversion left out, and any other number or number word
        /// <c>#</c>.
        /// </summary>
        public static List<string> CopyTokens(string prepared)
        {
            var spans = ProseQuantities.ReadPrepared(prepared);
            var tokens = new List<string>();
            ProseQuantity? last = null;
            foreach (Match m in CopyWord.Matches(prepared))
            {
                var span = spans.FirstOrDefault(q => q.Start <= m.Index && m.Index < q.End);
                if (span != null)
                {
                    if (!span.Conversion && (last == null || last.Start != span.Start)) tokens.Add(ProsePhrases.Number);
                    last = span;
                    continue;
                }

                last = null;
                var word = Apostrophe.Replace(m.Value.ToLowerInvariant(), string.Empty);
                tokens.Add(Digit.IsMatch(word) || ProseQuantities.IsNumberWord(word) ? ProsePhrases.Number : word);
            }

            return tokens;
        }

        /// <summary>
        /// The rewrite's sentences as one stream in the order of the source passages they follow most
        /// closely (each placed by where its best close stretch ends in the source; sentences that
        /// follow none last, in their own order), so no copy measure depends on the order of the
        /// rewrite's sentences or steps.
        /// </summary>
        public static List<string> InSourceOrder(IReadOnlyList<List<string>> sentences, IReadOnlyList<string> theirs) =>
            sentences
                .Select((tokens, index) => (Tokens: tokens, Index: index, Anchor: Anchor(tokens, theirs)))
                .OrderBy(x => x.Anchor < 0 ? int.MaxValue : x.Anchor)
                .ThenBy(x => x.Index)
                .SelectMany(x => x.Tokens)
                .ToList();

        private static int Anchor(IReadOnlyList<string> ours, IReadOnlyList<string> theirs)
        {
            var penalty = (int)(StretchPenalty * 2);
            var m = theirs.Count;
            var previous = new int[m + 1];
            var current = new int[m + 1];
            var best = 0;
            var anchor = -1;
            foreach (var word in ours)
            {
                current[0] = 0;
                for (var j = 1; j <= m; j++)
                {
                    var score = word == theirs[j - 1] ? previous[j - 1] + 2 : previous[j - 1] - penalty;
                    score = Math.Max(score, Math.Max(previous[j] - penalty, current[j - 1] - penalty));
                    current[j] = Math.Max(score, 0);
                    if (current[j] <= best) continue;
                    best = current[j];
                    anchor = j;
                }

                (previous, current) = (current, previous);
            }

            return best >= 2 * 2 ? anchor : -1;
        }

        private static string RunDetail(List<string> tokens, int end, int length) =>
            $"a run of {length} words: \"{string.Join(" ", tokens.Skip(end - length + 1).Take(length))}\"";

        private static (decimal Score, int Start, int End) CloseStretch(List<string> ours, List<string> theirs)
        {
            var penalty = (int)(StretchPenalty * 2);
            var m = theirs.Count;
            var previous = new int[m + 1];
            var current = new int[m + 1];
            var previousStart = new int[m + 1];
            var currentStart = new int[m + 1];
            var best = (Score: 0, Start: 0, End: -1);
            for (var i = 0; i < ours.Count; i++)
            {
                current[0] = 0;
                for (var j = 1; j <= m; j++)
                {
                    var (score, start) = ours[i] == theirs[j - 1]
                        ? (previous[j - 1] + 2, previous[j - 1] > 0 ? previousStart[j - 1] : i)
                        : (previous[j - 1] - penalty, previousStart[j - 1]);
                    if (previous[j] - penalty > score) (score, start) = (previous[j] - penalty, previousStart[j]);
                    if (current[j - 1] - penalty > score) (score, start) = (current[j - 1] - penalty, currentStart[j - 1]);
                    if (score <= 0) (score, start) = (0, i + 1);
                    current[j] = score;
                    currentStart[j] = start;
                    if (score > best.Score) best = (score, start, i);
                }

                (previous, current) = (current, previous);
                (previousStart, currentStart) = (currentStart, previousStart);
            }

            return (best.Score / 2m, best.Start, best.End);
        }

        private static int[] CommonRuns(List<string> ours, List<string> theirs)
        {
            var ids = new Dictionary<string, int>(StringComparer.Ordinal);
            int Id(string w) => ids.TryGetValue(w, out var id) ? id : ids[w] = ids.Count;
            var a = ours.Select(Id).ToArray();
            var b = theirs.Select(Id).ToArray();
            var cover = new int[a.Length];
            var previous = new int[b.Length + 1];
            var current = new int[b.Length + 1];
            for (var i = 0; i < a.Length; i++)
            {
                for (var j = 0; j < b.Length; j++)
                {
                    current[j + 1] = a[i] == b[j] ? previous[j] + 1 : 0;
                    if (current[j + 1] > cover[i]) cover[i] = current[j + 1];
                }

                (previous, current) = (current, previous);
            }

            return cover;
        }

        private static int Lcs(List<string> a, List<string> b)
        {
            var previous = new int[b.Count + 1];
            var current = new int[b.Count + 1];
            foreach (var x in a)
            {
                for (var j = 0; j < b.Count; j++)
                    current[j + 1] = x == b[j] ? previous[j] + 1 : Math.Max(previous[j + 1], current[j]);
                (previous, current) = (current, previous);
            }

            return previous[b.Count];
        }

        private static string Excerpt(string text) => text.Length <= 120 ? text : text[..120] + "...";
    }
}
