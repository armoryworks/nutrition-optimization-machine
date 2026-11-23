using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nom.Data.Nutrition;
using Nom.Orch.Services.Support;

namespace Nom.Import.Golden
{
    /// <summary>One judged line of link-recheck.tsv: the set it was read in, the food it sat on, its lines, and whether the food was wrong, right or doubtful.</summary>
    public sealed record RecheckJudgedRow(string Id, string Set, string Wording, long FoodId, IReadOnlyList<string> Lines, string? Title, string Judged, long? RightFood, string Status, string Source);

    /// <summary>What the re-check decides on one judged row now: its verdict, the class that refutes the food, what that class would write, and whether that is right by the judgement.</summary>
    public sealed record RecheckRowOutcome(RecheckJudgedRow Row, RecheckVerdict Verdict, string? Class, string? Acts, long? TargetId, bool Writes, bool Correct);

    /// <summary>
    /// One re-check class over the judged rows: the rows it would move or take back (the doubtful
    /// left out), how many of them were right, its precision, how many of the rows judged wrong it
    /// catches (coverage), the agent-judged and rule-given split, and whether it is proven and
    /// whether it holds the bar: only the checkpoint-judged rows (status agent) count toward the
    /// minimum and their precision must hold it; rule-given rows must be right too but never lift a
    /// class over the minimum.
    /// </summary>
    public sealed record RecheckClassScore(string Class, int Judged, int Right, int AgentJudged, int AgentRight, int RuleJudged, int RuleRight, int Caught, int WrongRows, bool Proven)
    {
        public double Precision => Judged == 0 ? 0 : (double)Right / Judged;
        public double AgentPrecision => AgentJudged == 0 ? 0 : (double)AgentRight / AgentJudged;
        public double Coverage => WrongRows == 0 ? 0 : (double)Caught / WrongRows;
        public bool MeetsBar => AgentJudged >= LinkRecheckRules.ClassMinimumJudged && AgentPrecision >= LinkRecheckRules.ClassPrecisionBar && Precision >= LinkRecheckRules.ClassPrecisionBar;
    }

    /// <summary>
    /// The per-class scorer of the link re-check (staging checkpoint 3, N12): every judged line of
    /// link-recheck.tsv (the lines both staging checkpoints read, judged wrong or right, and
    /// rule-given rows) is decided by <see cref="LinkRecheckRules.Judge"/> over the frozen catalog
    /// of gate-catalog.tsv, as a run decides it, and each refuting class is scored as if it were
    /// allowed to write: a take-back is right when the food was wrong, a move when the food was
    /// wrong and the new food is the one the row names. A class may write by rule only when it
    /// holds <see cref="LinkRecheckRules.ClassPrecisionBar"/> over at least
    /// <see cref="LinkRecheckRules.ClassMinimumJudged"/> checkpoint-judged rows; <c>--record</c> writes
    /// the classes that hold it to scores/link-recheck-classes.json, which is what
    /// <see cref="LinkRecheckRules.ProvenClasses"/> reads, and <c>--rows</c> prints every row's
    /// decision. Run with <c>dotnet run --project nom-api/Nom.Import -- --recheck-classes [--rows]
    /// [--record] [--golden &lt;dir&gt;]</c>; it opens no database and asks no model.
    /// </summary>
    public static class LinkRecheckClassScorer
    {
        public const string Flag = "--recheck-classes";
        public const string File = "link-recheck.tsv";
        public const string Wrong = "wrong";
        public const string Right = "right";
        public const string Doubtful = "doubtful";
        public const string Separator = " ¦ ";
        public const string RecordFlag = "--record";
        public const string RowsFlag = "--rows";

        /// <summary>The judged rows of link-recheck.tsv.</summary>
        public static IReadOnlyList<RecheckJudgedRow> Rows(string goldenDir) =>
            GoldenTsv.Read(Path.Combine(goldenDir, File)).Rows.Select(r => new RecheckJudgedRow(
                r.Id, r["set"], r["wording"], long.Parse(r["foodId"], CultureInfo.InvariantCulture),
                r["lines"] == GoldenTsv.NoAnswer ? Array.Empty<string>() : r["lines"].Split(Separator),
                r.Value("title"), r["judged"],
                long.TryParse(r["rightFood"], NumberStyles.None, CultureInfo.InvariantCulture, out var right) ? right : null,
                r["status"], r["source"])).ToList();

        /// <summary>Every row decided now, over the frozen catalog.</summary>
        public static IReadOnlyList<RecheckRowOutcome> Decide(string goldenDir)
        {
            var catalog = ModelLanes.GoldenGateCatalog(goldenDir);
            var foods = ModelLanes.GateFoods(goldenDir).ToDictionary(f => f.Id);
            var byFdc = foods.Values.Where(f => f.FdcId != null).GroupBy(f => f.FdcId!, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(f => f.Status == (long)Nom.Data.Recipe.CurationStatusEnum.Curated).ThenBy(f => f.Id).First(), StringComparer.Ordinal);
            return Rows(goldenDir).Select(row => Decide(row, catalog, foods, byFdc)).ToList();
        }

        /// <summary>One row decided as a run decides its group: the alcohol, container and prepared sets act only on the lines of their kind; the others on the wording's lines on the food.</summary>
        public static RecheckRowOutcome Decide(RecheckJudgedRow row, GateCatalog catalog, IReadOnlyDictionary<long, GateFood> foods, IReadOnlyDictionary<string, GateFood> byFdc)
        {
            if (!foods.TryGetValue(row.FoodId, out var food)) throw new InvalidDataException($"{row.Id}: food #{row.FoodId} is not in gate-catalog.tsv");
            var keep = new RecheckVerdict(LinkRecheckRules.Keep, null, null, Array.Empty<GateHold>());
            var contexts = row.Lines.Select((l, i) => new AlcoholRecipeContext(row.Title, row.Lines.Where((_, j) => j != i).ToList(), null, false)).ToList();
            var wording = row.Wording;
            string? sense = null;
            var drained = false;
            switch (row.Set)
            {
                case LinkRecheckRules.Alcohol:
                {
                    var readings = row.Lines.Select((l, i) => AlcoholLineContext.Read(l, contexts[i])).ToList();
                    var refusing = readings.Where(r => r.Kind != AlcoholSenseKind.None && AlcoholLineContext.Refuse(r, food.FdcId, food.Text) != null).ToList();
                    if (refusing.Count == 0) return Outcome(row, keep);
                    sense = refusing[0].Sense == AlcoholLineContext.ListSense ? refusing[0].ToString() : refusing[0].Sense;
                    wording = WordingOf(row.Lines[0]);
                    break;
                }
                case LinkRecheckRules.ContainerForm:
                    if (!row.Lines.Any(l => LinkRecheckRules.WrongForm(l, food))) return Outcome(row, keep);
                    drained = row.Lines.Any(LinkRecheckRules.IsDrainedLine);
                    wording = WordingOf(row.Lines[0]);
                    break;
                case LinkRecheckRules.Prepared:
                    if (!PreparedFoods.IsPrepared(food.CategoryId)) return Outcome(row, keep);
                    wording = WordingOf(row.Lines[0]);
                    break;
            }

            var lines = row.Lines.Count == 0 ? null : (IReadOnlyList<GateLine>)row.Lines.Select((l, i) => new GateLine(l, null, null, contexts[i])).ToList();
            var candidates = LinkRecheckRules.CandidateIds(row.Set, wording, sense, drained, food, lines, catalog, null, byFdc)
                .Where(c => foods.ContainsKey(c.Id)).Select(c => (c.Tier, foods[c.Id]));
            return Outcome(row, LinkRecheckRules.Judge(row.Set, wording, food, lines, catalog, candidates));
        }

        private static string WordingOf(string raw) => IngredientNameNormalizer.WordingKey(raw.Split(" + ", 2)[0]);

        private static RecheckRowOutcome Outcome(RecheckJudgedRow row, RecheckVerdict verdict)
        {
            var acts = verdict.Outcome is LinkRecheckRules.Move or LinkRecheckRules.Unlink ? verdict.Outcome : verdict.Candidate;
            var target = acts == LinkRecheckRules.Move ? verdict.Target?.Food.Id : null;
            var correct = acts != null && row.Judged == Wrong && (acts == LinkRecheckRules.Unlink || (row.RightFood != null && target == row.RightFood));
            return new RecheckRowOutcome(row, verdict, acts == null ? null : verdict.Class, acts, target, verdict.Outcome is LinkRecheckRules.Move or LinkRecheckRules.Unlink, correct);
        }

        /// <summary>
        /// Each class's score over the decided rows, as if it were allowed to write, most judged rows
        /// first: a row counts for every class that refutes its food (<see cref="RecheckVerdict.Classes"/>),
        /// not only the one that decided, since each of them would make its move or take-back once
        /// proven; so the figures do not depend on which classes are proven when they are scored.
        /// </summary>
        public static IReadOnlyList<RecheckClassScore> Score(IReadOnlyList<RecheckRowOutcome> outcomes)
        {
            var wrongRows = outcomes.Count(o => o.Row.Judged == Wrong);
            return outcomes.Where(o => o.Class != null && o.Row.Judged != Doubtful)
                .SelectMany(o => (o.Verdict.Classes ?? new[] { o.Class! }).Select(c => (Class: c, Outcome: o)))
                .GroupBy(x => x.Class, x => x.Outcome, StringComparer.Ordinal)
                .Select(g =>
                {
                    bool Rule(RecheckRowOutcome o) => o.Row.Status == "ok";
                    return new RecheckClassScore(g.Key, g.Count(), g.Count(o => o.Correct),
                        g.Count(o => !Rule(o)), g.Count(o => !Rule(o) && o.Correct), g.Count(Rule), g.Count(o => Rule(o) && o.Correct),
                        g.Count(o => o.Correct), wrongRows, LinkRecheckRules.Proven(g.Key));
                })
                .OrderByDescending(s => s.Judged).ThenBy(s => s.Class, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>The scorer's table: one line a class with its judged rows, precision, coverage and whether it may write, then the rows written against their judgement.</summary>
        public static IReadOnlyList<string> Report(IReadOnlyList<RecheckRowOutcome> outcomes)
        {
            var lines = new List<string>
            {
                $"link re-check classes ({LinkRecheckRules.Version}, signature {LinkRecheckRules.Signature}); bar: precision >= {LinkRecheckRules.ClassPrecisionBar:P0} over >= {LinkRecheckRules.ClassMinimumJudged} checkpoint-judged (agent) rows; rule rows must be right but do not count",
                $"rows {outcomes.Count}: judged wrong {outcomes.Count(o => o.Row.Judged == Wrong)}, right {outcomes.Count(o => o.Row.Judged == Right)}, doubtful {outcomes.Count(o => o.Row.Judged == Doubtful)}",
                "class\tjudged\tright\tprecision\tagent\trule\tcoverage\tbar\twrites",
            };
            foreach (var s in Score(outcomes))
            {
                lines.Add(string.Create(CultureInfo.InvariantCulture,
                    $"{s.Class}\t{s.Judged}\t{s.Right}\t{s.Precision:P1}\t{s.AgentRight}/{s.AgentJudged}\t{s.RuleRight}/{s.RuleJudged}\t{s.Coverage:P1}\t{(s.MeetsBar ? "meets" : "below")}\t{(s.Proven ? "by rule" : "candidates only")}"));
            }
            var written = outcomes.Where(o => o.Writes && o.Row.Judged == Wrong).ToList();
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"written by rule: {outcomes.Count(o => o.Writes)} rows, {written.Count(o => o.Correct)} of the {outcomes.Count(o => o.Row.Judged == Wrong)} rows judged wrong put right"));
            foreach (var o in outcomes.Where(o => o.Writes && o.Row.Judged != Doubtful && !o.Correct))
                lines.Add($"  written against its judgement: {o.Row.Id} {o.Row.Set} \"{o.Row.Wording}\" #{o.Row.FoodId}: {o.Acts} ({o.Verdict.Reason}){(o.TargetId is { } t ? " -> #" + t : string.Empty)}");
            return lines;
        }

        /// <summary>Prints the table for the golden folder given or found from the working folder; 1 when a proven class is below the bar.</summary>
        public static int Run(string[] args)
        {
            var at = Array.IndexOf(args, "--golden");
            var goldenDir = at >= 0 && at + 1 < args.Length ? args[at + 1] : Find();
            if (goldenDir == null || !System.IO.File.Exists(Path.Combine(goldenDir, File)))
            {
                Console.Error.WriteLine($"{File} not found; pass --golden nom-api/Nom.Api.Tests/Fixtures/Golden.");
                return 2;
            }
            var outcomes = Decide(goldenDir);
            foreach (var line in Report(outcomes)) Console.WriteLine(line);
            if (args.Contains(RowsFlag))
                foreach (var line in RowLines(outcomes)) Console.WriteLine(line);
            if (args.Contains(RecordFlag))
            {
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")) || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")))
                {
                    Console.Error.WriteLine("--record is refused under CI: the per-class record is written on a desktop and committed by hand.");
                    return 2;
                }
                var path = Path.Combine(goldenDir, "scores", LinkRecheckClassRecord.FileName);
                var record = BuildRecord(goldenDir, outcomes, DateTime.UtcNow);
                System.IO.File.WriteAllText(path, JsonSerializer.Serialize(record, LinkRecheckClassRecord.Json) + "\n");
                Console.WriteLine($"Recorded {path}: proven {(record.Proven.Count == 0 ? "none" : string.Join(", ", record.Proven))}; commit it with the rows it scores, then build again: the API reads the record it was built with.");
            }
            return Score(outcomes).Any(s => s.Proven && !s.MeetsBar) ? 1 : 0;
        }

        /// <summary>Every row's decision now: id, set, judgement, outcome, the class that refutes and what it would write, the new food, whether that is right by the judgement, the rule's reason, and every class the row is scored for.</summary>
        public static IReadOnlyList<string> RowLines(IReadOnlyList<RecheckRowOutcome> outcomes) =>
            new[] { "id\tset\tjudged\toutcome\tclass\tacts\ttarget\tcorrect\treason\tclasses" }
                .Concat(outcomes.Select(o => string.Join('\t', o.Row.Id, o.Row.Set, o.Row.Judged, o.Verdict.Outcome, o.Class ?? "-", o.Acts ?? "-",
                    o.TargetId?.ToString(CultureInfo.InvariantCulture) ?? "-", o.Acts == null ? "-" : o.Correct ? "yes" : "no", o.Verdict.Reason ?? "-",
                    o.Acts == null ? "-" : string.Join(',', o.Verdict.Classes ?? new[] { o.Class ?? "-" }))))
                .ToList();

        /// <summary>A hash of the judged rows as the scorer reads them, so a record names the rows it was scored on.</summary>
        public static string CaseSet(string goldenDir) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", Rows(goldenDir).Select(r =>
                string.Join('\t', r.Id, r.Set, r.Wording, r.FoodId.ToString(CultureInfo.InvariantCulture), string.Join(Separator, r.Lines), r.Title ?? "-", r.Judged,
                    r.RightFood?.ToString(CultureInfo.InvariantCulture) ?? "-", r.Status))))))[..16].ToLowerInvariant();

        /// <summary>The per-class record of the decided rows: each class's figures and the classes at the bar, under the rules' version and decision signature.</summary>
        public static LinkRecheckClassRecord.Record BuildRecord(string goldenDir, IReadOnlyList<RecheckRowOutcome> outcomes, DateTime scoredAtUtc)
        {
            var scores = Score(outcomes);
            return new LinkRecheckClassRecord.Record
            {
                Version = LinkRecheckRules.Version,
                DecisionSignature = LinkRecheckRules.DecisionSignature,
                CaseSet = CaseSet(goldenDir),
                Rows = outcomes.Count,
                Bar = LinkRecheckRules.ClassPrecisionBar,
                Minimum = LinkRecheckRules.ClassMinimumJudged,
                ScoredAtUtc = scoredAtUtc,
                Classes = scores.OrderBy(c => c.Class, StringComparer.Ordinal).Select(c => new LinkRecheckClassRecord.ClassFigures
                {
                    Class = c.Class, Judged = c.Judged, Right = c.Right, AgentJudged = c.AgentJudged, AgentRight = c.AgentRight,
                    RuleJudged = c.RuleJudged, RuleRight = c.RuleRight, MeetsBar = c.MeetsBar,
                }).ToList(),
                Proven = scores.Where(c => c.MeetsBar).Select(c => c.Class).OrderBy(c => c, StringComparer.Ordinal).ToList(),
            };
        }

        private static string? Find()
        {
            for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir != null; dir = dir.Parent)
            {
                foreach (var candidate in new[] { Path.Combine(dir.FullName, "nom-api", "Nom.Api.Tests", "Fixtures", "Golden"), Path.Combine(dir.FullName, "Nom.Api.Tests", "Fixtures", "Golden") })
                    if (Directory.Exists(candidate)) return candidate;
            }
            return null;
        }
    }
}
