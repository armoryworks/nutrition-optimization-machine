using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using Nom.Api.Tests.Golden;
using Nom.Import.Golden;
using Nom.Orch.Services.Support;
using Xunit;
using Xunit.Abstractions;

namespace Nom.Api.Tests.Services.MealPlan
{
    /// <summary>
    /// Every food name and USDA description of the frozen catalog (gate-catalog.tsv) on which a severe type's word rule
    /// and ilike disagree, pattern by pattern, is in the reviewed register diet-reading-register.tsv: a change to the word
    /// rule, the term readings, the exceptions, the maker blanking or the vocabulary shows there as a diff a person reviews
    /// before any entry changes its verdict unseen (staging checkpoint 6, R6 and R7).
    /// </summary>
    public class DietReadingRegisterTests
    {
        private readonly ITestOutputHelper _output;

        public DietReadingRegisterTests(ITestOutputHelper output) => _output = output;

        private static readonly string[] SevereTypes =
        {
            "Celiac Disease", "Wheat Allergy", "Milk Allergy", "Egg Allergy", "Peanut Allergy", "Tree Nut Allergy",
            "Soy Allergy", "Sesame Allergy", "Fish Allergy", "Shellfish Allergy", "Sulfite Sensitivity",
        };

        private static string RegisterPath([CallerFilePath] string here = "") =>
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "Fixtures", "diet-reading-register.tsv"));

        internal static string RegisterTable()
        {
            var texts = GoldenSet.Read("gate-catalog.tsv").Rows
                .SelectMany(r => new[] { (Kind: "name", Text: r["name"], Branded: r["dataType"] == "branded_food"), (Kind: "description", Text: r["description"], Branded: false) })
                .Where(t => t.Text is not ("" or GoldenTsv.NoAnswer))
                .Distinct()
                .ToList();
            var table = new StringBuilder("type\tpattern\tread as\ttext\tilike\tword rule\n");
            foreach (var type in SevereTypes)
                foreach (var pattern in DefaultRestrictionCriteria.ByName[type].Patterns.Where(p => !DietCriteriaMatcher.IsWordPattern(p)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p, StringComparer.Ordinal))
                {
                    var rule = DietPatternRule.For(pattern, type);
                    var like = DietCriteriaMatcher.LikeRegex(pattern);
                    foreach (var t in texts.OrderBy(t => t.Kind, StringComparer.Ordinal).ThenBy(t => t.Text, StringComparer.Ordinal))
                    {
                        var read = t.Kind == "name" ? rule.IsMatch(DietPatternRule.WithoutMaker(t.Text, t.Branded)) : rule.IsDescriptionMatch(t.Text);
                        var ilike = like.IsMatch(Regex.Replace(t.Text, @"[\s\-]+", " "));
                        if (read != ilike)
                            table.Append(type).Append('\t').Append(pattern).Append('\t').Append(t.Kind).Append('\t').Append(t.Text).Append('\t')
                                .Append(ilike ? "reads" : "-").Append('\t').Append(read ? "reads" : "-").Append('\n');
                    }
                }
            return table.ToString();
        }

        [Fact]
        public void Every_catalog_text_the_word_rule_reads_otherwise_than_ilike_is_in_the_reviewed_register()
        {
            var actual = RegisterTable();
            var expected = File.Exists(RegisterPath()) ? File.ReadAllText(RegisterPath()) : string.Empty;
            if (actual != expected) _output.WriteLine(actual);
            actual.Should().Be(expected, "a change to the reading shows here as a diff a person reviews (the new register is in the test output)");
        }
    }
}
