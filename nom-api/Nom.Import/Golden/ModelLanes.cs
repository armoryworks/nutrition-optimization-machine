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
    public sealed record LaneCaseResult(string Id, bool Pass, bool FalseAccept, bool CanFalseAccept, string Expected, string Actual, string JudgedBy, bool? Accepted = null, bool? Flagged = null,
        bool? AuditorFlagsGood = null, bool? AuditorFlagsBad = null);

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

        /// <summary>The second model the run asked (--check-model): reader B of the reader lanes, the confirming reader of link-audit, the judge of the prose writer.</summary>
        public string? CheckModel { get; set; }
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
        public int? Accepted { get; set; }
        public double? Precision { get; set; }
        public Dictionary<string, int[]> ByJudge { get; set; } = new();
        public List<string> Failed { get; set; } = new();

        /// <summary>For a lane that is an audit check: known-bad cases, those its combined check flagged, known-good cases, and those it flagged.</summary>
        public int? AuditKnownBad { get; set; }
        public int? AuditFlaggedBad { get; set; }
        public int? AuditKnownGood { get; set; }
        public int? AuditFlaggedGood { get; set; }

        /// <summary>The auditor a lane also scored alone (link-audit: the model scored; alcohol-reading: the fixed-reading auditor), and its own flags on known-bad and known-good cases.</summary>
        public string? AuditModel { get; set; }
        public int? AuditorKnownBad { get; set; }
        public int? AuditorFlaggedBad { get; set; }
        public int? AuditorKnownGood { get; set; }
        public int? AuditorFlaggedGood { get; set; }
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
        public double? MinPrecision { get; set; }
        public string Note { get; set; } = string.Empty;
    }

    /// <summary>
    /// The model lanes the golden sets can score, run by an operator against a named host and
    /// model (never from CI). A run only asks the model and compares; it opens no database and
    /// writes nothing but its own score file. The signature of a lane is a hash of the request it
    /// sends for one fixed question, without the model name, so any change to its prompt, schema
    /// or options shows as a new signature.
    /// </summary>
    public static partial class ModelLanes
    {
        public const string LinkPick = "link-pick";
        public const string LinkVerify = "link-verify";
        public const string LineClassify = "line-classify";
        public const string LinkGateLane = "link-gate";
        public const string AlcoholContextLane = "alcohol-context";
        public const string AlcoholRetentionLane = "alcohol-retention";
        public const string WordingTiersLane = "wording-tiers";
        public const string LineJudge = "line-judge";
        public const string ProseRewriteLane = ProseLaneGolden.RewriteLane;
        public const string ProseJudgeLane = ProseLaneGolden.JudgeLane;
        public const string ProseAuditLane = ProseLaneGolden.AuditLane;
        public const string WordingReadLane = WordingReaderRecords.ReadLane;
        public const string WordingLexiconLane = WordingReaderRecords.LexiconLane;
        public const string AlcoholReadingLane = WordingReaderRecords.AlcoholLane;
        public const string LinkAuditLane = WordingReaderRecords.AuditLane;
        public const string CheckModelKey = "Golden:CheckModel";
        public const string AuditModelKey = "Golden:AuditModel";

        /// <summary>The file a prose-rewrite run on the working half writes each recipe's sheets and refused steps to, and a prose-audit run on it each difference the auditor lists (--trace); never written for the held-out half or all rows.</summary>
        public const string TraceKey = "Golden:Trace";

        /// <summary>A prose-rewrite run on one half that rewords its recipes only and leaves out the planted faults (--recipes-only), whose verdicts are the gate's and the judge's and do not move with the writer; never with --record.</summary>
        public const string RecipesOnlyKey = "Golden:RecipesOnly";
        public const string FloorsFile = "model-lanes.json";
        public const string ScoresFolder = "scores";
        public const int PickBatch = IngredientLinkService.QuestionsPerCall;
        public const int VerifyBatch = IngredientLinkService.PairsPerVerifyCall;
        public const int ClassifyBatch = 20;
        public const int JudgeBatch = 8;

        /// <summary>How many times a scorer asks one batch when the host does not answer within the client's timeout.</summary>
        public const int CallAttempts = 3;

        public const string SplitAll = "all";
        public const string SplitWorking = "working";
        public const string SplitHeldOut = "held-out";

        /// <summary>The values of --split.</summary>
        public static readonly IReadOnlyList<string> Splits = new[] { SplitAll, SplitWorking, SplitHeldOut };

        /// <summary>The lanes a run may score on one half of their golden rows (--split working or held-out).</summary>
        public static readonly IReadOnlyList<string> Splittable = new[] { LinkPick, LinkVerify, LineJudge, ProseJudgeLane, ProseRewriteLane, ProseAuditLane, DietSecondReaderLane };

        public static readonly IReadOnlyList<string> Scored = new[] { LinkPick, LinkVerify, LineClassify, LinkGateLane, AlcoholContextLane, AlcoholRetentionLane, WordingTiersLane, LineJudge, ProseRewriteLane, ProseJudgeLane, ProseAuditLane, WordingReadLane, WordingLexiconLane, AlcoholReadingLane, LinkAuditLane, DietSecondReaderLane };

        /// <summary>The prose lanes, scored on prose-judge.tsv; their signatures are <see cref="ProseLaneGolden.Signature"/>.</summary>
        public static readonly IReadOnlyList<string> Prose = new[] { ProseRewriteLane, ProseJudgeLane, ProseAuditLane };

        /// <summary>Lanes scored by rules alone: no host or model is asked, and the signature is the rules' own.</summary>
        public static readonly IReadOnlyList<string> NoModel = new[] { LinkGateLane, AlcoholContextLane, AlcoholRetentionLane, WordingTiersLane };

        /// <summary>The rule version a lane with no model records in place of a model name.</summary>
        public static string NoModelVersion(string lane) => lane switch
        {
            AlcoholContextLane => AlcoholLineContext.Version,
            AlcoholRetentionLane => AlcoholRetention.Version,
            WordingTiersLane => WordingTiers.Version,
            _ => LinkGate.Version,
        };

        private static readonly HashSet<string> Bad = new(StringComparer.Ordinal) { "wrong", "near", "compound" };

        private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        public static IConfiguration Config(string host, string model, string? checkModel = null, string? auditModel = null, string? trace = null, bool recipesOnly = false) => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [CheckModelKey] = checkModel,
                [AuditModelKey] = auditModel,
                [TraceKey] = trace,
                [RecipesOnlyKey] = recipesOnly ? "true" : null,
                ["Ai:BatchOllamaUrl"] = host,
                ["Ai:LinkModel"] = model,
                ["Ai:RepairModel"] = model,
                ["IngredientLinking:VerifyModel"] = model,
                ["Reprocess:JudgeModel"] = model,
            })
            .Build();

        public static async Task<string> SignatureAsync(string lane)
        {
            if (lane == LinkGateLane) return LinkGate.Signature;
            if (lane == AlcoholContextLane) return AlcoholLineContext.Signature;
            if (lane == AlcoholRetentionLane) return AlcoholRetention.Signature;
            if (lane == WordingTiersLane) return WordingTiers.Signature;
            if (lane == LineJudge) return OllamaLineJudge.Signature;
            if (lane == DietSecondReaderLane) return OllamaDietSecondReader.Signature;
            if (Prose.Contains(lane)) return ProseLaneGolden.Signature(lane);
            if (Readers.Contains(lane)) return WordingReaderRecords.ScorerSignature(lane);
            var capture = new CapturingHandler();
            using var http = new HttpClient(capture);
            var config = Config("http://signature.invalid", "signature");
            var candidates = new[] { new FoodCandidate(1, "1", "Butter, salted", "sr_legacy_food"), new FoodCandidate(2, "2", "Butter, without salt", "sr_legacy_food"), new FoodCandidate(3, "3", "Butter, without salt", "foundation_food") };
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
            var bytes = Encoding.UTF8.GetBytes(capture.Path + "\n" + body.ToJsonString() + (lane == LinkPick ? "\n" + LinkPickRules.Version : lane == LinkVerify ? "\n" + LinkVerifyRules.Version : string.Empty));
            return Convert.ToHexString(SHA256.HashData(bytes))[..16].ToLowerInvariant();
        }

        /// <summary>
        /// One model call of a scorer lane, asked again when the host does not answer within the
        /// client's timeout (a shared host can hold a request behind another lane's), up to
        /// <see cref="CallAttempts"/> times, so one slow answer does not end a run of hours. Any other
        /// failure, a timeout on the last attempt and the caller's own cancellation end the run as before.
        /// </summary>
        public static async Task<T> AskAsync<T>(Func<Task<T>> call, Action<string>? progress, CancellationToken cancellationToken)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    return await call();
                }
                catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException && !cancellationToken.IsCancellationRequested && attempt < CallAttempts)
                {
                    progress?.Invoke($"The model host did not answer in time; asking again ({attempt + 1} of {CallAttempts}).");
                }
            }
        }

        public static Dictionary<string, LaneFloor> Floors(string goldenDir) =>
            JsonSerializer.Deserialize<Dictionary<string, LaneFloor>>(File.ReadAllText(Path.Combine(goldenDir, FloorsFile)), Json)
            ?? new Dictionary<string, LaneFloor>();

        public static string RecordPath(string goldenDir, string lane) => Path.Combine(goldenDir, ScoresFolder, lane + ".json");

        /// <summary>
        /// The lanes with a golden scorer that have no score record and that model-lanes.json does
        /// not require yet: the release gate checks nothing for them until they are scored.
        /// </summary>
        public static IReadOnlyList<string> Unscored(string goldenDir) =>
            Floors(goldenDir)
                .Where(f => f.Value.Scorer && !f.Value.Required && !File.Exists(RecordPath(goldenDir, f.Key)))
                .Select(f => f.Key)
                .OrderBy(l => l, StringComparer.Ordinal)
                .ToList();

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
            if (floor.MinPrecision is { } precision && (score.Precision ?? 0) < precision)
                breaches.Add($"{score.Lane}: {score.Precision ?? 0:P1} of the {score.Accepted ?? 0} answers it accepted are judged right, below the floor {precision:P1}");
            return breaches;
        }

        public static LaneScore Score(string lane, string model, string host, string signature, IReadOnlyList<LaneCaseResult> results)
        {
            var possible = results.Count(r => r.CanFalseAccept);
            var accepted = results.Count(r => r.FalseAccept);
            var gated = results.Where(r => r.Accepted != null).ToList();
            var taken = gated.Count(r => r.Accepted == true);
            var flagged = results.Where(r => r.Flagged != null).ToList();
            var auditedBad = results.Where(r => r.AuditorFlagsBad != null).ToList();
            var auditedGood = results.Where(r => r.AuditorFlagsGood != null).ToList();
            var auditor = auditedBad.Count > 0 || auditedGood.Count > 0;
            return new LaneScore
            {
                AuditorKnownBad = auditor ? auditedBad.Count : null,
                AuditorFlaggedBad = auditor ? auditedBad.Count(r => r.AuditorFlagsBad == true) : null,
                AuditorKnownGood = auditor ? auditedGood.Count : null,
                AuditorFlaggedGood = auditor ? auditedGood.Count(r => r.AuditorFlagsGood == true) : null,
                AuditKnownBad = flagged.Count == 0 ? null : flagged.Count(r => r.CanFalseAccept),
                AuditFlaggedBad = flagged.Count == 0 ? null : flagged.Count(r => r.CanFalseAccept && r.Flagged == true),
                AuditKnownGood = flagged.Count == 0 ? null : flagged.Count(r => !r.CanFalseAccept),
                AuditFlaggedGood = flagged.Count == 0 ? null : flagged.Count(r => !r.CanFalseAccept && r.Flagged == true),
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
                Accepted = gated.Count == 0 ? null : taken,
                Precision = gated.Count == 0 ? null : taken == 0 ? 0 : Math.Round((double)gated.Count(r => r.Accepted == true && !r.FalseAccept) / taken, 4),
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

        /// <summary>
        /// The faithful rewrites the prose judge refused, of all the faithful rewrites scored, for the
        /// prose-judge lane: the cost in coverage its floor does not count; for the prose-audit lane the
        /// faithful rewrites the auditor flagged (its false alarms). Null for any other lane.
        /// </summary>
        public static (int Refused, int Faithful)? FalseRefusals(string lane, IReadOnlyList<LaneCaseResult> results)
        {
            if (lane != ProseJudgeLane && lane != ProseAuditLane) return null;
            var faithful = results.Where(r => !r.CanFalseAccept).ToList();
            return (faithful.Count(r => !r.Pass), faithful.Count);
        }

        /// <summary>
        /// The golden row a case comes from: the case id without its lane's prefix and without its
        /// "better" or "worse" ending, so both cases of one line-judge row fall in the same half.
        /// </summary>
        public static string SplitRow(string caseId)
        {
            var id = Prose.FirstOrDefault(l => caseId.StartsWith(l + ":", StringComparison.Ordinal)) is { } lane ? caseId[(lane.Length + 1)..] : caseId;
            return id.EndsWith(":better", StringComparison.Ordinal) ? id[..^":better".Length]
                : id.EndsWith(":worse", StringComparison.Ordinal) ? id[..^":worse".Length]
                : id;
        }

        /// <summary>
        /// Whether a case is in a half of its lane's golden rows: the working half holds the rows
        /// whose SHA-256 of the row id starts with an even byte, the held-out half the others, and
        /// "all" holds every case. The split is fixed by the id alone, so it never moves between runs.
        /// </summary>
        public static bool InSplit(string caseId, string split)
        {
            if (split == SplitAll) return true;
            var even = (SHA256.HashData(Encoding.UTF8.GetBytes(SplitRow(caseId)))[0] & 1) == 0;
            return split == SplitWorking ? even : split == SplitHeldOut ? !even : throw new ArgumentException($"Unknown split '{split}'.", nameof(split));
        }

        public static async Task<IReadOnlyList<LaneCaseResult>> RunAsync(string lane, string goldenDir, HttpClient http, IConfiguration config, int? limit, Action<string> progress, CancellationToken cancellationToken = default, string split = SplitAll)
        {
            if (split != SplitAll && !Splittable.Contains(lane)) throw new ArgumentException($"Lane '{lane}' is scored on all its rows only.", nameof(split));
            switch (lane)
            {
                case ProseRewriteLane:
                case ProseJudgeLane:
                case ProseAuditLane:
                    return await ProseResultsAsync(lane, goldenDir, http, config, limit, progress, cancellationToken, split);
                case WordingReadLane:
                case WordingLexiconLane:
                case AlcoholReadingLane:
                case LinkAuditLane:
                    return await ReaderResultsAsync(lane, goldenDir, http, config, limit, progress, cancellationToken);
                case DietSecondReaderLane:
                    return DietReaderResults(await DietReaderRunAsync(goldenDir, http, config["Ai:BatchOllamaUrl"] ?? string.Empty, config["Ai:LinkModel"] ?? string.Empty, limit, progress, cancellationToken, split));
                case AlcoholContextLane:
                {
                    var cases = AlcoholCases(goldenDir).Take(limit ?? int.MaxValue).ToList();
                    var results = cases.Select(c => c.Outcome()).ToList();
                    progress($"{lane}: {results.Count}/{cases.Count}");
                    return results;
                }
                case AlcoholRetentionLane:
                {
                    var cases = RetentionCases(goldenDir).Take(limit ?? int.MaxValue).ToList();
                    var results = cases.Select(c => c.Outcome()).ToList();
                    progress($"{lane}: {results.Count}/{cases.Count}");
                    return results;
                }
                case WordingTiersLane:
                {
                    var catalog = GoldenTierCatalog(goldenDir);
                    var cases = TierCases(goldenDir).Take(limit ?? int.MaxValue).ToList();
                    var decisions = new Dictionary<string, WordingTierDecision>(StringComparer.Ordinal);
                    var results = cases.Select(c =>
                    {
                        if (!decisions.TryGetValue(c.Wording, out var decision))
                            decisions[c.Wording] = decision = WordingTiers.Decide(c.Wording, 0, Array.Empty<TierLine>(), catalog);
                        return c.Outcome(decision);
                    }).ToList();
                    progress($"{lane}: {results.Count}/{cases.Count}");
                    return results;
                }
                case LinkGateLane:
                {
                    var cases = GateCases(goldenDir).Take(limit ?? int.MaxValue).ToList();
                    var catalog = GoldenGateCatalog(goldenDir);
                    var results = cases.Select(c => c.Outcome(LinkGate.Check(c.Pair, catalog))).ToList();
                    progress($"{lane}: {results.Count}/{cases.Count}");
                    return results;
                }
                case LineJudge:
                {
                    var cases = JudgeOrder(JudgeCases(goldenDir).Where(c => InSplit(c.Id, split))).Take(limit ?? int.MaxValue).ToList();
                    var judge = new OllamaLineJudge(http, config);
                    var results = new List<LaneCaseResult>();
                    foreach (var batch in cases.Chunk(JudgeBatch))
                    {
                        var verdicts = (await AskAsync(() => judge.JudgeAsync(batch.Select((c, i) => c.Case with { Id = i }).ToList(), cancellationToken), progress, cancellationToken)).ToDictionary(v => v.Id, v => v.Judgement);
                        results.AddRange(batch.Select((c, i) => c.Outcome(verdicts.TryGetValue(i, out var v) ? v : null)));
                        progress($"{lane}: {results.Count}/{cases.Count}");
                    }
                    return results;
                }
                case LinkPick:
                {
                    var all = PickCases(goldenDir).Where(c => InSplit(c.Id, split)).Take(limit ?? int.MaxValue).ToList();
                    var matcher = new OllamaIngredientLinkMatcher(http, config);
                    var (_, staples) = UsdaMatcher(goldenDir);
                    var results = new List<LaneCaseResult>();
                    var cases = new List<PickCase>();
                    foreach (var c in all)
                    {
                        if (LinkPickRules.RuleNone(c.Name) != null) results.Add(c.Outcome(new LinkAnswer(0, null, 1m)));
                        else if (LinkPickRules.RuleTarget(c.Name, staples) is { } plain) results.Add(c.Outcome(new LinkAnswer(0, plain.Food, 1m)));
                        else cases.Add(c);
                    }
                    foreach (var batch in cases.Chunk(PickBatch))
                    {
                        var questions = batch.Select((c, i) => new LinkQuestion(i, c.Name, c.Shortlist)).ToList();
                        var answers = (await AskAsync(() => matcher.ChooseAsync(questions, cancellationToken), progress, cancellationToken)).ToDictionary(a => a.IngredientId);
                        results.AddRange(batch.Select((c, i) => c.Outcome(answers.TryGetValue(i, out var a) ? a.Choice is { } picked && LinkPickRules.Contradicts(c.Name, picked) != null ? a with { Choice = null } : a : null)));
                        progress($"{lane}: {results.Count}/{all.Count}");
                    }
                    return results;
                }
                case LinkVerify:
                {
                    var all = VerifyCases(goldenDir).Where(c => InSplit(c.Id, split)).Take(limit ?? int.MaxValue).ToList();
                    var verifier = new OllamaIngredientLinkVerifier(http, config);
                    var results = new List<LaneCaseResult>();
                    var cases = new List<VerifyCase>();
                    foreach (var c in all)
                    {
                        if (LinkVerifyRules.Before(c.Wording) is { } ruled) results.Add(c.Outcome(ruled));
                        else cases.Add(c);
                    }
                    foreach (var batch in cases.Chunk(VerifyBatch))
                    {
                        var pairs = batch.Select((c, i) => new LinkPair(i, c.Wording, c.Description)).ToList();
                        var verdicts = (await AskAsync(() => verifier.JudgeAsync(pairs, cancellationToken), progress, cancellationToken)).ToDictionary(v => v.IngredientId, v => v.Judgement);
                        results.AddRange(batch.Select((c, i) => c.Outcome(verdicts.TryGetValue(i, out var v) ? LinkVerifyRules.After(c.Wording, c.Description, v) : null)));
                        progress($"{lane}: {results.Count}/{all.Count}");
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
                            var kinds = await AskAsync(() => model.ClassifyLinesAsync(recipe.Key, batch.Select(c => c.Raw).ToList(), cancellationToken), progress, cancellationToken);
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

        /// <summary>The rows of prose-judge.tsv.</summary>
        public static IReadOnlyList<ProseJudgeCase> ProseCases(string goldenDir) =>
            ProseLaneGolden.ParseCases(File.ReadAllText(Path.Combine(goldenDir, ProseLaneGolden.CasesFile)));

        /// <summary>The stock phrases of prose-stock.tsv that the golden prose rows are checked with.</summary>
        public static ProseStockIndex ProseStock(string goldenDir) =>
            new(1, GoldenTsv.Read(Path.Combine(goldenDir, "prose-stock.tsv")).Rows
                .Where(r => int.Parse(r["sites"], System.Globalization.CultureInfo.InvariantCulture) >= ProseStockIndex.StockSites)
                .Select(r => ProsePhrases.Hash(r.Id)));

        /// <summary>
        /// A prose lane over prose-judge.tsv. prose-judge: the judge (the model named) reads each
        /// rewrite against its method; a case passes when it finds a faithful row faithful and a
        /// fault row not, and a fault it finds faithful is a false accept. prose-audit: the auditor
        /// (the model named) reads each row with its ingredient lines, rules first, then each changed
        /// step, and re-reads a flagged step blind; a row counts as flagged when a rule decided it or
        /// both reads agree, as the lane's audit counts an error. prose-rewrite:
        /// the writer (the model named) rewords the whole method of each recipe of the file through the
        /// lane with the judge (--check-model, else Ai:ProseJudgeModel, else reader B), which passes
        /// when the rewrite gets through the gate, the strict copy check included, and each planted fault
        /// is put through the facts gate, the check then the judge, which passes when it is refused and is
        /// a false accept when it is not.
        /// </summary>
        private static async Task<IReadOnlyList<LaneCaseResult>> ProseResultsAsync(string lane, string goldenDir, HttpClient http, IConfiguration config, int? limit, Action<string> progress, CancellationToken cancellationToken, string split = SplitAll)
        {
            var host = config["Ai:BatchOllamaUrl"] ?? string.Empty;
            var model = config["Ai:LinkModel"] ?? string.Empty;
            var checkModel = config[CheckModelKey] is { Length: > 0 } check ? check
                : config[ProseLaneModels.JudgeModelKey] is { Length: > 0 } judgeModel ? judgeModel
                : new Nom.Orch.Settings.LaneModelRoster().ReaderB;
            var cases = ProseCases(goldenDir).Where(c => InSplit(c.Id, split)).ToList();
            var stock = ProseStock(goldenDir);
            var results = new List<LaneCaseResult>();
            switch (lane)
            {
                case ProseJudgeLane:
                {
                    var judge = new OllamaProseStepJudge(http, host, model);
                    foreach (var c in cases.Take(limit ?? int.MaxValue))
                    {
                        var verdict = await AskAsync(() => judge.JudgeAsync(c.Method.Select(ProseQuantities.Prepare).ToList(), c.Rewrite, cancellationToken, c.Title), progress, cancellationToken);
                        var faithful = verdict?.Faithful;
                        results.Add(new LaneCaseResult($"{lane}:{c.Id}", faithful == c.Faithful, !c.Faithful && faithful == true, !c.Faithful,
                            c.Faithful ? "faithful" : "fault " + c.Fault, faithful == null ? "no readable answer" : faithful.Value ? "faithful" : $"fault: step {verdict!.FirstFlagged?.Number} {verdict.FirstFlagged?.Note}", c.Source));
                        progress($"{lane}: {results.Count}/{cases.Count}");
                    }
                    return results;
                }
                case ProseAuditLane:
                {
                    string? current = null;
                    Action<string>? trace = config[TraceKey] is { Length: > 0 } tracePath && split == SplitWorking
                        ? line => File.AppendAllText(tracePath, $"{current}\t{line}\n")
                        : null;
                    var auditor = new OllamaProseAuditor(http, host, model, trace);
                    foreach (var c in cases.Take(limit ?? int.MaxValue))
                    {
                        current = c.Id;
                        var pair = new ProseAuditPair(c.Title, c.Lines, c.Method.Select(ProseQuantities.Prepare).ToList(), c.Rewrite, c.RewriteDescription);
                        var verdict = await AskAsync(() => auditor.AuditAsync(pair, cancellationToken), progress, cancellationToken);
                        var flagged = verdict is { Faithful: false } && await AskAsync(() => Nom.Orch.Services.ProseLaneAudit.ConfirmAsync(auditor, pair, verdict, cancellationToken), progress, cancellationToken) == true;
                        var read = verdict == null ? "no readable answer"
                            : verdict.Faithful ? "not flagged"
                            : $"{(flagged ? "flagged" : "not flagged, the blind re-read disagrees")}{(verdict.ByRule ? " by rule" : string.Empty)}: {verdict.Note}".TrimEnd();
                        results.Add(new LaneCaseResult($"{lane}:{c.Id}", flagged != c.Faithful, !c.Faithful && !flagged, !c.Faithful,
                            c.Faithful ? "not flagged" : "flagged: " + c.Fault, read, c.Source));
                        progress($"{lane}: {results.Count}/{cases.Count}");
                    }
                    return results;
                }
                default:
                {
                    var writer = new OllamaProseWriter(http, host, model);
                    var judge = new OllamaProseStepJudge(http, host, checkModel);
                    var recipes = ProseCases(goldenDir).GroupBy(c => c.Recipe)
                        .Where(r => InSplit(RecipeCase(lane, r.Key), split) || r.Any(c => !c.Faithful && InSplit(c.Id, split)))
                        .Take(limit ?? int.MaxValue).ToList();
                    foreach (var recipe in recipes)
                    {
                        var first = recipe.First();
                        if (InSplit(RecipeCase(lane, recipe.Key), split))
                        {
                            var clock = System.Diagnostics.Stopwatch.StartNew();
                            Action<string>? trace = config[TraceKey] is { Length: > 0 } tracePath && split == SplitWorking
                                ? line => File.AppendAllText(tracePath, $"{recipe.Key}\t{line}\n")
                                : null;
                            var outcome = await AskAsync(() => ProseReword.RunAsync(first.Input(stock), writer, judge, cancellationToken, trace), progress, cancellationToken);
                            trace?.Invoke(string.Join(" | ", outcome.Attempts.Select(a => $"{a.Number} {a.Style}: {string.Join(", ", a.Reasons)}")));
                            if (outcome.Rewrite != null) trace?.Invoke("written: " + string.Join(" ¦ ", outcome.Rewrite.Steps));
                            var result = new LaneCaseResult(RecipeCase(lane, recipe.Key), outcome.Passed, false, false, "a rewrite through the gate",
                                RewriteActual(outcome, clock.Elapsed.TotalSeconds), "rule:end-to-end");
                            results.Add(result);
                            progress($"{result.Id}: {result.Actual}");
                        }

                        foreach (var c in recipe.Where(c => !c.Faithful && InSplit(c.Id, split) && !(split != SplitAll && config[RecipesOnlyKey] == "true")))
                        {
                            var reasons = await AskAsync(() => ProseCleanRoom.GateAsync(c.Input(stock), c.RewriteText, judge, cancellationToken), progress, cancellationToken);
                            results.Add(new LaneCaseResult($"{lane}:{c.Id}", reasons.Count > 0, reasons.Count == 0, true, "refused",
                                reasons.Count == 0 ? "accepted" : string.Join(", ", reasons), c.Source));
                        }

                        progress($"{lane}: {results.Count}");
                    }
                    return results;
                }
            }
        }

        /// <summary>
        /// The prose writer's end-to-end figures over the recipes of a run: recipes, those the first
        /// reword round got through the gate, those that ended on the rule rendering, those given
        /// up, and the mean seconds a recipe took; null for another lane or a run with no recipe.
        /// </summary>
        public static (int Recipes, int FirstTry, int RuleRendered, int GaveUp, double Seconds)? RewriteFigures(string lane, IReadOnlyList<LaneCaseResult> results)
        {
            if (lane != ProseRewriteLane) return null;
            var recipes = results.Where(r => r.Id.StartsWith(lane + ":recipe:", StringComparison.Ordinal)).ToList();
            if (recipes.Count == 0) return null;
            var seconds = recipes.Select(r => System.Text.RegularExpressions.Regex.Match(r.Actual, @", (\d+) s\b")).Where(m => m.Success)
                .Select(m => double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).DefaultIfEmpty(0).Average();
            return (recipes.Count,
                recipes.Count(r => r.Pass && r.Actual.StartsWith("rewritten in round 1,", StringComparison.Ordinal)),
                recipes.Count(r => r.Pass && r.Actual.Contains($"({ProseLanePrompts.RuleAttempt}", StringComparison.Ordinal)),
                recipes.Count(r => !r.Pass),
                Math.Round(seconds, 1));
        }

        /// <summary>The case of one recipe the prose writer rewrites end to end; its half is fixed by the hash of "recipe:" and the recipe key.</summary>
        public static string RecipeCase(string lane, string recipe) => $"{lane}:recipe:{recipe}";

        /// <summary>
        /// What the clean-room pipeline made of one recipe, as a case's actual text: the reword rounds it
        /// took, the attempt that passed and its style, or the failure class and the
        /// reasons of every attempt, each in the lane's own words, and the seconds the recipe took.
        /// </summary>
        public static string RewriteActual(ProseLaneOutcome outcome, double seconds)
        {
            var time = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{seconds:0} s");
            var tried = string.Join(" | ", outcome.Attempts.Where(a => a.Reasons.Count > 0)
                .Select(a => $"{a.Number} {a.Style}: {string.Join("; ", a.Reasons.Where(r => r != ProseCleanRoom.FormReason).DefaultIfEmpty(a.Reasons[0]).Take(4))}"));
            var sheets = outcome.RuleSheets ? ", rule sheets" : string.Empty;
            if (outcome.Passed)
            {
                var last = outcome.Attempts[^1];
                return $"rewritten in round {outcome.Attempts.Count(a => a.Style == ProseReword.StyleReword)}, attempt {last.Number} ({last.Style}{sheets}), {time}{(tried.Length == 0 ? string.Empty : "; refused before: " + tried)}";
            }

            return $"gave up: {outcome.FailureClass}{sheets}, {time}{(tried.Length == 0 ? string.Empty : "; " + tried)}";
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

        public sealed record GateCase(string Id, GatePair Pair, bool Right, string Verdict, string Description, string JudgedBy)
        {
            public LaneCaseResult Outcome(IReadOnlyList<GateHold> holds)
            {
                var passes = holds.Count == 0;
                return new LaneCaseResult(Id, Right == passes, !Right && passes, !Right,
                    Right ? $"pass ({Pair.Wording} -> {Description})" : $"hold ({Verdict}: {Pair.Wording} -> {Description})",
                    passes ? "pass" : string.Join("; ", holds.Select(h => h.Code + ": " + h)), JudgedBy, passes);
            }
        }

        /// <summary>
        /// One judged pair of link-pairs.tsv as the wording tiers read it: the wording in key form,
        /// the judged food, and whether it was judged right or wrong, near or compound.
        /// </summary>
        public sealed record TierCase(string Id, string Wording, long FoodId, bool Right, string Verdict, string Description, string JudgedBy)
        {
            public LaneCaseResult Outcome(WordingTierDecision decision)
            {
                var answered = decision.Writes ? decision.Answer!.FoodId : (long?)null;
                var accepted = answered == FoodId;
                var actual = decision.Answer == null ? decision.Abstain ?? WordingTiers.NoTier
                    : decision.Writes ? $"{decision.Answer.Tier} #{decision.Answer.FoodId}"
                    : $"{decision.Answer.Tier} #{decision.Answer.FoodId} held: {decision.Hold}";
                return new LaneCaseResult(Id, Right ? accepted : !accepted, !Right && accepted, !Right,
                    Right ? $"#{FoodId} ({Wording} -> {Description})" : $"not #{FoodId} ({Verdict}: {Wording} -> {Description})",
                    actual, JudgedBy, accepted);
            }
        }

        /// <summary>
        /// Every pair of link-pairs.tsv judged right, wrong, near or compound whose wording is not
        /// itself a row of the verified wording table of wording-pairs.tsv (the lane never asks the
        /// tiers about a wording the table holds).
        /// </summary>
        public static IEnumerable<TierCase> TierCases(string goldenDir)
        {
            var table = VerifiedRows(goldenDir).Select(v => v.Wording).ToHashSet(StringComparer.Ordinal);
            foreach (var r in GoldenTsv.Read(Path.Combine(goldenDir, "link-pairs.tsv")).Rows)
            {
                if (r["verdict"] != "ok" && !Bad.Contains(r["verdict"])) continue;
                var wording = IngredientNameNormalizer.WordingKey(r["wording"]);
                if (wording.Length == 0 || table.Contains(wording)) continue;
                yield return new TierCase("tiers:" + r.Id, wording, long.Parse(r["foodId"]), r["verdict"] == "ok", r["verdict"], r["description"], r["judgedBy"]);
            }
        }

        /// <summary>The kept w01 rows of wording-pairs.tsv (expect =), which the verified wording table holds on staging.</summary>
        public static IEnumerable<(string Wording, string Verdict, long? FoodId)> VerifiedRows(string goldenDir) =>
            GoldenTsv.Read(Path.Combine(goldenDir, "wording-pairs.tsv")).Rows
                .Where(r => r["expect"] == "=" && r.Id.StartsWith("wp-w01-", StringComparison.Ordinal))
                .Select(r => (IngredientNameNormalizer.WordingKey(r["wording"]), IngredientWordingVerdict.Food, (long?)long.Parse(r["foodId"])));

        /// <summary>
        /// The tiers' catalog over gate-catalog.tsv and usda-catalog.tsv, with the w01 rows of
        /// wording-pairs.tsv as the verified wording table. The fixtures hold no trusted aliases and no curated lines, so the alias and
        /// head-default tiers are scored only by the rule-given rows of wording-tiers.tsv.
        /// </summary>
        public static WordingTierCatalog GoldenTierCatalog(string goldenDir)
        {
            var (matcher, staples) = UsdaMatcher(goldenDir);
            return WordingTierCatalog.Build(GateFoods(goldenDir).ToList(), matcher, staples, verified: VerifiedRows(goldenDir));
        }

        /// <summary>
        /// One row of alcohol-context.tsv: a line, what its recipe says, the sense the rules must
        /// read, a food (FDC id and USDA description) the sense must allow and one it must refuse.
        /// </summary>
        public sealed record AlcoholCase(string Id, string Raw, AlcoholRecipeContext? Context, string Expect, string? Allows, string? AllowsText, string? Refuses, string? RefusesText, string JudgedBy)
        {
            public LaneCaseResult Outcome()
            {
                var reading = AlcoholLineContext.Read(Raw, Context);
                var allowed = Allows == null || AlcoholLineContext.Refuse(reading, Allows, AllowsText) == null;
                var refused = Refuses == null || AlcoholLineContext.Refuse(reading, Refuses, RefusesText) != null;
                var pass = reading.Sense == Expect && allowed && refused;
                return new LaneCaseResult(Id, pass, Refuses != null && !refused, Refuses != null,
                    $"{Expect}{(Allows == null ? string.Empty : $", allows {Allows}")}{(Refuses == null ? string.Empty : $", refuses {Refuses}")}",
                    $"{reading}{(allowed ? string.Empty : $", refuses {Allows}")}{(refused ? string.Empty : $", allows {Refuses}")}", JudgedBy);
            }
        }

        /// <summary>
        /// One line-judge case: a judged golden line with the row its golden file gives as right and
        /// a row carrying the defect its class is about, one before and one after. A "better" case
        /// rebuilds the wrong row into the right one, a "worse" case the right one into the wrong
        /// one. <see cref="RuleFlags"/> is whether the rule audit fails on the case's new row, so a
        /// run also scores the combined audit check (the judge's "worse" confirmed by the rule audit).
        /// </summary>
        public sealed record JudgeCase(string Id, LineJudgeCase Case, LineJudgement Expected, bool RuleFlags, string JudgedBy)
        {
            public LaneCaseResult Outcome(LineJudgement? verdict)
            {
                var worseCase = Expected == LineJudgement.Worse;
                return new LaneCaseResult(Id, verdict == Expected, worseCase && verdict == LineJudgement.Better, worseCase,
                    Expected.ToString().ToLowerInvariant(), verdict?.ToString().ToLowerInvariant() ?? "no readable answer", JudgedBy,
                    Flagged: verdict == LineJudgement.Worse && RuleFlags);
            }
        }

        private static readonly System.Text.RegularExpressions.Regex NotedFill = new(@"filled with (?<q>\d+(?:\.\d+)?) (?<unit>[A-Z]\w+)");

        /// <summary>
        /// The line-judge cases of the judged rows of lines.tsv, startup-fills.tsv and line-kinds.tsv
        /// (reprocess.md section 3): each row gives a right row and a wrong one, and becomes a "better"
        /// case (wrong to right) and a "worse" case (right to wrong). The wrong row is the defect the
        /// row's class is about: a stated measure counted in pieces, a stated amount lost, an amount
        /// filled from the method that the golden row says the line has not (its note names the fill),
        /// a heading made an ingredient, an ingredient made a note.
        /// </summary>
        public static IEnumerable<JudgeCase> JudgeCases(string goldenDir)
        {
            var judged = new[] { "ok", "agent" };
            var invariant = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var r in GoldenTsv.Read(Path.Combine(goldenDir, "lines.tsv")).Rows)
            {
                if (!judged.Contains(r["parse"]) || r["quantity"] == GoldenTsv.NotJudged || r["measurement"] == GoldenTsv.NotJudged) continue;
                var food = r.Value("wording") is { Length: > 0 } w && w != GoldenTsv.NotJudged ? w : r["raw"];
                var quantity = r.Value("quantity") is { } q ? decimal.Parse(q, invariant) : 0m;
                var unit = r.Value("measurement");
                var right = new LineJudgeRow(food, quantity, quantity == 0 ? null : unit);
                var wrong = quantity == 0 ? new LineJudgeRow(food, 1, IngredientLineParser.Piece)
                    : unit == IngredientLineParser.Piece ? new LineJudgeRow(food, 0, null)
                    : new LineJudgeRow(food, quantity, IngredientLineParser.Piece);
                foreach (var c in JudgePair("lines:" + r.Id, r["raw"], string.Empty, right, wrong, false, r["source"])) yield return c;
            }

            foreach (var r in GoldenTsv.Read(Path.Combine(goldenDir, "startup-fills.tsv")).Rows)
            {
                if (!judged.Contains(r["status"])) continue;
                var food = IngredientNameNormalizer.Normalize(r["part"]) is { Length: > 0 } n ? n : r["part"];
                var method = r["steps"] == GoldenTsv.NoAnswer ? string.Empty : r["steps"].Replace(" ¦ ", " ", StringComparison.Ordinal);
                var parts = r["expect"].Split(' ', 2);
                var right = parts.Length == 2 && decimal.TryParse(parts[0], System.Globalization.NumberStyles.Number, invariant, out var expected)
                    ? new LineJudgeRow(food, expected, parts[1])
                    : new LineJudgeRow(food, 0, null);
                var noted = NotedFill.Match(r["note"]);
                var wrong = right.Quantity > 0 ? new LineJudgeRow(food, 0, null)
                    : noted.Success ? new LineJudgeRow(food, decimal.Parse(noted.Groups["q"].Value, invariant), noted.Groups["unit"].Value)
                    : new LineJudgeRow(food, 1, IngredientLineParser.Piece);
                foreach (var c in JudgePair("fills:" + r.Id, r["part"], method, right, wrong, true, r["source"])) yield return c;
            }

            foreach (var r in GoldenTsv.Read(Path.Combine(goldenDir, "line-kinds.tsv")).Rows)
            {
                if (!judged.Contains(r["status"])) continue;
                var food = IngredientNameNormalizer.Normalize(r["raw"]) is { Length: > 0 } n ? n : r["raw"];
                var kind = r.Value("kind");
                var right = new LineJudgeRow(food, 0, null, kind);
                var wrong = kind == null ? new LineJudgeRow(food, 0, null, "note") : new LineJudgeRow(food, 1, IngredientLineParser.Piece);
                foreach (var c in JudgePair("kinds:" + r.Id, r["raw"], string.Empty, right, wrong, false, r["source"])) yield return c;
            }
        }

        /// <summary>
        /// The order a line-judge run asks its cases in: by a hash of the case id, so the "better"
        /// and "worse" case of one row seldom share a call and one confusion does not cost both.
        /// </summary>
        public static IEnumerable<JudgeCase> JudgeOrder(IEnumerable<JudgeCase> cases) =>
            cases.OrderBy(c => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(c.Id))), StringComparer.Ordinal);

        private static IEnumerable<JudgeCase> JudgePair(string id, string raw, string method, LineJudgeRow right, LineJudgeRow wrong, bool fromMethod, string judgedBy)
        {
            bool Flags(LineJudgeRow row) => ReprocessRuleAudit.Failures(
                new ReprocessRuleAudit.Line(raw, row.Food, row.Quantity, row.Unit, row.Kind, FilledFromMethod: fromMethod && row.Quantity > 0),
                method.Length == 0 ? null : new[] { method }).Count > 0;
            yield return new JudgeCase($"judge:{id}:better", new LineJudgeCase(0, "Golden", raw, method, wrong, right), LineJudgement.Better, Flags(right), judgedBy);
            yield return new JudgeCase($"judge:{id}:worse", new LineJudgeCase(0, "Golden", raw, method, right, wrong), LineJudgement.Worse, Flags(wrong), judgedBy);
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
                var shortlist = matcher.Shortlist(name, 10, AlcoholLineContext.Read(group.First()["wording"]));
                var listed = shortlist.Select(c => c.FdcId).ToHashSet(StringComparer.Ordinal);
                var right = group.Where(r => r["verdict"] == "ok" && listed.Contains(r["fdcId"])).Select(r => r["fdcId"]).ToHashSet(StringComparer.Ordinal);
                var wrong = group.Where(r => Bad.Contains(r["verdict"]) && listed.Contains(r["fdcId"])).Select(r => r["fdcId"]).ToHashSet(StringComparer.Ordinal);
                wrong.ExceptWith(right);
                if (right.Count == 0 && wrong.Count == 0) continue;
                yield return new PickCase("pick:" + name, name, shortlist, right, wrong, string.Join('+', group.Select(r => r["judgedBy"]).Distinct().OrderBy(j => j)));
            }
        }

        /// <summary>
        /// Every pair of link-pairs.tsv judged right, wrong, near or compound, as the link gate
        /// reads it: the judged food with its facts from gate-catalog.tsv when that holds it, the
        /// pair's own USDA description otherwise; a pair that was not a name match is a model pick.
        /// </summary>
        public static IEnumerable<GateCase> GateCases(string goldenDir)
        {
            var foods = GateFoods(goldenDir).ToDictionary(f => f.Id);
            foreach (var r in GoldenTsv.Read(Path.Combine(goldenDir, "link-pairs.tsv")).Rows)
            {
                if (r["verdict"] != "ok" && !Bad.Contains(r["verdict"])) continue;
                var foodId = long.Parse(r["foodId"]);
                var description = r["description"];
                var target = foods.TryGetValue(foodId, out var known)
                    ? known with { Description = known.Description ?? description }
                    : new GateFood(foodId, description, description, FdcId: r.Value("fdcId"));
                var nameMatch = r["nameMatch"] == "yes";
                yield return new GateCase("gate:" + r.Id, new GatePair(r["wording"], 0, target, NameMatch: nameMatch, ModelPick: !nameMatch),
                    r["verdict"] == "ok", r["verdict"], description, r["judgedBy"]);
            }
        }

        /// <summary>
        /// One row of alcohol-retention.tsv: a recipe's steps, a drink line, the recipe's other drink
        /// lines before and after it, the basis the retention reader must give and its factor. A false
        /// accept is a factor below the expected one, which would understate the alcohol that stays.
        /// </summary>
        public sealed record RetentionCase(string Id, IReadOnlyList<string> Steps, string Line, string Expect, decimal Factor, string JudgedBy, IReadOnlyList<string>? Before = null, IReadOnlyList<string>? After = null)
        {
            public LaneCaseResult Outcome()
            {
                var before = Before ?? Array.Empty<string>();
                var lines = before.Append(Line).Concat(After ?? Array.Empty<string>()).Select(l => (string?)l).ToList();
                var reading = AlcoholRetention.ReadRecipe(Steps, lines)[before.Count];
                var pass = reading.Basis == Expect && reading.Factor == Factor;
                return new LaneCaseResult(Id, pass, reading.Factor < Factor, true,
                    string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Expect} {Factor:0.00}"),
                    string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{reading.Basis} {reading.Factor:0.00}"), JudgedBy);
            }
        }

        /// <summary>The rows of alcohol-retention.tsv as cases; the steps column holds the recipe's steps separated by " | ", or "-" for none, and the before and after columns the recipe's other drink lines the same way (blank or "-" for none).</summary>
        public static IEnumerable<RetentionCase> RetentionCases(string goldenDir) =>
            GoldenTsv.Read(Path.Combine(goldenDir, "alcohol-retention.tsv")).Rows.Select(r => new RetentionCase(
                "retention:" + r.Id,
                (r.Value("steps") ?? string.Empty).Split(" | ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
                r["line"], r["expect"], decimal.Parse(r["factor"], System.Globalization.CultureInfo.InvariantCulture), r["source"],
                (r.Value("before") ?? string.Empty).Split(" | ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
                (r.Value("after") ?? string.Empty).Split(" | ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)));

        /// <summary>
        /// The rows of alcohol-context.tsv as cases, with the USDA description of each FDC id from
        /// gate-catalog.tsv (or its name when it has none). The context column holds the recipe's other
        /// lines, separated by ";"; site = yes marks a recipe from a web source.
        /// </summary>
        public static IEnumerable<AlcoholCase> AlcoholCases(string goldenDir)
        {
            var texts = GoldenTsv.Read(Path.Combine(goldenDir, "gate-catalog.tsv")).Rows
                .Where(r => r.Value("fdcId") != null)
                .GroupBy(r => r["fdcId"], StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First().Value("description") ?? g.First()["name"], StringComparer.Ordinal);
            foreach (var r in GoldenTsv.Read(Path.Combine(goldenDir, "alcohol-context.tsv")).Rows)
            {
                var title = r.Value("title");
                var others = r.Value("context")?.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                var site = r["site"] == "yes";
                var context = title == null && others == null && !site ? null : new AlcoholRecipeContext(title, others, null, site);
                var allows = r.Value("allows");
                var refuses = r.Value("refuses");
                yield return new AlcoholCase("alcohol:" + r.Id, r["raw"], context, r["expect"],
                    allows, allows == null ? null : texts.GetValueOrDefault(allows),
                    refuses, refuses == null ? null : texts.GetValueOrDefault(refuses), r["source"]);
            }
        }

        /// <summary>The foods of gate-catalog.tsv: the curated foods and the pending USDA foods a link may target, with their USDA category, admission reason and nutrient rows.</summary>
        public static IEnumerable<GateFood> GateFoods(string goldenDir) =>
            GoldenTsv.Read(Path.Combine(goldenDir, "gate-catalog.tsv")).Rows.Select(r => new GateFood(
                long.Parse(r.Id),
                r["name"],
                r.Value("description"),
                r.Value("dataType"),
                r["status"] == "curated" ? (long)CurationStatusEnum.Curated : (long)CurationStatusEnum.PendingCuration,
                int.TryParse(r["categoryId"], out var category) ? category : null,
                string.IsNullOrEmpty(r.Value("admission")) ? null : r["admission"],
                int.TryParse(r["nutrients"], out var rows) ? rows : null,
                FdcId: r.Value("fdcId")));

        /// <summary>The gate's catalog over gate-catalog.tsv, with the name matcher and staples of usda-catalog.tsv.</summary>
        public static GateCatalog GoldenGateCatalog(string goldenDir)
        {
            var (matcher, staples) = UsdaMatcher(goldenDir);
            return GateCatalog.Build(GateFoods(goldenDir), matcher, staples: staples);
        }

        public static IEnumerable<VerifyCase> VerifyCases(string goldenDir) =>
            GoldenTsv.Read(Path.Combine(goldenDir, "link-pairs.tsv")).Rows
                .Where(r => r["verdict"] == "ok" || Bad.Contains(r["verdict"]))
                .Select(r => new VerifyCase("verify:" + r.Id, IngredientNameNormalizer.Normalize(r["wording"]) is { Length: > 0 } name ? name : r["wording"], r["description"], r["verdict"] == "ok", r["judgedBy"]));

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
