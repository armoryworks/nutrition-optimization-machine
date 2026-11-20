using System;
using System.Collections.Generic;
using System.Linq;
using Nom.Orch.Services.Support;
using Xunit;
using Xunit.Abstractions;

namespace Nom.Api.Tests.Golden
{
    /// <summary>
    /// The reviewed lines of compound.tsv against the compound-line rules, with the vocabulary
    /// built from the frozen catalog, the aliases of compound-vocabulary.tsv and the ingredient
    /// names that occur in the golden lines. And the reviewed folded lines of unfold.tsv against
    /// the reading of a joined line back into its source lines.
    /// </summary>
    [Trait("Category", "Golden")]
    public class GoldenCompoundTests
    {
        private readonly ITestOutputHelper _output;

        public GoldenCompoundTests(ITestOutputHelper output) => _output = output;

        private static CompoundVocabulary Vocabulary()
        {
            var catalog = GoldenCatalog.Instance;
            var extra = GoldenSet.Read("compound-vocabulary.tsv").Rows;
            return CompoundVocabulary.Build(
                catalog.Foods.Select(f => new CompoundFood(f.Id, f.Name, f.HasNutrition, f.CountedInPieces)),
                extra.Where(r => r["kind"] == "alias").Select(r => new CompoundAlias(long.Parse(r.Id), r["name"], r.Value("context"))),
                catalog.Foods.Select(f => f.Name).Concat(extra.Where(r => r["kind"] == "known").Select(r => r["name"])));
        }

        [Fact]
        public void Each_reviewed_compound_line_is_split_picked_or_left_as_reviewed()
        {
            var vocabulary = Vocabulary();
            var names = GoldenCatalog.Instance.Names;
            var report = GoldenSet.Run("compound.tsv", "status", row =>
            {
                var plan = CompoundLineSplitter.Plan(row["raw"], vocabulary);
                return GoldenSet.Compare(row, new Dictionary<string, string>
                {
                    ["kind"] = plan == null ? "-" : plan.Kind.ToString(),
                    ["rule"] = plan?.Rule ?? (plan == null ? "-" : "leave: " + plan.LeaveReason),
                    ["parts"] = plan == null || plan.Parts.Count == 0 ? "-" : string.Join('|', plan.Parts.Select(p => names.TryGetValue(p.FoodId, out var n) ? n : $"#{p.FoodId}")),
                });
            });
            _output.WriteLine(report.Summary);
            GoldenSet.Assert(report);
        }

        [Fact]
        public void Each_reviewed_folded_line_reads_back_into_its_source_lines()
        {
            var report = GoldenSet.Run("unfold.tsv", "status", row =>
            {
                IReadOnlySet<string>? sources = row["sources"] switch
                {
                    "-" => null,
                    "{}" => new HashSet<string>(StringComparer.Ordinal),
                    var list => list.Split(" ¦ ").ToHashSet(StringComparer.Ordinal),
                };
                var segments = FoldedLine.Segments(row["raw"], sources);
                return GoldenSet.Compare(row, new Dictionary<string, string> { ["segments"] = segments == null ? "-" : segments.Count.ToString() });
            });
            _output.WriteLine(report.Summary);
            GoldenSet.Assert(report);
        }
    }
}
