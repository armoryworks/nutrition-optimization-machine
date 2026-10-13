using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Plan;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Orch.Models.Household;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// Recipes are checked against the household's kitchen: needs are inferred from step text when a
    /// recipe lists no tools, owned alternatives stand in for missing tools, and esoteric tools count
    /// as missing until the household says it has them.
    /// </summary>
    public class KitchenToolTests
    {
        private const long HouseholdId = 5;

        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        private static IReadOnlySet<long> Detect(string name, params string[] steps) => KitchenToolCatalog.Detect(name, steps);

        [Theory]
        [InlineData("Sheet-Pan Chicken", "Preheat the oven to 425°F.", KitchenToolCatalog.Oven)]
        [InlineData("Beef Stew", "Add everything to the slow cooker and cook on low.", KitchenToolCatalog.SlowCooker)]
        [InlineData("Chili", "Pressure cook on high for 20 minutes.", KitchenToolCatalog.InstantPot)]
        [InlineData("Steak", "Cook sous vide at 130°F for 2 hours.", KitchenToolCatalog.SousVide)]
        [InlineData("Smoothie", "Blend until smooth.", KitchenToolCatalog.Blender)]
        [InlineData("Soup", "Puree with an immersion blender.", KitchenToolCatalog.ImmersionBlender)]
        public void Detects_tools_from_step_text(string name, string step, long tool)
        {
            Detect(name, step).Should().Contain(tool);
        }

        [Theory]
        [InlineData("Braise", "Brown the meat in a Dutch oven on the stove.")]
        [InlineData("Pancakes", "Whisk the flour with baking soda and baking powder.")]
        [InlineData("Pasta", "Toss with jarred roasted red peppers.")]
        public void Does_not_infer_an_oven_from_phrases_that_only_mention_one(string name, string step)
        {
            Detect(name, step).Should().NotContain(KitchenToolCatalog.Oven);
        }

        [Fact]
        public void Immersion_blender_does_not_also_imply_a_countertop_blender()
        {
            Detect("Soup", "Puree with a stick blender.").Should().NotContain(KitchenToolCatalog.Blender);
        }

        [Fact]
        public void Explicit_recipe_tools_win_over_inference()
        {
            KitchenToolEvaluator.RequiredTools(new[] { KitchenToolCatalog.Grill }, "Cake", new[] { "Bake at 350°F." })
                .Should().BeEquivalentTo(new[] { KitchenToolCatalog.Grill });
        }

        [Fact]
        public void Defaults_own_common_tools_and_not_esoteric_ones()
        {
            var owned = KitchenToolEvaluator.ResolveOwned(new Dictionary<long, bool>());
            owned.Should().Contain(new[] { KitchenToolCatalog.Stovetop, KitchenToolCatalog.Oven });
            owned.Should().NotContain(new[] { KitchenToolCatalog.SousVide, KitchenToolCatalog.InstantPot, KitchenToolCatalog.Smoker });
        }

        [Fact]
        public void No_oven_and_no_alternative_is_missing()
        {
            var owned = KitchenToolEvaluator.ResolveOwned(new Dictionary<long, bool> { [KitchenToolCatalog.Oven] = false });
            var check = KitchenToolEvaluator.Evaluate(new[] { KitchenToolCatalog.Oven }, owned);
            check.Fit.Should().Be(KitchenToolFit.Missing);
            check.Notes.Should().ContainSingle().Which.Should().Be("This will be hard without an oven.");
        }

        [Fact]
        public void Instant_pot_stands_in_for_a_missing_stovetop()
        {
            var owned = KitchenToolEvaluator.ResolveOwned(new Dictionary<long, bool>
            {
                [KitchenToolCatalog.Stovetop] = false,
                [KitchenToolCatalog.InstantPot] = true,
            });
            var check = KitchenToolEvaluator.Evaluate(new[] { KitchenToolCatalog.Stovetop }, owned);
            check.Fit.Should().Be(KitchenToolFit.Substitute);
            check.Needs.Single().UsingToolId.Should().Be(KitchenToolCatalog.InstantPot);
        }

        [Fact]
        public void Sous_vide_without_one_is_missing_until_a_thermometer_makes_it_harder()
        {
            var none = KitchenToolEvaluator.ResolveOwned(new Dictionary<long, bool>());
            KitchenToolEvaluator.Evaluate(new[] { KitchenToolCatalog.SousVide }, none).Fit.Should().Be(KitchenToolFit.Missing);

            var withThermometer = KitchenToolEvaluator.ResolveOwned(new Dictionary<long, bool> { [KitchenToolCatalog.Thermometer] = true });
            KitchenToolEvaluator.Evaluate(new[] { KitchenToolCatalog.SousVide }, withThermometer).Fit.Should().Be(KitchenToolFit.Harder);
        }

        [Fact]
        public async Task Service_round_trips_answers_mode_and_resets_to_default()
        {
            using var db = NewContext();
            var service = new KitchenToolService(db);

            (await service.GetModeAsync(HouseholdId)).Should().Be(KitchenToolModes.Warn);

            await service.UpdateSettingsAsync(HouseholdId, new KitchenSettingsUpdateModel
            {
                Mode = KitchenToolModes.Hide,
                Tools = new() { new() { ToolId = KitchenToolCatalog.Oven, Owned = false }, new() { ToolId = KitchenToolCatalog.SousVide, Owned = true } },
            });
            var settings = await service.GetSettingsAsync(HouseholdId);
            settings.Mode.Should().Be(KitchenToolModes.Hide);
            settings.Tools.Single(t => t.Id == KitchenToolCatalog.Oven).Should().Match<KitchenToolModel>(t => !t.Owned && t.Answered);
            settings.Tools.Single(t => t.Id == KitchenToolCatalog.SousVide).Owned.Should().BeTrue();

            await service.UpdateSettingsAsync(HouseholdId, new KitchenSettingsUpdateModel
            {
                Tools = new() { new() { ToolId = KitchenToolCatalog.Oven, Owned = null } },
            });
            var oven = (await service.GetSettingsAsync(HouseholdId)).Tools.Single(t => t.Id == KitchenToolCatalog.Oven);
            oven.Owned.Should().BeTrue("clearing the answer falls back to the default");
            oven.Answered.Should().BeFalse();
        }

        [Fact]
        public async Task Service_rejects_unknown_tools_and_modes()
        {
            using var db = NewContext();
            var service = new KitchenToolService(db);
            await FluentActions.Awaiting(() => service.UpdateSettingsAsync(HouseholdId, new KitchenSettingsUpdateModel { Mode = "sometimes" }))
                .Should().ThrowAsync<ArgumentException>();
            await FluentActions.Awaiting(() => service.UpdateSettingsAsync(HouseholdId, new KitchenSettingsUpdateModel
            {
                Tools = new() { new() { ToolId = 1, Owned = true } },
            })).Should().ThrowAsync<ArgumentException>();
        }

        [Fact]
        public async Task Recipe_check_infers_from_steps_and_explains_the_gap()
        {
            using var db = NewContext();
            var recipe = new RecipeEntity { Name = "Roast Chicken" };
            recipe.RecipeSteps = new List<RecipeStepEntity>
            {
                new() { StepNumber = 1, Summary = "Preheat", Description = "Preheat the oven to 425°F." },
            };
            db.Recipes.Add(recipe);
            db.HouseholdTools.Add(new HouseholdToolEntity { HouseholdId = HouseholdId, ToolId = KitchenToolCatalog.Oven, IsAvailable = false });
            await db.SaveChangesAsync();

            var check = await new KitchenToolService(db).CheckRecipeAsync(HouseholdId, recipe.Id);

            check!.Fit.Should().Be("missing");
            check.Inferred.Should().BeTrue();
            check.Needs.Should().ContainSingle(n => n.ToolId == KitchenToolCatalog.Oven && n.Note == "This will be hard without an oven.");
        }
    }
}
