using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using Nom.Data.Recipe;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;
using Xunit.Abstractions;

namespace Nom.Api.Tests.Golden
{
    /// <summary>
    /// The label rule (<see cref="StatementLabelTerms"/>) on label-terms.tsv, and its measure on the FDC statements of
    /// the branded whole meals staging held at checkpoint 3 (Fixtures/fdc-meal-statements.tsv, public domain): how many
    /// read statements are fully known with no catalog to resolve against, and for each default restriction type how
    /// many meals the gate can decide either way (offered, or held because the contents name what it restricts). The
    /// rule needs no model, so its floor is every row.
    /// </summary>
    [Trait("Category", "Golden")]
    public class GoldenLabelTermsTests
    {
        /// <summary>The share of read statements that must be fully known by the label rule alone (82 of 97 at label-terms/1).</summary>
        public const double KnownFloor = 0.80;

        private readonly ITestOutputHelper _output;

        public GoldenLabelTermsTests(ITestOutputHelper output) => _output = output;

        [Fact]
        public void Each_name_is_read_as_the_rule_gives()
        {
            var report = GoldenSet.Run("label-terms.tsv", "status", row =>
            {
                var terms = StatementLabelTerms.Match(row["name"]);
                return GoldenSet.Compare(row, new Dictionary<string, string>
                {
                    ["expectTerms"] = terms == null ? "-" : string.Join('|', terms.Select(t => t.Term)),
                });
            });
            _output.WriteLine(report.Summary);
            GoldenSet.Assert(report);
        }

        private static List<(string FdcId, string Statement)> Statements() =>
            File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "fdc-meal-statements.tsv"))
                .Where(l => l.Length > 0 && !l.StartsWith('#'))
                .Skip(1)
                .Select(l => l.Split('\t'))
                .Select(c => (c[0], c[2]))
                .ToList();

        private static IngredientContentsEntity Row(long id, string statement) =>
            PreparedContentsService.Build(new ContentsStatement(id, "fdc:" + id, statement), statement, IngredientStatementReader.Read(statement), new Dictionary<string, long>(), "measure", DateTime.UnixEpoch) is var row
                ? WithId(row, id) : throw new InvalidOperationException();

        private static IngredientContentsEntity WithId(IngredientContentsEntity row, long id)
        {
            row.Id = id;
            return row;
        }

        internal static RestrictedIngredientSet DefaultSet(string type)
        {
            var (severity, patterns) = DefaultRestrictionCriteria.ByName[type];
            var set = new RestrictedIngredientSet();
            var id = 1L;
            foreach (var p in patterns)
                set.Criteria.Add(DietCriteriaMatcher.IsWordPattern(p)
                    ? new DietDataCriterion(id++, 1, p, null, null, null, severity, null, null, type)
                    : new DietDataCriterion(id++, 1, null, null, null, null, severity, null, p, type));
            if (DefaultRestrictionCriteria.NutrientsByName.ContainsKey(type))
                set.Criteria.Add(new DietDataCriterion(id, 1, null, 2, DietCriteriaMatcher.AlcoholNutrientName, DefaultRestrictionCriteria.MaxPerServingByName.TryGetValue(type, out var cap) ? cap : null, severity, null, null, type));
            set.HasSevere = severity >= 4;
            set.ReadsContents = true;
            return set;
        }

        [Fact]
        public void On_the_checkpoint_statements_most_are_fully_known_and_no_meal_naming_a_restricted_allergen_is_offered()
        {
            var statements = Statements();
            statements.Should().HaveCount(100);
            var rows = statements.Select((s, i) => (s.FdcId, s.Statement, Row(i + 1, s.Statement))).ToList();
            var read = rows.Where(r => r.Item3.Outcome == IngredientContentsOutcome.Read).ToList();
            var known = read.Count(r => r.Item3.Unresolved == 0);
            var text = new StringBuilder();
            text.AppendLine($"statements {rows.Count}, read {read.Count}, fully known {known} ({(double)known / read.Count:P1})");
            text.AppendLine("type\tseverity\toffered\theld (named)\tunknown\tdecided");
            foreach (var type in DefaultRestrictionCriteria.ByName.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                var set = DefaultSet(type);
                var verdicts = rows.Select(r => PreparedContentsGate.Judge(r.Item3.Id, new[] { r.Item3 }, 1, set, new HashSet<long>())).ToList();
                var offered = verdicts.Count(v => v.Offered);
                var held = verdicts.Count(v => v.Held is PreparedContentsHeld.Restricted or PreparedContentsHeld.Food);
                text.AppendLine($"{type}\t{DefaultRestrictionCriteria.ByName[type].Severity}\t{offered}\t{held}\t{rows.Count - offered - held}\t{offered + held}");
            }
            _output.WriteLine(text.ToString());
            ((double)known / read.Count).Should().BeGreaterThanOrEqualTo(KnownFloor);

            foreach (var (type, words) in new[]
            {
                ("Milk Allergy", @"(?<!coconut |peanut |cocoa |shea )\b(?:milk|cream|cheese|whey|butter|casein|lactose|yogurt)"),
                ("Celiac Disease", @"\b(?:wheat|barley|rye|malt(?!odextrin)|semolina|spelt|durum|kamut|farro)"),
                ("Peanut Allergy", @"\bpeanut"),
                ("Soy Allergy", @"\b(?:soy|soya|edamame|tofu)"),
                ("Egg Allergy", @"\begg"),
                ("Shellfish Allergy", @"\b(?:shrimp|crab|lobster|vannamei)"),
            })
            {
                var set = DefaultSet(type);
                rows.Where(r => Regex.IsMatch(r.Statement, words, RegexOptions.IgnoreCase))
                    .Where(r => PreparedContentsGate.Judge(r.Item3.Id, new[] { r.Item3 }, 1, set, new HashSet<long>()).Offered)
                    .Select(r => r.FdcId)
                    .Should().BeEmpty($"{type} is never offered a meal whose statement names what it restricts");
            }
        }
    }
}
