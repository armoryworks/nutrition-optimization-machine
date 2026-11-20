using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Nutrient;
using Nom.Data.Recipe;
using Nom.Orch.Services;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The catalog audit counts a food's stored alcohol toward its energy, so wine is not
    /// reported as an Atwater mismatch.
    /// </summary>
    public class FoodCatalogAuditServiceTests
    {
        private const long Calories = 5035, Protein = 5006, Carbs = 5003, Fat = 5000, Alcohol = 5038;

        private static async Task<ApplicationDbContext> NewContextAsync()
        {
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            db.Set<NutrientEntity>().AddRange(
                new NutrientEntity { Id = Calories, Name = "Calories" },
                new NutrientEntity { Id = Protein, Name = "Protein" },
                new NutrientEntity { Id = Carbs, Name = "Total Carbohydrates" },
                new NutrientEntity { Id = Fat, Name = "Fat" },
                new NutrientEntity { Id = Alcohol, Name = "Alcohol" });
            await db.SaveChangesAsync();
            return db;
        }

        private static async Task<IngredientEntity> WineAsync(ApplicationDbContext db, string name, decimal? alcohol)
        {
            var wine = new IngredientEntity { Name = name, CurationStatusId = 9003 };
            db.Ingredients.Add(wine);
            await db.SaveChangesAsync();
            db.Set<IngredientNutrientEntity>().AddRange(
                new IngredientNutrientEntity { IngredientId = wine.Id, NutrientId = Calories, Amount = 82m, MeasurementId = 16 },
                new IngredientNutrientEntity { IngredientId = wine.Id, NutrientId = Protein, Amount = 0.07m, MeasurementId = 1 },
                new IngredientNutrientEntity { IngredientId = wine.Id, NutrientId = Carbs, Amount = 2.6m, MeasurementId = 1 },
                new IngredientNutrientEntity { IngredientId = wine.Id, NutrientId = Fat, Amount = 0m, MeasurementId = 1 });
            if (alcohol is { } grams)
                db.Set<IngredientNutrientEntity>().Add(new IngredientNutrientEntity { IngredientId = wine.Id, NutrientId = Alcohol, Amount = grams, MeasurementId = 1 });
            await db.SaveChangesAsync();
            return wine;
        }

        [Fact]
        public async Task A_wine_with_its_alcohol_stored_is_not_an_atwater_mismatch()
        {
            using var db = await NewContextAsync();
            var withAlcohol = await WineAsync(db, "Dry White Wine", 10.3m);
            var without = await WineAsync(db, "Dry Red Wine", null);

            var result = await new FoodCatalogAuditService(db).AuditAsync();

            result.Findings.Where(f => f.IngredientId == withAlcohol.Id).Select(f => f.Code).Should().NotContain(c => c.Contains("atwater_mismatch"));
            result.Findings.Where(f => f.IngredientId == without.Id).Select(f => f.Code).Should().Contain("atwater_mismatch");
        }
    }
}
