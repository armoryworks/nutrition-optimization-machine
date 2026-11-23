using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// The committed per-class score of the link re-check (scores/link-recheck-classes.json, built
    /// into the API), written by <c>dotnet run --project nom-api/Nom.Import -- --recheck-classes --record</c>
    /// from the judged lines of link-recheck.tsv. The classes it finds at the bar are the classes
    /// that may move or take back a line by rule (<see cref="LinkRecheckRules.ProvenClasses"/>), so a
    /// class is proven by the data and never by a hand edit: the golden test re-scores the file and
    /// refuses a record that differs. A record scored under other rules (another version or decision
    /// signature) proves nothing until it is recorded again.
    /// </summary>
    public static class LinkRecheckClassRecord
    {
        public const string RecordResource = "Nom.Orch.Golden.link-recheck-classes.json";
        public const string FileName = "link-recheck-classes.json";

        /// <summary>One class's figures over the judged rows when the record was written.</summary>
        public sealed class ClassFigures
        {
            public string Class { get; set; } = string.Empty;
            public int Judged { get; set; }
            public int Right { get; set; }
            public int AgentJudged { get; set; }
            public int AgentRight { get; set; }
            public int RuleJudged { get; set; }
            public int RuleRight { get; set; }
            public bool MeetsBar { get; set; }
        }

        /// <summary>A per-class record as written by Nom.Import --recheck-classes --record.</summary>
        public sealed class Record
        {
            public string Version { get; set; } = string.Empty;

            /// <summary>The re-check's decision signature when it was scored (<see cref="LinkRecheckRules.DecisionSignature"/>).</summary>
            public string DecisionSignature { get; set; } = string.Empty;

            /// <summary>A hash of the judged rows it was scored on.</summary>
            public string CaseSet { get; set; } = string.Empty;
            public int Rows { get; set; }
            public double Bar { get; set; }
            public int Minimum { get; set; }
            public DateTime ScoredAtUtc { get; set; }
            public List<ClassFigures> Classes { get; set; } = new();

            /// <summary>The classes that met the bar: precision at least <see cref="Bar"/> over at least <see cref="Minimum"/> checkpoint-judged rows, with every rule-given row right.</summary>
            public List<string> Proven { get; set; } = new();
        }

        public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

        private const string Rescore = "score it with: dotnet run --project nom-api/Nom.Import -- --recheck-classes --record";

        public static Record? Embedded()
        {
            using var stream = typeof(LinkRecheckClassRecord).Assembly.GetManifestResourceStream(RecordResource);
            if (stream == null) return null;
            using var reader = new StreamReader(stream);
            return JsonSerializer.Deserialize<Record>(reader.ReadToEnd(), Json);
        }

        /// <summary>The classes a record proves under the rules given, and why it proves none; the problem is null when the record was scored under this version and decision signature, on the bar and minimum the rules hold.</summary>
        public static (IReadOnlySet<string> Proven, string? Problem) Evaluate(Record? record, string version, string decisionSignature)
        {
            var none = new HashSet<string>(StringComparer.Ordinal);
            if (record == null) return (none, $"the link re-check has no per-class record; {Rescore}");
            if (!string.Equals(record.Version, version, StringComparison.Ordinal) || !string.Equals(record.DecisionSignature, decisionSignature, StringComparison.Ordinal))
                return (none, $"the per-class record was scored under {record.Version} ({record.DecisionSignature}), the rules are {version} ({decisionSignature}); {Rescore}");
            if (record.Bar < LinkRecheckRules.ClassPrecisionBar || record.Minimum < LinkRecheckRules.ClassMinimumJudged)
                return (none, $"the per-class record was scored on a lower bar ({record.Bar:P0} over {record.Minimum}); {Rescore}");
            var proven = record.Classes.Where(c => c.MeetsBar && record.Proven.Contains(c.Class, StringComparer.Ordinal)).Select(c => c.Class);
            return (new HashSet<string>(proven, StringComparer.Ordinal), null);
        }
    }
}
