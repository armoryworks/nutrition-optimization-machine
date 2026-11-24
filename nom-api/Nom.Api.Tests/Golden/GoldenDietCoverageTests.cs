using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FluentAssertions;
using Nom.Import.Golden;
using Nom.Orch.Services.Support;
using Xunit;
using Xunit.Abstractions;

namespace Nom.Api.Tests.Golden
{
    /// <summary>
    /// What the severe types know, measured (staging checkpoint 5, Q5, Q7, Q19) against diet-coverage.tsv: per type at least
    /// 150 foods of the frozen USDA catalog across its categories, each read by its name and its USDA description as the
    /// gate reads them (<see cref="DietPatternRule.IsMatch"/>, <see cref="DietPatternRule.IsDescriptionMatch"/>), and
    /// constructed recipe lines read by their own words, under the type's default patterns. The bar: no row that names
    /// the restricted thing is missed, and under 5 % of each type's clear rows are flagged.
    /// </summary>
    [Trait("Category", "Golden")]
    public class GoldenDietCoverageTests
    {
        public const string File = "diet-coverage.tsv";
        public const decimal OverFlagBar = 0.05m;
        public const int FoodsPerType = 150;

        private readonly ITestOutputHelper _output;

        public GoldenDietCoverageTests(ITestOutputHelper output) => _output = output;

        /// <summary>Whether the type's default patterns flag the row: a food by its name or its USDA description, a line by its own words.</summary>
        public static bool Flags(GoldenRow row)
        {
            var type = row["restriction"];
            var rules = DefaultRestrictionCriteria.ByName[type].Patterns.Where(p => !DietCriteriaMatcher.IsWordPattern(p)).Select(p => DietPatternRule.For(p, type)).ToList();
            var description = row["description"] is "" or GoldenTsv.NoAnswer ? null : row["description"];
            return row["kind"] == "food"
                ? rules.Any(r => r.IsMatch(row["text"]) || r.IsDescriptionMatch(description))
                : rules.Any(r => r.IsMatch(row["text"]));
        }

        [Fact]
        public void No_severe_type_misses_a_row_that_names_what_it_restricts_and_each_over_flags_under_the_bar()
        {
            var rows = GoldenSet.Read(File).Rows.Where(r => r["status"] is GoldenSet.Ok or GoldenSet.Agent).ToList();
            var table = new StringBuilder("type\tfoods\tlines\thit rows\tmissed\tclear rows\tover-flagged\tshare\n");
            var missed = new List<string>();
            var over = new List<string>();
            var failing = new List<string>();
            foreach (var type in rows.GroupBy(r => r["restriction"]).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var hits = type.Where(r => r["expect"] == "hit").ToList();
                var clears = type.Where(r => r["expect"] == "clear").ToList();
                var lost = hits.Where(r => !Flags(r)).ToList();
                var wrong = clears.Where(Flags).ToList();
                var share = clears.Count == 0 ? 0m : (decimal)wrong.Count / clears.Count;
                table.Append(type.Key).Append('\t').Append(type.Count(r => r["kind"] == "food")).Append('\t').Append(type.Count(r => r["kind"] == "line")).Append('\t')
                    .Append(hits.Count).Append('\t').Append(lost.Count).Append('\t').Append(clears.Count).Append('\t').Append(wrong.Count).Append('\t')
                    .Append((share * 100).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)).Append(" %\n");
                missed.AddRange(lost.Select(r => $"{r.Id} {type.Key}: {r["text"]} | {r["description"]}"));
                over.AddRange(wrong.Select(r => $"{r.Id} {type.Key}: {r["text"]} | {r["description"]}"));
                if (type.Count(r => r["kind"] == "food") < FoodsPerType) failing.Add($"{type.Key}: {type.Count(r => r["kind"] == "food")} foods, fewer than {FoodsPerType}");
                if (share >= OverFlagBar) failing.Add($"{type.Key}: {wrong.Count} of {clears.Count} clear rows flagged ({share:P1}), at or over the bar");
            }
            _output.WriteLine(table.ToString());
            _output.WriteLine("missed:\n" + string.Join('\n', missed));
            _output.WriteLine("over-flagged:\n" + string.Join('\n', over));
            missed.Should().BeEmpty("no severe type may miss a food or line that names what it restricts");
            failing.Should().BeEmpty();
        }

        [Fact]
        public void Every_severe_type_with_a_vocabulary_is_measured()
        {
            var measured = GoldenSet.Read(File).Rows.Select(r => r["restriction"]).ToHashSet(StringComparer.Ordinal);
            DietVocabulary.Classes.Select(c => c.ReferenceType).Where(t => !measured.Contains(t)).Should().BeEmpty();
            measured.Should().Contain(new[] { "Celiac Disease" });
        }
    }
}
