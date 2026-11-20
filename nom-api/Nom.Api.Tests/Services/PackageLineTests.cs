using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Nom.Data;
using Nom.Data.Measurement;
using Nom.Data.Nutrient;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Services;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// Which stored recipe lines take the size or the package their own wording states, and how a
    /// batch is journaled and undone.
    /// </summary>
    public class PackageLineTests
    {
        private const long Gram = 1, Milliliter = 2, Piece = 3, Ounce = 7, Can = 20, Package = 22;

        private sealed class NoUser : ICurrentUserService
        {
            public long? PersonId => null;
            public long PersonIdOrSystem => SystemConstants.SystemPersonId;
            public long RequiredPersonId => throw new UnauthorizedAccessException();
            public string? UserId => null;
            public string RequiredUserId => throw new UnauthorizedAccessException();
        }

        private static ApplicationDbContext NewContext(bool withPackage = true)
        {
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);
            db.Set<BaseMeasurementEntity>().AddRange(
                new BaseMeasurementEntity { Id = Gram, Name = "Gram", Symbol = "g", MeasurementCategoryId = 1, BaseUnitConversionFactor = 1m },
                new BaseMeasurementEntity { Id = Milliliter, Name = "Milliliter", Symbol = "ml", MeasurementCategoryId = 2, BaseUnitConversionFactor = 1m },
                new BaseMeasurementEntity { Id = Piece, Name = "Piece", Symbol = "pc", MeasurementCategoryId = 3, BaseUnitConversionFactor = 1m },
                new BaseMeasurementEntity { Id = Ounce, Name = "Ounce", Symbol = "oz", MeasurementCategoryId = 1, BaseUnitConversionFactor = 28.3495m },
                new BaseMeasurementEntity { Id = Can, Name = "Can", Symbol = "can", MeasurementCategoryId = 3, BaseUnitConversionFactor = 1m, IsContainer = true });
            if (withPackage)
            {
                db.Set<BaseMeasurementEntity>().Add(
                    new BaseMeasurementEntity { Id = Package, Name = "Package", Symbol = "pkg", MeasurementCategoryId = 3, BaseUnitConversionFactor = 1m, IsContainer = true });
            }
            db.SaveChanges();
            return db;
        }

        private static IngredientEntity Food(string name, decimal? gramsPerItem = null, decimal? servingGrams = null, bool nutrition = true) => new()
        {
            Name = name,
            CurationStatusId = 9003,
            GramsPerItem = gramsPerItem,
            ReferenceServingGrams = servingGrams,
            IngredientNutrients = nutrition ? new List<IngredientNutrientEntity> { new() { NutrientId = 1, Amount = 1 } } : new List<IngredientNutrientEntity>(),
        };

        private sealed record Row(IngredientEntity Food, decimal Quantity, long MeasurementId, string Line, string? Kind = null);

        private static RecipeEntity Recipe(string name, bool scraped, params Row[] rows) => new()
        {
            Name = name,
            ScrapedAtUtc = scraped ? new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc) : null,
            RecipeIngredients = rows.Select((r, i) => new RecipeIngredientEntity
            {
                IngredientId = r.Food.Id, SortOrder = i, Quantity = r.Quantity, MeasurementId = r.MeasurementId, RawLine = r.Line, LineKind = r.Kind,
                CreatedDate = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            }).ToList(),
        };

        private static async Task<ApplicationDbContext> SeedAsync()
        {
            var db = NewContext();
            var beans = Food("Canned Black Beans", servingGrams: 254m);
            var corn = Food("Corn", gramsPerItem: 73m);
            var marinara = Food("Pasta Sauce", servingGrams: 135m);
            var tomatoes = Food("Tomatoes", gramsPerItem: 123m);
            var coconutMilk = Food("Coconut Milk");
            var chickpeas = Food("Canned Chickpeas");
            var eggs = Food("Eggs", gramsPerItem: 50m);
            var capers = Food("Capers", nutrition: false);
            db.Ingredients.AddRange(beans, corn, marinara, tomatoes, coconutMilk, chickpeas, eggs, capers);
            await db.SaveChangesAsync();

            db.Recipes.AddRange(
                Recipe("Scraped chili", scraped: true,
                    new Row(beans, 1, Piece, "1 (15 oz) can black beans"),
                    new Row(corn, 1, Can, "1 can (15 oz) corn"),
                    new Row(marinara, 1, Piece, "1 jar marinara"),
                    new Row(tomatoes, 1, Can, "1 can tomatoes"),
                    new Row(coconutMilk, 1, Piece, "1 (13.5 fl oz) can coconut milk"),
                    new Row(chickpeas, 1, Piece, "1 15-oz can chickpeas, rinsed and drained"),
                    new Row(eggs, 3, Piece, "3 eggs"),
                    new Row(capers, 1, Piece, "1 jar of patience", RecipeIngredientLineKind.Reference),
                    new Row(capers, 0, Gram, "1 jar capers")),
                Recipe("Written by a member", scraped: false,
                    new Row(beans, 1, Piece, "1 (15 oz) can black beans")));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            return db;
        }

        private static async Task<Dictionary<string, RecipeIngredientEntity>> LinesAsync(ApplicationDbContext db, string recipe) =>
            await db.RecipeIngredients.AsNoTracking().Where(ri => ri.Recipe.Name == recipe).ToDictionaryAsync(ri => ri.RawLine);

        [Fact]
        public async Task The_preview_counts_what_would_change_and_writes_nothing()
        {
            using var db = await SeedAsync();

            var preview = await new PackageLineService(db, new NoUser()).PreviewAsync(100);

            preview.LinesExamined.Should().Be(7, "the scraped recipe's measured ingredient lines in pieces or cans");
            preview.ChangeCount.Should().Be(5);
            preview.TouchCount.Should().Be(1, "the can of tomatoes stays a can but was weighed as one tomato");
            preview.StopCounting.Should().Be(2, "the jar of marinara and the can of tomatoes have no stated size");
            preview.StartCounting.Should().Be(1, "the chickpeas had no item weight and now have 15 ounces");
            preview.MissingMeasurements.Should().BeEmpty();
            preview.Transitions.Select(t => (t.FromMeasurement, t.ToMeasurement, t.Lines)).Should().BeEquivalentTo(new[]
            {
                ("Piece", "Ounce", 2), ("Can", "Ounce", 1), ("Piece", "Package", 1), ("Piece", "Milliliter", 1),
            });
            preview.Samples.Should().HaveCount(5);
            var beans = preview.Samples.Single(s => s.Line == "1 (15 oz) can black beans");
            (beans.FromQuantity, beans.FromMeasurement, beans.ToQuantity, beans.ToMeasurement).Should().Be((1m, "Piece", 15m, "Ounce"));

            (await db.AuditLogEntries.AnyAsync()).Should().BeFalse();
            (await LinesAsync(db, "Scraped chili")).Values.Should().OnlyContain(l => l.LastModifiedDate == null);
        }

        [Fact]
        public async Task Apply_gives_each_line_the_amount_its_wording_states_and_journals_it()
        {
            using var db = await SeedAsync();
            var service = new PackageLineService(db, new NoUser());

            var result = await service.ApplyAsync();

            result.Changed.Should().Be(5);
            result.Skipped.Should().Be(0);
            result.Touched.Should().Be(1);
            result.Remaining.Should().Be(0);

            var lines = await LinesAsync(db, "Scraped chili");
            (lines["1 (15 oz) can black beans"].Quantity, lines["1 (15 oz) can black beans"].MeasurementId).Should().Be((15m, Ounce));
            (lines["1 can (15 oz) corn"].Quantity, lines["1 can (15 oz) corn"].MeasurementId).Should().Be((15m, Ounce));
            (lines["1 jar marinara"].Quantity, lines["1 jar marinara"].MeasurementId).Should().Be((1m, Package));
            (lines["1 (13.5 fl oz) can coconut milk"].Quantity, lines["1 (13.5 fl oz) can coconut milk"].MeasurementId).Should().Be((399.242m, Milliliter));
            (lines["1 15-oz can chickpeas, rinsed and drained"].Quantity, lines["1 15-oz can chickpeas, rinsed and drained"].MeasurementId).Should().Be((15m, Ounce));

            (lines["1 can tomatoes"].Quantity, lines["1 can tomatoes"].MeasurementId).Should().Be((1m, Can));
            lines["1 can tomatoes"].LastModifiedDate.Should().NotBeNull("its recipe's label is due for recalculation without the weight of one tomato");
            (lines["3 eggs"].Quantity, lines["3 eggs"].MeasurementId, lines["3 eggs"].LastModifiedDate).Should().Be((3m, Piece, null));
            (lines["1 jar of patience"].Quantity, lines["1 jar of patience"].MeasurementId).Should().Be((1m, Piece), "a line that is not an ingredient is left alone");
            (lines["1 jar capers"].Quantity, lines["1 jar capers"].MeasurementId).Should().Be((0m, Gram), "a line with no quantity is left to the startup repair");
            var members = (await LinesAsync(db, "Written by a member")).Values.Single();
            (members.Quantity, members.MeasurementId).Should().Be((1m, Piece), "only scraped recipes are re-read");

            var journal = await db.AuditLogEntries.AsNoTracking().ToListAsync();
            journal.Should().HaveCount(5);
            journal.Should().OnlyContain(e => e.EntityType == "RecipeIngredient" && e.ChangeType == "LineReread" && e.PropertyName == "Quantity"
                && e.EntityId == lines["3 eggs"].RecipeId && e.ChangedByPersonId == SystemConstants.SystemPersonId);
            var beans = lines["1 (15 oz) can black beans"];
            journal.Should().ContainSingle(e => e.OldValue == $"{beans.IngredientId}@0|1|{Piece}" && e.NewValue == $"{beans.IngredientId}@0|15|{Ounce}");

            var again = await service.ApplyAsync();

            (again.Changed, again.Skipped, again.Touched, again.Remaining).Should().Be((0, 0, 0, 0));
            (await db.AuditLogEntries.CountAsync()).Should().Be(5);
        }

        [Fact]
        public async Task Apply_stops_at_the_number_of_lines_it_is_given()
        {
            using var db = await SeedAsync();
            var service = new PackageLineService(db, new NoUser());

            var first = await service.ApplyAsync(maxLines: 2);

            (first.Changed, first.Remaining).Should().Be((2, 3));
            (await db.AuditLogEntries.CountAsync()).Should().Be(2);

            var rest = await service.ApplyAsync();

            (rest.Changed, rest.Remaining, rest.Touched).Should().Be((3, 0, 0), "the can of tomatoes was marked by the first run");
        }

        [Fact]
        public async Task Undo_puts_back_what_a_line_had_unless_it_has_changed_since()
        {
            using var db = await SeedAsync();
            var service = new PackageLineService(db, new NoUser());
            var before = DateTime.UtcNow.AddMinutes(-1);
            await service.ApplyAsync();

            var corn = await db.RecipeIngredients.SingleAsync(ri => ri.RawLine == "1 can (15 oz) corn");
            corn.Quantity = 30m;
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            (await service.UndoAsync(DateTime.UtcNow.AddMinutes(1))).Restored.Should().Be(0, "nothing was re-read after that time");

            var undone = await service.UndoAsync(before);

            undone.Restored.Should().Be(4);
            undone.Skipped.Should().Be(1, "the corn was edited after the re-read");
            db.ChangeTracker.Clear();
            var lines = await LinesAsync(db, "Scraped chili");
            (lines["1 (15 oz) can black beans"].Quantity, lines["1 (15 oz) can black beans"].MeasurementId).Should().Be((1m, Piece));
            (lines["1 jar marinara"].Quantity, lines["1 jar marinara"].MeasurementId).Should().Be((1m, Piece));
            (lines["1 (13.5 fl oz) can coconut milk"].Quantity, lines["1 (13.5 fl oz) can coconut milk"].MeasurementId).Should().Be((1m, Piece));
            (lines["1 15-oz can chickpeas, rinsed and drained"].Quantity, lines["1 15-oz can chickpeas, rinsed and drained"].MeasurementId).Should().Be((1m, Piece));
            (lines["1 can (15 oz) corn"].Quantity, lines["1 can (15 oz) corn"].MeasurementId).Should().Be((30m, Ounce));
            var left = await db.AuditLogEntries.AsNoTracking().SingleAsync();
            left.NewValue.Should().Be($"{lines["1 can (15 oz) corn"].IngredientId}@1|15|{Ounce}");

            (await service.PreviewAsync()).ChangeCount.Should().Be(4, "the restored lines read as they did before, and the edited one is in ounces already");
        }

        [Fact]
        public async Task A_line_is_left_out_while_the_measurement_it_reads_as_has_no_row()
        {
            using var db = NewContext(withPackage: false);
            var marinara = Food("Pasta Sauce", servingGrams: 135m);
            db.Ingredients.Add(marinara);
            await db.SaveChangesAsync();
            db.Recipes.Add(Recipe("Scraped pasta", scraped: true, new Row(marinara, 1, Piece, "1 jar marinara")));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            var service = new PackageLineService(db, new NoUser());

            var preview = await service.PreviewAsync();
            var result = await service.ApplyAsync();

            preview.ChangeCount.Should().Be(0);
            preview.MissingMeasurements.Should().Equal("Package");
            (result.Changed, result.Touched).Should().Be((0, 0));
            (await db.RecipeIngredients.AsNoTracking().SingleAsync()).MeasurementId.Should().Be(Piece);
        }

        [Fact]
        public async Task A_sized_can_of_a_food_weighed_drained_is_read_as_cans_and_never_as_its_net_weight()
        {
            using var db = NewContext();
            var chickpeas = Food("Chickpeas (garbanzo beans, bengal gram), mature seeds, canned, drained, rinsed in tap water", servingGrams: 254m);
            var pinto = Food("Beans, pinto, mature seeds, canned, drained solids", servingGrams: 171m);
            db.Ingredients.AddRange(chickpeas, pinto);
            await db.SaveChangesAsync();
            db.Recipes.Add(Recipe("Scraped hummus", scraped: true,
                new Row(chickpeas, 1, Piece, "1 (15-ounce) can chickpeas, drained and rinsed"),
                new Row(pinto, 2, Can, "2 cans (15 oz each) pinto beans, drained")));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            var service = new PackageLineService(db, new NoUser());

            var preview = await service.PreviewAsync();
            await service.ApplyAsync();

            preview.DrainedAsContainers.Should().Be(2);
            preview.Transitions.Select(t => (t.FromMeasurement, t.ToMeasurement, t.Lines)).Should().Equal(("Piece", "Can", 1));
            var lines = await LinesAsync(db, "Scraped hummus");
            (lines["1 (15-ounce) can chickpeas, drained and rinsed"].Quantity, lines["1 (15-ounce) can chickpeas, drained and rinsed"].MeasurementId).Should().Be((1m, Can));
            (lines["2 cans (15 oz each) pinto beans, drained"].Quantity, lines["2 cans (15 oz each) pinto beans, drained"].MeasurementId).Should().Be((2m, Can));
            (await db.RecipeIngredients.AsNoTracking().AnyAsync(ri => ri.MeasurementId == Ounce)).Should().BeFalse();
        }

        [Fact]
        public void A_journaled_amount_reads_back_as_the_line_and_amount_it_was_written_from()
        {
            PackageLineService.ReadAmount(PackageLineService.Amount(282236, 3, 399.242m, 2)).Should().Be((282236L, 3, 399.242m, 2L));
            PackageLineService.ReadAmount(PackageLineService.Amount(280833, 0, 15.0m, 7)).Should().Be((280833L, 0, 15m, 7L));
            PackageLineService.Amount(280833, 0, 15.0m, 7).Should().Be("280833@0|15|7");
            PackageLineService.ReadAmount("280833@0|15").Should().BeNull();
            PackageLineService.ReadAmount("280833@0").Should().BeNull();
            PackageLineService.ReadAmount(null).Should().BeNull();
        }
    }
}
