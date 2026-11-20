using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// Check v2 of a recipe's prose in our own words against its source, the gate the re-check and
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
    /// ingredient a source step names is kept, the source steps' amounts appear in their order,
    /// and a method of <see cref="MinimumWordsForLength"/> words or more is rewritten at
    /// <see cref="MinimumLengthRatio"/> to <see cref="MaximumLengthRatio"/> times its length.</item>
    /// <item>Copy, ignoring stock phrases (<see cref="ProseStockIndex"/>): at most
    /// <see cref="MaximumContainment"/> of the source's distinctive five-word phrases (counted when
    /// it has <see cref="MinimumPhrasesForContainment"/> or more), no copied distinctive run longer
    /// than <see cref="MaximumRun"/> words, and no source sentence of
    /// <see cref="SentenceWords"/> or more distinctive words kept whole or with
    /// <see cref="SentenceCopiedShare"/> or more of those words inside copied phrases. The
    /// description is checked against the source description and method together.</item>
    /// <item>Form: no step marker and no empty step.</item>
    /// </list>
    /// <see cref="Version"/> changes whenever a rule or a limit does. Text only: no database and no
    /// model.
    /// </summary>
    public static class ProseCheck
    {
        public const int Version = 2;

        public const decimal MaximumContainment = 0.12m;
        public const int MinimumPhrasesForContainment = 20;
        public const int MaximumRun = 11;
        public const int SentenceWords = 10;
        public const decimal SentenceCopiedShare = 0.8m;
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

        private static readonly Regex WordToken = new(@"\p{L}+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex SentenceEnd = new(@"(?<=[.!?;])\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex Brackets = new(@"\([^()]*\)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>One rule a rewrite broke, and what broke it.</summary>
        public sealed record Fault(string Reason, string Detail)
        {
            public override string ToString() => $"{Reason}: {Detail}";
        }

        /// <summary>What check v2 found, with the copy measures it computed and the check and stock-index versions it ran with.</summary>
        public sealed record Finding(
            IReadOnlyList<Fault> Faults,
            decimal Containment,
            int DistinctivePhrases,
            int LongestRun,
            decimal LengthRatio,
            int StockVersion)
        {
            public int CheckVersion { get; init; } = Version;

            public bool Passed => Faults.Count == 0;

            public IReadOnlyList<string> Reasons => Faults.Select(f => f.Reason).Distinct().ToList();

            public override string ToString() => Passed ? "passed" : string.Join("; ", Faults);
        }

        private sealed record Ingredient(string Head, IReadOnlyList<ProseQuantity> Amounts);

        private sealed record Located(ProseQuantity Quantity, string Text, int Step);

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
            var (containment, distinctive, run) = CheckCopy(method, sourceDescription, steps.Where(s => s.Length > 0).ToList(), description, stock, faults);

            return new Finding(faults, containment, distinctive, run, lengthRatio, stock.Version);
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
                var name = line;
                foreach (var q in amounts.OrderByDescending(q => q.Start)) name = name[..q.Start] + " " + name[q.End..];
                name = Brackets.Replace(name, " ");
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
            var ourGroups = verbs.GroupsIn(ourWords);
            var ourWordSet = ourWords.ToHashSet(StringComparer.Ordinal);

            var missingVerbs = new List<string>();
            var missingIngredients = new List<string>();
            for (var i = 0; i < method.Count; i++)
            {
                var words = ProsePhrases.WordsOfPrepared(method[i]);
                foreach (var verb in verbs.VerbsIn(words))
                {
                    if (verb.Overlaps(ourGroups)) continue;
                    var name = string.Join("/", verb.OrderBy(g => g, StringComparer.Ordinal));
                    if (!missingVerbs.Contains($"{name} (step {i + 1})")) missingVerbs.Add($"{name} (step {i + 1})");
                }

                foreach (var ingredient in ingredients.Select(x => x.Head).Distinct())
                {
                    if (!words.Any(w => Names(w, ingredient))) continue;
                    if (ourWordSet.Any(w => Names(w, ingredient))) continue;
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

        private static (decimal Containment, int Distinctive, int Run) CheckCopy(
            List<string> method, string sourceDescription, List<string> steps, string description,
            ProseStockIndex stock, List<Fault> faults)
        {
            var sourceTexts = method.Prepend(sourceDescription).Where(t => t.Length > 0).ToList();
            var ourTexts = steps.Prepend(description).Where(t => t.Length > 0).ToList();

            var sourceWords = sourceTexts.Select(ProsePhrases.WordsOfPrepared).ToList();
            var sourcePhrases = sourceWords.SelectMany(ProsePhrases.Hashes).ToHashSet();
            var distinctive = sourcePhrases.Where(h => !stock.IsStock(h)).ToHashSet();
            var ourWords = ourTexts.Select(ProsePhrases.WordsOfPrepared).ToList();
            var ourPhrases = ourWords.SelectMany(ProsePhrases.Hashes).ToHashSet();

            var methodWords = string.Join(" ", method.SelectMany(ProsePhrases.WordsOfPrepared));
            var stepWords = string.Join(" ", steps.SelectMany(ProsePhrases.WordsOfPrepared));
            if (methodWords.Length > 0 && methodWords == stepWords) faults.Add(new Fault(Unchanged, "the steps are the source method word for word"));

            var shared = distinctive.Count(ourPhrases.Contains);
            var containment = distinctive.Count == 0 ? 0m : Math.Round((decimal)shared / distinctive.Count, 3);
            if (distinctive.Count >= MinimumPhrasesForContainment && containment > MaximumContainment)
                faults.Add(new Fault(CopiedText, $"{shared} of the source's {distinctive.Count} distinctive phrases ({containment.ToString("0.###", CultureInfo.InvariantCulture)})"));

            var longest = 0;
            var longestText = string.Empty;
            foreach (var words in ourWords)
            {
                var hashes = ProsePhrases.Hashes(words);
                var k = 0;
                for (var i = 0; i <= hashes.Count; i++)
                {
                    if (i < hashes.Count && distinctive.Contains(hashes[i]))
                    {
                        k++;
                        continue;
                    }

                    if (k > 0 && k + ProsePhrases.Length - 1 > longest)
                    {
                        longest = k + ProsePhrases.Length - 1;
                        longestText = string.Join(" ", words.Skip(i - k).Take(longest));
                    }

                    k = 0;
                }
            }

            if (longest > MaximumRun) faults.Add(new Fault(CopiedText, $"a run of {longest} words: \"{longestText}\""));

            foreach (var text in sourceTexts)
            {
                foreach (var sentence in SentenceEnd.Split(text))
                {
                    var words = ProsePhrases.WordsOfPrepared(sentence);
                    var (distinctiveWords, copiedWords) = SentenceCopy(words, stock, ourPhrases);
                    if (distinctiveWords < SentenceWords || copiedWords < SentenceCopiedShare * distinctiveWords) continue;
                    faults.Add(new Fault(CopiedText, copiedWords == distinctiveWords
                        ? $"the sentence \"{Excerpt(sentence)}\""
                        : $"{copiedWords} of the {distinctiveWords} distinctive words of the sentence \"{Excerpt(sentence)}\""));
                    return (containment, distinctive.Count, longest);
                }
            }

            return (containment, distinctive.Count, longest);
        }

        private static (int Distinctive, int Copied) SentenceCopy(List<string> words, ProseStockIndex stock, HashSet<long> ourPhrases)
        {
            var stockWord = new bool[words.Count];
            var copiedWord = new bool[words.Count];
            for (var i = 0; i + ProsePhrases.Length <= words.Count; i++)
            {
                var hash = ProsePhrases.Hash(words, i);
                var covered = stock.IsStock(hash) ? stockWord : ourPhrases.Contains(hash) ? copiedWord : null;
                if (covered == null) continue;
                for (var j = i; j < i + ProsePhrases.Length; j++) covered[j] = true;
            }

            var distinctiveWords = stockWord.Count(c => !c);
            var copiedWords = Enumerable.Range(0, words.Count).Count(i => copiedWord[i] && !stockWord[i]);
            return (distinctiveWords, copiedWords);
        }

        private static string Excerpt(string text) => text.Length <= 120 ? text : text[..120] + "...";
    }
}
