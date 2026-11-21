using System;
using System.IO;
using System.Text.Json;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// The committed golden score of the link auditor (scores/link-audit.json, built into the API)
    /// as the lanes read it at run time: the combined audit check's own score (the auditor's flag
    /// confirmed by reader B re-reading the pair blind) for the wording tiers and the link re-check,
    /// the auditor's score alone for the wording readers (whose decisions reader B made), and the
    /// reason no tier may go live when the record is missing, scored another prompt, another auditor
    /// or another reader B than the roster names, lacks the counts the lane corrects with, or is
    /// below its floor in model-lanes.json. The scorer lane that writes it is built with the wording readers.
    /// </summary>
    public static class WordingTierAuditRecord
    {
        public const string Lane = "link-audit";
        public const string RecordResource = "Nom.Orch.Golden.link-audit.json";

        /// <summary>A link-audit score record as written by Nom.Import --golden-score --record.</summary>
        public sealed class Record
        {
            public string Model { get; set; } = string.Empty;

            /// <summary>The reader B that confirmed the auditor's flags when the combined check was scored.</summary>
            public string? CheckModel { get; set; }

            /// <summary>The scorer's signature when it was scored (<see cref="WordingReaderRecords.ScorerSignature"/>).</summary>
            public string? Signature { get; set; }
            public DateTime ScoredAtUtc { get; set; }

            /// <summary>The share of golden pairs the combined check answered as judged, held to the lane's floor.</summary>
            public double? Score { get; set; }
            public int? AuditKnownBad { get; set; }
            public int? AuditFlaggedBad { get; set; }
            public int? AuditKnownGood { get; set; }
            public int? AuditFlaggedGood { get; set; }

            /// <summary>The same known-bad and known-good pairs as the auditor alone flagged them, before reader B.</summary>
            public int? AuditorKnownBad { get; set; }
            public int? AuditorFlaggedBad { get; set; }
            public int? AuditorKnownGood { get; set; }
            public int? AuditorFlaggedGood { get; set; }
        }

        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        private const string Rescore = "score it with: dotnet run --project nom-api/Nom.Import -- --golden-score --lane link-audit --host <model host> --model <auditor> --check-model <reader B> --record";

        public static Record? Embedded()
        {
            using var stream = typeof(WordingTierAuditRecord).Assembly.GetManifestResourceStream(RecordResource);
            if (stream == null) return null;
            using var reader = new StreamReader(stream);
            return JsonSerializer.Deserialize<Record>(reader.ReadToEnd(), Json);
        }

        /// <summary>The combined audit check's score and why no tier may go live on this record; the problem is null when the record scored the current prompt with this auditor and this reader B, carries its counts and meets the floor in model-lanes.json.</summary>
        public static (AuditorScore? Score, string? Problem) Evaluate(Record? record, string auditorModel, string checkModel) =>
            Evaluate(record, auditorModel, checkModel, WordingReaderRecords.EmbeddedFloor(Lane));

        public static (AuditorScore? Score, string? Problem) Evaluate(Record? record, string auditorModel, string checkModel, WordingReaderRecords.Floor? floor)
        {
            if (record == null) return (null, $"the link auditor has no golden score record; {Rescore}");
            if (record.AuditKnownBad is not { } bad || record.AuditFlaggedBad is not { } flaggedBad || record.AuditKnownGood is not { } good || record.AuditFlaggedGood is not { } flaggedGood)
                return (null, $"the link auditor's record has no audit-check counts; {Rescore}");
            var score = new AuditorScore(bad, flaggedBad, good, flaggedGood, $"{Lane} {record.Model}, {record.ScoredAtUtc:yyyy-MM-dd}");
            if (Stale(record, auditorModel, floor) is { } problem) return (score, problem);
            if (string.IsNullOrWhiteSpace(record.CheckModel))
                return (score, $"the link auditor's record does not name the reader B that confirmed its flags; {Rescore}");
            if (!string.Equals(record.CheckModel, checkModel, StringComparison.OrdinalIgnoreCase))
                return (score, $"the link audit check was scored with reader B {record.CheckModel}, the lane asks {checkModel}; {Rescore}");
            return (score, null);
        }

        /// <summary>The auditor's score alone, for a lane whose decisions reader B made, and why no tier may go live on this record; the problem is null when the record scored the current prompt with this auditor, carries the auditor's own counts and meets the floor.</summary>
        public static (AuditorScore? Score, string? Problem) EvaluateAlone(Record? record, string auditorModel) =>
            EvaluateAlone(record, auditorModel, WordingReaderRecords.EmbeddedFloor(Lane));

        public static (AuditorScore? Score, string? Problem) EvaluateAlone(Record? record, string auditorModel, WordingReaderRecords.Floor? floor)
        {
            if (record == null) return (null, $"the link auditor has no golden score record; {Rescore}");
            if (record.AuditorKnownBad is not { } bad || record.AuditorFlaggedBad is not { } flaggedBad || record.AuditorKnownGood is not { } good || record.AuditorFlaggedGood is not { } flaggedGood)
                return (null, $"the link auditor's record has no counts for the auditor alone; {Rescore}");
            var score = new AuditorScore(bad, flaggedBad, good, flaggedGood, $"{Lane} {record.Model} alone, {record.ScoredAtUtc:yyyy-MM-dd}");
            return (score, Stale(record, auditorModel, floor));
        }

        private static string? Stale(Record record, string auditorModel, WordingReaderRecords.Floor? floor)
        {
            var signature = WordingReaderRecords.ScorerSignature(Lane);
            if (record.Signature != signature)
                return $"the link auditor's prompt or scorer changed since it was scored ({record.Signature ?? "no signature"}, now {signature}); {Rescore}";
            if (!string.Equals(record.Model, auditorModel, StringComparison.OrdinalIgnoreCase))
                return $"the link auditor was scored with {record.Model}, the lane asks {auditorModel}; {Rescore}";
            if (floor != null && (record.Score ?? 0) < floor.MinScore)
                return $"the link audit check scored {record.Score ?? 0:P1}, below its floor {floor.MinScore:P1}";
            return null;
        }
    }
}
