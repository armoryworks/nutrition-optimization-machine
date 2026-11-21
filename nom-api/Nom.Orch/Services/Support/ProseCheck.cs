using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// Check v3 of a recipe's prose in our own words against its source, the gate the re-check and
    /// the rewrite lane share. A rewrite passes only when every rule holds:
    /// <list type="number">
    /// <item>Quantities: every amount of the source method is stated with its unit and range
    /// (<see cref="ProseQuantities"/>), once for each time the source states it (an oven
    /// temperature restated counts once), and the rewrite states no amount the source, its title
    /// or its ingredient lines lack. A bracketed conversion may be dropped. A soft count is
    /// compared only where both texts state one before the same word ("two equal parts").</item>
    /// <item>Amounts against the ingredient lines: an amount within <see cref="IngredientWindow"/>
    /// words before an ingredient's name equals the source method's amount for it, or else that
    /// line's amount, the sum of its lines or a method amount next to no other ingredient; an
    /// amount the method states for another ingredient does not count. One that conflicts is
    /// <see cref="WrongAmount"/>, and
    /// where the method disagrees with its own lines the method's amount is the one to keep.</item>
    /// <item>Coverage: every cooking verb of the source (<see cref="ProseVerbLexicon"/>) and every
    /// ingredient a source step names is kept (under its own name or a listed other name), the
    /// source steps' amounts appear in their order, and a method of
    /// <see cref="MinimumWordsForLength"/> words or more is rewritten at
    /// <see cref="MinimumLengthRatio"/> to <see cref="MaximumLengthRatio"/> times its length.</item>
    /// <item>Copy, measured on the whole text, never step by step: the rewrite's description and
    /// steps are one stream of words and the source's description and method another
    /// (<see cref="CopyTokens"/>: every amount one token however it is written). No common run
    /// may weigh <see cref="CopiedRun"/> or more, an ingredient name or amount counting
    /// <see cref="ListWordWeight"/>; the words inside common runs of <see cref="SharedRun"/> or
    /// more words, weighed the same way, may make up less than <see cref="MaximumCopiedShare"/>
    /// of a rewrite of <see cref="MinimumWordsForShare"/> words or more; the description may share
    /// no run weighing <see cref="DescriptionRun"/> or more with the source description; and no
    /// sentence of <see cref="SentenceWords"/> words or more may match a source sentence at
    /// <see cref="SentenceSimilarity"/> of its words in order; and no stretch of the rewrite may follow
    /// the source in order with a score of <see cref="CloseStretchLimit"/> or more, each word kept in
    /// order adding its weight and each word changed, added or dropped costing
    /// <see cref="StretchPenalty"/> (a close paraphrase that changes a word every few words). Stock
    /// phrases count like any other words; the <see cref="ProseStockIndex"/> version is only
    /// recorded.</item>
    /// <item>Form: no step marker and no empty step.</item>
    /// </list>
    /// <see cref="Version"/> changes whenever a rule or a limit does. Text only: no database and no
    /// model.
    /// </summary>
    public static class ProseCheck
    {
        public const int Version = 3;

        public const decimal CopiedRun = 14.5m;
        public const int SharedRun = 7;
        public const decimal MaximumCopiedShare = 0.37m;
        public const int MinimumWordsForShare = 40;
        public const int SentenceWords = 15;
        public const decimal DescriptionRun = 10m;
        public const decimal ListWordWeight = 0.5m;
        public const decimal SentenceSimilarity = 0.9m;
        public const decimal CloseStretchLimit = 26m;
        public const decimal StretchPenalty = 2m;
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
        public const string OutOfOrder = "out-of-order";
        public const string TooShort = "too-short";
        public const string TooLong = "too-long";
        public const string CopiedText = "copied-text";
        public const string Unchanged = "unchanged";
        public const string StepMarker = "step-marker";
        public const string EmptyStep = "empty-step";

        /// <summary>The reasons that concern amounts, most serious first.</summary>
        public static readonly IReadOnlyList<string> QuantityReasons = new[] { WrongAmount, ChangedUnit, ChangedRange, LostQuantity, AddedQuantity };

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

        /// <summary>One rule a rewrite broke, and what broke it.</summary>
        public sealed record Fault(string Reason, string Detail)
        {
            public override string ToString() => $"{Reason}: {Detail}";
        }

        /// <summary>What the check found, with the copy measures it computed (the share of the rewrite's words in copied runs, the sentences kept nearly word for word, the longest copied run) and the check and stock-index versions it ran with.</summary>
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

            public bool Passed => Faults.Count == 0;

            public IReadOnlyList<string> Reasons => Faults.Select(f => f.Reason).Distinct().ToList();

            public override string ToString() => Passed ? "passed" : string.Join("; ", Faults);
        }

        private sealed record Ingredient(string Head, IReadOnlyList<ProseQuantity> Amounts);

        private sealed class Located
        {
            public Located(ProseQuantity quantity, string text, int step)
            {
                Quantity = quantity;
                Text = text;
                Step = step;
            }

            public ProseQuantity Quantity { get; }

            public string Text { get; }

            public int Step { get; }
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
            var (share, sentences, run, stretch) = CheckCopy(method, sourceDescription, steps.Where(s => s.Length > 0).ToList(), description, ListWords(ingredientLines ?? Array.Empty<string>()), faults);

            return new Finding(faults, share, sentences, run, lengthRatio, stock.Version) { CloseStretch = stretch };
        }

        private static void CheckQuantities(
            List<string> method, string sourceDescription, string sourceTitle,
            List<string> steps, string description, string title,
            List<Ingredient> ingredients, List<Fault> faults)
        {
            var sourceQuantities = method.SelectMany((text, i) => ProseQuantities.ReadPrepared(text).Select(q => new Located(q, text, i))).ToList();
            var optional = ProseQuantities.Read(sourceDescription).Concat(ProseQuantities.Read(sourceTitle)).Concat(ProseQuantities.Read(title)).ToList();
            var ours = steps.Select((text, i) => (Text: text, Step: i)).Prepend((Text: description, Step: -1))
                .SelectMany(s => ProseQuantities.ReadPrepared(s.Text).Select(q => new Located(q, s.Text, s.Step)))
                .ToList();

            var softContexts = sourceQuantities.Where(s => s.Quantity.Soft).Select(SoftWord)
                .Intersect(ours.Where(o => o.Quantity.Soft).Select(SoftWord), StringComparer.Ordinal)
                .Where(w => w.Length > 0)
                .ToHashSet(StringComparer.Ordinal);
            bool Compared(Located q) => !q.Quantity.Soft || softContexts.Contains(SoftWord(q));

            var required = sourceQuantities.Where(s => !s.Quantity.Conversion && Compared(s)).ToList();
            var used = new HashSet<Located>();
            var lost = new List<Located>();
            foreach (var s in required)
            {
                var match = ours.FirstOrDefault(o => (s.Quantity.Family == ProseQuantities.Temperature || !used.Contains(o)) && ProseQuantities.Equal(s.Quantity, o.Quantity));
                if (match == null) lost.Add(s);
                else used.Add(match);
            }

            var unmatched = new List<Located>();
            var conflicting = new List<Located>();
            foreach (var o in ours)
            {
                if (!Compared(o)) continue;

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

        private static string SoftWord(Located located)
        {
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
            if (ingredients.Count == 0) return null;
            if (BracketOwner.Match(located.Text[..located.Quantity.Start]) is { Success: true } owner
                && ingredients.FirstOrDefault(i => Names(owner.Groups["word"].Value.ToLowerInvariant(), i.Head)) is { } bracketed)
                return bracketed;
            var after = WordToken.Matches(located.Text[located.Quantity.End..]).Select(m => m.Value.ToLowerInvariant()).Take(IngredientWindow).ToList();
            foreach (var word in after)
            {
                var match = ingredients.FirstOrDefault(i => Names(word, i.Head));
                if (match != null) return match;
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
                var words = WordToken.Matches(name.ToLowerInvariant()).Select(m => m.Value)
                    .Where(w => w.Length > 1 && !LineStopWords.Contains(w) && !IsUnitWord(w))
                    .ToList();
                if (words.Count == 0) continue;
                ingredients.Add(new Ingredient(words[^1], amounts));
            }

            return ingredients;
        }

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
                var words = ProsePhrases.WordsOfPrepared(method[i]);
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
                    if (IngredientSynonyms.TryGetValue(ingredient, out var others) && ourWordSet.Any(w => others.Any(o => Names(w, o)))) continue;
                    if (!missingIngredients.Contains($"{ingredient} (step {i + 1})")) missingIngredients.Add($"{ingredient} (step {i + 1})");
                }
            }

            if (missingVerbs.Count > 0) faults.Add(new Fault(LostStep, $"no {string.Join(", ", missingVerbs)}"));
            if (missingIngredients.Count > 0) faults.Add(new Fault(LostStep, $"no {string.Join(", ", missingIngredients)}"));

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
            List<string> method, string sourceDescription, List<string> steps, string description,
            HashSet<string> listWords, List<Fault> faults)
        {
            var methodWords = string.Join(" ", method.SelectMany(ProsePhrases.WordsOfPrepared));
            var stepWords = string.Join(" ", steps.SelectMany(ProsePhrases.WordsOfPrepared));
            if (methodWords.Length > 0 && methodWords == stepWords) faults.Add(new Fault(Unchanged, "the steps are the source method word for word"));

            var sourceTexts = method.Prepend(sourceDescription).Where(t => t.Length > 0).ToList();
            var ourTexts = steps.Prepend(description).Where(t => t.Length > 0).ToList();
            var theirs = sourceTexts.SelectMany(CopyTokens).ToList();
            var ours = ourTexts.SelectMany(CopyTokens).ToList();

            var cover = CommonRuns(ours, theirs);
            var weight = Weights(ours, listWords);

            var copied = new bool[ours.Count];
            var longest = 0;
            var longestWeight = 0m;
            var longestEnd = -1;
            for (var i = 0; i < ours.Count; i++)
            {
                var start = i - cover[i] + 1;
                if (cover[i] < SharedRun) continue;
                for (var j = start; j <= i; j++) copied[j] = true;
                var w = weight[i + 1] - weight[start];
                if (w <= longestWeight) continue;
                longest = cover[i];
                longestWeight = w;
                longestEnd = i;
            }

            if (longestWeight >= CopiedRun)
                faults.Add(new Fault(CopiedText, RunDetail(ours, longestEnd, longest, longestWeight)));

            var stretch = CloseStretch(ours, theirs, listWords);
            if (stretch.Score >= CloseStretchLimit)
                faults.Add(new Fault(CopiedText, $"a stretch of {stretch.End - stretch.Start + 1} words follows the source in order with a few words changed (score {stretch.Score.ToString("0.#", CultureInfo.InvariantCulture)}): \"{Excerpt(string.Join(" ", ours.Skip(stretch.Start).Take(stretch.End - stretch.Start + 1)))}\""));

            var ourDescription = CopyTokens(description);
            var descriptionCover = CommonRuns(ourDescription, CopyTokens(sourceDescription));
            var descriptionWeight = Weights(ourDescription, listWords);
            var descriptionRun = 0;
            var descriptionEnd = -1;
            var descriptionRunWeight = 0m;
            for (var i = 0; i < ourDescription.Count; i++)
            {
                var w = descriptionWeight[i + 1] - descriptionWeight[i - descriptionCover[i] + 1];
                if (w <= descriptionRunWeight) continue;
                (descriptionRun, descriptionEnd, descriptionRunWeight) = (descriptionCover[i], i, w);
            }

            if (descriptionRunWeight >= DescriptionRun)
                faults.Add(new Fault(CopiedText, "the description keeps " + RunDetail(ourDescription, descriptionEnd, descriptionRun, descriptionRunWeight) + " of the source's"));

            var copiedWeight = Enumerable.Range(0, ours.Count).Where(i => copied[i]).Sum(i => weight[i + 1] - weight[i]);
            var share = ours.Count == 0 ? 0m : Math.Round(copiedWeight / weight[ours.Count], 3);
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

        private static string RunDetail(List<string> tokens, int end, int length, decimal weight) =>
            $"a run of {length} words ({weight.ToString("0.#", CultureInfo.InvariantCulture)} with ingredient names and amounts at half): \"{string.Join(" ", tokens.Skip(end - length + 1).Take(length))}\"";

        private static decimal[] Weights(List<string> tokens, HashSet<string> listWords)
        {
            var weights = new decimal[tokens.Count + 1];
            for (var i = 0; i < tokens.Count; i++)
                weights[i + 1] = weights[i] + (tokens[i] == ProsePhrases.Number || listWords.Contains(tokens[i]) ? ListWordWeight : 1m);
            return weights;
        }

        private static HashSet<string> ListWords(IReadOnlyList<string> lines)
        {
            var words = new HashSet<string>(StringComparer.Ordinal);
            foreach (var raw in lines)
            {
                var line = ProseQuantities.Prepare(raw);
                var name = Brackets.Replace(ProseQuantities.WithoutAmounts(line, ProseQuantities.ReadPrepared(line)), " ");
                var comma = name.IndexOf(',');
                if (comma > 0) name = name[..comma];
                foreach (var word in WordToken.Matches(name.ToLowerInvariant()).Select(m => m.Value).Where(w => w.Length > 2 && !IsUnitWord(w) && !LineStopWords.Contains(w)))
                {
                    words.Add(word);
                    words.Add(word.EndsWith('s') ? word[..^1] : word + "s");
                    if (word.EndsWith('y')) words.Add(word[..^1] + "ies");
                }
            }

            return words;
        }

        private static (decimal Score, int Start, int End) CloseStretch(List<string> ours, List<string> theirs, HashSet<string> listWords)
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
                var kept = ours[i] == ProsePhrases.Number || listWords.Contains(ours[i]) ? 1 : 2;
                current[0] = 0;
                for (var j = 1; j <= m; j++)
                {
                    var (score, start) = ours[i] == theirs[j - 1]
                        ? (previous[j - 1] + kept, previous[j - 1] > 0 ? previousStart[j - 1] : i)
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
