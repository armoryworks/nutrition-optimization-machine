using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Nom.Import.Golden;
using Nom.Orch.Services.Support;
using Xunit;
using Xunit.Abstractions;

namespace Nom.Api.Tests.Golden
{
    /// <summary>
    /// The lines the staging checkpoints judged (line-holdout.tsv), split once by a fixed hash of the row id
    /// into a rules half and a held-out half. The rules half must read as judged, like any golden file. The
    /// held-out half is never required to pass: its errors per class (an amount read wrong, or an amount the
    /// line or its method states left empty) are measured and must equal line-holdout.json, so a change of the
    /// parser or the method finder shows its effect on lines nobody wrote a rule for, in the diff.
    /// </summary>
    [Trait("Category", "Golden")]
    public class GoldenLineHoldoutTests
    {
        private const string File = "line-holdout.tsv";
        private const string Record = "line-holdout.json";
        private const string Rules = "rules";
        private const string HeldOut = "held-out";

        private readonly ITestOutputHelper _output;

        public GoldenLineHoldoutTests(ITestOutputHelper output) => _output = output;

        /// <summary>The figures of one class of the held-out half.</summary>
        public sealed record HeldOutFigures(int Rows, int Wrong, int Lost, int UnseenRows, int UnseenWrong, int UnseenLost);

        private static string[] Cells(string value) => value == GoldenTsv.NoAnswer ? Array.Empty<string>() : value.Split(" ¦ ");

        private static string Read(GoldenRow row)
        {
            var found = row["class"] == "line"
                ? IngredientLineParser.Parse(row["raw"])
                : MethodAmountFinder.Find(row["raw"], Cells(row["steps"]), Cells(row["others"]), row["pieces"] != "no");
            return found == null ? GoldenTsv.NoAnswer : $"{GoldenSet.Number(found.Quantity)} {found.MeasurementName}";
        }

        private static Dictionary<string, string> Current(GoldenRow row) => new()
        {
            ["expect"] = Read(row),
            ["name"] = IngredientNameNormalizer.Normalize(row["raw"]),
        };

        public static string Half(string id) => SHA256.HashData(Encoding.UTF8.GetBytes(id))[0] % 2 == 0 ? Rules : HeldOut;

        [Fact]
        public void The_rules_half_reads_as_judged()
        {
            var report = GoldenSet.Run(File, "status", row => row["half"] == Rules ? GoldenSet.Compare(row, Current(row)) : null);
            _output.WriteLine(report.Summary);
            GoldenSet.Assert(report);
        }

        [Fact]
        public void Each_row_lies_in_the_half_the_hash_of_its_id_gives()
        {
            var table = GoldenSet.Read(File);

            table.Rows.Where(r => r["half"] != Half(r.Id)).Select(r => r.Id).Should().BeEmpty("the split is fixed once by the hash of the id, never chosen by hand");
            table.Rows.Select(r => r["class"]).Should().OnlyContain(c => c == "line" || c == "method");
        }

        [Fact]
        public void The_held_out_half_scores_as_recorded()
        {
            var measured = new SortedDictionary<string, HeldOutFigures>(StringComparer.Ordinal);
            var misses = new List<string>();
            foreach (var row in GoldenSet.Read(File).Rows.Where(r => r["half"] == HeldOut && r["status"] != GoldenTsv.NoAnswer))
            {
                var outcome = GoldenSet.Compare(row, Current(row));
                var unseen = row["finding"] == GoldenTsv.NoAnswer;
                var lost = !outcome.Pass && row["expect"] != GoldenTsv.NoAnswer && Read(row) == GoldenTsv.NoAnswer;
                var wrong = !outcome.Pass && !lost;
                if (!outcome.Pass) misses.Add($"{row.Id} [{row["class"]}/{row["stratum"]}, {(unseen ? "unseen" : row["finding"])}]: expected {outcome.Expected}, got {outcome.Actual}{(lost ? " (left empty)" : string.Empty)}");
                var was = measured.GetValueOrDefault(row["class"]) ?? new HeldOutFigures(0, 0, 0, 0, 0, 0);
                measured[row["class"]] = was with
                {
                    Rows = was.Rows + 1,
                    Wrong = was.Wrong + (wrong ? 1 : 0),
                    Lost = was.Lost + (lost ? 1 : 0),
                    UnseenRows = was.UnseenRows + (unseen ? 1 : 0),
                    UnseenWrong = was.UnseenWrong + (unseen && wrong ? 1 : 0),
                    UnseenLost = was.UnseenLost + (unseen && lost ? 1 : 0),
                };
            }

            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
            var report = JsonSerializer.Serialize(measured, options);
            _output.WriteLine(report);
            foreach (var miss in misses) _output.WriteLine(miss);

            using var recorded = JsonDocument.Parse(System.IO.File.ReadAllText(Path.Combine(GoldenSet.Folder, Record)));
            var expected = JsonSerializer.Deserialize<SortedDictionary<string, HeldOutFigures>>(recorded.RootElement.GetProperty("heldOut").GetRawText(), options);
            measured.Should().BeEquivalentTo(expected,
                $"the held-out figures are recorded by hand in {Record} whenever a rule change moves them; now:\n{report}\n{string.Join("\n", misses)}");
        }
    }
}
