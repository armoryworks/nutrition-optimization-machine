using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Nom.Orch.Services;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// The committed golden score of the line judge (scores/line-judge.json, built into the API with
    /// the floors of model-lanes.json) as the reprocess lane reads it at run time: the audit check's
    /// own score for the lane frame, and the reason the lane may not go live when the record is
    /// missing, scored another prompt or another model, or is below its floor. The release gate
    /// (GoldenModelLaneTests) checks the same record against the golden cases of the commit.
    /// </summary>
    public static class ReprocessJudgeRecord
    {
        public const string Lane = "line-judge";
        public const string RecordResource = "Nom.Orch.Golden.line-judge.json";
        public const string FloorsResource = "Nom.Orch.Golden.model-lanes.json";

        /// <summary>A line-judge score record as written by Nom.Import --golden-score --record.</summary>
        public sealed class Record
        {
            public string Model { get; set; } = string.Empty;
            public string Signature { get; set; } = string.Empty;
            public DateTime ScoredAtUtc { get; set; }
            public int Cases { get; set; }
            public double Score { get; set; }
            public double FalseAcceptRate { get; set; }
            public int? AuditKnownBad { get; set; }
            public int? AuditFlaggedBad { get; set; }
            public int? AuditKnownGood { get; set; }
            public int? AuditFlaggedGood { get; set; }
        }

        /// <summary>The floor of the line-judge lane in model-lanes.json.</summary>
        public sealed class Floor
        {
            public double MinScore { get; set; }
            public double? MaxFalseAcceptRate { get; set; }
        }

        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        public static Record? Embedded() => Read<Record>(RecordResource);

        public static Floor? EmbeddedFloor()
        {
            using var stream = typeof(ReprocessJudgeRecord).Assembly.GetManifestResourceStream(FloorsResource);
            if (stream == null) return null;
            using var doc = JsonDocument.Parse(stream);
            return doc.RootElement.TryGetProperty(Lane, out var floor) ? floor.Deserialize<Floor>(Json) : null;
        }

        private static T? Read<T>(string resource) where T : class
        {
            using var stream = typeof(ReprocessJudgeRecord).Assembly.GetManifestResourceStream(resource);
            if (stream == null) return null;
            using var reader = new StreamReader(stream);
            return JsonSerializer.Deserialize<T>(reader.ReadToEnd(), Json);
        }

        /// <summary>
        /// The combined audit check's score (the judge's "worse" confirmed by the rule audit) and why
        /// the lane may not go live on this record; the problem is null when the record scored this
        /// prompt with this model and meets its floor.
        /// </summary>
        public static (AuditorScore? Score, string? Problem) Evaluate(Record? record, Floor? floor, string judgeModel)
        {
            const string rescore = "score it with: dotnet run --project nom-api/Nom.Import -- --golden-score --lane line-judge --host <model host> --model <judge model> --record";
            if (record == null) return (null, $"the line judge has no golden score record; {rescore}");
            AuditorScore? score = record.AuditKnownBad is { } bad && record.AuditFlaggedBad is { } flaggedBad && record.AuditKnownGood is { } good && record.AuditFlaggedGood is { } flaggedGood
                ? new AuditorScore(bad, flaggedBad, good, flaggedGood, $"{Lane} {record.Model}, {record.ScoredAtUtc:yyyy-MM-dd}")
                : null;
            if (record.Signature != OllamaLineJudge.Signature)
                return (score, $"the line judge's prompt changed since it was scored ({record.Signature}, now {OllamaLineJudge.Signature}); {rescore}");
            if (!string.Equals(record.Model, judgeModel, StringComparison.OrdinalIgnoreCase))
                return (score, $"the line judge was scored with {record.Model}, the lane asks {judgeModel}; {rescore}");
            if (floor != null && record.Score < floor.MinScore)
                return (score, $"the line judge scored {record.Score:P1}, below its floor {floor.MinScore:P1}");
            if (floor?.MaxFalseAcceptRate is { } ceiling && record.FalseAcceptRate > ceiling)
                return (score, $"the line judge called {record.FalseAcceptRate:P1} of known-worse rows better, above its ceiling {ceiling:P1}");
            if (score == null) return (null, $"the line judge's record has no audit-check counts; {rescore}");
            return (score, null);
        }
    }
}
