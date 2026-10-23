using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nom.Data;
using Nom.Data.Nutrient;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Shopping;
using Nom.Orch.Services;
using Nom.Orch.UtilityInterfaces;
using Nom.Orch.UtilityServices;
using SkiaSharp;
using Xunit;

namespace Nom.Api.Tests.Services.Shopping
{
    /// <summary>
    /// A household's label, barcode and measurements are evidence about a food, scoped to that
    /// household: they decide whether we ask again, they are never written to the catalog's
    /// nutrient rows, and a barcode only fills an empty GtinUpc.
    /// </summary>
    public class IngredientVerificationServiceTests
    {
        private const long Household = 100;
        private const long OtherHousehold = 200;
        private const long Person = 10;
        private const long CaloriesId = 5035;
        private const long Verified = 1;
        private const long UserTyped = 2;
        private const long CuratedNoNutrients = 3;
        private const long Plain = 4;

        private sealed class MemoryMedia : IMediaStorageService
        {
            public Dictionary<string, byte[]> Files { get; } = new();
            public bool IsConfigured { get; init; }
            public Task<string> SaveAsync(string relativePath, byte[] data) { Files[relativePath] = data; return Task.FromResult(relativePath); }
            public Task<byte[]?> ReadAsync(string relativePath) => Task.FromResult(Files.TryGetValue(relativePath, out var d) ? d : null);
            public Task DeleteAsync(string relativePath) { Files.Remove(relativePath); return Task.CompletedTask; }
        }

        private sealed class Fixture
        {
            public ApplicationDbContext Db { get; } = new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            public Mock<ITesseractOcrService> Ocr { get; } = new();
            public Mock<IBarcodeDecodeService> Barcodes { get; } = new();
            public Mock<IVerificationLookupLane> Lane { get; } = new();
            public MemoryMedia Media { get; init; } = new();

            public ScanRateLimiter Limiter { get; } = new();

            public IngredientVerificationService Service() =>
                new(Db, Ocr.Object, Barcodes.Object, Media, Lane.Object, Limiter, NullLogger<IngredientVerificationService>.Instance);
        }

        private static async Task<Fixture> SeedAsync(bool withCalories = true)
        {
            var f = new Fixture();
            if (withCalories)
                f.Db.Nutrients.Add(new NutrientEntity { Id = CaloriesId, Name = "Calories", DefaultMeasurementId = 16, CreatedDate = DateTime.UtcNow });
            f.Db.Ingredients.AddRange(
                new IngredientEntity { Id = Verified, Name = "Rolled oats", FdcId = "173904", CurationStatusId = (long)CurationStatusEnum.Curated, CreatedDate = DateTime.UtcNow },
                new IngredientEntity { Id = UserTyped, Name = "Grandma's granola", CurationStatusId = (long)CurationStatusEnum.PendingCuration, AuthorId = Person, CreatedDate = DateTime.UtcNow },
                new IngredientEntity { Id = CuratedNoNutrients, Name = "Mystery bar", CurationStatusId = (long)CurationStatusEnum.Curated, CreatedDate = DateTime.UtcNow },
                new IngredientEntity { Id = Plain, Name = "Store cookies", CurationStatusId = (long)CurationStatusEnum.NonCurated, CreatedDate = DateTime.UtcNow });
            f.Db.IngredientNutrients.AddRange(
                new IngredientNutrientEntity { IngredientId = Verified, NutrientId = CaloriesId, Amount = 379, MeasurementId = 16, CreatedDate = DateTime.UtcNow },
                new IngredientNutrientEntity { IngredientId = UserTyped, NutrientId = CaloriesId, Amount = 450, MeasurementId = 16, CreatedDate = DateTime.UtcNow });
            await f.Db.SaveChangesAsync();
            return f;
        }

        private static byte[] SmallPng()
        {
            using var bitmap = new SKBitmap(24, 24);
            using (var canvas = new SKCanvas(bitmap)) canvas.Clear(SKColors.White);
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }

        private const string Label = "Serving size 2/3 cup (55g)\nCalories 230\nTotal Fat 8g 10%\nSodium 160mg 7%\nTotal Carbohydrate 37g 13%\nProtein 3g";

        [Fact]
        public async Task NeedsVerification_OnlyFdcOrCuratedFoodsWithCaloriesCountAsVerified()
        {
            var f = await SeedAsync();
            var rows = await f.Service().NeedsVerificationAsync(Household, new[] { Verified, UserTyped, CuratedNoNutrients, Plain, Verified });

            rows.Should().HaveCount(4);
            rows.Single(r => r.IngredientId == Verified).Should().BeEquivalentTo(new { HasVerifiedNutrition = true, ShouldAsk = false, AskedRecently = false });
            rows.Single(r => r.IngredientId == UserTyped).Should().BeEquivalentTo(new { HasVerifiedNutrition = false, ShouldAsk = true });
            rows.Single(r => r.IngredientId == CuratedNoNutrients).Should().BeEquivalentTo(new { HasVerifiedNutrition = false, ShouldAsk = true });
            rows.Single(r => r.IngredientId == Plain).ShouldAsk.Should().BeTrue();
        }

        [Fact]
        public async Task NeedsVerification_ThrowsWhenTheCaloriesNutrientIsMissing()
        {
            var f = await SeedAsync(withCalories: false);
            await f.Service().Invoking(s => s.NeedsVerificationAsync(Household, new[] { Verified }))
                .Should().ThrowAsync<InvalidOperationException>();
        }

        [Fact]
        public async Task Skip_StopsAskingThisHouseholdForThirtyDays_ButNotOtherHouseholds()
        {
            var f = await SeedAsync();
            var service = f.Service();

            (await service.SkipAsync(Plain, Household, Person)).Should().BeTrue();
            (await service.SkipAsync(999, Household, Person)).Should().BeFalse();

            var row = f.Db.IngredientVerifications.Single();
            row.Should().BeEquivalentTo(new { IngredientId = Plain, HouseholdId = Household, Kind = "skip", Status = "skipped", Source = "label:user:10", CreatedByPersonId = Person });
            row.LastAskedDate.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));

            (await service.NeedsVerificationAsync(Household, new[] { Plain })).Single().Should().BeEquivalentTo(new { AskedRecently = true, ShouldAsk = false });
            (await service.NeedsVerificationAsync(OtherHousehold, new[] { Plain })).Single().ShouldAsk.Should().BeTrue();

            row.LastAskedDate = DateTime.UtcNow.AddDays(-31);
            await f.Db.SaveChangesAsync();
            (await service.NeedsVerificationAsync(Household, new[] { Plain })).Single().ShouldAsk.Should().BeTrue();
        }

        [Fact]
        public async Task OnlyAConfirmedLabelStaysOpen_AbandonedScansAndBarcodesFallBackToTheThirtyDayWindow()
        {
            var f = await SeedAsync();
            var old = DateTime.UtcNow.AddDays(-90);
            f.Db.IngredientVerifications.AddRange(
                new IngredientVerificationEntity { IngredientId = Plain, HouseholdId = Household, Kind = "label", Status = "pending", Source = "label:user:10", Confirmed = true, LastAskedDate = old, CreatedDate = old },
                new IngredientVerificationEntity { IngredientId = UserTyped, HouseholdId = Household, Kind = "label", Status = "pending", Source = "label:user:10", Confirmed = false, LastAskedDate = old, CreatedDate = old },
                new IngredientVerificationEntity { IngredientId = CuratedNoNutrients, HouseholdId = Household, Kind = "barcode", Status = "pending", Source = "label:user:10", Upc = "036000291452", LastAskedDate = old, CreatedDate = old },
                new IngredientVerificationEntity { IngredientId = CuratedNoNutrients, HouseholdId = OtherHousehold, Kind = "label", Status = "pending", Source = "label:user:10", Confirmed = false, LastAskedDate = DateTime.UtcNow.AddDays(-2), CreatedDate = DateTime.UtcNow.AddDays(-2) });
            await f.Db.SaveChangesAsync();

            var rows = await f.Service().NeedsVerificationAsync(Household, new[] { Plain, UserTyped, CuratedNoNutrients });
            rows.Single(r => r.IngredientId == Plain).ShouldAsk.Should().BeFalse();
            rows.Single(r => r.IngredientId == UserTyped).ShouldAsk.Should().BeTrue();
            rows.Single(r => r.IngredientId == CuratedNoNutrients).ShouldAsk.Should().BeTrue();
            (await f.Service().NeedsVerificationAsync(OtherHousehold, new[] { CuratedNoNutrients })).Single().ShouldAsk.Should().BeFalse();
        }

        [Fact]
        public async Task NotFood_UnlinksTheHouseholdsItems_AndRetiresOnlyTheirOwnOrphanEntry()
        {
            var f = await SeedAsync();
            f.Db.HouseholdMembers.Add(new Nom.Data.Plan.HouseholdMemberEntity { HouseholdId = Household, PersonId = Person, IsActive = true, CreatedDate = DateTime.UtcNow });
            f.Db.Ingredients.Add(new IngredientEntity { Id = 50, Name = "Dish soap", FdcDataType = "Custom", CurationStatusId = (long)CurationStatusEnum.PendingCuration, AuthorId = Person, CreatedDate = DateTime.UtcNow });
            f.Db.HouseholdShoppingItems.AddRange(
                new Nom.Data.Shopping.HouseholdShoppingItemEntity { HouseholdId = Household, Name = "Dish soap", IngredientId = 50, CreatedDate = DateTime.UtcNow },
                new Nom.Data.Shopping.HouseholdShoppingItemEntity { HouseholdId = Household, Name = "Mystery bar", IngredientId = CuratedNoNutrients, CreatedDate = DateTime.UtcNow },
                new Nom.Data.Shopping.HouseholdShoppingItemEntity { HouseholdId = OtherHousehold, Name = "Mystery bar", IngredientId = CuratedNoNutrients, CreatedDate = DateTime.UtcNow });
            await f.Db.SaveChangesAsync();
            var service = f.Service();

            var soap = await service.MarkNotFoodAsync(50, Household, Person);
            soap!.Should().BeEquivalentTo(new { ItemsUnlinked = 1, IngredientRetired = true });
            f.Db.Ingredients.IgnoreQueryFilters().Single(i => i.Id == 50).IsDeleted.Should().BeTrue();
            f.Db.HouseholdShoppingItems.Single(h => h.Name == "Dish soap").IngredientId.Should().BeNull();
            f.Db.IngredientVerifications.Should().BeEmpty();

            var bar = await service.MarkNotFoodAsync(CuratedNoNutrients, Household, Person);
            bar!.Should().BeEquivalentTo(new { ItemsUnlinked = 1, IngredientRetired = false });
            f.Db.Ingredients.Single(i => i.Id == CuratedNoNutrients).IsDeleted.Should().BeFalse();
            f.Db.HouseholdShoppingItems.Single(h => h.HouseholdId == OtherHousehold).IngredientId.Should().Be(CuratedNoNutrients);
            f.Db.IngredientVerifications.Single().Should().BeEquivalentTo(new { IngredientId = CuratedNoNutrients, HouseholdId = Household, Kind = "skip", Status = "skipped" });
            (await service.NeedsVerificationAsync(Household, new[] { CuratedNoNutrients })).Single().ShouldAsk.Should().BeFalse();

            (await service.MarkNotFoodAsync(999, Household, Person)).Should().BeNull();
        }

        [Fact]
        public async Task ScanLabel_StoresPhotoOcrAndParsedValuesAsPending_AndWritesNoNutrientRows()
        {
            var f = await SeedAsync();
            f.Ocr.Setup(o => o.ExtractRawTextAsync(It.IsAny<byte[]>())).ReturnsAsync(Label);
            var nutrientRowsBefore = await f.Db.IngredientNutrients.CountAsync();

            var result = await f.Service().ScanLabelAsync(Plain, Household, Person, SmallPng());

            result.Should().NotBeNull();
            result!.ImageStored.Should().BeTrue();
            result.Parsed.Parsed.Should().BeTrue();
            result.Parsed.Calories.Should().Be(230);
            result.Parsed.ServingGrams.Should().Be(55);
            result.Parsed.Per100g!.Calories.Should().Be(418.18m);
            result.OcrText.Should().Be(Label);

            var row = f.Db.IngredientVerifications.Single(v => v.Id == result.VerificationId);
            row.Should().BeEquivalentTo(new { IngredientId = Plain, HouseholdId = Household, Kind = "label", Status = "pending", Source = "label:user:10", Confirmed = false, ServingGrams = 55m, ServingText = "2/3 cup" });
            row.OcrText.Should().Be(Label);
            row.NutrientsJson.Should().Contain("\"calories\":230").And.Contain("\"parserVersion\":1");
            row.LabelImageData.Should().NotBeNullOrEmpty();
            row.LabelImageData![0].Should().Be(0xFF);
            row.LabelImagePath.Should().BeNull();
            row.LabelImageContentType.Should().Be("image/jpeg");

            (await f.Db.IngredientNutrients.CountAsync()).Should().Be(nutrientRowsBefore);
            f.Db.Set<Nom.Data.Curation.FoodCatalogProposalEntity>().Should().BeEmpty();
            f.Lane.Verify(l => l.QueueAsync(row.Id, It.IsAny<System.Threading.CancellationToken>()), Times.Once);

            (await f.Service().NeedsVerificationAsync(Household, new[] { Plain })).Single().ShouldAsk.Should().BeFalse();
        }

        [Fact]
        public async Task ScanLabel_UsesMediaStorageWhenConfigured_AndServesTheImageBack()
        {
            var f = await SeedAsync();
            var media = new MemoryMedia { IsConfigured = true };
            var service = new IngredientVerificationService(f.Db, f.Ocr.Object, f.Barcodes.Object, media, f.Lane.Object, f.Limiter, NullLogger<IngredientVerificationService>.Instance);
            f.Ocr.Setup(o => o.ExtractRawTextAsync(It.IsAny<byte[]>())).ReturnsAsync(string.Empty);

            var result = await service.ScanLabelAsync(Plain, Household, Person, SmallPng());

            result!.Parsed.Parsed.Should().BeFalse();
            var row = f.Db.IngredientVerifications.Single();
            row.LabelImagePath.Should().StartWith($"ingredient-labels/{Plain}/");
            row.LabelImageData.Should().BeNull();
            media.Files.Should().ContainKey(row.LabelImagePath!);

            var image = await service.GetImageAsync(row.Id);
            image.Should().NotBeNull();
            image!.Value.ContentType.Should().Be("image/jpeg");
            image.Value.Data.Should().BeEquivalentTo(media.Files[row.LabelImagePath!]);
        }

        [Fact]
        public async Task ScanLabel_RejectsBytesThatAreNotAnImage_AndUnknownIngredients()
        {
            var f = await SeedAsync();
            await f.Service().Invoking(s => s.ScanLabelAsync(Plain, Household, Person, new byte[] { 1, 2, 3 }))
                .Should().ThrowAsync<ArgumentException>();
            (await f.Service().ScanLabelAsync(999, Household, Person, SmallPng())).Should().BeNull();
            f.Db.IngredientVerifications.Should().BeEmpty();
        }

        [Fact]
        public async Task ScanLabel_IsRateLimitedPerPerson()
        {
            var f = await SeedAsync();
            f.Ocr.Setup(o => o.ExtractRawTextAsync(It.IsAny<byte[]>())).ReturnsAsync(string.Empty);
            for (var i = 0; i < IngredientVerificationService.MaxLabelScansPerHour; i++)
                f.Db.IngredientVerifications.Add(new IngredientVerificationEntity { IngredientId = Plain, HouseholdId = Household, Kind = "label", Status = "pending", Source = "label:user:10", CreatedByPersonId = Person, CreatedDate = DateTime.UtcNow, LastAskedDate = DateTime.UtcNow });
            await f.Db.SaveChangesAsync();

            await f.Service().Invoking(s => s.ScanLabelAsync(Plain, Household, Person, SmallPng()))
                .Should().ThrowAsync<InvalidOperationException>();
        }

        [Fact]
        public async Task Confirm_OverwritesWithUserValues_DerivesPer100g_WarnsButNeverBlocks()
        {
            var f = await SeedAsync();
            f.Ocr.Setup(o => o.ExtractRawTextAsync(It.IsAny<byte[]>())).ReturnsAsync(Label);
            var service = f.Service();
            var scan = await service.ScanLabelAsync(Plain, Household, Person, SmallPng());

            var confirmed = await service.ConfirmAsync(scan!.VerificationId, Person, new IngredientVerificationConfirmModel
            {
                ServingText = "2/3 cup",
                ServingGrams = 55,
                Calories = 900,
                Nutrients = new List<ConfirmNutrientModel>
                {
                    new() { Key = "fat", Amount = 9, Unit = "g", PercentDailyValue = 12 },
                    new() { Key = "sodium", Amount = 160, Unit = "mg" },
                    new() { Key = "carbohydrate", Amount = 37, Unit = "g" },
                    new() { Key = "protein", Amount = 3, Unit = "g" },
                },
            });

            confirmed.Should().NotBeNull();
            confirmed!.Confirmed.Should().BeTrue();
            confirmed.Status.Should().Be("pending");
            confirmed.Nutrients!.Calories.Should().Be(900);
            confirmed.Nutrients.Nutrients.Single(n => n.Key == "fat").Amount.Should().Be(9);
            confirmed.Nutrients.Per100g!.Calories.Should().Be(1636.36m);
            confirmed.Warnings.Should().Contain("per100g_calories_impossible");
            confirmed.Warnings.Should().Contain("calories_impossible");

            var row = f.Db.IngredientVerifications.Single();
            row.Confirmed.Should().BeTrue();
            row.ConfirmedByPersonId.Should().Be(Person);
            row.NutrientsJson.Should().Contain("\"calories\":900").And.NotContain("per100g");
            (await f.Db.IngredientNutrients.CountAsync(n => n.IngredientId == Plain)).Should().Be(0);
        }

        private const string EuLabel = "NUTRITION INFORMATION\nTypical values per 100g\nEnergy 1046kJ/250kcal\nFat 8.0g\nof which saturates 1.0g\nCarbohydrate 37g\nof which sugars 12g\nFibre 4g\nProtein 3g\nSalt 0.4g";

        [Fact]
        public async Task Confirm_KeepsAPer100gLabelOnItsBasis_AndNeverScalesItByAServingWeight()
        {
            var f = await SeedAsync();
            f.Ocr.Setup(o => o.ExtractRawTextAsync(It.IsAny<byte[]>())).ReturnsAsync(EuLabel);
            var service = f.Service();
            var scan = await service.ScanLabelAsync(Plain, Household, Person, SmallPng());
            scan!.Parsed.Basis.Should().Be(NutritionBasis.Per100g);

            var confirmed = await service.ConfirmAsync(scan.VerificationId, Person, new IngredientVerificationConfirmModel
            {
                ServingGrams = 30,
                Calories = 250,
                Nutrients = new List<ConfirmNutrientModel>
                {
                    new() { Key = "fat", Amount = 8, Unit = "g" },
                    new() { Key = "carbohydrate", Amount = 37, Unit = "g" },
                    new() { Key = "protein", Amount = 3, Unit = "g" },
                },
            });

            confirmed!.Nutrients!.Basis.Should().Be(NutritionBasis.Per100g);
            confirmed.Nutrients.Per100g!.Calories.Should().Be(250);
            confirmed.Nutrients.Per100g.Nutrients.Single(n => n.Key == "fat").Amount.Should().Be(8);
            confirmed.Warnings.Should().NotContain("per100g_calories_impossible");
            f.Db.IngredientVerifications.Single().NutrientsJson.Should().Contain("\"basis\":\"per100g\"");

            var forced = await service.ConfirmAsync(scan.VerificationId, Person, new IngredientVerificationConfirmModel { Basis = "serving", ServingGrams = 50, Calories = 250 });
            forced!.Nutrients!.Basis.Should().Be(NutritionBasis.Serving);
            forced.Nutrients.Per100g!.Calories.Should().Be(500);
        }

        [Fact]
        public async Task Confirm_PreservesPrintedLessThanAndNotSignificantReadings_TheUserDidNotOverride()
        {
            var f = await SeedAsync();
            f.Ocr.Setup(o => o.ExtractRawTextAsync(It.IsAny<byte[]>())).ReturnsAsync(Label + "\nDietary Fiber less than 1g\nNot a significant source of trans fat");
            var service = f.Service();
            var scan = await service.ScanLabelAsync(Plain, Household, Person, SmallPng());
            scan!.Parsed.Nutrients.Single(n => n.Key == "fiber").LessThan.Should().Be(1);
            scan.Parsed.NotSignificantSource.Should().Contain("trans_fat");

            var confirmed = await service.ConfirmAsync(scan.VerificationId, Person, new IngredientVerificationConfirmModel
            {
                ServingGrams = 55,
                Calories = 230,
                Nutrients = new List<ConfirmNutrientModel> { new() { Key = "fat", Amount = 8, Unit = "g" } },
            });

            confirmed!.Nutrients!.Nutrients.Single(n => n.Key == "fiber").Should().BeEquivalentTo(new { Amount = (decimal?)null, LessThan = 1m });
            confirmed.Nutrients.NotSignificantSource.Should().Contain("trans_fat");

            var typed = await service.ConfirmAsync(scan.VerificationId, Person, new IngredientVerificationConfirmModel
            {
                Nutrients = new List<ConfirmNutrientModel> { new() { Key = "fiber", Amount = 0.5m, Unit = "g" } },
            });
            typed!.Nutrients!.Nutrients.Single(n => n.Key == "fiber").Should().BeEquivalentTo(new { Amount = 0.5m, LessThan = (decimal?)null });

            await service.Invoking(s => s.ConfirmAsync(scan.VerificationId, Person, new IngredientVerificationConfirmModel { ServingGrams = -1 }))
                .Should().ThrowAsync<ArgumentException>();
        }

        [Fact]
        public async Task Confirm_RefusesNonLabelRows_BadUnits_AndMissingRows()
        {
            var f = await SeedAsync();
            var service = f.Service();
            await service.SkipAsync(Plain, Household, Person);
            var skip = f.Db.IngredientVerifications.Single();

            await service.Invoking(s => s.ConfirmAsync(skip.Id, Person, new IngredientVerificationConfirmModel()))
                .Should().ThrowAsync<InvalidOperationException>();
            (await service.ConfirmAsync(999, Person, new IngredientVerificationConfirmModel())).Should().BeNull();

            f.Ocr.Setup(o => o.ExtractRawTextAsync(It.IsAny<byte[]>())).ReturnsAsync(Label);
            var scan = await service.ScanLabelAsync(Plain, Household, Person, SmallPng());
            await service.Invoking(s => s.ConfirmAsync(scan!.VerificationId, Person, new IngredientVerificationConfirmModel
            {
                Nutrients = new List<ConfirmNutrientModel> { new() { Key = "fat", Amount = 1, Unit = "cups" } },
            })).Should().ThrowAsync<ArgumentException>();
        }

        [Fact]
        public async Task Barcode_StoresTheUpc_FillsAnEmptyGtinOnly_AndAttachesToARecentLabelScan()
        {
            var f = await SeedAsync();
            var service = f.Service();

            var first = await service.RecordBarcodeAsync(Plain, Household, Person, "0 36000 29145 2");
            first.Should().NotBeNull();
            first!.Should().BeEquivalentTo(new { Upc = "036000291452", Decoded = true, Source = "client", AppliedToIngredient = true });
            f.Db.Ingredients.Single(i => i.Id == Plain).GtinUpc.Should().Be("036000291452");
            f.Db.IngredientVerifications.Single().Should().BeEquivalentTo(new { Kind = "barcode", Status = "pending", Upc = "036000291452" });

            var second = await service.RecordBarcodeAsync(Plain, Household, Person, "4006381333931");
            second!.AppliedToIngredient.Should().BeFalse();
            f.Db.Ingredients.Single(i => i.Id == Plain).GtinUpc.Should().Be("036000291452");

            await service.Invoking(s => s.RecordBarcodeAsync(Plain, Household, Person, "036000291453"))
                .Should().ThrowAsync<ArgumentException>();
            (await service.RecordBarcodeAsync(999, Household, Person, "036000291452")).Should().BeNull();

            f.Ocr.Setup(o => o.ExtractRawTextAsync(It.IsAny<byte[]>())).ReturnsAsync(Label);
            var scan = await service.ScanLabelAsync(UserTyped, Household, Person, SmallPng());
            var attached = await service.RecordBarcodeAsync(UserTyped, Household, Person, "96385074");
            attached!.VerificationId.Should().Be(scan!.VerificationId);
            f.Db.IngredientVerifications.Single(v => v.Id == scan.VerificationId).Upc.Should().Be("96385074");
        }

        [Fact]
        public async Task Barcode_NeverWritesAGtinOntoAVerifiedCatalogFood()
        {
            var f = await SeedAsync();
            var result = await f.Service().RecordBarcodeAsync(Verified, Household, Person, "036000291452");

            result!.Should().BeEquivalentTo(new { Upc = "036000291452", Decoded = true, AppliedToIngredient = false });
            f.Db.Ingredients.Single(i => i.Id == Verified).GtinUpc.Should().BeNull();
            f.Db.IngredientVerifications.Single().Should().BeEquivalentTo(new { IngredientId = Verified, HouseholdId = Household, Kind = "barcode", Upc = "036000291452" });
        }

        [Fact]
        public async Task BarcodeFromImage_IsCappedPerPersonEvenWhenNothingDecodes()
        {
            var f = await SeedAsync();
            f.Barcodes.Setup(b => b.Decode(It.IsAny<byte[]>())).Returns((BarcodeDecodeResult?)null);
            var service = f.Service();
            for (var i = 0; i < IngredientVerificationService.MaxLabelScansPerHour; i++)
                (await service.RecordBarcodeFromImageAsync(Plain, Household, Person, SmallPng()))!.Decoded.Should().BeFalse();

            await service.Invoking(s => s.RecordBarcodeFromImageAsync(Plain, Household, Person, SmallPng()))
                .Should().ThrowAsync<InvalidOperationException>();
            (await service.RecordBarcodeFromImageAsync(Plain, Household, Person + 1, SmallPng()))!.Decoded.Should().BeFalse();
        }

        [Fact]
        public async Task BarcodeFromImage_UsesTheDecoder_AndRecordsNothingWhenItFinds_Nothing()
        {
            var f = await SeedAsync();
            var service = f.Service();
            f.Barcodes.Setup(b => b.Decode(It.IsAny<byte[]>())).Returns((BarcodeDecodeResult?)null);

            var none = await service.RecordBarcodeFromImageAsync(Plain, Household, Person, SmallPng());
            none!.Should().BeEquivalentTo(new { Decoded = false, Source = "server", VerificationId = (long?)null });
            f.Db.IngredientVerifications.Should().BeEmpty();

            f.Barcodes.Setup(b => b.Decode(It.IsAny<byte[]>())).Returns(new BarcodeDecodeResult("036000291452", "UPC_A"));
            var found = await service.RecordBarcodeFromImageAsync(Plain, Household, Person, SmallPng());
            found!.Should().BeEquivalentTo(new { Decoded = true, Source = "server", Format = "UPC_A", Upc = "036000291452", AppliedToIngredient = true });
        }

        [Fact]
        public async Task Measurements_ConvertMassUnits_KeepVolumeAsVolume_AndLeaveTheIngredientAlone()
        {
            var f = await SeedAsync();
            var service = f.Service();

            var oz = await service.RecordMeasurementsAsync(Plain, Household, Person, new IngredientMeasurementsModel { HouseholdId = Household, PackageAmount = 14.3m, PackageUnit = "oz", PiecesPerPackage = 36, CupGrams = 82.5m });
            oz!.PackageGrams.Should().Be(405.4m);
            oz.PackageMl.Should().BeNull();
            oz.PiecesPerPackage.Should().Be(36);
            oz.CupGrams.Should().Be(82.5m);
            oz.Kind.Should().Be("measurements");

            var floz = await service.RecordMeasurementsAsync(UserTyped, Household, Person, new IngredientMeasurementsModel { HouseholdId = Household, PackageAmount = 16, PackageUnit = "floz" });
            floz!.PackageMl.Should().Be(473.18m);
            floz.PackageGrams.Should().BeNull();

            var ingredient = f.Db.Ingredients.Single(i => i.Id == Plain);
            ingredient.GramsPerItem.Should().BeNull();
            ingredient.GramsPerMilliliter.Should().BeNull();
            ingredient.ReferenceServingGrams.Should().BeNull();

            await service.Invoking(s => s.RecordMeasurementsAsync(Plain, Household, Person, new IngredientMeasurementsModel { HouseholdId = Household }))
                .Should().ThrowAsync<ArgumentException>();
            await service.Invoking(s => s.RecordMeasurementsAsync(Plain, Household, Person, new IngredientMeasurementsModel { HouseholdId = Household, PackageAmount = 2, PackageUnit = "stone" }))
                .Should().ThrowAsync<ArgumentException>();
        }

        [Fact]
        public async Task List_ReturnsOnlyThisHouseholdsRows_NewestFirst()
        {
            var f = await SeedAsync();
            var service = f.Service();
            await service.SkipAsync(Plain, OtherHousehold, Person);
            await service.SkipAsync(Plain, Household, Person);
            await service.RecordMeasurementsAsync(Plain, Household, Person, new IngredientMeasurementsModel { HouseholdId = Household, CupGrams = 90 });

            var rows = await service.ListAsync(Household, Plain);
            rows.Should().HaveCount(2);
            rows.Select(r => r.HouseholdId).Should().AllBeEquivalentTo(Household);
            rows[0].Kind.Should().Be("measurements");
        }
    }
}
