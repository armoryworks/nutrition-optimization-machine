using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using FluentAssertions;
using Nom.Data.Nutrition;
using Nom.Data.Recipe;
using Nom.Orch.Services.Support;
using Xunit;
using Xunit.Abstractions;

namespace Nom.Api.Tests.Services.MealPlan
{
    /// <summary>
    /// ops/diet-gate-count.sql, the staging count of the diet gate's name-pattern change, reads every
    /// criterion the gate reads: beside the <c>%</c> patterns, the word patterns, the nutrient
    /// criteria with no per-serving cap, every nutrient criterion's stored figure as deciding a word
    /// pattern's food, and for an alcohol type, capped or not, the drink lines, so a recipe a word,
    /// a stored figure or a drink line still keeps out is never counted as lost. Its shared values
    /// are the code's own: the drink-word probe, the presence threshold, the trusted alias sources
    /// and the category patterns.
    /// </summary>
    public class DietGateCountScriptTests
    {
        private static readonly string Script = File.ReadAllText(ScriptPath());

        private readonly ITestOutputHelper _output;

        public DietGateCountScriptTests(ITestOutputHelper output) => _output = output;

        internal static string RuleTable()
        {
            static string Quote(string text) => "'" + text.Replace("'", "''") + "'";
            var rows = DefaultRestrictionCriteria.ByName.Values.SelectMany(v => v.Patterns)
                .Concat(SeedRestrictionCriteria.All.Select(c => c.Pattern)).Concat(DefaultRestrictionCriteria.Companions.Select(c => c.Companion)).Concat(DefaultRestrictionCriteria.CompositeCompanions.Select(c => c.Companion))
                .Where(p => !DietCriteriaMatcher.IsWordPattern(p))
                .Select(p => p.Trim().ToLowerInvariant()).Distinct().OrderBy(p => p, System.StringComparer.Ordinal)
                .Select(p => DietPatternRule.For(p))
                .Select(r => "        (" + Quote(r.Pattern) + ", " + Quote("%" + r.Words[0] + "%") + ", " + Quote(r.Expression(DietRegexDialect.Postgres)) + ", "
                    + (r.ExceptionExpression(DietRegexDialect.Postgres) is { } blank ? Quote(blank) : "NULL") + ")");
            return new StringBuilder("rule_pats (pattern, seed, term_rx, blank_rx) AS (\n    VALUES\n").Append(string.Join(",\n", rows)).Append("\n),").ToString();
        }

        private static string ScriptPath([CallerFilePath] string here = "") =>
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "..", "..", "ops", "diet-gate-count.sql"));

        [Fact]
        public void The_count_reads_the_word_and_nutrient_criteria_and_the_drink_lines_beside_the_name_patterns()
        {
            Script.Should().NotContain("<> '\\m'", "a word pattern is a criterion the gate applies, so the count must apply it too");
            Script.Should().Contain("\"MaxAmountPerServing\" IS NULL").And.Contain("nutrient.\"IngredientNutrient\"");
            Script.Should().Contain("\"Name\" ~* c.pattern").And.Contain("\"AliasName\" ~* c.pattern").And.Contain("\"RawLine\" ~* c.pattern");
            Script.Should().Contain("drink_lines");
        }

        [Fact]
        public void By_default_the_count_reads_every_type_with_a_criterion_once_per_name_samples_a_spread_and_counts_each_set_once()
        {
            Script.Should().Contain("\\set names ''").And.Contain(":'names' = '' AND EXISTS (SELECT 1 FROM plan.\"RestrictionCriterion\"",
                "the default list left out No Alcohol, the one type the alcohol work changes (checkpoint 2, M14)");
            Script.Should().Contain("GROUP BY type_name", "Halal (2011, 60901) and the other types with two reference rows printed their summary and sample twice");
            Script.Should().Contain("md5(x.\"RecipeId\"::text || :'seed')", "the lost sample was the 20 lowest recipe ids, the same short modern recipes for every type");
            Script.Should().NotContain("(SELECT count(*) FROM", "a count per type over each whole set took 14 minutes on the staging copy");
            Script.Should().Contain("drink_recipes").And.Contain("touched_lines");
        }

        [Fact]
        public void The_count_reads_every_live_line_by_its_own_text_whatever_its_food_and_samples_a_spread()
        {
            Script.Should().Contain("line_pat AS").And.Contain("JOIN live l ON l.\"RawLine\" ILIKE p.pattern", "the line rule of staging checkpoint 2 (M27) reads every live line, on any food");
            Script.Should().Contain("WHERE c.type_id NOT IN (SELECT type_id FROM alcohol_types)", "a type with the alcohol criterion leaves its drink words to the alcohol rule, as DietCriteriaMatcher.LinePatterns does");
            Script.Should().Contain("gained_by_any_line").And.Contain("SELECT 'line'");
            Script.Should().Contain("ORDER BY x.approved DESC, md5(x.\"RecipeId\"::text || :'seed')) AS n\n    FROM by_any_line", "the line samples are a spread, not the lowest recipe ids (M14)");
        }

        [Fact]
        public void The_count_reads_every_default_seeded_and_companion_pattern_by_the_codes_word_rule_and_a_pattern_it_does_not_know_by_ilike()
        {
            var table = RuleTable();
            if (!Script.Contains(table)) _output.WriteLine(table);
            Script.Should().Contain(table, "the count's table of expressions is the code's (the expected table is in the test output)");
            Script.Should().Contain("LEFT JOIN rule_pats r ON r.pattern = lower(p.pattern)").And.Contain("r.pattern IS NULL AS by_like");
            Script.Should().Contain("'patterns read as substring'").And.Contain("'patterns the table lacks (read by ILIKE)'", "a seeded or admin pattern the rule reads by words is not a substring pattern, and the count says which it read by ILIKE");
            Script.Should().Contain("regexp_replace(l.\"RawLine\", o.blank_rx, ' ', 'gi') ~* o.term_rx").And.Contain("regexp_replace(i.\"Name\", o.blank_rx, ' ', 'gi') ~* o.term_rx");
            Script.Should().Contain("gained_by_rule_line").And.Contain("SELECT 'rule-drop'").And.Contain("SELECT 'rule-line'").And.Contain("SELECT 'rule-food'");
            Script.Should().Contain("ORDER BY x.approved DESC, md5(x.\"RecipeId\"::text || :'seed')) AS n\n    FROM rule_dropped", "the dropped samples are a spread, like the others");
        }

        [Fact]
        public void The_counts_shared_values_are_the_codes()
        {
            Script.Should().Contain("'" + AlcoholLineContext.DrinkNamePattern + "'");
            Script.Should().Contain("> " + DietCriteriaMatcher.PresencePer100g.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Script.Should().Contain("IS DISTINCT FROM " + CatalogAdmissionRule.SpicesAndHerbs);
            IngredientAliasTrust.TrustedPrefixes.Where(p => !Script.Contains("LIKE '" + p + "%'")).Should().BeEmpty();
            DietCriteriaMatcher.CategoryPatterns.Where(kv => !Script.Contains($"lower(p.pattern) = '{kv.Key}'") || kv.Value.Any(c => !Script.Contains($"\"FdcCategoryId\" = {c}"))).Should().BeEmpty();
        }
    }
}
