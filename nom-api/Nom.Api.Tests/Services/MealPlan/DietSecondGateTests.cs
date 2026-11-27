using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Person;
using Nom.Data.Plan;
using Nom.Data.Platform;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services.MealPlan
{
    /// <summary>
    /// Plan item ck21: under the platform setting diet-second-reader-gate the stored gate shows a recipe to a severe type of
    /// the second reader only when the rules find it clear and both stored readings of the pair call it clearly free; a recipe
    /// not yet read is held as not yet read. With the setting off the build reads no reading and decides as before.
    /// </summary>
    public class DietSecondGateTests
    {
        private const long PersonId = 80, HouseholdId = 8, CeliacRef = 60800, VegetarianRef = 60850, Gram = 1;
        private const string AllFree = "fffffffffff";

        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
                new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        private sealed record Kitchen(long Bread, long Pilaf, long Salad);

        private static RecipeEntity Recipe(string name, params (long Food, string Raw)[] lines)
        {
            var recipe = new RecipeEntity { Name = name, AuthorId = PersonId, CurationStatusId = (long)CurationStatusEnum.Curated, Visibility = RecipeVisibilityEnum.Public, RecipeIngredients = new List<RecipeIngredientEntity>() };
            var order = 0;
            foreach (var (food, raw) in lines)
                recipe.RecipeIngredients.Add(new RecipeIngredientEntity { IngredientId = food, MeasurementId = Gram, Quantity = 1m, RawLine = raw, SortOrder = order++ });
            return recipe;
        }

        private static async Task<Kitchen> SeedAsync(ApplicationDbContext db)
        {
            db.HouseholdMembers.Add(new HouseholdMemberEntity { HouseholdId = HouseholdId, PersonId = PersonId, IsActive = true });
            db.Set<ReferenceEntity>().AddRange(new ReferenceEntity { Id = CeliacRef, Name = "Celiac Disease" }, new ReferenceEntity { Id = VegetarianRef, Name = "Vegetarian" });
            var flour = new IngredientEntity { Name = "Wheat Flour", CurationStatusId = (long)CurationStatusEnum.Curated };
            var rice = new IngredientEntity { Name = "Rice", CurationStatusId = (long)CurationStatusEnum.Curated };
            var water = new IngredientEntity { Name = "Water", CurationStatusId = (long)CurationStatusEnum.Curated };
            var lettuce = new IngredientEntity { Name = "Lettuce", CurationStatusId = (long)CurationStatusEnum.Curated };
            db.Ingredients.AddRange(flour, rice, water, lettuce);
            await db.SaveChangesAsync();
            var bread = Recipe("Bread", (flour.Id, "3 cups wheat flour"), (water.Id, "1 cup water"));
            var pilaf = Recipe("Pilaf", (rice.Id, "1 cup rice"), (water.Id, "2 cups water"));
            pilaf.RecipeSteps = new List<RecipeStepEntity> { new() { StepNumber = 1, Summary = "Boil", Description = "Boil the rice  in the water." } };
            var salad = Recipe("Salad", (lettuce.Id, "1 head lettuce"));
            db.Recipes.AddRange(bread, pilaf, salad);
            db.Restrictions.AddRange(
                new RestrictionEntity { PersonId = PersonId, Name = "Celiac Disease", RestrictionTypeId = CeliacRef },
                new RestrictionEntity { PersonId = PersonId, Name = "Vegetarian", RestrictionTypeId = VegetarianRef });
            await db.SaveChangesAsync();
            await DefaultRestrictionCriteria.EnsureAsync(db);
            return new Kitchen(bread.Id, pilaf.Id, salad.Id);
        }

        private static string PilafText => DietSecondGate.TextSignature("Pilaf", new[] { "1 cup rice", "2 cups water" }, new[] { "Boil the rice in the water." });

        private static string SaladText => DietSecondGate.TextSignature("Salad", new[] { "1 head lettuce" }, Array.Empty<string>());

        private static string BreadText => DietSecondGate.TextSignature("Bread", new[] { "3 cups wheat flour", "1 cup water" }, Array.Empty<string>());

        private static void Read(ApplicationDbContext db, long recipe, string text, string mistral, string phi, string? prompt = null)
        {
            foreach (var (model, letters) in new[] { (DietSecondGate.Pair[0], mistral), (DietSecondGate.Pair[1], phi) })
            {
                if (letters.Length == 0) continue;
                db.RecipeDietReadings.Add(new RecipeDietReadingEntity
                {
                    RecipeId = recipe, Model = model, PromptSignature = prompt ?? OllamaDietSecondReader.Signature, TextSignature = text, StoreVersion = DietSecondGate.Version,
                    Readable = true, Complete = true, Verdicts = letters, ReadAt = DateTime.UtcNow,
                });
            }
        }

        private static void TurnOn(ApplicationDbContext db) =>
            db.PlatformFeatures.Add(new PlatformFeatureEntity { Key = DietSecondGate.FeatureKey, IsEnabled = true });

        private static async Task<Dictionary<long, DietRecipeFlagEntity>> FlagsAsync(ApplicationDbContext db, long typeId)
        {
            var sets = await db.DietGateSets.Where(s => s.RestrictionTypeId == typeId && s.MinimumSeverity == 0).Select(s => s.Id).ToListAsync();
            return await db.DietRecipeFlags.Where(f => sets.Contains(f.SetId)).ToDictionaryAsync(f => f.RecipeId);
        }

        [Fact]
        public async Task With_the_setting_off_the_build_reads_no_reading_and_decides_exactly_as_without_the_reader()
        {
            using var plain = NewContext();
            await SeedAsync(plain);
            await new DietGateStoreBuilder().RunAsync(plain);

            using var db = NewContext();
            var k = await SeedAsync(db);
            Read(db, k.Pilaf, PilafText, "cffffffffff", "cffffffffff");
            db.PlatformFeatures.Add(new PlatformFeatureEntity { Key = DietSecondGate.FeatureKey, IsEnabled = false });
            await db.SaveChangesAsync();
            await new DietGateStoreBuilder().RunAsync(db);

            static async Task<List<string>> Stored(ApplicationDbContext c) =>
                (await c.DietRecipeFlags.Select(f => new { f.RecipeId, f.Verdict, f.Reason, Type = c.DietGateSets.Where(s => s.Id == f.SetId).Select(s => s.RestrictionTypeId + "/" + s.MinimumSeverity).First() }).ToListAsync())
                .Select(f => $"{f.Type}:{f.RecipeId}:{f.Verdict}:{f.Reason}").OrderBy(x => x, StringComparer.Ordinal)
                .Concat((await c.DietRecipeStates.Select(s => new { s.RecipeId, s.Fingerprint }).ToListAsync()).Select(s => $"state:{s.RecipeId}:{s.Fingerprint}").OrderBy(x => x, StringComparer.Ordinal))
                .ToList();
            (await Stored(db)).Should().Equal(await Stored(plain), "with the setting off a reading changes neither a flag nor a recipe's fingerprint");
            (await FlagsAsync(db, CeliacRef)).Should().NotContainKey(k.Pilaf, "a reading never holds anything while the setting is off");
        }

        [Fact]
        public async Task With_the_setting_on_a_recipe_the_rules_find_clear_is_shown_to_a_severe_type_only_when_both_readings_call_it_free()
        {
            using var db = NewContext();
            var k = await SeedAsync(db);
            TurnOn(db);
            Read(db, k.Pilaf, PilafText, AllFree, AllFree);
            Read(db, k.Salad, SaladText, AllFree, "mffffffffff");
            Read(db, k.Bread, BreadText, AllFree, AllFree);
            await db.SaveChangesAsync();
            await new DietGateStoreBuilder().RunAsync(db);

            var celiac = await FlagsAsync(db, CeliacRef);
            celiac.Should().NotContainKey(k.Pilaf, "the rules and both readers find the pilaf clear");
            celiac[k.Salad].Should().Match<DietRecipeFlagEntity>(f => f.Verdict == DietVerdict.Unknown && f.Reason == DietFlagReason.SecondReader, "phi4 does not call the salad clearly free of gluten");
            celiac[k.Bread].Should().Match<DietRecipeFlagEntity>(f => f.Verdict == DietVerdict.Contains && f.Reason == DietFlagReason.Food, "the readers never clear what the rules hold");
            (await FlagsAsync(db, VegetarianRef)).Should().NotContainKey(k.Salad, "a diet of choice is not a severe type and reads no reading");
            RecipeOrchestrationService.DietTextName(DietFlagReason.SecondUnread).Should().Be("Not yet read by the second reader");
            RecipeOrchestrationService.DietTextName(DietFlagReason.SecondReader).Should().Be("Not clearly free to the second reader");
        }

        [Fact]
        public async Task A_recipe_not_read_by_both_models_under_the_current_prompt_and_text_is_held_as_not_yet_read()
        {
            using var db = NewContext();
            var k = await SeedAsync(db);
            TurnOn(db);
            Read(db, k.Pilaf, DietSecondGate.TextSignature("Pilaf", new[] { "1 cup rice" }, Array.Empty<string>()), AllFree, AllFree);
            Read(db, k.Salad, SaladText, AllFree, AllFree, prompt: "0000000000000000");
            Read(db, k.Bread, BreadText, AllFree, "");
            await db.SaveChangesAsync();
            await new DietGateStoreBuilder().RunAsync(db);

            var celiac = await FlagsAsync(db, CeliacRef);
            celiac[k.Pilaf].Reason.Should().Be(DietFlagReason.SecondUnread, "the reading was of other lines");
            celiac[k.Salad].Reason.Should().Be(DietFlagReason.SecondUnread, "the reading was under another prompt");
            celiac[k.Bread].Reason.Should().Be(DietFlagReason.Food, "the rules decide first");
            celiac.Values.Should().OnlyContain(f => f.Verdict != DietVerdict.Unknown || f.Reason == DietFlagReason.SecondUnread);
        }

        [Fact]
        public async Task A_reading_logged_after_the_build_is_decided_again_by_the_cycle_over_the_changes_and_turning_the_setting_on_reads_every_recipe()
        {
            using var db = NewContext();
            var k = await SeedAsync(db);
            var builder = new DietGateStoreBuilder();
            await builder.RunAsync(db);
            (await FlagsAsync(db, CeliacRef)).Should().NotContainKey(k.Pilaf);

            TurnOn(db);
            db.DietGateWrites.Add(new DietGateWriteEntity { WrittenAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
            var whole = await builder.RunChangesAsync(db);
            whole.RecipesDecided.Should().Be(3, "the setting changes every recipe's reading, so the cycle over the changes runs as a whole cycle");
            (await FlagsAsync(db, CeliacRef))[k.Pilaf].Reason.Should().Be(DietFlagReason.SecondUnread);

            Read(db, k.Pilaf, PilafText, AllFree, AllFree);
            db.DietGateWrites.Add(new DietGateWriteEntity { RecipeId = k.Pilaf, WrittenAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
            var changes = await builder.RunChangesAsync(db);
            changes.RecipesDecided.Should().Be(1, "only the recipe whose reading was written is decided again");
            (await FlagsAsync(db, CeliacRef)).Should().NotContainKey(k.Pilaf);
        }

        [Fact]
        public void The_build_the_lane_and_the_approval_sign_a_recipe_text_alike_and_the_store_version_carries_the_rule()
        {
            DietSecondGate.TextSignature("Pilaf", new[] { "1 cup rice", " ", "2 cups  water" }, new[] { "Boil the rice\nin the water.", "", "Serve." })
                .Should().Be(DietSecondGate.TextSignature("Pilaf", new[] { "1 cup rice", "2 cups water" }, new[] { "Boil the rice in the water.\nServe." }),
                    "the build reads the method as one string and the lane step by step; white space is folded");
            DietSecondGate.TextSignature("Pilaf", new[] { "1 cup rice" }, Array.Empty<string>()).Should().NotBe(DietSecondGate.TextSignature("Pilaf", new[] { "1 cup brown rice" }, Array.Empty<string>()));
            var recipe = new RecipeEntity
            {
                Name = "Pilaf",
                RecipeIngredients = new List<RecipeIngredientEntity>
                {
                    new() { IngredientId = 2, SortOrder = 1, RawLine = "", Ingredient = new IngredientEntity { Name = "Water" } },
                    new() { IngredientId = 1, SortOrder = 0, RawLine = "1 cup rice" },
                    new() { IngredientId = 3, SortOrder = 2, RawLine = "salt", IsDeleted = true },
                },
                RecipeSteps = new List<RecipeStepEntity> { new() { StepNumber = 2, Description = "Serve." }, new() { StepNumber = 1, Description = "Boil." } },
            };
            DietApprovalHolds.TextSignature(recipe).Should().Be(DietSecondGate.TextSignature("Pilaf", new[] { "1 cup rice", "Water" }, new[] { "Boil.", "Serve." }));
            DietGateStore.Version.Should().Contain(DietSecondGate.StoreEntry);
            DietGateStore.Version.Length.Should().BeLessThanOrEqualTo(1023, "the build's version column holds 1,023 characters");
            DietSecondGate.IndexOf("Celiac Disease").Should().Be(0);
            DietSecondGate.IndexOf("Vegetarian").Should().Be(-1);
            Nom.Orch.Settings.LaneModelRoster.Family(DietSecondGate.Pair[0]).Should().NotBe(Nom.Orch.Settings.LaneModelRoster.Family(DietSecondGate.Pair[1]), "the two readers are of two families");
        }
    }
}
