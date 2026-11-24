using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using FluentAssertions;
using Nom.Api.Tests.Golden;
using Nom.Orch.Services.Support;
using Xunit;
using Xunit.Abstractions;

namespace Nom.Api.Tests.Services.MealPlan
{
    /// <summary>
    /// The severe types' vocabulary (<see cref="DietVocabulary"/>, staging checkpoint 5, Q5, Q7, Q19): the committed file holds
    /// exactly the terms the rule derives from the frozen USDA catalog (gate-catalog.tsv), with the categories and foods that
    /// gave each, and every decision holds against the defaults: a term decided in is read by its own pattern and no other,
    /// a covered term is read by another pattern of the class, and a term taken out is read by none, so its cited exception
    /// is in force.
    /// </summary>
    public class DietVocabularyTests
    {
        private readonly ITestOutputHelper _output;

        public DietVocabularyTests(ITestOutputHelper output) => _output = output;

        private static IReadOnlyList<DietVocabularyFood> Catalog() =>
            GoldenSet.Read("gate-catalog.tsv").Rows
                .Select(r => new DietVocabularyFood(long.Parse(r.Id, CultureInfo.InvariantCulture), r["description"], int.TryParse(r["categoryId"], out var c) ? c : null))
                .Where(f => f.Description is not ("" or "-"))
                .ToList();

        private static string Key(string cls, string term, string basis, string categories, int foods) => $"{cls}\t{term}\t{basis}\t{categories}\t{foods}";

        private static string Categories(IEnumerable<int> ids) => ids.Any() ? string.Join('|', ids) : "-";

        private static bool Reads(string pattern, string type, string term) => DietPatternRule.For(pattern, type).IsMatch("1 cup " + term);

        [Fact]
        public void The_committed_vocabulary_is_the_derivation_of_the_frozen_catalog()
        {
            var derived = DietVocabulary.Derive(Catalog());
            var committed = DietVocabulary.All.Where(t => t.Basis is not (DietVocabularyBasis.Label or DietVocabularyBasis.Translation)).ToList();

            var want = derived.Select(d => Key(d.Class, d.Term, d.Basis, Categories(d.Categories), d.Foods)).ToList();
            var have = committed.Select(t => Key(t.Class, t.Term, t.Basis, t.Categories, t.Foods)).ToList();
            if (!want.SequenceEqual(have))
            {
                var byKey = committed.ToDictionary(t => (t.Class, t.Term, t.Basis));
                var draft = new StringBuilder();
                foreach (var d in derived)
                {
                    var t = byKey.GetValueOrDefault((d.Class, d.Term, d.Basis));
                    draft.Append(d.Class).Append('\t').Append(d.Term).Append('\t').Append(t?.Pattern ?? "%" + d.Term + "%").Append('\t').Append(d.Basis).Append('\t')
                        .Append(Categories(d.Categories)).Append('\t').Append(d.Foods).Append('\t').Append(t?.Decision ?? "?").Append('\t').Append(t?.Source ?? "?").Append('\n');
                }
                _output.WriteLine(draft.ToString());
            }
            have.Should().Equal(want, "the derived rows of diet-vocabulary.tsv change only with the catalog or the rule, in the order the rule gives (the derivation is in the test output)");
        }

        [Fact]
        public void Every_decision_holds_against_the_class_reference_types_defaults()
        {
            var wrong = new List<string>();
            foreach (var t in DietVocabulary.All)
            {
                var type = DietVocabulary.ClassOf(t.Class).ReferenceType;
                var patterns = DefaultRestrictionCriteria.ByName[type].Patterns.Where(p => !DietCriteriaMatcher.IsWordPattern(p)).ToList();
                var others = patterns.Where(p => !string.Equals(p, t.Pattern, StringComparison.OrdinalIgnoreCase)).Where(p => Reads(p, type, t.Term)).ToList();
                switch (t.Decision)
                {
                    case DietVocabularyDecision.In:
                        if (!patterns.Contains(t.Pattern, StringComparer.OrdinalIgnoreCase)) wrong.Add($"{t.Class} {t.Term}: {t.Pattern} is not among {type}'s defaults");
                        else if (!Reads(t.Pattern, type, t.Term)) wrong.Add($"{t.Class} {t.Term}: {t.Pattern} does not read its own term");
                        else if (others.Count > 0) wrong.Add($"{t.Class} {t.Term}: decided in, but {string.Join(", ", others)} already reads it (covered)");
                        break;
                    case DietVocabularyDecision.Covered:
                        if (others.Count == 0) wrong.Add($"{t.Class} {t.Term}: decided covered, but no other pattern of {type} reads it");
                        break;
                    case DietVocabularyDecision.Out:
                        if (others.Count > 0 || patterns.Contains(t.Pattern, StringComparer.OrdinalIgnoreCase)) wrong.Add($"{t.Class} {t.Term}: taken out, but {string.Join(", ", others.DefaultIfEmpty(t.Pattern))} still reads it");
                        break;
                }
            }
            _output.WriteLine(string.Join('\n', wrong));
            wrong.Should().BeEmpty();
        }
    }
}
