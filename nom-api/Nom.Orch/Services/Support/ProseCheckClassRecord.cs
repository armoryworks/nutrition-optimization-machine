using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// The committed per-class score of the prose check's fact rules (scores/prose-check-classes.json,
    /// built into the API), written by <c>dotnet run --project nom-api/Nom.Import -- --prose-classes --record</c>
    /// from the judged rows of prose-check.tsv. The classes it finds at the bar are the fact rules
    /// that may fail a recipe (<see cref="ProseCheck.ProvenClasses"/>); every other fact rule only marks
    /// it unsure. A class is proven by the data and never by a hand edit: the golden test re-scores
    /// the rows and refuses a record that differs, and a record scored under another check version
    /// proves nothing until it is recorded again.
    /// </summary>
    public static class ProseCheckClassRecord
    {
        public const string FileName = "prose-check-classes.json";
        public const string RecordResource = ProseLaneGolden.RecordResourcePrefix + "prose-check-classes.json";

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
            public int Doubtful { get; set; }
            public bool MeetsBar { get; set; }
        }

        /// <summary>A per-class record as written by Nom.Import --prose-classes --record.</summary>
        public sealed class Record
        {
            /// <summary>The prose check version it was scored under (<see cref="ProseCheck.Version"/>).</summary>
            public int CheckVersion { get; set; }

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

        private const string Rescore = "score it with: dotnet run --project nom-api/Nom.Import -- --prose-classes --record";

        public static Record? Embedded()
        {
            using var stream = typeof(ProseCheckClassRecord).Assembly.GetManifestResourceStream(RecordResource);
            if (stream == null) return null;
            using var reader = new StreamReader(stream);
            return JsonSerializer.Deserialize<Record>(reader.ReadToEnd(), Json);
        }

        /// <summary>
        /// The fact classes a record proves under the check version given, and why it proves none or
        /// fewer than it lists; the problem is null when the record was scored under this version, on the
        /// bar and minimum the check holds, and every class it lists as proven holds the bar on its own
        /// figures. The record's own <see cref="ClassFigures.MeetsBar"/> and <see cref="Record.Proven"/>
        /// are never trusted alone: each listed class is decided again from its counts
        /// (<see cref="ProseCheck.ClassMeetsBar"/>), so a class raised by hand proves nothing.
        /// </summary>
        public static (IReadOnlySet<string> Proven, string? Problem) Evaluate(Record? record, int version)
        {
            var none = new HashSet<string>(StringComparer.Ordinal);
            if (record == null) return (none, $"the prose check has no per-class record; {Rescore}");
            if (record.CheckVersion != version)
                return (none, $"the per-class record was scored under prose check {record.CheckVersion}, the check is {version}; {Rescore}");
            if (record.Bar < ProseCheck.ClassPrecisionBar || record.Minimum < ProseCheck.ClassMinimumJudged)
                return (none, $"the per-class record was scored on a lower bar ({record.Bar:P0} over {record.Minimum}); {Rescore}");
            var proven = new HashSet<string>(StringComparer.Ordinal);
            var refused = new List<string>();
            foreach (var name in record.Proven.Distinct(StringComparer.Ordinal))
            {
                var figures = record.Classes.Where(c => string.Equals(c.Class, name, StringComparison.Ordinal)).ToList();
                if (ProseCheck.FactClasses.Contains(name) && figures.Count == 1 && figures[0].MeetsBar
                    && ProseCheck.ClassMeetsBar(figures[0].Judged, figures[0].Right, figures[0].AgentJudged, figures[0].AgentRight, figures[0].RuleJudged, figures[0].RuleRight))
                    proven.Add(name);
                else
                    refused.Add(name);
            }

            return refused.Count == 0
                ? (proven, null)
                : (proven, $"the per-class record lists {string.Join(", ", refused)} as proven, but {(refused.Count == 1 ? "its figures do" : "their figures do")} not hold {ProseCheck.ClassPrecisionBar:P0} over {ProseCheck.ClassMinimumJudged} checkpoint-judged rows with every rule-given row right; {Rescore}");
        }
    }
}
