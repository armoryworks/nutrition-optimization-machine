using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// The committed golden scores of the reader lanes (scores/wording-read.json,
    /// scores/wording-lexicon.json, scores/alcohol-reading.json, built into the API with the floors of
    /// model-lanes.json) as the wording reader lane reads them at run time: the reason a tier may not
    /// go live when its record is missing, scored another prompt or rule set, another reader A or
    /// another reader B than the roster names, or is below its floor. A record is never made at run
    /// time: an operator scores the lane with <c>--golden-score --record</c> and commits it, and the
    /// release gate (GoldenModelLaneTests) checks it against the golden cases of the commit.
    /// </summary>
    public static class WordingReaderRecords
    {
        public const string ReadLane = "wording-read";
        public const string LexiconLane = "wording-lexicon";
        public const string AlcoholLane = "alcohol-reading";
        public const string AuditLane = WordingTierAuditRecord.Lane;
        public const string FloorsResource = ReprocessJudgeRecord.FloorsResource;

        /// <summary>The scored lane whose record a frame tier needs.</summary>
        public static readonly IReadOnlyDictionary<string, string> LaneOfTier = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [WordingReaders.Read] = ReadLane,
            [WordingReaders.Lexicon] = LexiconLane,
            [WordingReaders.Alcohol] = AlcoholLane,
        };

        /// <summary>A reader lane's score record as written by Nom.Import --golden-score --record.</summary>
        public sealed class Record
        {
            public string Model { get; set; } = string.Empty;
            public string? CheckModel { get; set; }
            public string Signature { get; set; } = string.Empty;
            public DateTime ScoredAtUtc { get; set; }
            public int Cases { get; set; }
            public double Score { get; set; }
            public double FalseAcceptRate { get; set; }
            public double? Precision { get; set; }
        }

        /// <summary>A reader lane's floor in model-lanes.json.</summary>
        public sealed class Floor
        {
            public double MinScore { get; set; }
            public double? MaxFalseAcceptRate { get; set; }
            public double? MinPrecision { get; set; }
        }

        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        public static string Resource(string lane) => $"Nom.Orch.Golden.{lane}.json";

        public static Record? Embedded(string lane)
        {
            using var stream = typeof(WordingReaderRecords).Assembly.GetManifestResourceStream(Resource(lane));
            if (stream == null) return null;
            using var reader = new StreamReader(stream);
            return JsonSerializer.Deserialize<Record>(reader.ReadToEnd(), Json);
        }

        public static Floor? EmbeddedFloor(string lane)
        {
            using var stream = typeof(WordingReaderRecords).Assembly.GetManifestResourceStream(FloorsResource);
            if (stream == null) return null;
            using var doc = JsonDocument.Parse(stream);
            return doc.RootElement.TryGetProperty(lane, out var floor) ? floor.Deserialize<Floor>(Json) : null;
        }

        /// <summary>
        /// The signature a reader lane's record must carry: the readers' prompts and request options,
        /// and the rules the scorer runs (the readers' rules with the gate's and the alcohol rules'
        /// signatures for wording-read and wording-lexicon; the fixed readings for alcohol-reading);
        /// for link-audit the same-food judge's prompt read as the combined audit check.
        /// </summary>
        public static string ScorerSignature(string lane) => lane switch
        {
            ReadLane => WordingTiers.Hash(string.Join('|', ReadLane, OllamaWordingReader.Signature, WordingReaders.Signature)),
            LexiconLane => WordingTiers.Hash(string.Join('|', LexiconLane, OllamaWordingReader.Signature, WordingReaders.Signature)),
            AlcoholLane => WordingTiers.Hash(string.Join('|', AlcoholLane, OllamaWordingReader.Signature, AlcoholFixedReading.SignatureSource())),
            AuditLane => WordingTiers.Hash(string.Join('|', AuditLane, OllamaIngredientLinkVerifier.Signature, "auditor flag confirmed by reader B blind")),
            _ => throw new ArgumentException($"No reader scorer for lane '{lane}'.", nameof(lane)),
        };

        /// <summary>
        /// Why a tier may not go live on this record, or null when the record scored this prompt and
        /// rule set with this reader A and this reader B and meets its floor.
        /// </summary>
        public static string? Evaluate(string lane, Record? record, Floor? floor, string readerA, string readerB)
        {
            var rescore = $"score it with: dotnet run --project nom-api/Nom.Import -- --golden-score --lane {lane} --host <model host> --model <reader A> --check-model <reader B> --record";
            if (record == null) return $"the {lane} lane has no golden score record; {rescore}";
            var signature = ScorerSignature(lane);
            if (record.Signature != signature) return $"the {lane} prompt or rules changed since it was scored ({record.Signature}, now {signature}); {rescore}";
            if (!string.Equals(record.Model, readerA, StringComparison.OrdinalIgnoreCase)) return $"{lane} was scored with reader A {record.Model}, the lane asks {readerA}; {rescore}";
            if (string.IsNullOrWhiteSpace(record.CheckModel)) return $"the {lane} record does not name its reader B; {rescore}";
            if (!string.Equals(record.CheckModel, readerB, StringComparison.OrdinalIgnoreCase)) return $"{lane} was scored with reader B {record.CheckModel}, the lane asks {readerB}; {rescore}";
            if (floor != null && record.Score < floor.MinScore) return $"{lane} scored {record.Score:P1}, below its floor {floor.MinScore:P1}";
            if (floor?.MaxFalseAcceptRate is { } ceiling && record.FalseAcceptRate > ceiling) return $"{lane} accepted {record.FalseAcceptRate:P1} of judged-wrong cases, above its ceiling {ceiling:P1}";
            if (floor?.MinPrecision is { } precision && (record.Precision ?? 0) < precision) return $"{lane} wrote {record.Precision ?? 0:P1} right, below its floor {precision:P1}";
            return null;
        }
    }
}
