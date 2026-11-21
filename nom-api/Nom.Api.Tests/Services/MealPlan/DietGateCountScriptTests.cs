using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using FluentAssertions;
using Nom.Data.Nutrition;
using Nom.Data.Recipe;
using Nom.Orch.Services.Support;
using Xunit;

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
