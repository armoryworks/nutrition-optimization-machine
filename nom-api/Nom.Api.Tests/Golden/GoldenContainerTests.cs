using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FluentAssertions;
using Nom.Import.Services;
using Nom.Orch.Services.Support;
using Xunit;
using Xunit.Abstractions;

namespace Nom.Api.Tests.Golden
{
    /// <summary>
    /// The container size lane against its golden sets: containers.tsv (a container line, its era,
    /// its food and the grams it weighs, or "-" where it stays uncounted), package-weights.tsv
    /// (FDC branded package_weight strings) and the cited can table. The sizes come from
    /// container-facts.tsv, frozen from the local USDA files by ops/golden/container_facts.py: the
    /// branded package sizes of each class and the SR Legacy portions of each food, read the same
    /// way the import reads them. A hidden row asks what the lane would assume with the stated size
    /// hidden, and passes within 10 % of it, as the hold-out does. No model is involved, so the
    /// floor is every ok row.
    /// </summary>
    [Trait("Category", "Golden")]
    public class GoldenContainerTests
    {
        private readonly ITestOutputHelper _output;

        public GoldenContainerTests(ITestOutputHelper output) => _output = output;

        [Fact]
        public void Each_container_line_weighs_what_its_sources_give()
        {
            var proposals = Proposals();
            var report = GoldenSet.Run("containers.tsv", "status", row =>
            {
                var food = row.Value("fdcId") is { } id ? long.Parse(id, CultureInfo.InvariantCulture) : 0L;
                var facts = food == 0 ? null : Facts().Single(f => f.FdcId == row["fdcId"]);
                decimal? density = row.Value("density") is { } d ? decimal.Parse(d, CultureInfo.InvariantCulture) : null;
                var candidates = proposals(density)
                    .Where(p => p.IngredientId == food)
                    .Select(p => new ContainerSizeCandidate(p.Kind, p.Era, p.SizeWord, p.NetGrams, p.DrainedGrams, p.SourceRef));
                var hide = row["hide"] == "yes";
                var weight = ContainerSizeResolver.Resolve(row["line"], row["era"], candidates, facts?.Description, density,
                    hideStatedSize: hide, netOnly: hide);
                var actual = weight.Grams is { } g ? Math.Round(g, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture) : GoldenSet.Cell(null);
                var expected = row["expectGrams"];
                var matches = expected == actual
                    || (expected != "-" && weight.Grams is { } got && decimal.Parse(expected, CultureInfo.InvariantCulture) is var want
                        && (hide ? ContainerSizeAgreement.Hit(got, want) : Math.Abs(got - want) <= 1m));
                return GoldenSet.Compare(row, new Dictionary<string, string> { ["expectGrams"] = matches ? expected : actual + (weight.Reason is { } r ? $" ({r})" : string.Empty) });
            });
            _output.WriteLine(report.Summary);
            GoldenSet.Assert(report);
        }

        [Fact]
        public void Each_branded_package_weight_reads_as_the_independent_reader_gives()
        {
            var report = GoldenSet.Run("package-weights.tsv", "status", row =>
            {
                var size = FdcPackageWeight.Parse(row["text"] == "(empty)" ? string.Empty : row["text"]);
                return GoldenSet.Compare(row, new Dictionary<string, string>
                {
                    ["expectGrams"] = GoldenSet.Number(size?.Grams),
                    ["expectMl"] = GoldenSet.Number(size?.Milliliters),
                });
            });
            _output.WriteLine(report.Summary);
            GoldenSet.Assert(report);
        }

        [Fact]
        public void The_can_table_and_the_class_map_ship_with_every_row_cited_and_well_formed()
        {
            CanSizeTable.Problems(CanSizeTable.Rows).Should().BeEmpty();
            ContainerClasses.Problems(ContainerClasses.All).Should().BeEmpty();
            CanSizeTable.Rows.Should().Contain(r => r.Era == ContainerEras.Historic).And.Contain(r => r.Era == ContainerEras.Modern).And.Contain(r => r.Era == ContainerEras.Any);
        }

        [Fact]
        public void Every_class_the_frozen_facts_hold_is_a_class_of_the_map_and_back()
        {
            var frozen = GoldenSet.Read("container-facts.tsv").Rows.Where(r => r["kind"] == "branded").Select(r => r.Id["class:".Length..]).ToList();
            frozen.Should().BeEquivalentTo(ContainerClasses.All.Select(c => c.Id), "container-facts.tsv is recaptured with ops/golden/container_facts.py whenever container-classes.tsv changes");
        }

        private static IReadOnlyList<ContainerSizeImportService.ProposalFood> Facts()
        {
            return GoldenSet.Read("container-facts.tsv").Rows.Where(r => r["kind"] == "portions").Select(r =>
            {
                var fdc = r.Id["fdc:".Length..];
                var portions = r["facts"] == "-" ? new List<UsdaPortion>() : r["facts"].Split(';').Select(p =>
                {
                    var f = p.Split('|');
                    return new UsdaPortion(decimal.Parse(f[1], CultureInfo.InvariantCulture), "9999", f[2] == "-" ? string.Empty : f[2], string.Empty,
                        decimal.Parse(f[3], CultureInfo.InvariantCulture), null, f[0]);
                }).ToList();
                return new ContainerSizeImportService.ProposalFood(long.Parse(fdc, CultureInfo.InvariantCulture), r["description"], fdc, r["description"],
                    int.Parse(r["categoryId"], CultureInfo.InvariantCulture), null, portions);
            }).ToList();
        }

        private static Func<decimal?, IReadOnlyList<Nom.Orch.Models.Curation.ContainerSizeProposal>> Proposals()
        {
            var sizes = GoldenSet.Read("container-facts.tsv").Rows.Where(r => r["kind"] == "branded")
                .ToDictionary(r => r.Id["class:".Length..], r => ContainerModeRule.ParseSizes(r["facts"]));
            var modes = ContainerSizeImportService.Modes(sizes, ContainerClasses.All);
            var foods = Facts();
            var cache = new Dictionary<decimal, IReadOnlyList<Nom.Orch.Models.Curation.ContainerSizeProposal>>();
            return density =>
            {
                var key = density ?? 0m;
                if (cache.TryGetValue(key, out var hit)) return hit;
                var withDensity = foods.Select(f => f with { GramsPerMilliliter = density }).ToList();
                return cache[key] = ContainerSizeImportService.Propose(withDensity, modes, "2025-12-18", ContainerClasses.All, (_, _) => { });
            };
        }
    }
}
