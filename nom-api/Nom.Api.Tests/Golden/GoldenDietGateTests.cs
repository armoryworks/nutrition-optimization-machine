using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nom.Data;
using Nom.Data.Measurement;
using Nom.Data.Nutrient;
using Nom.Data.Plan;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Import.Golden;
using Nom.Orch.Interfaces;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;
using Xunit.Abstractions;

namespace Nom.Api.Tests.Golden
{
    /// <summary>
    /// The diet gate's name-pattern criteria against diet-gate.tsv: each row is one food with its
    /// aliases (text and source) and one recipe line on it, under one default restriction; the
    /// recipe must be kept out of a household's plans and flagged on the recipe page (and, for a
    /// severity 4 or 5 restriction, hidden from the person's search) or offered and clear, as the
    /// row says. A food counts by its own name, its USDA category or an alias a rule or a person gave
    /// it, never by a recipe line stored as an alias; a line on a food matched only through such an
    /// alias decides for its own recipe. Over the rows, no recipe the old rule (name or any alias)
    /// kept out loses its flag while its line names a pattern of the restriction. A row with an
    /// <c>alcohol</c> figure (per 100 g) also stores that figure on the food and weighs the line at
    /// <c>grams</c> in a recipe of <c>servings</c> with <c>steps</c>, whose retention the alcohol
    /// retention run reads first: No Alcohol excludes it only above 0.5 g of retained alcohol per
    /// serving, Halal and Whole30 whatever the cooking, and a flavouring for none of them.
    /// </summary>
    [Trait("Category", "Golden")]
    public class GoldenDietGateTests
    {
        private const long HouseholdId = 7, PersonId = 70, TypeId = 61000, Gram = 1, AlcoholNutrient = 5038;

        private readonly ITestOutputHelper _output;

        public GoldenDietGateTests(ITestOutputHelper output) => _output = output;

        private sealed record Verdict(bool Plan, bool Page, bool? Severe);

        private static async Task<Verdict> RunAsync(GoldenRow row)
        {
            using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
                new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
            var restriction = row["restriction"];
            db.HouseholdMembers.Add(new HouseholdMemberEntity { HouseholdId = HouseholdId, PersonId = PersonId, IsActive = true });
            db.Set<ReferenceEntity>().Add(new ReferenceEntity { Id = TypeId, Name = restriction });
            var food = new IngredientEntity { Name = row["food"], CurationStatusId = (long)CurationStatusEnum.Curated, FdcCategoryId = int.TryParse(row["category"], out var category) ? category : null };
            foreach (var alias in Aliases(row["aliases"])) food.Aliases.Add(new IngredientAliasEntity { AliasName = alias.Text, SourceContext = alias.Context });
            var alcohol = Number(row["alcohol"]);
            if (alcohol is { } per100g)
            {
                db.Set<BaseMeasurementEntity>().Add(new BaseMeasurementEntity { Id = Gram, Name = "Gram", Symbol = "g", MeasurementCategoryId = 1, IsBaseUnit = true, BaseUnitConversionFactor = 1m });
                db.Nutrients.Add(new NutrientEntity { Id = AlcoholNutrient, Name = DietCriteriaMatcher.AlcoholNutrientName, DefaultMeasurementId = Gram });
                food.IngredientNutrients.Add(new IngredientNutrientEntity { NutrientId = AlcoholNutrient, Amount = per100g, MeasurementId = Gram });
            }
            db.Ingredients.Add(food);
            await db.SaveChangesAsync();
            var recipe = new RecipeEntity { Name = "Golden " + row.Id, AuthorId = PersonId, CurationStatusId = (long)CurationStatusEnum.NonCurated, RecipeIngredients = new List<RecipeIngredientEntity>() };
            recipe.RecipeIngredients.Add(new RecipeIngredientEntity { IngredientId = food.Id, MeasurementId = Gram, Quantity = Number(row["grams"]) ?? 1m, RawLine = row["line"], SortOrder = 0 });
            if (alcohol != null)
            {
                recipe.Servings = (long?)Number(row["servings"]);
                recipe.RecipeSteps = row.Value("steps") is { } steps
                    ? steps.Split(" | ").Select((text, i) => new RecipeStepEntity { StepNumber = i + 1, Summary = "Step", Description = text }).ToList()
                    : new List<RecipeStepEntity>();
            }
            db.Recipes.Add(recipe);
            db.Restrictions.Add(new RestrictionEntity { PersonId = PersonId, Name = restriction, RestrictionTypeId = TypeId });
            await db.SaveChangesAsync();
            if (alcohol != null)
                await new AlcoholRetentionService(db, new RecipeNutritionService(db, NullLogger<RecipeNutritionService>.Instance), NullLogger<AlcoholRetentionService>.Instance).RunAsync(apply: true, afterRecipeId: 0);
            await DefaultRestrictionCriteria.EnsureAsync(db);

            var resolver = new HouseholdRestrictionResolver(db);
            var set = await resolver.ResolveAsync(HouseholdId);
            await Nom.Api.Tests.Services.MealPlan.DietLineIndexTests.AssertIndexAgreesAsync(db, HouseholdId, PersonId);
            await Nom.Api.Tests.Services.MealPlan.DietGateStoreTests.AssertStoreAgreesAsync(db, HouseholdId, PersonId);
            var plan = set.IngredientIds.Contains(food.Id) || set.RecipeIds.Contains(recipe.Id);
            var service = new RecipeOrchestrationService(db, new HttpContextAccessor(), new Mock<ICurrentUserService>().Object,
                new Mock<Nom.Orch.UtilityInterfaces.IMediaStorageService>().Object, new Mock<IRecipeNutritionService>().Object);
            var page = (await service.GetDietMatchesAsync(recipe.Id, PersonId)).Any(m => m.RestrictionName == restriction);
            bool? severe = null;
            if (DefaultRestrictionCriteria.ByName[restriction].Severity >= 4)
            {
                var hidden = await resolver.ResolveSevereForPersonAsync(PersonId);
                var conflicts = await service.GetSevereDietConflictsAsync(new[] { recipe.Id }, PersonId);
                severe = (hidden.IngredientIds.Contains(food.Id) || hidden.RecipeIds.Contains(recipe.Id)) && conflicts.ContainsKey(recipe.Id);
            }
            return new Verdict(plan, page, severe);
        }

        private static decimal? Number(string cell) =>
            decimal.TryParse(cell, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : null;

        private static IEnumerable<(string Text, string? Context)> Aliases(string cell) =>
            cell == GoldenTsv.NoAnswer ? Enumerable.Empty<(string, string?)>()
            : cell.Split(" ¦ ").Select(a => a.Split('|')).Select(p => (p[0], p[1] == GoldenTsv.NoAnswer ? (string?)null : p[1]));

        private static IEnumerable<string> Patterns(string restriction) => DefaultRestrictionCriteria.ByName[restriction].Patterns.Where(p => !DietCriteriaMatcher.IsWordPattern(p));

        private static Regex Like(string pattern) =>
            new("^" + Regex.Escape(pattern).Replace("%", ".*").Replace("_", ".") + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);

        private static bool OldRule(GoldenRow row) =>
            Patterns(row["restriction"]).Select(Like).Any(p => p.IsMatch(row["food"]) || Aliases(row["aliases"]).Any(a => p.IsMatch(a.Text)));

        private static bool LineNamesPattern(GoldenRow row) => Patterns(row["restriction"]).Any(p => DietPatternRule.For(p, row["restriction"]).IsMatch(row["line"]));

        private static async Task<Dictionary<string, Verdict>> VerdictsAsync()
        {
            var verdicts = new Dictionary<string, Verdict>(StringComparer.Ordinal);
            foreach (var row in GoldenSet.Read("diet-gate.tsv").Rows) verdicts[row.Id] = await RunAsync(row);
            return verdicts;
        }

        [Fact]
        public async Task Each_row_keeps_its_recipe_out_or_offers_it_in_plans_on_the_page_and_in_a_severe_search()
        {
            var verdicts = await VerdictsAsync();
            var report = GoldenSet.Run("diet-gate.tsv", "status", row =>
            {
                var v = verdicts[row.Id];
                var expect = row["expect"] == "out";
                var pass = v.Plan == expect && v.Page == expect && (v.Severe ?? expect) == expect;
                var actual = $"plan {(v.Plan ? "out" : "in")}, page {(v.Page ? "flag" : "clear")}" + (v.Severe is { } s ? $", severe {(s ? "hidden" : "shown")}" : string.Empty);
                return new GoldenOutcome(pass, $"{row["expect"]} ({row["restriction"]}: {row["line"]} on {row["food"]})", actual, new Dictionary<string, string>());
            }, modelJudged: row => GoldenSet.ByModel(row["source"]));
            _output.WriteLine(report.Summary);
            GoldenSet.Assert(report);
        }

        [Fact]
        public async Task No_recipe_the_old_rule_kept_out_loses_its_flag_while_its_line_names_the_restricted_thing()
        {
            var rows = GoldenSet.Read("diet-gate.tsv").Rows;
            var verdicts = await VerdictsAsync();

            var lost = rows.Where(r => OldRule(r) && LineNamesPattern(r) && !verdicts[r.Id].Plan).Select(r => $"{r.Id}: {r["line"]} on {r["food"]}");
            var cleared = rows.Where(r => OldRule(r) && !verdicts[r.Id].Plan).Select(r => r.Id).ToList();
            var added = rows.Where(r => !OldRule(r) && verdicts[r.Id].Plan).Select(r => r.Id).ToList();
            _output.WriteLine($"cleared against the old rule: {string.Join(", ", cleared)}; newly kept out: {string.Join(", ", added)}");

            lost.Should().BeEmpty();
            cleared.Should().NotBeEmpty("the rows hold the recipe-line aliases the old rule read");
            rows.Where(r => r["expect"] == "out").Should().Contain(r => OldRule(r) && Aliases(r["aliases"]).All(a => !IngredientAliasTrust.IsTrusted(a.Context)) && !Patterns(r["restriction"]).Select(Like).Any(p => p.IsMatch(r["food"])),
                "both directions: a recipe kept out only by its own line on a food matched through a recipe-line alias");
        }
    }
}
