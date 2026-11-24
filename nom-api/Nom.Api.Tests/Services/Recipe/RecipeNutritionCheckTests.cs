using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nom.Api.Services;
using Nom.Data;
using Nom.Data.Measurement;
using Nom.Data.Nutrient;
using Nom.Data.Recipe;
using Nom.Orch.Services;
using Xunit;

namespace Nom.Api.Tests.Services.Recipe
{
    /// <summary>
    /// Staging checkpoint 6, R9: the API rewrote <c>recipe."RecipeNutrition"</c> for over an hour after every start (about
    /// 40,000 inserts and as many deletes a minute), because every line write made a recipe due (an order-only pass writes
    /// nearly every line) and every due recipe's label was deleted and inserted again whatever it came out as. Now only a
    /// recipe whose inputs changed is derived, and a label that derives as it is stored is not rewritten.
    /// </summary>
    public class RecipeNutritionCheckTests
    {
        private const long Fat = 5000, Gram = 1, Cup = 2, Package = 3;

        private static async Task<(ApplicationDbContext Db, RecipeEntity Recipe, RecipeIngredientEntity Line)> SeedAsync()
        {
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            db.Set<BaseMeasurementEntity>().Add(new BaseMeasurementEntity { Id = Gram, Name = "Gram", Symbol = "g", MeasurementCategoryId = RecipeNutritionService.CategoryMass, BaseUnitConversionFactor = 1m });
            db.Nutrients.Add(new NutrientEntity { Id = Fat, Name = "Fat" });
            var egg = new IngredientEntity { Name = "Egg", CurationStatusId = 9003 };
            db.Ingredients.Add(egg);
            await db.SaveChangesAsync();
            db.IngredientNutrients.Add(new IngredientNutrientEntity { IngredientId = egg.Id, NutrientId = Fat, Amount = 9.5m, MeasurementId = Gram, CreatedDate = DateTime.UtcNow.AddDays(-1) });
            var recipe = new RecipeEntity { Name = "Omelette", AuthorId = 1, Servings = 1 };
            db.Recipes.Add(recipe);
            await db.SaveChangesAsync();
            var line = new RecipeIngredientEntity { RecipeId = recipe.Id, IngredientId = egg.Id, Quantity = 100m, MeasurementId = Gram, RawLine = "100 g egg", CreatedDate = DateTime.UtcNow.AddDays(-1) };
            db.RecipeIngredients.Add(line);
            await db.SaveChangesAsync();
            return (db, recipe, line);
        }

        private static RecipeNutritionService Service(ApplicationDbContext db) => new(db, NullLogger<RecipeNutritionService>.Instance);

        private static async Task<(IngredientEntity Flour, RecipeIngredientEntity Line)> AddFlourAsync(ApplicationDbContext db, long recipeId, long unit, string raw)
        {
            if (!await db.Set<BaseMeasurementEntity>().AnyAsync(m => m.Id == unit))
                db.Set<BaseMeasurementEntity>().Add(unit == Cup
                    ? new BaseMeasurementEntity { Id = Cup, Name = "Cup", Symbol = "cup", MeasurementCategoryId = RecipeNutritionService.CategoryVolume, BaseUnitConversionFactor = 240m }
                    : new BaseMeasurementEntity { Id = Package, Name = "Package", Symbol = "pkg", MeasurementCategoryId = RecipeNutritionService.CategoryCount, BaseUnitConversionFactor = 1m, IsContainer = true });
            var flour = new IngredientEntity { Name = "Flour", CurationStatusId = 9003, CreatedDate = DateTime.UtcNow.AddDays(-1) };
            db.Ingredients.Add(flour);
            await db.SaveChangesAsync();
            db.IngredientNutrients.Add(new IngredientNutrientEntity { IngredientId = flour.Id, NutrientId = Fat, Amount = 1m, MeasurementId = Gram, CreatedDate = DateTime.UtcNow.AddDays(-1) });
            var line = new RecipeIngredientEntity { RecipeId = recipeId, IngredientId = flour.Id, Quantity = 2m, MeasurementId = unit, RawLine = raw, SortOrder = 1, CreatedDate = DateTime.UtcNow.AddDays(-1) };
            db.RecipeIngredients.Add(line);
            await db.SaveChangesAsync();
            return (flour, line);
        }

        [Fact]
        public async Task A_writer_that_gives_a_food_a_weight_and_touches_its_lines_derives_the_label_again()
        {
            var (db, recipe, _) = await SeedAsync();
            using var _db = db;
            var (flour, line) = await AddFlourAsync(db, recipe.Id, Cup, "2 cups flour");
            await Service(db).RecalculateAsync(recipe.Id);
            (await db.RecipeNutrition.AsNoTracking().SingleAsync()).Amount.Should().Be(9.5m, "the flour cannot be weighed without a density");

            flour.GramsPerMilliliter = 0.5m;
            line.LastModifiedDate = DateTime.UtcNow.AddMinutes(1);
            await db.SaveChangesAsync();
            var batch = await RecipeNutritionRecalcHostedService.SweepBatchAsync(db, Service(db), 0, 200);

            (batch.Derived, batch.Written, batch.Checked).Should().Be((1, 1, 0), "the line can now be weighed, as the portion backfill reports (review CK11-DM-2)");
            (await db.RecipeNutrition.AsNoTracking().SingleAsync()).Amount.Should().Be(11.9m);
        }

        [Fact]
        public async Task A_recipe_that_could_not_be_weighed_is_due_again_when_its_food_gains_a_weight_or_a_container_size()
        {
            var (db, recipe, egg) = await SeedAsync();
            using var _db = db;
            db.RecipeIngredients.Remove(egg);
            await db.SaveChangesAsync();
            var (flour, _) = await AddFlourAsync(db, recipe.Id, Cup, "2 cups flour");
            await Service(db).RecalculateAsync(recipe.Id);
            (await db.RecipeNutrition.AnyAsync()).Should().BeFalse();
            (await db.RecipeNutritionChecks.AnyAsync()).Should().BeTrue();
            (await RecipeNutritionRecalcHostedService.DueRecipeIds(db, 0).ToListAsync()).Should().BeEmpty();

            flour.GramsPerMilliliter = 0.5m;
            flour.LastModifiedDate = DateTime.UtcNow.AddMinutes(1);
            await db.SaveChangesAsync();
            (await RecipeNutritionRecalcHostedService.DueRecipeIds(db, 0).ToListAsync()).Should().Equal(new[] { recipe.Id }, "a recipe with no label is due again when a line food changes (review CK11-DM-3)");
            var batch = await RecipeNutritionRecalcHostedService.SweepBatchAsync(db, Service(db), 0, 200);
            (batch.Derived, batch.Written).Should().Be((1, 1));
            (await db.RecipeNutrition.AsNoTracking().SingleAsync()).Amount.Should().Be(2.4m);

            var other = new RecipeEntity { Name = "Cake from a box", AuthorId = 1, Servings = 1 };
            db.Recipes.Add(other);
            await db.SaveChangesAsync();
            var (mix, _) = await AddFlourAsync(db, other.Id, Package, "1 package cake mix");
            await Service(db).RecalculateAsync(other.Id);
            (await db.RecipeNutrition.AnyAsync(n => n.RecipeId == other.Id)).Should().BeFalse();
            (await RecipeNutritionRecalcHostedService.ContainerDueRecipeIds(db, 0).ToListAsync()).Should().NotContain(other.Id);

            db.IngredientContainerSizes.Add(new IngredientContainerSizeEntity { IngredientId = mix.Id, ContainerKind = "package", Era = "modern", SizeWord = string.Empty, NetGrams = 500m, SizeText = "500 g", SourceRef = "test", Class = "derived", CreatedDate = DateTime.UtcNow.AddMinutes(2) });
            await db.SaveChangesAsync();
            (await RecipeNutritionRecalcHostedService.ContainerDueRecipeIds(db, 0).ToListAsync()).Should().Contain(other.Id, "a container size arriving brings back a recipe that has a check but no label (review CK11-DM-3)");
        }

        [Fact]
        public async Task A_label_that_derives_as_it_is_stored_is_not_rewritten_and_only_its_check_moves_on()
        {
            var (db, recipe, _) = await SeedAsync();
            using var _db = db;
            (await Service(db).RecalculateAsync(recipe.Id)).Should().Be(1);
            var label = await db.RecipeNutrition.AsNoTracking().SingleAsync();
            var first = await db.RecipeNutritionChecks.AsNoTracking().SingleAsync();
            label.Amount.Should().Be(9.5m);

            (await Service(db).RecalculateAsync(recipe.Id)).Should().Be(0, "nothing about the label changed");

            var again = await db.RecipeNutrition.AsNoTracking().SingleAsync();
            (again.Id, again.DateCalculated).Should().Be((label.Id, label.DateCalculated), "the stored row is kept, not deleted and inserted again");
            (await db.RecipeNutritionChecks.AsNoTracking().SingleAsync()).CheckedAt.Should().BeOnOrAfter(first.CheckedAt);
        }

        [Fact]
        public async Task A_line_write_that_changes_no_input_only_moves_the_check_and_a_changed_amount_derives_the_label_again()
        {
            var (db, recipe, line) = await SeedAsync();
            using var _db = db;
            await Service(db).RecalculateAsync(recipe.Id);
            var label = await db.RecipeNutrition.AsNoTracking().SingleAsync();
            (await RecipeNutritionRecalcHostedService.DueRecipeIds(db, 0).ToListAsync()).Should().BeEmpty();

            line.RawLine = "100 g egg, beaten";
            line.LastModifiedDate = DateTime.UtcNow.AddMinutes(1);
            await db.SaveChangesAsync();
            (await RecipeNutritionRecalcHostedService.DueRecipeIds(db, 0).ToListAsync()).Should().Equal(recipe.Id);

            var text = await RecipeNutritionRecalcHostedService.SweepBatchAsync(db, Service(db), 0, 200);

            (text.Derived, text.Written, text.Checked).Should().Be((0, 0, 1), "the line's food, amount, unit and kind are as they were");
            (await db.RecipeNutrition.AsNoTracking().SingleAsync()).Id.Should().Be(label.Id);
            var check = await db.RecipeNutritionChecks.SingleAsync();
            check.CheckedAt = DateTime.UtcNow.AddMinutes(2);
            await db.SaveChangesAsync();
            (await RecipeNutritionRecalcHostedService.DueRecipeIds(db, 0).ToListAsync()).Should().BeEmpty();

            line.Quantity = 200m;
            line.LastModifiedDate = DateTime.UtcNow.AddMinutes(3);
            await db.SaveChangesAsync();
            var amount = await RecipeNutritionRecalcHostedService.SweepBatchAsync(db, Service(db), 0, 200);

            (amount.Derived, amount.Written, amount.Checked).Should().Be((1, 1, 0));
            (await db.RecipeNutrition.AsNoTracking().SingleAsync()).Amount.Should().Be(19m);
        }
    }
}
