using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using FluentAssertions;
using Nom.Import.Services;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services.Support;
using Xunit;
using Xunit.Abstractions;

namespace Nom.Api.Tests.Golden
{
    /// <summary>
    /// The corpus tiers of the container size lane against their golden sets. container-era-lines.tsv
    /// holds constructed sized lines of old and undated books; container-era-facts.tsv is what
    /// ops/container-era-facts.py, written apart from the C# code, mines from them, and the C# tiers
    /// must give the same facts row for row. container-eras.tsv holds unsized lines of old, undated
    /// and modern recipes with the grams and the tier (basis) the resolver must give them from those
    /// corpus sizes and the frozen USDA sizes of container-facts.tsv. Every row is rule-given; no
    /// model is involved, so the floor is every ok row.
    /// </summary>
    [Trait("Category", "Golden")]
    public class GoldenContainerEraTests
    {
        private readonly ITestOutputHelper _output;

        public GoldenContainerEraTests(ITestOutputHelper output) => _output = output;

        private static List<CorpusLine> CorpusLines() =>
            GoldenSet.Read("container-era-lines.tsv").Rows
                .Select(r => ContainerCorpusSizes.Read(r["book"] == "-" ? null : r["book"], r["era"], long.Parse(r["fdcId"], CultureInfo.InvariantCulture), r["line"],
                    r.Value("density") is { } d ? decimal.Parse(d, CultureInfo.InvariantCulture) : null, r.Id))
                .Where(l => l != null)
                .Select(l => l!)
                .ToList();

        [Fact]
        public void The_corpus_tiers_give_the_facts_the_independent_miner_gives()
        {
            var lines = CorpusLines();
            var mine = ContainerCorpusSizes.Facts(ContainerCorpusSizes.Book(lines).Concat(ContainerCorpusSizes.Era(lines)), id => id.ToString(CultureInfo.InvariantCulture)).ToList();
            var frozen = File.ReadAllLines(Path.Combine(GoldenSet.Folder, "container-era-facts.tsv")).Where(l => l.Length > 0 && !l.StartsWith('#')).ToList();
            mine.Should().Equal(frozen, "container-era-facts.tsv is rewritten with ops/container-era-facts.py whenever container-era-lines.tsv or the corpus rule changes");
        }

        [Fact]
        public void Each_unsized_line_of_an_old_or_undated_book_weighs_what_its_tier_gives()
        {
            var lines = CorpusLines();
            var eraOf = lines.GroupBy(l => l.Book).ToDictionary(g => g.Key, g => g.First().Era, StringComparer.Ordinal);
            var corpus = ContainerCorpusImportService.Propose(ContainerCorpusSizes.Book(lines).Concat(ContainerCorpusSizes.Era(lines)), eraOf);
            var facts = GoldenSet.Read("container-facts.tsv").Rows.Where(r => r["kind"] == "portions").ToDictionary(r => r.Id["fdc:".Length..]);
            var usda = UsdaProposals();
            var report = GoldenSet.Run("container-eras.tsv", "status", row =>
            {
                var food = long.Parse(row["fdcId"], CultureInfo.InvariantCulture);
                var fact = facts.GetValueOrDefault(row["fdcId"]);
                var candidates = usda.Concat(corpus).Where(p => p.IngredientId == food)
                    .Select(p => new ContainerSizeCandidate(p.Kind, p.Era, p.SizeWord, p.NetGrams, p.DrainedGrams, p.SourceRef, ContainerSizeStatus.Active, 0, p.Book));
                var weight = ContainerSizeResolver.Resolve(row["line"], row["era"], candidates, fact?["description"], null,
                    usdaCategoryId: fact != null ? int.Parse(fact["categoryId"], CultureInfo.InvariantCulture) : null, book: row["book"] == "-" ? null : row["book"]);
                var grams = weight.Grams is { } g ? Math.Round(g, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture) : GoldenSet.Cell(null);
                var expected = row["expectGrams"];
                var matches = expected == grams || (expected != "-" && weight.Grams is { } got && Math.Abs(got - decimal.Parse(expected, CultureInfo.InvariantCulture)) <= 1m);
                return GoldenSet.Compare(row, new Dictionary<string, string>
                {
                    ["expectGrams"] = matches ? expected : grams + (weight.Reason is { } r ? $" ({r})" : string.Empty),
                    ["expectBasis"] = weight.Basis ?? GoldenSet.Cell(null),
                });
            });
            _output.WriteLine(report.Summary);
            GoldenSet.Assert(report);
        }

        private static IReadOnlyList<ContainerSizeProposal> UsdaProposals()
        {
            var sizes = GoldenSet.Read("container-facts.tsv").Rows.Where(r => r["kind"] == "branded")
                .ToDictionary(r => r.Id["class:".Length..], r => ContainerModeRule.ParseSizes(r["facts"]));
            var modes = ContainerSizeImportService.Modes(sizes, ContainerClasses.All);
            var foods = GoldenSet.Read("container-facts.tsv").Rows.Where(r => r["kind"] == "portions").Select(r =>
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
            return ContainerSizeImportService.Propose(foods, modes, "2025-12-18", ContainerClasses.All, (_, _) => { });
        }
    }
}
