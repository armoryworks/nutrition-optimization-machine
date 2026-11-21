using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Nom.Orch.Services.Support;

namespace Nom.Import.Golden
{
    /// <summary>
    /// Scores one model lane against its golden set on a model host the operator names:
    /// <c>dotnet run -- --golden-score --lane link-verify --host http://ai-host:11434 --model qwen3:8b [--limit 200] [--golden &lt;dir&gt;] [--out failures.tsv] [--record]</c>; the prose lanes also take <c>[--check-model mistral-nemo]</c>, the judge that checks the writer, and the reader lanes (wording-read, wording-lexicon, alcohol-reading) and link-audit take it as reader B (default the roster's), which the record names; alcohol-reading also takes <c>[--audit-model phi4]</c>, the auditor it scores in the fixed-reading mode (default the roster's).
    /// Prints the lane's score, its false-accept rate, the floor from <c>model-lanes.json</c> and every
    /// case that passed in the recorded run and fails now, and exits 1 when the lane is below its
    /// floor. <c>--record</c> writes the score file the release gate reads. Refused under CI, and it
    /// opens no database.
    /// </summary>
    public static class GoldenScoreCommand
    {
        public const string Flag = "--golden-score";

        public static async Task<int> RunAsync(string[] args)
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")) || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")))
            {
                Console.Error.WriteLine("--golden-score asks a model host and is never run from CI.");
                return 2;
            }

            var lane = Option(args, "--lane");
            var noModel = lane != null && ModelLanes.NoModel.Contains(lane);
            var host = Option(args, "--host") ?? (noModel ? "none" : null);
            var model = Option(args, "--model") ?? (noModel ? ModelLanes.NoModelVersion(lane!) : null);
            if (lane == null || host == null || model == null || !ModelLanes.Scored.Contains(lane))
            {
                Console.Error.WriteLine($"usage: {Flag} --lane <{string.Join('|', ModelLanes.Scored)}> --host <url> --model <name> [--limit N] [--golden <dir>] [--out <file>] [--record] [--check-model <name>] [--audit-model <name>] ({string.Join(", ", ModelLanes.NoModel)} needs no host or model)");
                return 2;
            }

            var goldenDir = Option(args, "--golden") ?? FindGoldenDir();
            if (goldenDir == null || !File.Exists(Path.Combine(goldenDir, ModelLanes.FloorsFile)))
            {
                Console.Error.WriteLine("Golden folder not found; pass --golden nom-api/Nom.Api.Tests/Fixtures/Golden.");
                return 2;
            }

            int? limit = int.TryParse(Option(args, "--limit"), out var l) && l > 0 ? l : null;
            var floor = ModelLanes.Floors(goldenDir).TryGetValue(lane, out var f) ? f : new LaneFloor();
            var signature = await ModelLanes.SignatureAsync(lane);
            var recorded = ModelLanes.Recorded(goldenDir, lane);

            Console.WriteLine($"Lane {lane}, model {model} on {host}, prompt signature {signature}{(limit is { } n ? $", first {n} cases" : string.Empty)}");
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            IReadOnlyList<LaneCaseResult> results;
            try
            {
                results = await ModelLanes.RunAsync(lane, goldenDir, http, ModelLanes.Config(host, model, Option(args, "--check-model"), Option(args, "--audit-model")), limit, Console.Error.WriteLine);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                Console.Error.WriteLine($"The run stopped: {ex.Message}. No score was recorded.");
                return 3;
            }
            var score = ModelLanes.Score(lane, model, Uri.TryCreate(host, UriKind.Absolute, out var uri) ? uri.Host : host, signature, results);
            score.CheckModel = ModelLanes.CheckModelOf(lane, Option(args, "--check-model"));
            if (score.AuditorKnownBad != null) score.AuditModel = ModelLanes.AuditModelOf(lane, model, Option(args, "--audit-model"));
            var breaches = ModelLanes.Breaches(score, floor);
            var regressed = recorded?.CaseSet == score.CaseSet ? ModelLanes.Regressed(recorded, results) : Array.Empty<LaneCaseResult>();

            Console.WriteLine($"Score {score.Score:P1} ({score.Passed}/{score.Cases}); floor {floor.MinScore:P1}");
            Console.WriteLine($"False accepts {score.FalseAccepts}/{score.FalseAcceptCases} ({score.FalseAcceptRate:P1}); ceiling {(floor.MaxFalseAcceptRate is { } max ? max.ToString("P1") : "none")}");
            if (score.Precision is { } precision)
                Console.WriteLine($"Accepted {score.Accepted}/{score.Cases}; judged right among them {precision:P1}; floor {(floor.MinPrecision is { } minPrecision ? minPrecision.ToString("P1") : "none")}");
            foreach (var (judge, counts) in score.ByJudge) Console.WriteLine($"  judged by {judge}: {counts[0]}/{counts[1]}");
            if (recorded == null) Console.WriteLine("No recorded run to compare with.");
            else if (recorded.CaseSet != score.CaseSet) Console.WriteLine($"The case set differs from the recorded run ({recorded.Model}, {recorded.ScoredAtUtc:u}); regressions are not compared.");
            else
            {
                Console.WriteLine($"Recorded run: {recorded.Model}, signature {recorded.Signature}, score {recorded.Score:P1}, {recorded.ScoredAtUtc:u}");
                Console.WriteLine($"Regressed since the recorded run: {regressed.Count}");
                foreach (var r in regressed) Console.WriteLine($"  {r.Id}: expected {r.Expected}, got {r.Actual}");
            }

            if (Option(args, "--out") is { } outPath)
            {
                File.WriteAllLines(outPath, new[] { "id\tjudgedBy\texpected\tactual\tfalseAccept" }
                    .Concat(results.Where(r => !r.Pass).Select(r => $"{r.Id}\t{r.JudgedBy}\t{r.Expected}\t{r.Actual}\t{r.FalseAccept}")));
                Console.WriteLine($"Failures written to {outPath}");
            }

            if (args.Contains("--record"))
            {
                ModelLanes.Record(goldenDir, score);
                Console.WriteLine($"Recorded {ModelLanes.RecordPath(goldenDir, lane)}; commit it with the change it scores.");
            }

            foreach (var breach in breaches) Console.WriteLine("BELOW FLOOR: " + breach);
            return breaches.Count == 0 ? 0 : 1;
        }

        private static string? Option(string[] args, string name)
        {
            var at = Array.IndexOf(args, name);
            return at >= 0 && at + 1 < args.Length && !args[at + 1].StartsWith("--", StringComparison.Ordinal) ? args[at + 1] : null;
        }

        private static string? FindGoldenDir()
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
