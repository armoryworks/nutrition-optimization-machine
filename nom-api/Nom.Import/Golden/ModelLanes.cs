using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;

namespace Nom.Import.Golden
{
    /// <summary>What one golden case got from the model, and whether that is the judged answer.</summary>
    public sealed record LaneCaseResult(string Id, bool Pass, bool FalseAccept, bool CanFalseAccept, string Expected, string Actual, string JudgedBy);

    /// <summary>
    /// One operator run of a model lane over its golden set: the lane's prompt signature, the model
    /// and host asked, the share of cases answered as judged, the share of known-wrong answers the
    /// lane accepted, and the ids of the cases it got wrong. Committed under <c>scores/</c> it is the
    /// record the release gate checks.
    /// </summary>
    public sealed class LaneScore
    {
        public string Lane { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public string Host { get; set; } = string.Empty;
        public string Signature { get; set; } = string.Empty;
        public DateTime ScoredAtUtc { get; set; }
        public string CaseSet { get; set; } = string.Empty;
        public int Cases { get; set; }
        public int Passed { get; set; }
        public double Score { get; set; }
        public int FalseAccepts { get; set; }
        public int FalseAcceptCases { get; set; }
        public double FalseAcceptRate { get; set; }
        public Dictionary<string, int[]> ByJudge { get; set; } = new();
        public List<string> Failed { get; set; } = new();
    }

    /// <summary>
    /// The floor a model lane must reach before a release ships. <see cref="Required"/> makes a
    /// missing score record hold the release; a record whose signature no longer matches the code
    /// always does.
    /// </summary>
    public sealed class LaneFloor
    {
        public bool Scorer { get; set; }
        public bool Required { get; set; }
        public double MinScore { get; set; }
        public double? MaxFalseAcceptRate { get; set; }
        public string Note { get; set; } = string.Empty;
    }

    /// <summary>
    /// The model lanes the golden sets can score, run by an operator against a named host and
    /// model (never from CI). A run only asks the model and compares; it opens no database and
    /// writes nothing but its own score file. The signature of a lane is a hash of the request it
    /// sends for one fixed question, without the model name, so any change to its prompt, schema
    /// or options shows as a new signature.
    /// </summary>
    public static class ModelLanes
    {
        public const string LinkPick = "link-pick";
        public const string LinkVerify = "link-verify";
        public const string LineClassify = "line-classify";
        public const string FloorsFile = "model-lanes.json";
        public const string ScoresFolder = "scores";
        public const int PickBatch = 6;
        public const int VerifyBatch = 12;
        public const int ClassifyBatch = 20;

        public static readonly IReadOnlyList<string> Scored = new[] { LinkPick, LinkVerify, LineClassify };

        private static readonly HashSet<string> Bad = new(StringComparer.Ordinal) { "wrong", "near", "compound" };

        private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        public static IConfiguration Config(string host, string model) => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ai:BatchOllamaUrl"] = host,
                ["Ai:LinkModel"] = model,
                ["Ai:RepairModel"] = model,
                ["IngredientLinking:VerifyModel"] = model,
            })
            .Build();

        public static async Task<string> SignatureAsync(string lane)
        {
            var capture = new CapturingHandler();
            using var http = new HttpClient(capture);
            var config = Config("http://signature.invalid", "signature");
            var candidates = new[] { new FoodCandidate(1, "1", "Butter, salted", "sr_legacy_food"), new FoodCandidate(2, "2", "Butter, without salt", "sr_legacy_food") };
            switch (lane)
            {
                case LinkPick:
                    await new OllamaIngredientLinkMatcher(http, config).ChooseAsync(new[] { new LinkQuestion(1, "sweet butter", candidates) });
                    break;
                case LinkVerify:
                    await new OllamaIngredientLinkVerifier(http, config).JudgeAsync(new[] { new LinkPair(1, "sweet butter", "Butter, without salt") });
                    break;
                case LineClassify:
                    await new OllamaRecipeClassificationModel(http, config).ClassifyLinesAsync("Shortbread", new[] { "butter" });
                    break;
                default:
                    throw new ArgumentException($"No scorer for lane '{lane}'.", nameof(lane));
            }

            var body = JsonNode.Parse(capture.Body ?? "{}")!.AsObject();
            body.Remove("model");
            var bytes = Encoding.UTF8.GetBytes(capture.Path + "\n" + body.ToJsonString());
            return Convert.ToHexString(SHA256.HashData(bytes))[..16].ToLowerInvariant();
        }

        public static Dictionary<string, LaneFloor> Floors(string goldenDir) =>
            JsonSerializer.Deserialize<Dictionary<string, LaneFloor>>(File.ReadAllText(Path.Combine(goldenDir, FloorsFile)), Json)
            ?? new Dictionary<string, LaneFloor>();

        public static string RecordPath(string goldenDir, string lane) => Path.Combine(goldenDir, ScoresFolder, lane + ".json");

        public static LaneScore? Recorded(string goldenDir, string lane) =>
            File.Exists(RecordPath(goldenDir, lane))
                ? JsonSerializer.Deserialize<LaneScore>(File.ReadAllText(RecordPath(goldenDir, lane)), Json)
                : null;

        public static void Record(string goldenDir, LaneScore score)
        {
            Directory.CreateDirectory(Path.Combine(goldenDir, ScoresFolder));
            File.WriteAllText(RecordPath(goldenDir, score.Lane), JsonSerializer.Serialize(score, Json) + "\n");
        }

        /// <summary>Why the score does not reach the lane's floor; empty when it does.</summary>
        public static IReadOnlyList<string> Breaches(LaneScore score, LaneFloor floor)
        {
            var breaches = new List<string>();
            if (score.Score < floor.MinScore)
                breaches.Add($"{score.Lane}: score {score.Score:P1} ({score.Passed}/{score.Cases}) is below the floor {floor.MinScore:P1}");
            if (floor.MaxFalseAcceptRate is { } max && score.FalseAcceptRate > max)
                breaches.Add($"{score.Lane}: accepted {score.FalseAccepts} of {score.FalseAcceptCases} judged-wrong answers ({score.FalseAcceptRate:P1}), above the ceiling {max:P1}");
            return breaches;
        }

        public static LaneScore Score(string lane, string model, string host, string signature, IReadOnlyList<LaneCaseResult> results)
        {
            var possible = results.Count(r => r.CanFalseAccept);
            var accepted = results.Count(r => r.FalseAccept);
            return new LaneScore
            {
                Lane = lane,
                Model = model,
                Host = host,
                Signature = signature,
                ScoredAtUtc = DateTime.UtcNow,
                CaseSet = CaseSet(results.Select(r => r.Id)),
                Cases = results.Count,
                Passed = results.Count(r => r.Pass),
                Score = results.Count == 0 ? 0 : Math.Round((double)results.Count(r => r.Pass) / results.Count, 4),
                FalseAccepts = accepted,
                FalseAcceptCases = possible,
                FalseAcceptRate = possible == 0 ? 0 : Math.Round((double)accepted / possible, 4),
                ByJudge = results.GroupBy(r => r.JudgedBy).OrderBy(g => g.Key, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => new[] { g.Count(r => r.Pass), g.Count() }),
                Failed = results.Where(r => !r.Pass).Select(r => r.Id).OrderBy(id => id, StringComparer.Ordinal).ToList(),
            };
        }

        public static string CaseSet(IEnumerable<string> ids) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', ids.OrderBy(id => id, StringComparer.Ordinal)))))[..16].ToLowerInvariant();

        /// <summary>The cases that fail now and passed in the recorded run of the same case set.</summary>
        public static IReadOnlyList<LaneCaseResult> Regressed(LaneScore? recorded, IReadOnlyList<LaneCaseResult> results)
        {
            if (recorded == null) return Array.Empty<LaneCaseResult>();
            var before = recorded.Failed.ToHashSet(StringComparer.Ordinal);
            return results.Where(r => !r.Pass && !before.Contains(r.Id)).ToList();
        }

        public static async Task<IReadOnlyList<LaneCaseResult>> RunAsync(string lane, string goldenDir, HttpClient http, IConfiguration config, int? limit, Action<string> progress, CancellationToken cancellationToken = default)
        {
            switch (lane)
            {
                case LinkPick:
                {
                    var cases = PickCases(goldenDir).Take(limit ?? int.MaxValue).ToList();
                    var matcher = new OllamaIngredientLinkMatcher(http, config);
                    var results = new List<LaneCaseResult>();
                    foreach (var batch in cases.Chunk(PickBatch))
                    {
                        var questions = batch.Select((c, i) => new LinkQuestion(i, c.Name, c.Shortlist)).ToList();
                        var answers = (await matcher.ChooseAsync(questions, cancellationToken)).ToDictionary(a => a.IngredientId);
                        results.AddRange(batch.Select((c, i) => c.Outcome(answers.TryGetValue(i, out var a) ? a : null)));
                        progress($"{lane}: {results.Count}/{cases.Count}");
                    }
                    return results;
                }
                case LinkVerify:
                {
                    var cases = VerifyCases(goldenDir).Take(limit ?? int.MaxValue).ToList();
                    var verifier = new OllamaIngredientLinkVerifier(http, config);
                    var results = new List<LaneCaseResult>();
                    foreach (var batch in cases.Chunk(VerifyBatch))
                    {
                        var pairs = batch.Select((c, i) => new LinkPair(i, c.Wording, c.Description)).ToList();
                        var verdicts = (await verifier.JudgeAsync(pairs, cancellationToken)).ToDictionary(v => v.IngredientId, v => v.Judgement);
                        results.AddRange(batch.Select((c, i) => c.Outcome(verdicts.TryGetValue(i, out var v) ? v : null)));
                        progress($"{lane}: {results.Count}/{cases.Count}");
                    }
                    return results;
                }
                case LineClassify:
                {
                    var cases = ClassifyCases(goldenDir).Take(limit ?? int.MaxValue).ToList();
                    var model = new OllamaRecipeClassificationModel(http, config);
                    var results = new List<LaneCaseResult>();
                    foreach (var recipe in cases.GroupBy(c => c.Recipe))
                    {
                        foreach (var batch in recipe.Chunk(ClassifyBatch))
                        {
                            var kinds = await model.ClassifyLinesAsync(recipe.Key, batch.Select(c => c.Raw).ToList(), cancellationToken);
                            results.AddRange(batch.Select((c, i) => c.Outcome(kinds.TryGetValue(i, out var k) ? k : null)));
                            progress($"{lane}: {results.Count}/{cases.Count}");
                        }
                    }
                    return results;
                }
                default:
                    throw new ArgumentException($"No scorer for lane '{lane}'.", nameof(lane));
            }
        }

        public sealed record PickCase(string Id, string Name, IReadOnlyList<FoodCandidate> Shortlist, IReadOnlySet<string> Right, IReadOnlySet<string> Wrong, string JudgedBy)
        {
            public LaneCaseResult Outcome(LinkAnswer? answer)
            {
                var expected = Right.Count > 0 ? "one of " + string.Join(' ', Right.OrderBy(f => f)) : "none of " + string.Join(' ', Wrong.OrderBy(f => f));
                if (answer == null) return new LaneCaseResult(Id, false, false, Wrong.Count > 0, expected, "no readable answer", JudgedBy);
                var pick = answer.Choice?.FdcId;
                var falseAccept = pick != null && Wrong.Contains(pick);
                var pass = Right.Count > 0 ? pick != null && Right.Contains(pick) : !falseAccept;
                return new LaneCaseResult(Id, pass, falseAccept, Wrong.Count > 0, expected, pick == null ? "none" : $"{pick} {answer.Choice!.Name}", JudgedBy);
            }
        }

        public sealed record VerifyCase(string Id, string Wording, string Description, bool Right, string JudgedBy)
        {
            public LaneCaseResult Outcome(LinkJudgement? judgement)
            {
                var same = judgement == LinkJudgement.Same;
                return new LaneCaseResult(Id, Right == same && judgement != null, !Right && same, !Right,
                    Right ? "same" : "not same", judgement?.ToString() ?? "no readable answer", JudgedBy);
            }
        }

        public sealed record ClassifyCase(string Id, string Recipe, string Raw, string Kind, string JudgedBy)
        {
            public LaneCaseResult Outcome(string? kind)
            {
                var ingredient = Kind == RecipeIngredientLineKind.Ingredient;
                var falseAccept = ingredient && kind != null && kind != RecipeIngredientLineKind.Ingredient;
                return new LaneCaseResult(Id, kind == Kind, falseAccept, ingredient, Kind, kind ?? "no readable answer", JudgedBy);
            }
        }

        /// <summary>
        /// The ambiguous wordings of the judged link pairs, as the link lane would ask them: wordings
        /// a staple or the name matcher already answers never reach the model and are left out, and
        /// so is a wording none of whose judged foods is on its shortlist.
        /// </summary>
        public static IEnumerable<PickCase> PickCases(string goldenDir)
        {
            var (matcher, staples) = UsdaMatcher(goldenDir);
            var pairs = GoldenTsv.Read(Path.Combine(goldenDir, "link-pairs.tsv")).Rows;
            foreach (var group in pairs.GroupBy(r => IngredientNameNormalizer.Normalize(r["wording"]).ToLowerInvariant()).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var name = group.Key;
                if (name.Length == 0 || IngredientLinkService.DeterministicTarget(name, matcher, staples) != null) continue;
                var shortlist = matcher.Shortlist(name, 10);
                var listed = shortlist.Select(c => c.FdcId).ToHashSet(StringComparer.Ordinal);
                var right = group.Where(r => r["verdict"] == "ok" && listed.Contains(r["fdcId"])).Select(r => r["fdcId"]).ToHashSet(StringComparer.Ordinal);
                var wrong = group.Where(r => Bad.Contains(r["verdict"]) && listed.Contains(r["fdcId"])).Select(r => r["fdcId"]).ToHashSet(StringComparer.Ordinal);
                wrong.ExceptWith(right);
                if (right.Count == 0 && wrong.Count == 0) continue;
                yield return new PickCase("pick:" + name, name, shortlist, right, wrong, string.Join('+', group.Select(r => r["judgedBy"]).Distinct().OrderBy(j => j)));
            }
        }

        public static IEnumerable<VerifyCase> VerifyCases(string goldenDir) =>
            GoldenTsv.Read(Path.Combine(goldenDir, "link-pairs.tsv")).Rows
                .Where(r => r["verdict"] == "ok" || Bad.Contains(r["verdict"]))
                .Select(r => new VerifyCase("verify:" + r.Id, r["wording"], r["description"], r["verdict"] == "ok", r["judgedBy"]));

        public static IEnumerable<ClassifyCase> ClassifyCases(string goldenDir) =>
            GoldenTsv.Read(Path.Combine(goldenDir, "line-labels.tsv")).Rows
                .Select(r => new ClassifyCase("classify:" + r.Id, r.Value("recipe") ?? string.Empty, r["raw"], r["kind"], r["judgedBy"]));

        public static (FoodNameMatcher Matcher, Dictionary<string, FoodCandidate> Staples) UsdaMatcher(string goldenDir)
        {
            var rows = GoldenTsv.Read(Path.Combine(goldenDir, "usda-catalog.tsv")).Rows;
            var foods = rows.Select(r => new FoodCandidate(long.Parse(r.Id), r["fdcId"], r.Value("description") ?? r["name"], r["dataType"])).ToList();
            var descriptions = rows.Where(r => r.Value("description") != null).Select(r => (long.Parse(r.Id), r["description"])).ToList();
            return (new FoodNameMatcher(foods), IngredientLinkService.StapleTargets(foods, descriptions));
        }

        private sealed class CapturingHandler : HttpMessageHandler
        {
            private const string Reply = "{\"answers\":[{\"n\":1,\"pick\":0,\"confidence\":0.5}],\"results\":[{\"n\":1,\"verdict\":\"same\"}],\"lines\":[{\"n\":1,\"kind\":\"ingredient\"}]}";

            public string? Body { get; private set; }
            public string Path { get; private set; } = string.Empty;

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Path = request.RequestUri?.AbsolutePath ?? string.Empty;
                Body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
                var payload = new JsonObject { ["response"] = Reply, ["message"] = new JsonObject { ["content"] = Reply } };
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json") };
            }
        }
    }
}
