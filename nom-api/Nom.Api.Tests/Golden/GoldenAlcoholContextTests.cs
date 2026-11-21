using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Nutrient;
using Nom.Data.Plan;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Moq;
using Nom.Import.Golden;
using Nom.Orch.Interfaces;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;
using Xunit.Abstractions;

namespace Nom.Api.Tests.Golden
{
    /// <summary>
    /// The alcohol sense rules against alcohol-context.tsv: every rule-given row reads its sense,
    /// allows the food it must allow and refuses the one it must refuse; the rows the builder
    /// judged must too, and are reported apart. The committed alcohol-context record must be what
    /// the rules score now. The allowed-food table names only USDA alcoholic beverages of the
    /// frozen catalog. Over the judged link pairs, the rules refuse no pair judged right. The diet
    /// gate replay: every golden line read as a drink is still flagged for No Alcohol when it is the
    /// name of an unlinked food, and the lines the old name patterns flagged by mistake are not; and
    /// every golden line its expected sense counts, linked to a food that is not a drink (the state of
    /// the wrong links on staging until the re-check), keeps its recipe flagged, in plans as on the
    /// recipe page, whether or not the food's name holds a drink word.
    /// </summary>
    [Trait("Category", "Golden")]
    public class GoldenAlcoholContextTests
    {
        private static readonly string[] OldAlcoholPatterns = { "%wine%", "%beer%", "%vodka%", "%rum%", "%whisk%", "%bourbon%", "%brandy%", "%liqueur%", "%sake%", "%tequila%", "%gin%", "%sherry%", "%vermouth%", "%cider%" };

        private readonly ITestOutputHelper _output;

        public GoldenAlcoholContextTests(ITestOutputHelper output) => _output = output;

        [Fact]
        public void Each_row_reads_its_sense_and_allows_and_refuses_its_foods()
        {
            var cases = ModelLanes.AlcoholCases(GoldenSet.Folder).ToDictionary(c => c.Id[("alcohol:".Length)..]);
            var report = GoldenSet.Run("alcohol-context.tsv", "status", row =>
            {
                var outcome = cases[row.Id].Outcome();
                return new GoldenOutcome(outcome.Pass, $"{outcome.Expected} ({row["raw"]})", outcome.Actual, new Dictionary<string, string>());
            }, modelJudged: row => GoldenSet.ByModel(row["source"]));
            _output.WriteLine(report.Summary);
            GoldenSet.Assert(report);
        }

        [Fact]
        public void Every_sense_the_rules_give_has_a_row_and_every_sense_of_the_design_a_rule_given_one()
        {
            var rows = GoldenSet.Read("alcohol-context.tsv").Rows;
            var senses = rows.Select(r => r["expect"]).ToHashSet(StringComparer.Ordinal);
            var ruleGiven = rows.Where(r => r["status"] == GoldenSet.Ok).Select(r => r["expect"]).ToHashSet(StringComparer.Ordinal);

            AlcoholLineContext.SenseCodes.Append(AlcoholLineContext.ListSense).Where(s => !senses.Contains(s)).Should().BeEmpty();
            AlcoholLineContext.SenseCodes.Append(AlcoholLineContext.ListSense).Where(s => !ruleGiven.Contains(s)).Should().BeEquivalentTo(new[] { "malt-liquor", "country-wine" },
                "only the two senses the builder added beyond the design rest on judged rows alone");
        }

        [Fact]
        public async Task The_committed_alcohol_context_record_is_what_the_rules_score_now()
        {
            var record = ModelLanes.Recorded(GoldenSet.Folder, ModelLanes.AlcoholContextLane);
            record.Should().NotBeNull("the alcohol-context lane needs no model host, so its record is always committed");

            var results = await ModelLanes.RunAsync(ModelLanes.AlcoholContextLane, GoldenSet.Folder, null!, null!, null, _ => { });
            var score = ModelLanes.Score(ModelLanes.AlcoholContextLane, record!.Model, record.Host, AlcoholLineContext.Signature, results);
            _output.WriteLine($"alcohol-context: {score.Passed}/{score.Cases}, false accepts {score.FalseAccepts}/{score.FalseAcceptCases}");

            score.Signature.Should().Be(record.Signature, "a changed rule needs a new record: dotnet run --project nom-api/Nom.Import -- --golden-score --lane alcohol-context --record");
            score.CaseSet.Should().Be(record.CaseSet);
            (score.Passed, score.FalseAccepts).Should().Be((record.Passed, record.FalseAccepts));
            score.Failed.Should().Equal(record.Failed);
        }

        [Fact]
        public void The_allowed_food_table_names_only_usda_alcoholic_beverages_of_the_frozen_catalog()
        {
            var foods = ModelLanes.GateFoods(GoldenSet.Folder).Where(f => f.FdcId != null).GroupBy(f => f.FdcId!).ToDictionary(g => g.Key, g => g.First());
            var drinks = AlcoholLineContext.SenseCodes.Where(s => AlcoholLineContext.KindOf(s) == AlcoholSenseKind.Drink).ToList();

            var wrong = drinks.SelectMany(s => AlcoholLineContext.Allowed(s).Select(id => (Sense: s, Id: id)))
                .Where(x => !foods.TryGetValue(x.Id, out var food) || food.CategoryId != 14 || !AlcoholLineContext.IsAlcoholicFood(null, food.Text))
                .Select(x => $"{x.Sense}: {x.Id} {(foods.TryGetValue(x.Id, out var f) ? f.Text : "not in the catalog")}");

            wrong.Should().BeEmpty();
            drinks.Where(s => AlcoholLineContext.Allowed(s).All(id => foods[id].NutrientRows < LinkGate.MinimumNutrientRows))
                .Should().BeEquivalentTo(new[] { "wine-late-harvest" }, "USDA's late harvest whites carry 6 nutrient rows, so the readiness rule holds them; every other sense has a ready food");
        }

        [Fact]
        public void Over_the_judged_link_pairs_the_rules_refuse_no_pair_judged_right_except_an_unclear_one()
        {
            var texts = ModelLanes.GateFoods(GoldenSet.Folder).Where(f => f.FdcId != null).GroupBy(f => f.FdcId!).ToDictionary(g => g.Key, g => g.First().Text);
            var pairs = GoldenSet.Read("link-pairs.tsv").Rows.Where(r => r["verdict"] is "ok" or "wrong" or "near" or "compound").ToList();
            var refused = pairs
                .Select(r => (Row: r, Reading: AlcoholLineContext.Read(r["wording"])))
                .Select(x => (x.Row, x.Reading, Refusal: AlcoholLineContext.Refuse(x.Reading, x.Row["fdcId"], texts.GetValueOrDefault(x.Row["fdcId"]) ?? x.Row["description"])))
                .Where(x => x.Refusal != null)
                .ToList();

            _output.WriteLine($"the alcohol rules refuse {refused.Count} of {pairs.Count} judged pairs: {refused.Count(x => x.Row["verdict"] != "ok")} judged wrong, near or compound");
            foreach (var x in refused) _output.WriteLine($"  {x.Row["verdict"]}: {x.Row["wording"]} -> {x.Row["description"]} ({x.Refusal!.Code}: {x.Refusal.Detail})");
            refused.Where(x => x.Row["verdict"] == "ok" && x.Reading.Kind != AlcoholSenseKind.Unclear).Select(x => $"{x.Row.Id}: {x.Row["wording"]} -> {x.Row["description"]}").Should().BeEmpty(
                "a pair judged right is refused only when its line is unclear (bare cider, bare wine), which the residue lane reads");
            refused.Count(x => x.Row["verdict"] != "ok").Should().BeGreaterThan(10, "the brandy, sherry and beer classes of the design are among the judged pairs");
        }

        [Fact]
        public async Task The_diet_gate_replay_keeps_every_drink_line_flagged_and_drops_the_false_flags_of_the_old_patterns()
        {
            var rows = GoldenSet.Read("alcohol-context.tsv").Rows.Where(r => r["status"] == GoldenSet.Ok && r.Value("title") == null).ToList();
            await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
                new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
            const long NoAlcohol = 60913, Person = 70, Household = 7;
            db.Set<ReferenceEntity>().Add(new ReferenceEntity { Id = NoAlcohol, Name = "No Alcohol" });
            db.Nutrients.Add(new NutrientEntity { Id = 5038, Name = "Alcohol", DefaultMeasurementId = 1 });
            db.HouseholdMembers.Add(new HouseholdMemberEntity { HouseholdId = Household, PersonId = Person, IsActive = true });
            db.Restrictions.Add(new RestrictionEntity { PersonId = Person, Name = "No Alcohol", RestrictionTypeId = NoAlcohol });
            var foods = rows.Select(r => (Row: r, Food: new IngredientEntity { Name = r["raw"], CurationStatusId = (long)CurationStatusEnum.PendingCuration })).ToList();
            db.Ingredients.AddRange(foods.Select(f => f.Food));
            await db.SaveChangesAsync();
            await DefaultRestrictionCriteria.EnsureAsync(db);

            var flagged = (await new HouseholdRestrictionResolver(db).ResolveAsync(Household)).IngredientIds;
            var old = OldAlcoholPatterns.Select(p => new Regex("^" + Regex.Escape(p).Replace("%", ".*") + "$", RegexOptions.IgnoreCase)).ToList();

            var lost = foods.Where(f => AlcoholLineContext.CountsForDiet(AlcoholLineContext.Read(f.Row["raw"])) && !flagged.Contains(f.Food.Id)).Select(f => f.Row["raw"]);
            var falseFlags = foods.Where(f => !AlcoholLineContext.CountsForDiet(AlcoholLineContext.Read(f.Row["raw"])) && flagged.Contains(f.Food.Id)).Select(f => f.Row["raw"]);
            var cleared = foods.Count(f => old.Any(o => o.IsMatch(f.Row["raw"])) && !flagged.Contains(f.Food.Id));
            _output.WriteLine($"{flagged.Count} of {foods.Count} golden lines flagged; {cleared} lines the old patterns flagged are cleared");

            lost.Should().BeEmpty("no line read as a drink may lose its flag");
            falseFlags.Should().BeEmpty();
            cleared.Should().BeGreaterThan(20, "vinegars, extracts, soft drinks, crumbs and ginger no longer match");
        }

        [Fact]
        public async Task The_diet_gate_replay_keeps_a_recipe_flagged_when_its_drink_line_is_linked_to_a_food_that_is_not_a_drink()
        {
            var rows = GoldenSet.Read("alcohol-context.tsv").Rows.Where(r => r["status"] == GoldenSet.Ok && r.Value("title") == null).ToList();
            await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
                new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
            const long NoAlcohol = 60913, Person = 70, Household = 7, Gram = 1;
            db.Set<ReferenceEntity>().Add(new ReferenceEntity { Id = NoAlcohol, Name = "No Alcohol" });
            db.Nutrients.Add(new NutrientEntity { Id = 5038, Name = "Alcohol", DefaultMeasurementId = Gram });
            db.HouseholdMembers.Add(new HouseholdMemberEntity { HouseholdId = Household, PersonId = Person, IsActive = true });
            db.Restrictions.Add(new RestrictionEntity { PersonId = Person, Name = "No Alcohol", RestrictionTypeId = NoAlcohol });
            var wrongFoods = new[] { "Red Wine Vinegar", "Apple Cider Vinegar", "Non-Alcoholic Wine", "Sugar" }
                .Select(n => new IngredientEntity { Name = n, CurationStatusId = (long)CurationStatusEnum.Curated }).ToList();
            foreach (var food in wrongFoods) food.IngredientNutrients.Add(new IngredientNutrientEntity { NutrientId = 5038, Amount = 0m, MeasurementId = Gram });
            db.Ingredients.AddRange(wrongFoods);
            await db.SaveChangesAsync();
            var recipes = rows.Select((r, i) => (Row: r, Food: wrongFoods[i % wrongFoods.Count], Recipe: new RecipeEntity
            {
                Name = "replay " + r.Id, AuthorId = Person, CurationStatusId = (long)CurationStatusEnum.NonCurated,
                RecipeIngredients = new List<RecipeIngredientEntity> { new() { IngredientId = wrongFoods[i % wrongFoods.Count].Id, MeasurementId = Gram, Quantity = 1m, RawLine = r["raw"] } },
            })).ToList();
            db.Recipes.AddRange(recipes.Select(x => x.Recipe));
            await db.SaveChangesAsync();
            await DefaultRestrictionCriteria.EnsureAsync(db);

            var set = await new HouseholdRestrictionResolver(db).ResolveAsync(Household);
            var service = new RecipeOrchestrationService(db, new HttpContextAccessor(), new Mock<ICurrentUserService>().Object,
                new Mock<Nom.Orch.UtilityInterfaces.IMediaStorageService>().Object, new Mock<IRecipeNutritionService>().Object);

            var lostInPlans = new List<string>();
            var lostOnPage = new List<string>();
            var falseFlags = new List<string>();
            foreach (var (row, food, recipe) in recipes)
            {
                var drink = ExpectedToCount(row);
                var planned = !set.IngredientIds.Contains(food.Id) && !set.RecipeIds.Contains(recipe.Id);
                var onPage = (await service.GetDietMatchesAsync(recipe.Id, Person)).Count > 0;
                if (drink && planned) lostInPlans.Add($"{row["raw"]} on {food.Name}");
                if (drink && !onPage) lostOnPage.Add($"{row["raw"]} on {food.Name}");
                if (!drink && (onPage || !planned)) falseFlags.Add($"{row["raw"]} on {food.Name}");
            }
            _output.WriteLine($"{recipes.Count(x => ExpectedToCount(x.Row))} of {recipes.Count} golden lines are drinks, each linked to a food that is not one");

            lostInPlans.Should().BeEmpty("every drink line keeps its recipe out of a No Alcohol household's plans, on Sugar as on a vinegar");
            lostOnPage.Should().BeEmpty("the recipe page reads every line's own text");
            falseFlags.Should().BeEmpty();
        }

        [Fact]
        public void Every_line_the_diet_gate_counts_holds_a_word_of_the_drink_probe()
        {
            var probe = DietCriteriaMatcher.WordRegex(AlcoholLineContext.DrinkNamePattern);
            var lines = GoldenSet.Read("alcohol-context.tsv").Rows.Select(r => r["raw"])
                .Concat(GoldenSet.Read("link-pairs.tsv").Rows.Select(r => r["wording"]))
                .Concat(new[]
                {
                    "1 cup rosé", "1 oz Bénédictine", "2 tbsp eau-de-vie", "1 tbsp crème de cassis", "two port wine-glassfuls of curaçoa", "1/2 cup Gewürztraminer",
                    "a glass of Sémillon", "1 bottle of Catawba", "1/2 cup vin blanc", "3 tbsp Shaohsing", "1 cup IPA", "a gill of proof spirit", "2 oz Bailey's",
                })
                .Distinct()
                .ToList();

            var missed = lines.Where(l => AlcoholLineContext.CountsForDiet(AlcoholLineContext.Read(l)) && !probe.IsMatch(l)).ToList();

            missed.Should().BeEmpty("the household set reads only the lines the probe finds, so a line the rules count must hold one of its words");
        }

        private static bool ExpectedToCount(GoldenRow row)
        {
            var sense = row["expect"];
            if (sense == AlcoholLineContext.ListSense) return AlcoholLineContext.CountsForDiet(AlcoholLineContext.Read(row["raw"]));
            return AlcoholLineContext.KindOf(sense) is AlcoholSenseKind.Drink or AlcoholSenseKind.Held or AlcoholSenseKind.Unclear || sense == "dish";
        }
    }
}
