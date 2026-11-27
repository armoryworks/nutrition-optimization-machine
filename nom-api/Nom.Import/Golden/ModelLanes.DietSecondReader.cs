using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;

namespace Nom.Import.Golden
{
    /// <summary>
    /// One judged row of diet-second-reader.tsv: a recipe and one severe type, the judged truth
    /// (contains: contains or may contain the type's food; safe: plainly safe; doubtful), the rules'
    /// verdict when it was judged (shown, flagged, unknown), the kind (the miss kind of a shown
    /// recipe judged a miss, else the hold or pick kind), the row's weight in its frame (a sampled
    /// safe pick stands for the picks of its draw it was drawn from) and the frame it belongs to
    /// ("draws" for the whole proof draws, whose shown picks are the bar's denominator).
    /// </summary>
    public sealed record DietReaderCase(string Id, long RecipeId, string Type, string Truth, string Rules, string Kind, double Weight, string Frame, string JudgedBy, DietSecondRecipe Recipe)
    {
        public const string Contains = "contains";
        public const string Safe = "safe";
        public const string Doubtful = "doubtful";

        /// <summary>Whether the row is scored: a doubtful row is reported but neither passes nor fails.</summary>
        public bool Scored => Truth is Contains or Safe;

        /// <summary>A case passes when a contains row is not read free and a safe row is; reading a contains row free is a false accept, and no readable answer is never free.</summary>
        public LaneCaseResult Outcome(DietSecondReading? reading)
        {
            var free = reading == DietSecondReading.Free;
            var actual = reading is { } r ? OllamaDietSecondReader.WordOf(r) : "no answer";
            return new LaneCaseResult(Id, Truth == Contains ? !free : free, Truth == Contains && free, Truth == Contains, Truth, actual, JudgedBy);
        }
    }

    /// <summary>The bar figures of the draws frame under a combined rule: the misses still shown, the weighted picks still shown, those picks at their 95 % lower bound and the upper bound of the hidden-food rate over them.</summary>
    public sealed record DietReaderBar(int Hidden, int Any, double Kept, double KeptLow, double Upper)
    {
        public int KeptPicks => (int)Math.Round(Kept);

        public int KeptLowPicks => (int)Math.Round(KeptLow);

        /// <summary>Met at the upper bound, met at the point figure only, or not met, against <see cref="DietReaderReport.Bar"/>.</summary>
        public string Verdict => Upper <= DietReaderReport.Bar ? "met at the upper bound" : Hidden <= KeptPicks * DietReaderReport.Bar ? "met at the point figure only" : "not met";
    }

    /// <summary>The readings of one run of the second diet reader: each recipe's answers (null when unreadable) and the seconds each call took.</summary>
    public sealed record DietReaderRun(IReadOnlyList<DietReaderCase> Cases, IReadOnlyDictionary<long, DietSecondRead?> Answers, IReadOnlyList<double> Seconds);

    public static partial class ModelLanes
    {
        public const string DietSecondReaderLane = "diet-second-reader";
        public const string DietSecondReaderFile = "diet-second-reader.tsv";
        public const string AnswersKey = "Golden:Answers";
        public const string AgreeKey = "Golden:Agree";

        /// <summary>The live recipes the hours figure of a run is given for.</summary>
        public const int LiveRecipes = 116_000;

        /// <summary>The rows of diet-second-reader.tsv, each with its recipe's text (lines and method steps separated by ¦).</summary>
        public static IReadOnlyList<DietReaderCase> DietReaderCases(string goldenDir) =>
            GoldenTsv.Read(Path.Combine(goldenDir, DietSecondReaderFile)).Rows.Select(r => new DietReaderCase(
                r.Id, long.Parse(r["recipe"], CultureInfo.InvariantCulture), r["type"], r["truth"], r["rules"], r["kind"],
                r["weight"].Length == 0 ? 1 : double.Parse(r["weight"], CultureInfo.InvariantCulture), r["frame"], r["source"],
                new DietSecondRecipe(r["title"], Split(r["lines"]), Split(r["method"])))).ToList();

        /// <summary>Whether a row is in a half: the split is by a hash of the recipe id alone, so every type of one recipe falls in the same half and one call serves them all.</summary>
        public static bool InDietSplit(DietReaderCase c, string split) => InSplit(c.RecipeId.ToString(CultureInfo.InvariantCulture), split);

        /// <summary>
        /// Asks the reader once per recipe of the half, in recipe order, and returns every answer with
        /// the time each call took. A recipe the host does not answer within the client's timeout on any
        /// attempt (a reply that runs on to the context's end) is kept as unreadable, never free, and the
        /// run goes on. Writes nothing; the caller may keep the answers (--answers).
        /// </summary>
        public static async Task<DietReaderRun> DietReaderRunAsync(string goldenDir, HttpClient http, string host, string model, int? limit, Action<string> progress, CancellationToken cancellationToken, string split = SplitAll)
        {
            var cases = DietReaderCases(goldenDir).Where(c => InDietSplit(c, split)).ToList();
            var recipes = cases.GroupBy(c => c.RecipeId).OrderBy(g => g.Key).Take(limit ?? int.MaxValue).ToList();
            var keep = recipes.Select(g => g.Key).ToHashSet();
            var reader = new OllamaDietSecondReader(http, host, model);
            var answers = new Dictionary<long, DietSecondRead?>();
            var seconds = new List<double>();
            foreach (var recipe in recipes)
            {
                var watch = Stopwatch.StartNew();
                try
                {
                    answers[recipe.Key] = await AskAsync(() => reader.ReadAsync(recipe.First().Recipe, cancellationToken), progress, cancellationToken);
                }
                catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException && !cancellationToken.IsCancellationRequested)
                {
                    answers[recipe.Key] = null;
                    progress($"{DietSecondReaderLane}: recipe {recipe.Key} not answered in time on any attempt; kept as unreadable.");
                }
                seconds.Add(watch.Elapsed.TotalSeconds);
                if (answers.Count % 25 == 0 || answers.Count == recipes.Count) progress($"{DietSecondReaderLane}: {answers.Count}/{recipes.Count} recipes");
            }
            return new DietReaderRun(cases.Where(c => keep.Contains(c.RecipeId)).ToList(), answers, seconds);
        }

        /// <summary>A run made again from answers a run kept (--replay): the rows of the half whose recipes the answers hold, no model asked, no seconds.</summary>
        public static DietReaderRun DietReaderReplay(string goldenDir, IReadOnlyDictionary<long, DietSecondRead?> answers, string split = SplitAll)
        {
            var cases = DietReaderCases(goldenDir).Where(c => InDietSplit(c, split) && answers.ContainsKey(c.RecipeId)).ToList();
            var recipes = cases.Select(c => c.RecipeId).ToHashSet();
            return new DietReaderRun(cases, answers.Where(a => recipes.Contains(a.Key)).ToDictionary(a => a.Key, a => a.Value), Array.Empty<double>());
        }

        /// <summary>The scored outcome of every row a run read: doubtful rows are left out.</summary>
        public static IReadOnlyList<LaneCaseResult> DietReaderResults(DietReaderRun run) =>
            run.Cases.Where(c => c.Scored).Select(c => c.Outcome(ReadingOf(run.Answers, c))).ToList();

        /// <summary>The reading a run gave a row's type, or null when the recipe was not read or its reply was unreadable.</summary>
        public static DietSecondReading? ReadingOf(IReadOnlyDictionary<long, DietSecondRead?> answers, DietReaderCase c) =>
            answers.TryGetValue(c.RecipeId, out var a) && a?.Answers.FirstOrDefault(x => x.Type == c.Type) is { } answer ? answer.Reading : null;

        /// <summary>Writes a run's answers as JSON lines, one per recipe: {"recipe":1,"unlisted":[...],"complete":true,"answers":{"Celiac Disease":["free",""],...}} (null for an unreadable reply).</summary>
        public static void WriteDietAnswers(string path, IReadOnlyDictionary<long, DietSecondRead?> answers) =>
            File.WriteAllLines(path, answers.OrderBy(a => a.Key).Select(a => JsonSerializer.Serialize(new
            {
                recipe = a.Key,
                unlisted = a.Value?.Unlisted,
                complete = a.Value?.Complete,
                answers = a.Value?.Answers.ToDictionary(x => x.Type, x => new[] { OllamaDietSecondReader.WordOf(x.Reading), x.Food }),
            })));

        /// <summary>Reads answers written by <see cref="WriteDietAnswers"/> (another model's run, for --agree).</summary>
        public static IReadOnlyDictionary<long, DietSecondRead?> ReadDietAnswers(string path)
        {
            var result = new Dictionary<long, DietSecondRead?>();
            foreach (var line in File.ReadLines(path).Where(l => l.Trim().Length > 0))
            {
                using var doc = JsonDocument.Parse(line);
                var recipe = doc.RootElement.GetProperty("recipe").GetInt64();
                if (!doc.RootElement.TryGetProperty("answers", out var a) || a.ValueKind != JsonValueKind.Object)
                {
                    result[recipe] = null;
                    continue;
                }
                var unlisted = doc.RootElement.TryGetProperty("unlisted", out var u) && u.ValueKind == JsonValueKind.Array ? u.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToList() : new List<string>();
                var whole = doc.RootElement.TryGetProperty("complete", out var c) && c.ValueKind == JsonValueKind.True;
                result[recipe] = new DietSecondRead(unlisted, whole, a.EnumerateObject().Select(p => new DietSecondAnswer(p.Name,
                    Enum.GetValues<DietSecondReading>().First(r => OllamaDietSecondReader.WordOf(r) == p.Value[0].GetString()), p.Value[1].GetString() ?? string.Empty)).ToList());
            }
            return result;
        }

        private static IReadOnlyList<string> Split(string cell) =>
            cell.Length == 0 || cell == GoldenTsv.NoAnswer ? Array.Empty<string>() : cell.Split(" ¦ ", StringSplitOptions.None);
    }

    /// <summary>
    /// The figures a run of the second diet reader is judged by (plan item ck20), per severe type and
    /// in all: (a) of the shown recipes judged a miss, how many the reader does not call clearly free,
    /// by miss kind; (b) of the shown recipes judged safe, the weighted share it does not call free
    /// (the coverage a second reader costs); (c) of the unknown holds judged plainly safe, how many it
    /// calls free, and of those judged not safe, how many; the flagged side as a check; the bar of the
    /// "draws" frame (hidden-food misses that the rules and the reader would both show, per weighted
    /// pick still shown, with the Wilson 95 % interval; see <see cref="BarOf"/>) and the share of shown picks kept; with a second
    /// model's answers (--agree), the same unknown figures when both call a hold free; and the seconds
    /// per recipe with the hours for <see cref="ModelLanes.LiveRecipes"/> recipes.
    /// </summary>
    public static class DietReaderReport
    {
        /// <summary>The miss kinds that hide a food the type restricts (a referral that names no food, or a choice line, does not).</summary>
        public static readonly IReadOnlySet<string> HiddenKinds = new HashSet<string>(StringComparer.Ordinal)
        {
            "title", "base-lost", "paste-lost", "method-food", "named-dish", "word", "composite", "catalog", "link", "made-product", "analogue",
        };

        /// <summary>The bar the owner was asked about: at most one hidden-food miss in 330 weighted shown picks.</summary>
        public const double Bar = 1.0 / 330;

        public static IReadOnlyList<string> Lines(DietReaderRun run, IReadOnlyDictionary<long, DietSecondRead?>? agree = null)
        {
            var lines = new List<string>();
            bool Free(DietReaderCase c) => ModelLanes.ReadingOf(run.Answers, c) == DietSecondReading.Free;
            bool BothFree(DietReaderCase c) => Free(c) && agree != null && ModelLanes.ReadingOf(agree, c) == DietSecondReading.Free;
            var unreadable = run.Answers.Count(a => a.Value == null);
            lines.Add($"Recipes read {run.Answers.Count}, unreadable replies {unreadable}; rows {run.Cases.Count} ({run.Cases.Count(c => !c.Scored)} doubtful, not scored)");
            lines.Add("type | (a) shown misses held | (b) shown safe held (weighted) | (c) unknown safe called free | unknown not safe called free | flagged contains held | wrong flags called free");
            foreach (var type in OllamaDietSecondReader.Types.Select(t => t.Type).Append("all"))
            {
                var rows = run.Cases.Where(c => type == "all" || c.Type == type).ToList();
                var misses = rows.Where(c => c.Rules == "shown" && c.Truth == DietReaderCase.Contains).ToList();
                var safe = rows.Where(c => c.Rules == "shown" && c.Truth == DietReaderCase.Safe).ToList();
                var unknownSafe = rows.Where(c => c.Rules == "unknown" && c.Truth == DietReaderCase.Safe).ToList();
                var unknownNot = rows.Where(c => c.Rules == "unknown" && c.Truth == DietReaderCase.Contains).ToList();
                var flagged = rows.Where(c => c.Rules == "flagged" && c.Truth == DietReaderCase.Contains).ToList();
                var wrong = rows.Where(c => c.Rules == "flagged" && c.Truth == DietReaderCase.Safe).ToList();
                var safeWeight = safe.Sum(c => c.Weight);
                var safeHeld = safe.Where(c => !Free(c)).Sum(c => c.Weight);
                lines.Add($"{type} | {misses.Count(c => !Free(c))}/{misses.Count} | {Share(safeHeld, safeWeight)} of {safe.Count} | {unknownSafe.Count(Free)}/{unknownSafe.Count} | {unknownNot.Count(Free)}/{unknownNot.Count} | {flagged.Count(c => !Free(c))}/{flagged.Count} | {wrong.Count(Free)}/{wrong.Count}");
            }

            lines.Add("(a) by miss kind: held/total");
            foreach (var kind in run.Cases.Where(c => c.Rules == "shown" && c.Truth == DietReaderCase.Contains).GroupBy(c => c.Kind).OrderBy(g => g.Key, StringComparer.Ordinal))
                lines.Add($"  {kind.Key}{(HiddenKinds.Contains(kind.Key) ? " (hidden food)" : string.Empty)}: {kind.Count(c => !Free(c))}/{kind.Count()}");
            var doubtfulShown = run.Cases.Where(c => c.Rules == "shown" && c.Truth == DietReaderCase.Doubtful).ToList();
            lines.Add($"Shown doubtful held: {doubtfulShown.Count(c => !Free(c))}/{doubtfulShown.Count}");

            var frame = run.Cases.Where(c => c.Frame == "draws" && c.Rules == "shown").ToList();
            if (frame.Count > 0)
            {
                var weight = frame.Sum(c => c.Weight);
                var n = (int)Math.Round(weight);
                var hiddenBefore = frame.Count(c => c.Truth == DietReaderCase.Contains && HiddenKinds.Contains(c.Kind));
                var anyBefore = frame.Count(c => c.Truth == DietReaderCase.Contains);
                var both = BarOf(frame, Free);
                lines.Add($"Draws frame: {n} weighted shown picks ({frame.Count} rows read)");
                lines.Add($"  hidden-food misses shown by the rules alone {hiddenBefore} ({Rate(hiddenBefore, n)})");
                lines.Add($"  by rules and reader both clear {Line(both)}");
                lines.Add($"  any miss shown by the rules alone {anyBefore} ({Rate(anyBefore, n)}); by both {both.Any} ({Rate(both.Any, both.KeptPicks)})");
                lines.Add($"  shown picks the reader also calls clear {Share(both.Kept, weight)}");
            }

            if (agree != null)
            {
                var unknownSafe = run.Cases.Where(c => c.Rules == "unknown" && c.Truth == DietReaderCase.Safe).ToList();
                var unknownNot = run.Cases.Where(c => c.Rules == "unknown" && c.Truth == DietReaderCase.Contains).ToList();
                var released = unknownSafe.Count(BothFree) + unknownNot.Count(BothFree);
                lines.Add($"Both readers clear on unknown holds: plainly safe {unknownSafe.Count(BothFree)}/{unknownSafe.Count}; not safe {unknownNot.Count(BothFree)}/{unknownNot.Count} ({Rate(unknownNot.Count(BothFree), unknownNot.Count)} of not-safe holds released); of the holds released, not safe {Rate(unknownNot.Count(BothFree), released)}");
                if (frame.Count > 0)
                {
                    var both = BarOf(frame, BothFree);
                    lines.Add($"  draws frame, hidden-food misses shown when the rules and both readers are clear: {Line(both)}; shown picks kept {Share(both.Kept, frame.Sum(c => c.Weight))}");
                }
            }

            if (run.Seconds.Count > 0)
            {
                var mean = run.Seconds.Average();
                lines.Add($"Seconds per recipe {mean:0.00} (median {run.Seconds.OrderBy(s => s).ElementAt(run.Seconds.Count / 2):0.00}); {ModelLanes.LiveRecipes:N0} recipes: {mean * ModelLanes.LiveRecipes / 3600:0.0} h");
            }
            return lines;
        }

        /// <summary>
        /// The bar figures of the draws frame when a recipe is shown only if <paramref name="keep"/>
        /// also finds it clear: the hidden-food and any misses still shown, over the weighted picks
        /// still shown (not the picks the rules alone show). The kept weight of a sampled row (weight
        /// above 1) is an estimate from a few samples a draw, so the upper bound the verdict is taken
        /// from puts the kept share of the sampled rows at its own Wilson 95 % lower bound.
        /// </summary>
        public static DietReaderBar BarOf(IReadOnlyList<DietReaderCase> frame, Func<DietReaderCase, bool> keep)
        {
            var kept = frame.Where(keep).ToList();
            var keptWeight = kept.Sum(c => c.Weight);
            var sampled = frame.Count(c => c.Weight > 1);
            var sampledKept = kept.Where(c => c.Weight > 1).ToList();
            var sampledKeptWeight = sampledKept.Sum(c => c.Weight);
            var keptLow = keptWeight - sampledKeptWeight + (sampledKept.Count == 0 ? 0 : sampledKeptWeight * LaneQualifier.Wilson(sampledKept.Count, sampled).Lower * sampled / sampledKept.Count);
            var hidden = kept.Count(c => c.Truth == DietReaderCase.Contains && HiddenKinds.Contains(c.Kind));
            var any = kept.Count(c => c.Truth == DietReaderCase.Contains);
            return new DietReaderBar(hidden, any, keptWeight, keptLow, LaneQualifier.Wilson(hidden, (int)Math.Round(keptLow)).Upper);
        }

        private static string Line(DietReaderBar bar) =>
            $"{bar.Hidden} in {bar.KeptPicks} weighted picks still shown ({Rate(bar.Hidden, bar.KeptPicks)}); upper {Percent(bar.Upper, 2)} with the kept picks at their 95 % lower bound ({bar.KeptLowPicks}); bar {Percent(Bar, 2)} (1 in 330): {bar.Verdict}";

        /// <summary>A count over n with its Wilson 95 % interval.</summary>
        public static string Rate(int k, int n)
        {
            var (lower, upper) = LaneQualifier.Wilson(k, n);
            return n == 0 ? "n/a" : $"{Percent((double)k / n, 2)}, 95 % CI {Percent(lower, 2)} to {Percent(upper, 2)}";
        }

        private static string Share(double part, double whole) => whole <= 0 ? "n/a" : Percent(part / whole, 1);

        private static string Percent(double share, int decimals) => (share * 100).ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) + " %";
    }
}
