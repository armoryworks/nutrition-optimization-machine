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
    /// criterion the gate reads: beside the <c>%</c> patterns, the word patterns and the nutrient
    /// criteria with no per-serving cap, and for an alcohol type the drink lines, so a recipe a word,
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
