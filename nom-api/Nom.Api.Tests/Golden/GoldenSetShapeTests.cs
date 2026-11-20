using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using FluentAssertions;
using Nom.Import.Golden;
using Xunit;

namespace Nom.Api.Tests.Golden
{
    /// <summary>
    /// The golden files themselves: each holds at least its floor of rows, ids are unique, no row
    /// is left new or review, known rows stay within baseline.json, and every food a link row
    /// names is in the frozen catalog.
    /// </summary>
    [Trait("Category", "Golden")]
    public class GoldenSetShapeTests
    {
        public static readonly TheoryData<string, int, string[]> Files = new()
        {
            { "lines.tsv", 40, new[] { "parse", "name" } },
            { "wordings.tsv", 4, new[] { "status" } },
            { "line-kinds.tsv", 9, new[] { "status" } },
            { "veto.tsv", 10, new[] { "status" } },
            { "links.tsv", 700, new[] { "status", "stays" } },
            { "link-pairs.tsv", 7000, new[] { "status" } },
            { "compound.tsv", 60, new[] { "status" } },
            { "unfold.tsv", 20, new[] { "status" } },
            { "piece-counts.tsv", 40, new[] { "status" } },
            { "prose.tsv", 30, new[] { "status" } },
            { "startup-fills.tsv", 9, new[] { "status" } },
            { "catalog-admission.tsv", 80, new[] { "status" } },
            { "line-labels.tsv", 150, Array.Empty<string>() },
            { "catalog.tsv", 6000, Array.Empty<string>() },
            { "usda-catalog.tsv", 7000, Array.Empty<string>() },
            { "measurements.tsv", 15, Array.Empty<string>() },
        };

        [Theory]
        [MemberData(nameof(Files))]
        public void Each_golden_file_holds_its_floor_of_unique_reviewed_rows(string file, int floor, string[] statusColumns)
        {
            var table = GoldenSet.Read(file);
            var baseline = GoldenSet.Baseline(GoldenSet.Folder);

            table.Rows.Count.Should().BeGreaterThanOrEqualTo(floor, $"{file} must keep at least {floor} rows");
            table.Rows.GroupBy(r => r.Id).Where(g => g.Count() > 1).Select(g => g.Key).Should().BeEmpty($"row ids in {file} are unique");
            foreach (var column in statusColumns)
            {
                table.Rows.Where(r => r[column] is GoldenSet.New or GoldenSet.Review).Select(r => r.Id)
                    .Should().BeEmpty($"{file} may not ship an unreviewed {column} row");
                table.Rows.Select(r => r[column]).Should().OnlyContain(s => s == GoldenSet.Ok || s == GoldenSet.Agent || s == GoldenSet.Known || s == GoldenTsv.NoAnswer,
                    $"{column} in {file} is ok, agent, known or {GoldenTsv.NoAnswer}");
                var known = table.Rows.Count(r => r[column] == GoldenSet.Known);
                known.Should().BeLessThanOrEqualTo(baseline.TryGetValue($"{file}:{column}", out var allowed) ? allowed : 0,
                    $"known rows of {file}:{column} stay within baseline.json");
            }
        }

        [Theory]
        [InlineData("link-pairs.tsv", "judgedBy", "status")]
        [InlineData("links.tsv", "source", "status")]
        [InlineData("links.tsv", "source", "stays")]
        [InlineData("piece-counts.tsv", "source", "status")]
        [InlineData("compound.tsv", "source", "status")]
        [InlineData("unfold.tsv", "source", "status")]
        [InlineData("lines.tsv", "source", "parse")]
        [InlineData("veto.tsv", "source", "status")]
        [InlineData("startup-fills.tsv", "source", "status")]
        [InlineData("catalog-admission.tsv", "source", "status")]
        public void No_row_judged_by_a_model_reviewer_is_marked_ok(string file, string judgeColumn, string statusColumn)
        {
            GoldenSet.Read(file).Rows.Where(r => GoldenSet.ByModel(r[judgeColumn]) && r[statusColumn] == GoldenSet.Ok).Select(r => r.Id)
                .Should().BeEmpty($"only a person sets {file}:{statusColumn} to ok; a row a model reviewer judged stays agent until then");
        }

        [Fact]
        public void The_recipes_set_holds_its_floor_of_reviewed_recipes()
        {
            var recipes = JsonNode.Parse(File.ReadAllText(Path.Combine(GoldenSet.Folder, "recipes.json")))!.AsArray().Select(r => r!.AsObject()).ToList();
            var baseline = GoldenSet.Baseline(GoldenSet.Folder);

            recipes.Count.Should().BeGreaterThanOrEqualTo(30);
            recipes.Select(r => r["id"]!.GetValue<long>()).Should().OnlyHaveUniqueItems();
            recipes.Select(r => r["status"]!.GetValue<string>()).Should().OnlyContain(s => s == GoldenSet.Ok || s == GoldenSet.Known);
            recipes.Count(r => r["status"]!.GetValue<string>() == GoldenSet.Known).Should().BeLessThanOrEqualTo(baseline.GetValueOrDefault("recipes.json:status"));
            recipes.Where(r => r["licence"]!.GetValue<string>() != "PublicDomain")
                .Should().OnlyContain(r => r["name"] == null && r["lines"]!.AsArray().All(l => l!["raw"] == null),
                    "a recipe that is not public domain is kept as numbers only, with no title and no raw line");
        }

        [Fact]
        public void Every_food_a_link_row_names_is_in_the_frozen_catalog()
        {
            var catalog = GoldenCatalog.Instance;
            var missing = GoldenSet.Read("links.tsv").Rows
                .Where(r => r["expect"] != "stay" && !catalog.Targets.ContainsKey(r["expect"][1..]))
                .Select(r => $"{r.Id}: {r["expect"]}");

            missing.Should().BeEmpty();
        }

        [Fact]
        public void Every_judged_link_pair_names_a_food_in_the_frozen_usda_catalog()
        {
            var foods = GoldenSet.Read("usda-catalog.tsv").Rows.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
            var missing = GoldenSet.Read("link-pairs.tsv").Rows
                .Where(r => r["rule"].Length > 1 && r["rule"] != GoldenTsv.NotJudged && !foods.Contains(r["rule"][1..]))
                .Select(r => r.Id);

            missing.Should().BeEmpty();
        }
    }
}
