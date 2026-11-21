using System;
using System.IO;
using System.Text.Json;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// The committed golden score of the link auditor (scores/link-audit.json, built into the API)
    /// as the wording tier lane reads it at run time: the combined audit check's own score (the
    /// auditor's flag confirmed by reader B re-reading the pair blind) for the lane frame, and the
    /// reason no tier may go live when the record is missing, scored another auditor or another
    /// reader B than the roster names, or has no audit-check counts. The scorer lane that writes it is built with the wording readers.
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
            public DateTime ScoredAtUtc { get; set; }
            public int? AuditKnownBad { get; set; }
            public int? AuditFlaggedBad { get; set; }
            public int? AuditKnownGood { get; set; }
            public int? AuditFlaggedGood { get; set; }
        }

        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        public static Record? Embedded()
        {
            using var stream = typeof(WordingTierAuditRecord).Assembly.GetManifestResourceStream(RecordResource);
            if (stream == null) return null;
            using var reader = new StreamReader(stream);
            return JsonSerializer.Deserialize<Record>(reader.ReadToEnd(), Json);
        }

        /// <summary>The combined audit check's score and why no tier may go live on this record; the problem is null when the record scored this auditor with this reader B and carries its counts.</summary>
        public static (AuditorScore? Score, string? Problem) Evaluate(Record? record, string auditorModel, string checkModel)
        {
            const string rescore = "score it with: dotnet run --project nom-api/Nom.Import -- --golden-score --lane link-audit --host <model host> --model <auditor> --check-model <reader B> --record";
            if (record == null) return (null, $"the link auditor has no golden score record; {rescore}");
            if (record.AuditKnownBad is not { } bad || record.AuditFlaggedBad is not { } flaggedBad || record.AuditKnownGood is not { } good || record.AuditFlaggedGood is not { } flaggedGood)
                return (null, $"the link auditor's record has no audit-check counts; {rescore}");
            var score = new AuditorScore(bad, flaggedBad, good, flaggedGood, $"{Lane} {record.Model}, {record.ScoredAtUtc:yyyy-MM-dd}");
            if (!string.Equals(record.Model, auditorModel, StringComparison.OrdinalIgnoreCase))
                return (score, $"the link auditor was scored with {record.Model}, the lane asks {auditorModel}; {rescore}");
            if (string.IsNullOrWhiteSpace(record.CheckModel))
                return (score, $"the link auditor's record does not name the reader B that confirmed its flags; {rescore}");
            if (!string.Equals(record.CheckModel, checkModel, StringComparison.OrdinalIgnoreCase))
                return (score, $"the link audit check was scored with reader B {record.CheckModel}, the lane asks {checkModel}; {rescore}");
            return (score, null);
        }
    }
}
