using System;
using System.Collections.Generic;
using System.Linq;
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
using Nom.Orch.Interfaces;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// Alcohol retention (alcohol design A7, A8, owner answer 6): the USDA Release 6 alcohol rows ship
    /// as a cited table; the step reader gives no heat 100 %, a flaming up to 85 %, simmered 2 hours 10 % and
    /// "up to" the method's highest factor with no time, and an upper bound with its reason wherever the steps do not
    /// determine a single value; the recalculation lowers the alcohol by what
    /// does not stay and the energy by 6.93 kcal per gram of it; a run is journaled and undone; the
    /// recipe page's note names drinks, flavourings and drink lines that cannot be counted; and No
    /// Alcohol excludes a recipe only when more than 0.5 g of its drinks' alcohol stays per serving or
    /// a drink line cannot be counted, while Halal and Whole30 exclude any drink however long it cooks.
    /// </summary>
    public class AlcoholRetentionTests
    {
        private const long Gram = 1, Kcal = 16, Calories = 5035, Protein = 5006, Alcohol = 5038;
        private const long HouseholdId = 7, PersonId = 70, NoAlcoholRef = 60913, HalalRef = 60914, Whole30Ref = 60920;

        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
                new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        [Fact]
        public void The_table_is_the_ten_alcohol_rows_of_usda_release_6_each_with_its_citation()
        {
            AlcoholRetention.Problems(AlcoholRetention.Rows).Should().BeEmpty();
            AlcoholRetention.Rows.ToDictionary(r => r.Code, r => r.Percent).Should().BeEquivalentTo(new Dictionary<string, int>
            {
                ["5001"] = 70, ["5002"] = 85, ["5003"] = 75, ["5004"] = 40, ["5005"] = 35,
                ["5006"] = 25, ["5007"] = 20, ["5008"] = 10, ["5009"] = 5, ["5010"] = 45,
            });
            AlcoholRetention.Rows.Should().OnlyContain(r => r.Title.Contains("Release 6") && r.Page == "12" && r.Locator.Contains("retn06.pdf"));
            AlcoholRetention.Problems(AlcoholRetention.Rows.Select(r => r with { Locator = "" }).ToList()).Should().HaveCount(10);
        }

        [Theory]
        [InlineData("Stir the rum into the cold milk.|Chill and serve.", "2 tbsp rum", AlcoholRetention.NoHeat, 1.0)]
        [InlineData("Sear the steaks.|Pour in the brandy and flame.", "1/4 cup brandy", AlcoholRetention.UpTo, 0.85)]
        [InlineData("Brown the beef.|Add the red wine and simmer for 2 hours.", "1 cup red wine", AlcoholRetention.Timed, 0.10)]
        [InlineData("Add the wine and simmer until the sauce thickens.", "1 cup red wine", AlcoholRetention.UpTo, 0.85)]
        [InlineData("Mix the bourbon into the batter.|Bake until golden.", "2 tbsp bourbon", AlcoholRetention.UpTo, 0.45)]
        [InlineData("Remove from the heat and stir in the sherry.", "2 tbsp sherry", AlcoholRetention.HotLiquid, 0.85)]
        [InlineData("Pour the wine over the fruit.|Refrigerate overnight.", "1 cup wine", AlcoholRetention.Overnight, 0.70)]
        [InlineData("", "1 cup wine", AlcoholRetention.NoSteps, 1.0)]
        [InlineData("Whip the cream with the brandy and sugar; chill.|Bake the shortcakes for 20 minutes.", "2 tbsp brandy", AlcoholRetention.UpTo, 1.0)]
        [InlineData("Add the wine and simmer 5 minutes.|Meanwhile boil the pasta 10 minutes.", "1/2 cup white wine", AlcoholRetention.UpTo, 0.85)]
        [InlineData("Stir the rum into the cream and set aside.|Bake the cake for 40 minutes.", "2 tbsp rum", AlcoholRetention.UpTo, 1.0)]
        [InlineData("Add the sherry to the sauce and simmer 5 minutes.|In another pan, fry the mushrooms for 10 minutes.", "2 tbsp sherry", AlcoholRetention.UpTo, 0.85)]
        public void The_reader_gives_the_usda_factor_of_the_method_it_finds(string steps, string line, string kind, double factor)
        {
            var reading = AlcoholRetention.Read(steps.Split('|'), line);

            reading.Kind.Should().Be(kind);
            reading.Factor.Should().Be((decimal)factor);
        }

        [Theory]
        [InlineData("Mix the flour, milk, oil and vanilla until smooth.|Pour the batter into the ramekins or a baking dish.|Bake at 350 degrees for 30 minutes if using a baking dish; about 15 minutes if using ramekins.", "1 tsp vanilla", "up-to:bake:15:choice", 0.45)]
        [InlineData("Sear the steaks.|Pour in the brandy and flame.", "1/4 cup brandy", "up-to:flamed::flamed", 0.85)]
        [InlineData("Marinate the beef in the wine overnight. Grill the beef for 10 minutes.", "1/2 cup dry white wine", "up-to:simmer:10:marinade", 0.85)]
        [InlineData("Rub the butter into the flour.|Make into a paste with the eggs.|Roll out and bake in a quick oven.", "1 teaspoonful of vanilla", "up-to:no-heat::unnamed", 1.0)]
        [InlineData("Whip the cream with the brandy and sugar; chill.|Bake the shortcakes for 20 minutes.", "2 tbsp brandy", "up-to:no-heat::parts", 1.0)]
        [InlineData("Melt the butter, whisk in the flour and milk, stir in the vanilla and let it boil gently for 4 minutes.|Pour half a cup of the sauce over the tortillas and bake for 15 minutes.|Serve with the extra sauce.", "1 tablespoon vanilla", "up-to:simmer:4:lines", 0.85)]
        public void A_reading_the_steps_do_not_determine_is_an_upper_bound_with_its_reason(string steps, string line, string basis, double factor)
        {
            var reading = AlcoholRetention.Read(steps.Split('|'), line);

            (reading.Basis, reading.Factor).Should().Be((basis, (decimal)factor));
            reading.UpTo.Should().BeTrue();
            reading.Estimated.Should().BeTrue();
            reading.Reason.Should().Be(basis.Split(':')[3]);
        }

        [Theory]
        [InlineData("Combine all ingredients except the rum in a saucepan and simmer for 30 minutes. Remove from heat and stir in the rum. Serve.", "1/4 cup dark rum", "hot-liquid", 0.85)]
        [InlineData("Put all the ingredients except the sherry into a pan and simmer for 20 minutes.|Add the sherry and serve.", "2 tbsp sherry", "hot-liquid", 0.85)]
        [InlineData("Mix everything but the brandy and bake 1 hour. Brush the warm cake with the brandy.", "3 tbsp brandy", "no-heat", 1.0)]
        [InlineData("Combine all ingredients except the rum and simmer for 30 minutes.|Serve.", "1/4 cup dark rum", "up-to:no-heat::unnamed", 1.0)]
        [InlineData("Add half the wine and simmer for 30 minutes.|Stir in the remaining wine just before serving.", "1 cup red wine", "up-to:hot-liquid::lines", 0.85)]
        [InlineData("Add half of the brandy to the fruit and bake for 45 minutes.|Sprinkle the rest of the brandy over the hot pudding and serve.", "1/4 cup brandy", "up-to:hot-liquid::lines", 0.85)]
        [InlineData("Add half the wine, simmer for 30 minutes, then stir in the remaining wine.", "1 cup red wine", "up-to:hot-liquid::lines", 0.85)]
        [InlineData("Add the wine after the sauce has simmered for 30 minutes. Serve.", "1/2 cup red wine", "no-heat", 1.0)]
        [InlineData("Stir in the brandy after simmering the sauce for 20 minutes.|Serve hot.", "2 tbsp brandy", "no-heat", 1.0)]
        public void A_drink_held_back_or_added_again_after_the_heat_is_not_read_as_cooked_by_the_heat_before_it_goes_in(string steps, string line, string basis, double factor)
        {
            var reading = AlcoholRetention.Read(steps.Split('|'), line);

            (reading.Basis, reading.Factor).Should().Be((basis, (decimal)factor));
        }

        [Theory]
        [InlineData("Add the wine and simmer for 30 minutes.|Once it has simmered, stir in the cream and simmer for 10 minutes.", "1 cup red wine", "timed:simmer:40", 0.35)]
        [InlineData("Stir in the brandy and simmer for 30 minutes.|Pour the brandy mixture into the dish and bake for 20 minutes.", "2 tbsp brandy", "timed:simmer:50", 0.35)]
        [InlineData("Add the rum and simmer for 30 minutes.|Taste and season with salt.", "2 tbsp rum", "timed:simmer:30", 0.35)]
        public void A_drink_named_again_in_its_run_without_being_added_again_keeps_its_single_value(string steps, string line, string basis, double factor)
        {
            var reading = AlcoholRetention.Read(steps.Split('|'), line);

            (reading.Basis, reading.Factor).Should().Be((basis, (decimal)factor));
        }

        [Fact]
        public void An_upper_bound_keeps_the_row_of_what_is_certain_and_its_basis_carries_the_reason()
        {
            var timed = AlcoholRetention.Bound(AlcoholRetention.Of(AlcoholRetention.Timed, AlcoholRetention.Simmer, 60), AlcoholRetention.ReasonPause);
            (timed.Basis, timed.Factor, timed.Code).Should().Be(("up-to:simmer:60:pause", 0.25m, "5006"));
            var shortBake = AlcoholRetention.Bound(AlcoholRetention.Of(AlcoholRetention.Timed, AlcoholRetention.Bake, 20), AlcoholRetention.ReasonParts);
            (shortBake.Basis, shortBake.Factor, shortBake.Code).Should().Be(("up-to:bake:20:parts", 0.45m, "5010"));
            var none = AlcoholRetention.Bound(AlcoholRetention.Of(AlcoholRetention.NoHeat), AlcoholRetention.ReasonUnnamed);
            (none.Basis, none.Factor, none.Code).Should().Be(("up-to:no-heat::unnamed", 1m, (string?)null));
            AlcoholRetention.Bound(AlcoholRetention.Of(AlcoholRetention.HotLiquid), AlcoholRetention.ReasonLines).Factor.Should().Be(0.85m);
            AlcoholRetention.Bound(AlcoholRetention.Of(AlcoholRetention.Flamed), AlcoholRetention.ReasonFlamed).Factor.Should().Be(0.85m, "how long a flame burns is not stated, so only the warmed row 5002 is certain");

            AlcoholRetention.ReadBasis("up-to:no-heat::parts").Should().Be((AlcoholRetention.UpTo, AlcoholRetention.NoHeat, (int?)null, AlcoholRetention.ReasonParts));
            AlcoholRetention.ReadBasis("up-to:simmer:12").Should().Be((AlcoholRetention.UpTo, AlcoholRetention.Simmer, (int?)12, (string?)null));
            AlcoholRetention.BasisOf(AlcoholRetention.UpTo, AlcoholRetention.Simmer, 12).Should().Be("up-to:simmer:12", "heat with no time the reader can take keeps the basis it always had");
            AlcoholRetention.Reasons.Should().OnlyHaveUniqueItems().And.HaveCount(7);
        }

        [Fact]
        public async Task An_upper_bound_is_counted_by_the_run_shown_as_up_to_and_used_by_no_alcohol()
        {
            using var db = NewContext();
            var marinated = await SeedRecipeAsync(db, wineGrams: 236m, servings: 4, steps: "Marinate the beef in the wine overnight. Grill the beef for 10 minutes.");
            await SeedRecipeAsync(db);

            var run = await Service(db).RunAsync(apply: true);

            (run.UpperBound, run.RecipesUpperBound).Should().Be((1, 1));
            run.ByReason.Should().BeEquivalentTo(new Dictionary<string, int> { [AlcoholRetention.ReasonMarinade] = 1 });
            run.Samples.Should().ContainSingle(s => s.Reason == AlcoholRetention.ReasonMarinade);
            var recipe = await db.Recipes.AsNoTracking().Include(r => r.RecipeIngredients!).ThenInclude(ri => ri.Ingredient).Include(r => r.RecipeIngredients!).ThenInclude(ri => ri.Measurement)
                .SingleAsync(r => r.Id == marinated.RecipeId);
            var note = RecipeNutritionService.AlcoholNote(recipe, new Dictionary<long, decimal> { [marinated.WineId] = 10.3m });
            (note!.UpTo, note.Reason, note.RetainedPercent, note.Basis).Should().Be((true, AlcoholRetention.ReasonMarinade, (int?)85, AlcoholRetention.UpTo));

            var set = await ResolveAsync(db, NoAlcoholRef, "No Alcohol");

            set.RecipeIds.Should().Contain(marinated.RecipeId, "the bound, up to 85 % of 24 g, is what the gate counts");
            (await Orchestration(db).GetDietMatchesAsync(marinated.RecipeId, PersonId)).Should().ContainSingle()
                .Which.Notes.Should().StartWith("Up to ").And.Contain("stays after cooking");
        }

        [Theory]
        [InlineData("Simmer for 30 minutes.", 30)]
        [InlineData("Simmer 1 1/2 hours.", 90)]
        [InlineData("Simmer 1-1/2 hours.", 90)]
        [InlineData("Simmer 1½ hours.", 90)]
        [InlineData("Simmer for 1.5 hours.", 90)]
        [InlineData("Simmer for an hour and a half.", 90)]
        [InlineData("Simmer for half an hour.", 30)]
        [InlineData("Simmer for 1 hour 15 minutes.", 75)]
        [InlineData("Simmer for 20 to 25 minutes.", 20)]
        [InlineData("Simmer for two hours.", 120)]
        [InlineData("Bake at 350°F until set.", null)]
        public void A_cooking_time_is_read_as_whole_minutes_at_the_lower_end(string sentence, int? minutes)
        {
            AlcoholRetention.MinutesIn(sentence).Should().Be(minutes);
        }

        [Fact]
        public void Energy_falls_by_six_point_nine_three_kcal_for_each_gram_of_alcohol_that_does_not_stay()
        {
            RecipeNutritionService.Retained("Alcohol", 12.2m, 7.9m).Should().Be(4.3m);
            RecipeNutritionService.Retained("Calories", 97m, 7.9m).Should().Be(97m - 7.9m * 6.93m);
            RecipeNutritionService.Retained("Protein", 0.1m, 7.9m).Should().Be(0.1m);
            RecipeNutritionService.Retained("Calories", 10m, 7.9m).Should().Be(0m, "energy never falls below zero");
            RecipeNutritionService.Retained("Calories", 97m, 0m).Should().Be(97m);
        }

        private sealed record Seeded(long RecipeId, long WineId, long SugarId, long VanillaId);

        private static async Task<Seeded> SeedRecipeAsync(ApplicationDbContext db, decimal wineGrams = 118m, long? servings = 4, string steps = "Add the wine and simmer 30 minutes.")
        {
            if (!await db.Set<BaseMeasurementEntity>().AnyAsync())
            {
                db.Set<BaseMeasurementEntity>().AddRange(
                    new BaseMeasurementEntity { Id = Gram, Name = "Gram", Symbol = "g", MeasurementCategoryId = 1, IsBaseUnit = true, BaseUnitConversionFactor = 1m },
                    new BaseMeasurementEntity { Id = Kcal, Name = "Kilocalorie", Symbol = "kcal", MeasurementCategoryId = 5, BaseUnitConversionFactor = 1m });
                db.Nutrients.AddRange(
                    new NutrientEntity { Id = Calories, Name = "Calories", DefaultMeasurementId = Kcal },
                    new NutrientEntity { Id = Protein, Name = "Protein", DefaultMeasurementId = Gram },
                    new NutrientEntity { Id = Alcohol, Name = "Alcohol", DefaultMeasurementId = Gram });
                await db.SaveChangesAsync();
            }
            var wine = new IngredientEntity { Name = "Dry White Wine", CurationStatusId = (long)CurationStatusEnum.Curated, FdcCategoryId = 14 };
            wine.IngredientNutrients.Add(new IngredientNutrientEntity { NutrientId = Calories, Amount = 82m, MeasurementId = Kcal });
            wine.IngredientNutrients.Add(new IngredientNutrientEntity { NutrientId = Protein, Amount = 0.1m, MeasurementId = Gram });
            wine.IngredientNutrients.Add(new IngredientNutrientEntity { NutrientId = Alcohol, Amount = 10.3m, MeasurementId = Gram });
            var sugar = new IngredientEntity { Name = "Sugar", CurationStatusId = (long)CurationStatusEnum.Curated };
            sugar.IngredientNutrients.Add(new IngredientNutrientEntity { NutrientId = Calories, Amount = 387m, MeasurementId = Kcal });
            sugar.IngredientNutrients.Add(new IngredientNutrientEntity { NutrientId = Alcohol, Amount = 0m, MeasurementId = Gram });
            var vanilla = new IngredientEntity { Name = "Vanilla Extract", CurationStatusId = (long)CurationStatusEnum.Curated, FdcCategoryId = 2 };
            vanilla.IngredientNutrients.Add(new IngredientNutrientEntity { NutrientId = Alcohol, Amount = 34.4m, MeasurementId = Gram });
            db.Ingredients.AddRange(wine, sugar, vanilla);
            await db.SaveChangesAsync();
            var recipe = new RecipeEntity { Name = "Fish in wine", AuthorId = PersonId, Servings = servings, CurationStatusId = (long)CurationStatusEnum.NonCurated };
            recipe.RecipeSteps = new List<RecipeStepEntity> { new() { StepNumber = 1, Summary = "Cook", Description = steps } };
            recipe.RecipeIngredients = new List<RecipeIngredientEntity>
            {
                new() { IngredientId = wine.Id, Quantity = wineGrams, MeasurementId = Gram, RawLine = "1/2 cup dry white wine", SortOrder = 0 },
            };
            db.Recipes.Add(recipe);
            await db.SaveChangesAsync();
            return new Seeded(recipe.Id, wine.Id, sugar.Id, vanilla.Id);
        }

        private static AlcoholRetentionService Service(ApplicationDbContext db) =>
            new(db, new RecipeNutritionService(db, NullLogger<RecipeNutritionService>.Instance), NullLogger<AlcoholRetentionService>.Instance);

        [Fact]
        public async Task A_run_stores_the_retention_journals_it_recalculates_the_label_and_its_undo_gives_everything_back()
        {
            using var db = NewContext();
            var seeded = await SeedRecipeAsync(db);
            var nutrition = new RecipeNutritionService(db, NullLogger<RecipeNutritionService>.Instance);
            await nutrition.RecalculateAsync(seeded.RecipeId);
            async Task<decimal> Label(long nutrientId) => (await db.RecipeNutrition.AsNoTracking().SingleAsync(n => n.RecipeId == seeded.RecipeId && n.NutrientId == nutrientId)).Amount;
            var rawAlcohol = await Label(Alcohol);
            var rawCalories = await Label(Calories);

            var dry = await Service(db).RunAsync(apply: false);
            dry.Changed.Should().Be(1);
            dry.Samples.Single().NewBasis.Should().Be("timed:simmer:30");
            (await db.RecipeIngredients.AsNoTracking().SingleAsync()).AlcoholRetention.Should().BeNull("a dry run writes nothing");

            var applied = await Service(db).RunAsync(apply: true);
            applied.RecipesRecalculated.Should().Be(1);
            applied.ByKind.Should().BeEquivalentTo(new Dictionary<string, int> { [AlcoholRetention.Timed] = 1 });
            var line = await db.RecipeIngredients.AsNoTracking().SingleAsync();
            (line.AlcoholRetention, line.AlcoholRetentionBasis).Should().Be((0.35m, "timed:simmer:30"));
            var journal = await db.AuditLogEntries.AsNoTracking().SingleAsync(a => a.ChangeType == AlcoholRetentionService.ChangeType);
            (journal.OldValue, journal.NewValue).Should().Be((string.Empty, "0.3500|timed:simmer:30"));

            var lost = 118m * 10.3m / 100m * 0.65m;
            (await Label(Alcohol)).Should().BeApproximately(rawAlcohol - lost / 4m, 0.0001m);
            (await Label(Calories)).Should().BeApproximately(rawCalories - lost * 6.93m / 4m, 0.0001m);
            (await Label(Protein)).Should().BeApproximately(118m * 0.1m / 100m / 4m, 0.0001m);

            (await Service(db).RunAsync(apply: true)).Changed.Should().Be(0, "a second run finds nothing new");

            var undoDry = await Service(db).UndoAsync(applied.StampUtc.AddSeconds(-1));
            undoDry.Restored.Should().Be(1);
            var undo = await Service(db).UndoAsync(applied.StampUtc.AddSeconds(-1), apply: true);
            undo.RecipesRecalculated.Should().Be(1);
            (await db.RecipeIngredients.AsNoTracking().SingleAsync()).AlcoholRetention.Should().BeNull();
            (await db.AuditLogEntries.CountAsync(a => a.ChangeType == AlcoholRetentionService.ChangeType)).Should().Be(0);
            (await Label(Alcohol)).Should().Be(rawAlcohol);
            (await Label(Calories)).Should().Be(rawCalories);
        }

        [Theory]
        [InlineData("Add the wine and simmer 30 minutes.", false, false)]
        [InlineData("Add the wine and simmer until thick.", true, false)]
        [InlineData("Mix all the ingredients and simmer 30 minutes.", true, true)]
        [InlineData("Simmer the fish 30 minutes.|Serve with a brandy butter.", true, true)]
        [InlineData("Simmer the fish 30 minutes.", true, true)]
        public void A_reading_is_an_estimate_when_no_step_names_the_drink_or_the_heat_has_no_time(string steps, bool estimated, bool unnamed)
        {
            var reading = AlcoholRetention.Read(steps.Split('|'), "1/2 cup dry white wine");

            (reading.Estimated, reading.Unnamed).Should().Be((estimated, unnamed));
        }

        [Fact]
        public async Task A_run_reads_two_lines_of_one_drink_each_from_its_own_turn_in_the_steps()
        {
            using var db = NewContext();
            var seeded = await SeedRecipeAsync(db, steps: "Beat the eggs, bananas and vanilla. Bake for 55 minutes. Cool completely. For the frosting, beat the butter, sugar and vanilla until smooth.");
            db.RecipeIngredients.AddRange(
                new RecipeIngredientEntity { RecipeId = seeded.RecipeId, IngredientId = seeded.VanillaId, Quantity = 4m, MeasurementId = Gram, RawLine = "1 tsp vanilla", SortOrder = 1 },
                new RecipeIngredientEntity { RecipeId = seeded.RecipeId, IngredientId = seeded.VanillaId, Quantity = 4m, MeasurementId = Gram, RawLine = "1 tsp vanilla", SortOrder = 2 });
            await db.SaveChangesAsync();

            await Service(db).RunAsync(apply: true);

            var lines = await db.RecipeIngredients.AsNoTracking().Where(l => l.IngredientId == seeded.VanillaId).OrderBy(l => l.SortOrder).ToListAsync();
            lines.Select(l => (l.AlcoholRetention, l.AlcoholRetentionBasis)).Should().Equal(new[] { ((decimal?)0.35m, "timed:bake:55"), ((decimal?)1m, "no-heat") },
                "staging checkpoint 6, R14 (23883): the frosting's vanilla, the second line, is not read with the batter's bake");
        }

        [Fact]
        public async Task A_run_counts_the_lines_it_reads_as_estimates_and_marks_them_in_its_sample()
        {
            using var db = NewContext();
            await SeedRecipeAsync(db, steps: "Mix all the ingredients well together and simmer 30 minutes.");
            await SeedRecipeAsync(db);

            var dry = await Service(db).RunAsync(apply: false);

            (dry.Lines, dry.Estimated, dry.Unnamed).Should().Be((2, 1, 1),
                "staging checkpoint 4, P15 (review CK9S-4): a factor read from a step that names no drink is counted as estimated");
            dry.Samples.Select(s => (s.NewBasis, s.Estimated, s.Unnamed)).Should().BeEquivalentTo(new[]
            {
                ("timed:simmer:30", true, true),
                ("timed:simmer:30", false, false),
            });
        }

        [Fact]
        public async Task The_undo_dry_run_counts_what_the_undo_then_restores_when_two_runs_changed_one_line()
        {
            using var db = NewContext();
            var seeded = await SeedRecipeAsync(db);
            var first = await Service(db).RunAsync(apply: true);
            var step = await db.Set<RecipeStepEntity>().SingleAsync(s => s.RecipeId == seeded.RecipeId);
            step.Description = "Add the wine and simmer 1 hour.";
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            (await Service(db).RunAsync(apply: true)).Changed.Should().Be(1);
            (await db.RecipeIngredients.AsNoTracking().SingleAsync()).AlcoholRetention.Should().Be(0.25m);

            var dry = await Service(db).UndoAsync(first.StampUtc.AddSeconds(-1));
            var undo = await Service(db).UndoAsync(first.StampUtc.AddSeconds(-1), apply: true);

            (dry.Restored, dry.Skipped).Should().Be((2, 0));
            (undo.Restored, undo.Skipped).Should().Be((dry.Restored, dry.Skipped));
            (await db.RecipeIngredients.AsNoTracking().SingleAsync()).AlcoholRetention.Should().BeNull();
        }

        [Theory]
        [InlineData(NoAlcoholRef, "No Alcohol")]
        [InlineData(HalalRef, "Halal")]
        public async Task A_drink_line_linked_to_a_flavouring_food_keeps_its_recipe_excluded(long typeId, string name)
        {
            using var db = NewContext();
            var cream = await SeedRecipeAsync(db, wineGrams: 0m, servings: 12, steps: "Whip the cream with the brandy and sugar.");
            var line = await db.RecipeIngredients.SingleAsync(ri => ri.RecipeId == cream.RecipeId);
            db.RecipeIngredients.Remove(line);
            db.RecipeIngredients.Add(new RecipeIngredientEntity { RecipeId = cream.RecipeId, IngredientId = cream.VanillaId, Quantity = 60m, MeasurementId = Gram, RawLine = "1/4 cup brandy", SortOrder = 0 });
            await db.SaveChangesAsync();

            var set = await ResolveAsync(db, typeId, name);

            set.RecipeIds.Should().Contain(cream.RecipeId, "the line reads as brandy whatever food it is linked to, and a wrong food cannot count it");
            var recipe = await db.Recipes.AsNoTracking().Include(r => r.RecipeIngredients!).ThenInclude(ri => ri.Ingredient).Include(r => r.RecipeIngredients!).ThenInclude(ri => ri.Measurement)
                .SingleAsync(r => r.Id == cream.RecipeId);
            var note = RecipeNutritionService.AlcoholNote(recipe, new Dictionary<long, decimal> { [cream.VanillaId] = 34.4m });
            note!.Flavourings.Should().BeEmpty();
            note.Uncounted.Should().Equal("1/4 cup brandy");
            if (typeId == HalalRef) return;
            (await Orchestration(db).GetDietMatchesAsync(cream.RecipeId, PersonId)).Should().ContainSingle()
                .Which.IngredientName.Should().Be("1/4 cup brandy");
        }

        [Fact]
        public async Task The_note_names_the_drinks_the_flavourings_and_the_drink_lines_that_cannot_be_counted()
        {
            using var db = NewContext();
            var seeded = await SeedRecipeAsync(db);
            db.RecipeIngredients.AddRange(
                new RecipeIngredientEntity { RecipeId = seeded.RecipeId, IngredientId = seeded.VanillaId, Quantity = 4m, MeasurementId = Gram, RawLine = "1 tsp vanilla extract", SortOrder = 1 },
                new RecipeIngredientEntity { RecipeId = seeded.RecipeId, IngredientId = seeded.SugarId, Quantity = 1m, MeasurementId = Gram, RawLine = "a glass of sherry", SortOrder = 2 });
            await db.SaveChangesAsync();
            await Service(db).RunAsync(apply: true);
            var recipe = await db.Recipes.AsNoTracking().Include(r => r.RecipeIngredients!).ThenInclude(ri => ri.Ingredient).Include(r => r.RecipeIngredients!).ThenInclude(ri => ri.Measurement)
                .SingleAsync(r => r.Id == seeded.RecipeId);

            var note = RecipeNutritionService.AlcoholNote(recipe, new Dictionary<long, decimal> { [seeded.WineId] = 10.3m, [seeded.VanillaId] = 34.4m, [seeded.SugarId] = 0m });

            note.Should().NotBeNull();
            note!.Drinks.Should().Equal("Dry White Wine");
            note.Flavourings.Should().Equal("Vanilla Extract");
            note.Uncounted.Should().Equal("a glass of sherry");
            note.PerServing.Should().BeTrue();
            note.AsWrittenGrams.Should().Be(Math.Round(118m * 0.103m / 4m, 2));
            note.RetainedGrams.Should().Be(Math.Round(118m * 0.103m * 0.35m / 4m, 2));
            (note.Basis, note.Method, note.Minutes, note.RetainedPercent, note.UsdaCode, note.NotRead).Should().Be((AlcoholRetention.Timed, AlcoholRetention.Simmer, (int?)30, (int?)35, "5005", false));
            RecipeNutritionService.AlcoholNote(new RecipeEntity { Name = "Toast", RecipeIngredients = new List<RecipeIngredientEntity>() }, new Dictionary<long, decimal>()).Should().BeNull();
        }

        private static async Task<RestrictedIngredientSet> ResolveAsync(ApplicationDbContext db, long typeId, string name)
        {
            db.Set<ReferenceEntity>().AddRange(
                new ReferenceEntity { Id = NoAlcoholRef, Name = "No Alcohol" },
                new ReferenceEntity { Id = HalalRef, Name = "Halal" },
                new ReferenceEntity { Id = Whole30Ref, Name = "Whole30" });
            db.HouseholdMembers.Add(new HouseholdMemberEntity { HouseholdId = HouseholdId, PersonId = PersonId, IsActive = true });
            db.Restrictions.Add(new RestrictionEntity { PersonId = PersonId, Name = name, RestrictionTypeId = typeId });
            await db.SaveChangesAsync();
            await DefaultRestrictionCriteria.EnsureAsync(db);
            return await new HouseholdRestrictionResolver(db).ResolveAsync(HouseholdId);
        }

        private static RecipeOrchestrationService Orchestration(ApplicationDbContext db) =>
            new(db, new HttpContextAccessor(), new Mock<ICurrentUserService>().Object, new Mock<Nom.Orch.UtilityInterfaces.IMediaStorageService>().Object, new Mock<IRecipeNutritionService>().Object);

        [Fact]
        public async Task No_alcohol_excludes_a_recipe_only_above_half_a_gram_of_retained_drink_alcohol_per_serving()
        {
            const long typeId = NoAlcoholRef;
            const string name = "No Alcohol";
            using var db = NewContext();
            var stew = await SeedRecipeAsync(db, wineGrams: 236m, servings: 8, steps: "Add the wine and braise for 2 1/2 hours.");
            var unread = await SeedRecipeAsync(db, wineGrams: 236m, servings: 8, steps: "Add the wine and braise for 2 1/2 hours.");
            var spritzer = await SeedRecipeAsync(db, wineGrams: 118m, servings: 1, steps: "Top the wine with soda water.");
            var cake = await SeedRecipeAsync(db, wineGrams: 0m, servings: 8, steps: "Beat in the vanilla. Bake 30 minutes.");
            await Service(db).RunAsync(apply: true, afterRecipeId: 0);
            var unreadLine = await db.RecipeIngredients.SingleAsync(ri => ri.RecipeId == unread.RecipeId);
            unreadLine.AlcoholRetention = null;
            unreadLine.AlcoholRetentionBasis = null;
            var cakeLine = await db.RecipeIngredients.SingleAsync(ri => ri.RecipeId == cake.RecipeId);
            db.RecipeIngredients.Remove(cakeLine);
            db.RecipeIngredients.Add(new RecipeIngredientEntity { RecipeId = cake.RecipeId, IngredientId = cake.VanillaId, Quantity = 8m, MeasurementId = Gram, RawLine = "2 tsp vanilla extract", SortOrder = 0 });
            var toTaste = await SeedRecipeAsync(db, wineGrams: 0m, servings: 4);
            var toTasteLine = await db.RecipeIngredients.SingleAsync(ri => ri.RecipeId == toTaste.RecipeId);
            toTasteLine.RawLine = "white wine to taste";
            await db.SaveChangesAsync();

            var set = await ResolveAsync(db, typeId, name);

            set.RecipeIds.Should().NotContain(stew.RecipeId, "236 g of wine braised 2.5 hours keeps 5 %: about 0.15 g per serving of 8");
            set.RecipeIds.Should().NotContain(cake.RecipeId, "a flavouring gets a note, never an exclusion");
            set.RecipeIds.Should().Contain(unread.RecipeId, "with no retention read, all of the wine counts: about 3 g per serving");
            set.RecipeIds.Should().Contain(spritzer.RecipeId, "an uncooked glass of wine keeps all of its alcohol");
            set.RecipeIds.Should().Contain(toTaste.RecipeId, "a drink line with no amount cannot be counted, so it is the safe side");
            set.IngredientIds.Should().NotContain(new[] { stew.WineId, cake.VanillaId });

            var service = Orchestration(db);
            (await service.GetDietMatchesAsync(stew.RecipeId, PersonId)).Should().BeEmpty();
            (await service.GetDietMatchesAsync(cake.RecipeId, PersonId)).Should().BeEmpty();
            (await service.GetDietMatchesAsync(unread.RecipeId, PersonId)).Should().ContainSingle()
                .Which.Notes.Should().Contain("stays after cooking").And.Contain("0.5 g");
            (await service.GetDietMatchesAsync(toTaste.RecipeId, PersonId)).Should().ContainSingle()
                .Which.IngredientName.Should().Be("white wine to taste");
        }

        [Theory]
        [InlineData(HalalRef, "Halal")]
        [InlineData(Whole30Ref, "Whole30")]
        public async Task Halal_and_whole30_exclude_any_drink_however_long_it_cooks_and_leave_flavourings_to_a_note(long typeId, string name)
        {
            using var db = NewContext();
            var stew = await SeedRecipeAsync(db, wineGrams: 236m, servings: 8, steps: "Add the wine and braise for 2 1/2 hours.");
            var cake = await SeedRecipeAsync(db, wineGrams: 0m, servings: 8, steps: "Beat in the vanilla. Bake 30 minutes.");
            var cakeLine = await db.RecipeIngredients.SingleAsync(ri => ri.RecipeId == cake.RecipeId);
            db.RecipeIngredients.Remove(cakeLine);
            db.RecipeIngredients.Add(new RecipeIngredientEntity { RecipeId = cake.RecipeId, IngredientId = cake.VanillaId, Quantity = 8m, MeasurementId = Gram, RawLine = "2 tsp vanilla extract", SortOrder = 0 });
            await db.SaveChangesAsync();
            await Service(db).RunAsync(apply: true, afterRecipeId: 0);
            (await db.RecipeIngredients.AsNoTracking().SingleAsync(ri => ri.RecipeId == stew.RecipeId)).AlcoholRetention.Should().Be(0.05m);

            var set = await ResolveAsync(db, typeId, name);

            set.IngredientIds.Should().Contain(stew.WineId, "about 0.15 g per serving stays, and any drink is excluded");
            set.IngredientIds.Should().NotContain(cake.VanillaId, "a flavouring gets a note, never an exclusion");
            set.RecipeIds.Should().NotContain(cake.RecipeId);
            var service = Orchestration(db);
            (await service.GetDietMatchesAsync(stew.RecipeId, PersonId)).Should().NotBeEmpty();
            (await service.GetDietMatchesAsync(cake.RecipeId, PersonId)).Should().BeEmpty();
        }

        [Fact]
        public async Task Ensure_moves_an_untouched_uncapped_alcohol_row_to_the_capped_default()
        {
            using var db = NewContext();
            await SeedRecipeAsync(db);
            db.Set<ReferenceEntity>().Add(new ReferenceEntity { Id = NoAlcoholRef, Name = "No Alcohol" });
            db.Set<RestrictionCriterionEntity>().Add(new RestrictionCriterionEntity { RestrictionTypeId = NoAlcoholRef, NutrientId = Alcohol, Severity = 3, Notes = DefaultRestrictionCriteria.DefaultNote, CreatedDate = DateTime.UtcNow });
            await db.SaveChangesAsync();

            await DefaultRestrictionCriteria.EnsureAsync(db);

            (await db.Set<RestrictionCriterionEntity>().Where(c => c.RestrictionTypeId == NoAlcoholRef && c.NutrientId == Alcohol).Select(c => c.MaxAmountPerServing).ToListAsync())
                .Should().Equal(0.5m);
        }

        [Fact]
        public async Task Ensure_moves_an_untouched_capped_halal_alcohol_row_back_to_the_uncapped_default()
        {
            using var db = NewContext();
            await SeedRecipeAsync(db);
            db.Set<ReferenceEntity>().Add(new ReferenceEntity { Id = HalalRef, Name = "Halal" });
            db.Set<RestrictionCriterionEntity>().Add(new RestrictionCriterionEntity { RestrictionTypeId = HalalRef, NutrientId = Alcohol, MaxAmountPerServing = 0.5m, Severity = 3, Notes = DefaultRestrictionCriteria.DefaultNote, CreatedDate = DateTime.UtcNow });
            await db.SaveChangesAsync();

            await DefaultRestrictionCriteria.EnsureAsync(db);

            (await db.Set<RestrictionCriterionEntity>().Where(c => c.RestrictionTypeId == HalalRef && c.NutrientId == Alcohol).Select(c => c.MaxAmountPerServing).ToListAsync())
                .Should().Equal(new decimal?[] { null });
        }

        [Fact]
        public void The_alcohol_line_query_reads_the_line_table_alone()
        {
            using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=localhost;Database=none").Options,
                new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

            var sql = DietCriteriaMatcher.AlcoholLineRows(db, new List<long> { 1, 2 }, new List<long> { 3 }).ToQueryString();

            sql.Should().Contain("FROM recipe.\"RecipeIngredient\"").And.Contain("\"AlcoholRetention\"").And.Contain("\"AlcoholRetentionBasis\"");
            sql.Should().NotContain("JOIN");
        }
    }
}
