using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// One row of prose-judge.tsv: a recipe (constructed in our own words, or public domain), its
    /// ingredient lines and method, a rewrite of it, and whether that rewrite is faithful or carries
    /// a planted fault (<see cref="Fault"/>). <see cref="Check2"/> is what prose check v2 says of the
    /// rewrite: a fault it passes is one only the judge can catch.
    /// </summary>
    public sealed record ProseJudgeCase(
        string Id,
        string Recipe,
        string Title,
        IReadOnlyList<string> Lines,
        IReadOnlyList<string> Method,
        IReadOnlyList<string> Rewrite,
        string RewriteDescription,
        bool Faithful,
        string Fault,
        string Check2,
        string Source,
        string Status)
    {
        public ProseLaneInput Input(ProseStockIndex stock) => new(Title, new RewriteFidelity.SourceText(string.Empty, Method, Title), Lines, stock);

        public RewriteFidelity.SourceText RewriteText => new(RewriteDescription, Rewrite, Title);
    }

    /// <summary>A score record of a prose lane as the release gate commits it under the golden folder's <c>scores/</c>.</summary>
    public sealed record ProseLaneRecord(
        string Lane,
        string Model,
        string Signature,
        DateTime ScoredAtUtc,
        string CaseSet,
        int Cases,
        int Passed,
        double Score,
        int FalseAccepts,
        int FalseAcceptCases,
        double FalseAcceptRate);

    /// <summary>The floor of a prose lane from model-lanes.json.</summary>
    public sealed record ProseLaneFloor(double MinScore, double? MaxFalseAcceptRate);

    /// <summary>
    /// The golden side of the clean-room prose lane at run time: the cases of prose-judge.tsv (the
    /// judge's golden set and the lane's canaries) and the score records of the lanes
    /// <see cref="RewriteLane"/>, <see cref="JudgeLane"/> and <see cref="AuditLane"/> with their
    /// floors, all compiled into Nom.Orch from the golden folder. The lane may write only while
    /// the writer's and the judge's records are current for this build and its models and meet
    /// their floors (<see cref="GoldenProblem"/>), and its audit corrects for the audit check's
    /// own score (<see cref="AuditorScore"/>). A record is never made at run time: an operator
    /// scores the lane with <c>Nom.Import --golden-score</c> and commits it.
    /// </summary>
    public sealed class ProseLaneGolden
    {
        public const string RewriteLane = "prose-rewrite";
        public const string JudgeLane = "prose-judge";
        public const string AuditLane = "prose-audit";
        public const string CasesFile = "prose-judge.tsv";
        public const string CasesResource = "Nom.Orch.Prose.prose-judge.tsv";
        public const string FloorsResource = "Nom.Orch.Golden.model-lanes.json";
        public const string RecordResourcePrefix = "Nom.Orch.Prose.scores.";

        private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

        private static readonly Lazy<ProseLaneGolden> Shared = new(() =>
        {
            var assembly = typeof(ProseLaneGolden).Assembly;
            string? Read(string name)
            {
                using var stream = assembly.GetManifestResourceStream(name);
                if (stream == null) return null;
                using var reader = new StreamReader(stream, Encoding.UTF8);
                return reader.ReadToEnd();
            }

            var cases = Read(CasesResource) ?? throw new InvalidOperationException($"{CasesResource} is not compiled into {assembly.GetName().Name}");
            var records = new[] { RewriteLane, JudgeLane, AuditLane }
                .Select(l => (Lane: l, Text: Read(RecordResourcePrefix + l + ".json")))
                .Where(r => r.Text != null)
                .ToDictionary(r => r.Lane, r => JsonSerializer.Deserialize<ProseLaneRecord>(r.Text!, Json)!, StringComparer.Ordinal);
            return new ProseLaneGolden(ParseCases(cases), records, ParseFloors(Read(FloorsResource)));
        });

        public ProseLaneGolden(IReadOnlyList<ProseJudgeCase> cases, IReadOnlyDictionary<string, ProseLaneRecord> records, IReadOnlyDictionary<string, ProseLaneFloor> floors)
        {
            Cases = cases;
            Records = records;
            Floors = floors;
        }

        /// <summary>The cases and records compiled into this build.</summary>
        public static ProseLaneGolden Default => Shared.Value;

        public IReadOnlyList<ProseJudgeCase> Cases { get; }

        public IReadOnlyDictionary<string, ProseLaneRecord> Records { get; }

        public IReadOnlyDictionary<string, ProseLaneFloor> Floors { get; }

        /// <summary>The prompt signature of a prose lane.</summary>
        public static string Signature(string lane) => lane switch
        {
            RewriteLane => ProseLanePrompts.RewordSignature + "+" + ProseLanePrompts.WriterSignature + "+" + ProseLanePrompts.JudgeSignature,
            JudgeLane => ProseLanePrompts.JudgeSignature,
            AuditLane => ProseLanePrompts.AuditSignature,
            _ => throw new ArgumentException($"{lane} is not a prose lane.", nameof(lane)),
        };

        /// <summary>
        /// The case ids a prose lane is scored on: the judge and the audit read every row; the
        /// rewrite lane rewords the whole method of each recipe of the file once, as it rewords every
        /// recipe it takes, and puts each planted fault through its facts gate.
        /// </summary>
        public static IReadOnlyList<string> CaseIds(string lane, IReadOnlyList<ProseJudgeCase> cases) => lane switch
        {
            RewriteLane => cases.Select(c => c.Recipe).Distinct(StringComparer.Ordinal).Select(r => $"{lane}:recipe:{r}")
                .Concat(cases.Where(c => !c.Faithful).Select(c => $"{lane}:{c.Id}")).ToList(),
            _ => cases.Select(c => $"{lane}:{c.Id}").ToList(),
        };

        public static string CaseSet(IEnumerable<string> ids) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', ids.OrderBy(id => id, StringComparer.Ordinal)))))[..16].ToLowerInvariant();

        /// <summary>Why a lane's record does not vouch for this build run with <paramref name="model"/>; null when it does.</summary>
        public string? RecordProblem(string lane, string model)
        {
            if (!Records.TryGetValue(lane, out var record)) return $"{lane} has no committed golden score record";
            if (record.Signature != Signature(lane)) return $"{lane} was scored under signature {record.Signature}, the lane now runs {Signature(lane)}";
            if (!string.Equals(record.Model, model, StringComparison.OrdinalIgnoreCase)) return $"{lane} was scored with {record.Model}, the lane runs {model}";
            if (record.CaseSet != CaseSet(CaseIds(lane, Cases))) return $"the cases of {CasesFile} changed since {lane} was scored";
            if (Floors.TryGetValue(lane, out var floor))
            {
                if (record.Score < floor.MinScore) return $"{lane} scored {record.Score:P1}, below its floor {floor.MinScore:P1}";
                if (floor.MaxFalseAcceptRate is { } max && record.FalseAcceptRate > max) return $"{lane} accepted {record.FalseAcceptRate:P1} of its planted faults, above its ceiling {max:P1}";
            }

            return null;
        }

        /// <summary>Why the lane may not qualify or stay live on its golden records: the writer's (with the judge) and the judge's; null when both vouch for this build and these models.</summary>
        public string? GoldenProblem(ProseLaneModels models)
        {
            var problems = new[] { RecordProblem(RewriteLane, models.Writer), RecordProblem(JudgeLane, models.Judge) }.OfType<string>().ToList();
            return problems.Count == 0 ? null : string.Join("; ", problems);
        }

        /// <summary>
        /// The audit check's score on the golden rows (the auditor's flag confirmed by its own blind re-read):
        /// known-bad rows are the planted faults, known-good the faithful rewrites; null while the
        /// audit lane has no current record for the auditor.
        /// </summary>
        public AuditorScore? AuditorScore(ProseLaneModels models)
        {
            if (RecordProblem(AuditLane, models.Auditor) != null) return null;
            var record = Records[AuditLane];
            var knownBad = record.FalseAcceptCases;
            var flaggedBad = knownBad - record.FalseAccepts;
            var knownGood = record.Cases - knownBad;
            var flaggedGood = knownGood - (record.Passed - flaggedBad);
            return new AuditorScore(knownBad, flaggedBad, knownGood, Math.Max(0, flaggedGood), $"{AuditLane} {record.Model} {record.ScoredAtUtc:yyyy-MM-dd}");
        }

        public static IReadOnlyList<ProseJudgeCase> ParseCases(string tsv)
        {
            var lines = tsv.Replace("\r\n", "\n").Split('\n').Where(l => l.Length > 0 && !l.StartsWith('#')).ToList();
            if (lines.Count == 0) return Array.Empty<ProseJudgeCase>();
            var header = lines[0].Split('\t');
            int Col(string name) => Array.IndexOf(header, name) is var i and >= 0 ? i : throw new InvalidOperationException($"{CasesFile} has no column {name}");
            var columns = new[] { "id", "recipe", "title", "lines", "method", "rewrite", "rewriteDescription", "expect", "fault", "check2", "source", "status" }.ToDictionary(c => c, Col);
            var cases = new List<ProseJudgeCase>();
            foreach (var line in lines.Skip(1))
            {
                var cells = line.Split('\t');
                string Cell(string name) => columns[name] < cells.Length ? cells[columns[name]] : string.Empty;
                string Value(string name) => Cell(name) is "-" ? string.Empty : Cell(name);
                IReadOnlyList<string> List(string name) => Value(name).Length == 0 ? Array.Empty<string>() : Value(name).Split(" ¦ ");
                cases.Add(new ProseJudgeCase(Cell("id"), Cell("recipe"), Value("title"), List("lines"), List("method"), List("rewrite"),
                    Value("rewriteDescription"), Cell("expect") == "faithful", Value("fault"), Cell("check2"), Cell("source"), Cell("status")));
            }

            return cases;
        }

        public static IReadOnlyDictionary<string, ProseLaneFloor> ParseFloors(string? json)
        {
            var floors = new Dictionary<string, ProseLaneFloor>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(json)) return floors;
            using var document = JsonDocument.Parse(json);
            foreach (var lane in document.RootElement.EnumerateObject())
            {
                var min = lane.Value.TryGetProperty("minScore", out var m) && m.ValueKind == JsonValueKind.Number ? m.GetDouble() : 0;
                double? max = lane.Value.TryGetProperty("maxFalseAcceptRate", out var x) && x.ValueKind == JsonValueKind.Number ? x.GetDouble() : null;
                floors[lane.Name] = new ProseLaneFloor(min, max);
            }

            return floors;
        }
    }
}
