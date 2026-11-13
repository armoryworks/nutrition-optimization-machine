using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Nutrient;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Services;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// Which recipe lines move to the curated food their wording names. Foods and wordings here
    /// are real prod rows.
    /// </summary>
    public class LineRelinkTests
    {
        private sealed class NoUser : ICurrentUserService
        {
            public long? PersonId => null;
            public long PersonIdOrSystem => SystemConstants.SystemPersonId;
            public long RequiredPersonId => throw new UnauthorizedAccessException();
            public string? UserId => null;
            public string RequiredUserId => throw new UnauthorizedAccessException();
        }

        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        private static readonly Dictionary<string, long> Foods = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Water"] = 1, ["Well Water"] = 2, ["Butter"] = 3, ["Unsalted Butter"] = 4, ["Light Unsalted Butter"] = 5,
            ["Potatoes"] = 6, ["Boiled Potatoes"] = 7, ["Onion"] = 8, ["Yellow Onion"] = 9, ["Pepper"] = 10,
            ["Banana Peppers"] = 11, ["Turnip"] = 12, ["Turnips"] = 13, ["Fresh Rosemary"] = 14, ["Dried Rosemary"] = 15,
        };

        private static long? Target(string wording, string on) => LineRelinkService.TargetFor(wording, Foods[on], on, Foods);

        [Theory]
        [InlineData("water", "Well Water", "Water")]
        [InlineData("boiling water", "Well Water", "Water")]
        [InlineData("butter", "Light Unsalted Butter", "Butter")]
        [InlineData("unsalted butter", "Light Unsalted Butter", "Unsalted Butter")]
        [InlineData("cold unsalted butter", "Light Unsalted Butter", "Unsalted Butter")]
        [InlineData("unsalted butter", "Butter", "Unsalted Butter")]
        [InlineData("chopped onion", "Yellow Onion", "Onion")]
        [InlineData("whole pepper", "Banana Peppers", null)]
        [InlineData("pepper", "Banana Peppers", "Pepper")]
        [InlineData("fresh rosemary", "Dried Rosemary", "Fresh Rosemary")]
        public void A_line_goes_to_the_food_its_wording_names(string wording, string on, string? expected)
        {
            Target(wording, on).Should().Be(expected == null ? null : Foods[expected]);
        }

        [Theory]
        [InlineData("well water", "Well Water")]
        [InlineData("cold boiled potatoes", "Boiled Potatoes")]
        [InlineData("boiled potatoes", "Boiled Potatoes")]
        [InlineData("melted butter", "Butter")]
        [InlineData("turnip", "Turnips")]
        [InlineData("turnips", "Turnip")]
        [InlineData("sweet butter", "Unsalted Butter")]
        [InlineData("salt and pepper", "Water")]
        [InlineData("peppers", "Banana Peppers")]
        public void A_line_that_names_its_own_food_or_no_food_stays(string wording, string on)
        {
            Target(wording, on).Should().BeNull();
        }

        [Fact]
        public void A_journaled_place_reads_back_as_the_food_and_position_it_was_written_from()
        {
            LineRelinkService.ReadPlace(LineRelinkService.Place(282236, 0)).Should().Be((282236L, 0));
            LineRelinkService.ReadPlace(LineRelinkService.Place(280833, 14)).Should().Be((280833L, 14));
            LineRelinkService.ReadPlace("280833").Should().BeNull();
            LineRelinkService.ReadPlace(null).Should().BeNull();
        }

        [Fact]
        public async Task The_plan_moves_only_scraped_lines_and_only_to_a_food_with_nutrition()
        {
            using var db = NewContext();
            IngredientEntity Food(string name, bool nutrition = true) => new()
            {
                Name = name,
                CurationStatusId = 9003,
                IngredientNutrients = nutrition ? new List<IngredientNutrientEntity> { new() { NutrientId = 1, Amount = 1 } } : new List<IngredientNutrientEntity>(),
            };
            var water = Food("Water");
            var wellWater = Food("Well Water");
            var milk = Food("Milk", nutrition: false);
            var producerMilk = Food("3.7% Whole Milk");
            db.Ingredients.AddRange(water, wellWater, milk, producerMilk);
            await db.SaveChangesAsync();

            RecipeEntity Recipe(string name, DateTime? scraped, params (IngredientEntity Food, string Line)[] lines) => new()
            {
                Name = name,
                ScrapedAtUtc = scraped,
                RecipeIngredients = lines.Select((l, i) => new RecipeIngredientEntity
                {
                    IngredientId = l.Food.Id, SortOrder = i, Quantity = 1, MeasurementId = 1, RawLine = l.Line,
                }).ToList(),
            };
            db.Recipes.AddRange(
                Recipe("Scraped", DateTime.UtcNow,
                    (wellWater, "1 cup water"), (wellWater, "2 quarts boiling water"), (wellWater, "1 pint well water"),
                    (producerMilk, "1 cup milk"), (water, "1 cup water")),
                Recipe("Written by a member", null, (wellWater, "1 cup water")));
            await db.SaveChangesAsync();

            var service = new LineRelinkService(db, new NoUser());
            (await service.MoveFoodAsync(wellWater.Id, milk.Id)).Should().BeNull("Milk has no nutrition here");
            (await service.MoveFoodAsync(wellWater.Id, wellWater.Id)).Should().BeNull();
            (await service.MoveFoodAsync(wellWater.Id, 999999)).Should().BeNull();

            var preview = await service.PreviewAsync(100);

            preview.LinesExamined.Should().Be(5);
            preview.MoveCount.Should().Be(2);
            preview.Samples.Should().OnlyContain(m => m.FromIngredientId == wellWater.Id && m.ToIngredientId == water.Id);
            preview.Samples.Select(m => m.Wording).Should().BeEquivalentTo(new[] { "water", "boiling water" });
        }
    }
}
