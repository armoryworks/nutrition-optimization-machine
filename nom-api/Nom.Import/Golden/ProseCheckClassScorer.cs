using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nom.Orch.Services.Support;

namespace Nom.Import.Golden
{
    /// <summary>One judged row of prose-check.tsv: its texts, status, source and the judgement of each fact class on it (<c>right</c>, <c>false</c> or <c>doubtful</c>).</summary>
    public sealed record ProseJudgedRow(GoldenRow Row, string Status, string Source, IReadOnlyDictionary<string, string> Classes);

    /// <summary>What the check finds on one judged row now: the fact classes it fires, each with its judgement on the row (null when the row does not judge it).</summary>
    public sealed record ProseRowOutcome(ProseJudgedRow Row, ProseCheck.Finding Finding, IReadOnlyList<(string Class, string? Judged)> Fired);

    /// <summary>
    /// One fact class over the judged rows: the rows it fires on, how many of them it was right on,
    /// how many were judged doubtful, the agent-judged and rule-given split, and whether it holds the
    /// bar (<see cref="ProseCheck.ClassMeetsBar"/>): a doubtful firing counts as judged and not right,
    /// so it weighs against the class; only the checkpoint-judged rows (status agent) count toward the
    /// minimum and their precision must hold it, every rule-given row must be right, and rule-given
    /// rows never lift a class over the minimum.
    /// </summary>
    public sealed record ProseClassScore(string Class, int Judged, int Right, int AgentJudged, int AgentRight, int RuleJudged, int RuleRight, int Doubtful, bool Proven)
    {
        public double Precision => Judged == 0 ? 0 : (double)Right / Judged;
        public double AgentPrecision => AgentJudged == 0 ? 0 : (double)AgentRight / AgentJudged;
        public bool MeetsBar => ProseCheck.ClassMeetsBar(Judged, Right, AgentJudged, AgentRight, RuleJudged, RuleRight);
    }

    /// <summary>
    /// The per-class scorer of the prose check's fact rules (staging checkpoint 4, P7): every judged
    /// row of prose-check.tsv is checked as the re-check checks it, and each fact class
    /// (<see cref="ProseCheck.FactClasses"/>) is scored on the rows it fires on by the row's
    /// judgement of that class (column <c>classes</c>: <c>class=right</c>, <c>class=false</c> or
    /// <c>class=doubtful</c>, a doubtful row counting as not right). A class may fail a recipe only when it holds
    /// <see cref="ProseCheck.ClassPrecisionBar"/> over at least <see cref="ProseCheck.ClassMinimumJudged"/>
    /// checkpoint-judged rows; <c>--record</c> writes the classes that hold it to
    /// scores/prose-check-classes.json, which is what <see cref="ProseCheck.ProvenClasses"/> reads, and
    /// <c>--rows</c> prints every row's classes. Run with <c>dotnet run --project nom-api/Nom.Import --
    /// --prose-classes [--rows] [--record] [--golden &lt;dir&gt;]</c>; it opens no database and asks no model.
    /// </summary>
    public static class ProseCheckClassScorer
    {
        public const string Flag = "--prose-classes";
        public const string File = "prose-check.tsv";
        public const string Right = "right";
        public const string False = "false";
        public const string Doubtful = "doubtful";
        public const string RecordFlag = "--record";
        public const string RowsFlag = "--rows";

        /// <summary>The judged rows of prose-check.tsv (status ok, agent or known).</summary>
        public static IReadOnlyList<ProseJudgedRow> Rows(string goldenDir) =>
            GoldenTsv.Read(Path.Combine(goldenDir, File)).Rows
                .Where(r => r["status"] is "ok" or "agent" or "known")
                .Select(r => new ProseJudgedRow(r, r["status"], r["source"], ParseClasses(r.Value("classes"))))
                .ToList();

        /// <summary>The judgements of a <c>classes</c> cell ("lost-quantity=right wrong-amount=false").</summary>
        public static IReadOnlyDictionary<string, string> ParseClasses(string? cell)
        {
            var judged = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(cell) || cell == GoldenTsv.NotJudged) return judged;
            foreach (var part in cell.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var at = part.IndexOf('=');
                if (at <= 0 || part[(at + 1)..] is not (Right or False or Doubtful)) throw new InvalidDataException($"classes cell \"{cell}\": \"{part}\" is not class=right, class=false or class=doubtful");
                judged[part[..at]] = part[(at + 1)..];
            }

            return judged;
        }

        /// <summary>The check on one row of prose-check.tsv, as the golden tests run it.</summary>
        public static ProseCheck.Finding Check(GoldenRow row) =>
            ProseCheck.Check(
                new RewriteFidelity.SourceText(row.Value("description") ?? string.Empty, row["steps"].Split(" ¦ "), row.Value("title") ?? string.Empty),
                new RewriteFidelity.SourceText(row.Value("rewriteDescription") ?? string.Empty, row["rewriteSteps"].Split(" ¦ "), row.Value("title") ?? string.Empty),
                row.Value("lines")?.Split(" ¦ "));

        /// <summary>Every judged row checked now.</summary>
        public static IReadOnlyList<ProseRowOutcome> Decide(string goldenDir) =>
            Rows(goldenDir).Select(row =>
            {
                var finding = Check(row.Row);
                var fired = finding.Reasons.Where(ProseCheck.FactClasses.Contains)
                    .Select(c => (c, row.Classes.TryGetValue(c, out var j) ? j : null)).ToList();
                return new ProseRowOutcome(row, finding, fired);
            }).ToList();

        /// <summary>Each fact class's score over the checked rows, most judged rows first; a class that fires on no judged row is listed with no rows.</summary>
        public static IReadOnlyList<ProseClassScore> Score(IReadOnlyList<ProseRowOutcome> outcomes)
        {
            var fired = outcomes.SelectMany(o => o.Fired.Select(f => (f.Class, f.Judged, Rule: o.Row.Status == "ok"))).ToList();
            return ProseCheck.FactClasses.Select(c =>
                {
                    var rows = fired.Where(f => f.Class == c && f.Judged is Right or False or Doubtful).ToList();
                    return new ProseClassScore(c, rows.Count, rows.Count(r => r.Judged == Right),
                        rows.Count(r => !r.Rule), rows.Count(r => !r.Rule && r.Judged == Right),
                        rows.Count(r => r.Rule), rows.Count(r => r.Rule && r.Judged == Right),
                        fired.Count(f => f.Class == c && f.Judged == Doubtful), ProseCheck.ProvenClasses.Contains(c));
                })
                .OrderByDescending(s => s.Judged).ThenBy(s => s.Class, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>The rows on which the check fires a fact class the row does not judge: each must be judged before the classes can be scored.</summary>
        public static IReadOnlyList<string> Unjudged(IReadOnlyList<ProseRowOutcome> outcomes) =>
            outcomes.SelectMany(o => o.Fired.Where(f => f.Judged == null).Select(f => $"{o.Row.Row.Id} {f.Class}")).ToList();

        /// <summary>The scorer's table: one line a class with its judged rows, precision and whether it may fail a recipe, then the rows a class fires on wrongly and the unjudged firings.</summary>
        public static IReadOnlyList<string> Report(IReadOnlyList<ProseRowOutcome> outcomes)
        {
            var lines = new List<string>
            {
                $"prose check fact classes (check {ProseCheck.Version}); bar: precision >= {ProseCheck.ClassPrecisionBar:P0} over >= {ProseCheck.ClassMinimumJudged} checkpoint-judged (agent) rows; a doubtful row counts against its class; every rule row must be right and none counts toward the minimum",
                $"rows {outcomes.Count}",
                "class\tjudged\tright\tprecision\tagent\trule\tdoubtful\tbar\tfails",
            };
            foreach (var s in Score(outcomes))
            {
                lines.Add(string.Create(CultureInfo.InvariantCulture,
                    $"{s.Class}\t{s.Judged}\t{s.Right}\t{s.Precision:P1}\t{s.AgentRight}/{s.AgentJudged}\t{s.RuleRight}/{s.RuleJudged}\t{s.Doubtful}\t{(s.MeetsBar ? "meets" : "below")}\t{(s.Proven ? "fails the recipe" : "unsure only")}"));
            }

            foreach (var o in outcomes)
                foreach (var f in o.Fired.Where(f => f.Judged == False))
                    lines.Add($"  fires wrongly: {o.Row.Row.Id} {f.Class}: {string.Join("; ", o.Finding.Faults.Where(x => x.Reason == f.Class))}");
            foreach (var u in Unjudged(outcomes)) lines.Add($"  not judged: {u}");
            return lines;
        }

        /// <summary>Every row's outcome now: id, expect, outcome, each fact class fired with its judgement, the count of faults of each reason, the copy and length measures (longest run, close stretch, copied share, kept sentences, length ratio), and the faults.</summary>
        public static IReadOnlyList<string> RowLines(IReadOnlyList<ProseRowOutcome> outcomes) =>
            new[] { "id\texpect\toutcome\tclasses\treasons\tmeasures\tfaults" }
                .Concat(outcomes.Select(o => string.Join('\t', o.Row.Row.Id, o.Row.Row["expect"], o.Finding.Outcome,
                    o.Fired.Count == 0 ? "-" : string.Join(' ', o.Fired.Select(f => $"{f.Class}={f.Judged ?? "?"}")),
                    o.Finding.Faults.Count == 0 ? "-" : string.Join(' ', o.Finding.Faults.GroupBy(f => f.Reason).Select(g => $"{g.Key}:{g.Count()}")),
                    string.Create(CultureInfo.InvariantCulture, $"{o.Finding.LongestRun} {o.Finding.CloseStretch} {o.Finding.CopiedShare} {o.Finding.CopiedSentences} {o.Finding.LengthRatio}"),
                    o.Finding.Faults.Count == 0 ? "-" : string.Join(" | ", o.Finding.Faults))))
                .ToList();

        /// <summary>A hash of the judged rows as the scorer reads them, so a record names the rows it was scored on.</summary>
        public static string CaseSet(string goldenDir) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", Rows(goldenDir).Select(r =>
                string.Join('\t', r.Row.Id, r.Status, r.Row["title"], r.Row["description"], r.Row["steps"], r.Row["rewriteDescription"], r.Row["rewriteSteps"], r.Row["lines"],
                    string.Join(' ', r.Classes.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => c.Key + "=" + c.Value))))))))[..16].ToLowerInvariant();

        /// <summary>The per-class record of the checked rows: each class's figures and the classes at the bar, under the check version.</summary>
        public static ProseCheckClassRecord.Record BuildRecord(string goldenDir, IReadOnlyList<ProseRowOutcome> outcomes, DateTime scoredAtUtc)
        {
            var scores = Score(outcomes);
            return new ProseCheckClassRecord.Record
            {
                CheckVersion = ProseCheck.Version,
                CaseSet = CaseSet(goldenDir),
                Rows = outcomes.Count,
                Bar = ProseCheck.ClassPrecisionBar,
                Minimum = ProseCheck.ClassMinimumJudged,
                ScoredAtUtc = scoredAtUtc,
                Classes = scores.OrderBy(c => c.Class, StringComparer.Ordinal).Select(c => new ProseCheckClassRecord.ClassFigures
                {
                    Class = c.Class, Judged = c.Judged, Right = c.Right, AgentJudged = c.AgentJudged, AgentRight = c.AgentRight,
                    RuleJudged = c.RuleJudged, RuleRight = c.RuleRight, Doubtful = c.Doubtful, MeetsBar = c.MeetsBar,
                }).ToList(),
                Proven = scores.Where(c => c.MeetsBar).Select(c => c.Class).OrderBy(c => c, StringComparer.Ordinal).ToList(),
            };
        }

        /// <summary>Prints the table for the golden folder given or found from the working folder; 1 when a firing is unjudged or a proven class is below the bar.</summary>
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

                if (Unjudged(outcomes).Count > 0)
                {
                    Console.Error.WriteLine("--record is refused while the check fires a fact class a row does not judge; judge it in the row's classes cell first.");
                    return 1;
                }

                var path = Path.Combine(goldenDir, "scores", ProseCheckClassRecord.FileName);
                var record = BuildRecord(goldenDir, outcomes, DateTime.UtcNow);
                System.IO.File.WriteAllText(path, JsonSerializer.Serialize(record, ProseCheckClassRecord.Json) + "\n");
                Console.WriteLine($"Recorded {path}: proven {(record.Proven.Count == 0 ? "none" : string.Join(", ", record.Proven))}; commit it with the rows it scores, then build again: the API reads the record it was built with.");
            }

            return Unjudged(outcomes).Count > 0 || Score(outcomes).Any(s => s.Proven && !s.MeetsBar) ? 1 : 0;
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
