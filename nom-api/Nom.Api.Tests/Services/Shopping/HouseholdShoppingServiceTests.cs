using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nom.Data;
using Nom.Data.Nutrient;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Shopping;
using Nom.Orch.Services;
using Nom.Orch.UtilityInterfaces;
using Xunit;

namespace Nom.Api.Tests.Services.Shopping
{
    /// <summary>
    /// Ad-hoc items link to the catalog by exact name or create a pending food the user authored;
    /// check-off state is one row per line, toggled in place and expiring after the TTL.
    /// </summary>
    public class HouseholdShoppingServiceTests
    {
        private const long Household = 100;
        private const long OtherHousehold = 200;
        private const long Person = 10;
        private const long CaloriesId = 5035;
        private const long Yogurt = 1;
        private const long Dairy = 7000;

        private sealed class Fixture
        {
            public ApplicationDbContext Db { get; } = new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            public IConfiguration Config { get; init; } = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Shopping:CheckTtlDays"] = "3" }).Build();

            public HouseholdShoppingService Service()
            {
                var verifications = new IngredientVerificationService(Db, Mock.Of<ITesseractOcrService>(), Mock.Of<IBarcodeDecodeService>(),
                    Mock.Of<IMediaStorageService>(), new NoOpVerificationLookupLane(), NullLogger<IngredientVerificationService>.Instance);
                return new HouseholdShoppingService(Db, Config, verifications, NullLogger<HouseholdShoppingService>.Instance);
            }
        }

        private static async Task<Fixture> SeedAsync()
        {
            var f = new Fixture();
            f.Db.Nutrients.Add(new NutrientEntity { Id = CaloriesId, Name = "Calories", DefaultMeasurementId = 16, CreatedDate = DateTime.UtcNow });
            f.Db.Set<ReferenceEntity>().Add(new ReferenceEntity { Id = Dairy, Name = "Dairy" });
            f.Db.Ingredients.Add(new IngredientEntity { Id = Yogurt, Name = "Greek Yogurt", FdcId = "170903", CurationStatusId = (long)CurationStatusEnum.Curated, LabelId = Dairy, CreatedDate = DateTime.UtcNow });
            f.Db.IngredientNutrients.Add(new IngredientNutrientEntity { IngredientId = Yogurt, NutrientId = CaloriesId, Amount = 59, MeasurementId = 16, CreatedDate = DateTime.UtcNow });
            await f.Db.SaveChangesAsync();
            return f;
        }

        [Fact]
        public async Task AddItem_LinksAnExistingIngredientByNameCaseInsensitively_InsteadOfCreatingOne()
        {
            var f = await SeedAsync();
            var result = await f.Service().AddItemAsync(Household, Person, new HouseholdShoppingItemCreateModel { HouseholdId = Household, Name = "  greek yogurt " });

            result.Should().NotBeNull();
            result!.Created.Should().BeTrue();
            result.Item.Should().BeEquivalentTo(new { Name = "greek yogurt", IngredientId = Yogurt, Department = "Dairy", IngredientNeedsVerification = false, CreatedByPersonId = Person, HouseholdId = Household });
            (await f.Db.Ingredients.CountAsync()).Should().Be(1);
        }

        [Fact]
        public async Task AddItem_CreatesAPendingCurationIngredientAuthoredByTheUser_OnAMiss()
        {
            var f = await SeedAsync();
            var result = await f.Service().AddItemAsync(Household, Person, new HouseholdShoppingItemCreateModel { HouseholdId = Household, Name = "Grandma's granola", Quantity = 2, Unit = "bags" });

            result!.Created.Should().BeTrue();
            result.Item.IngredientId.Should().NotBeNull();
            result.Item.IngredientNeedsVerification.Should().BeTrue();
            result.Item.Quantity.Should().Be(2);
            result.Item.Unit.Should().Be("bags");

            var created = f.Db.Ingredients.Single(i => i.Id == result.Item.IngredientId);
            created.Should().BeEquivalentTo(new { Name = "Grandma's granola", CurationStatusId = (long)CurationStatusEnum.PendingCuration, AuthorId = Person, CreatedByPersonId = Person, FdcDataType = "Custom", FdcId = (string?)null });
            f.Db.IngredientNutrients.Where(n => n.IngredientId == created.Id).Should().BeEmpty();
        }

        [Fact]
        public async Task AddItem_ReturnsTheExistingActiveItemInsteadOfADuplicate_AndRejectsUnknownIngredientIds()
        {
            var f = await SeedAsync();
            var service = f.Service();
            var first = await service.AddItemAsync(Household, Person, new HouseholdShoppingItemCreateModel { HouseholdId = Household, Name = "Oat milk" });
            var again = await service.AddItemAsync(Household, Person, new HouseholdShoppingItemCreateModel { HouseholdId = Household, Name = "OAT MILK" });

            again!.Created.Should().BeFalse();
            again.Item.Id.Should().Be(first!.Item.Id);
            (await f.Db.HouseholdShoppingItems.CountAsync()).Should().Be(1);

            var other = await service.AddItemAsync(OtherHousehold, Person, new HouseholdShoppingItemCreateModel { HouseholdId = OtherHousehold, Name = "oat milk" });
            other!.Created.Should().BeTrue();
            other.Item.IngredientId.Should().Be(first.Item.IngredientId);

            (await service.AddItemAsync(Household, Person, new HouseholdShoppingItemCreateModel { HouseholdId = Household, Name = "Ghost", IngredientId = 999 })).Should().BeNull();
        }

        [Fact]
        public async Task Checks_ToggleInPlace_ExpireAfterTtl_AndCarryAVerificationHint()
        {
            var f = await SeedAsync();
            var service = f.Service();
            var granola = await service.AddItemAsync(Household, Person, new HouseholdShoppingItemCreateModel { HouseholdId = Household, Name = "Granola" });

            var on = await service.SetCheckAsync(Household, Person, new ShoppingCheckRequestModel { HouseholdId = Household, ItemKey = Yogurt.ToString(), Checked = true });
            on!.Should().BeEquivalentTo(new { ItemKey = "1", Checked = true, ItemId = (long?)null });
            on.Verification.Should().BeNull();

            var off = await service.SetCheckAsync(Household, Person, new ShoppingCheckRequestModel { HouseholdId = Household, ItemKey = Yogurt.ToString(), Checked = false });
            off!.Checked.Should().BeFalse();
            var onAgain = await service.SetCheckAsync(Household, Person, new ShoppingCheckRequestModel { HouseholdId = Household, ItemKey = Yogurt.ToString(), Checked = true });
            onAgain!.Checked.Should().BeTrue();
            (await f.Db.HouseholdShoppingChecks.IgnoreQueryFilters().CountAsync()).Should().Be(1);

            var unverified = await service.SetCheckAsync(Household, Person, new ShoppingCheckRequestModel { HouseholdId = Household, ItemKey = granola!.Item.IngredientId!.Value.ToString(), Checked = true });
            unverified!.Verification.Should().NotBeNull();
            unverified.Verification!.ShouldAsk.Should().BeTrue();

            var adHoc = await service.SetCheckAsync(Household, Person, new ShoppingCheckRequestModel { HouseholdId = Household, ItemId = granola.Item.Id, Checked = true });
            adHoc!.Should().BeEquivalentTo(new { ItemId = granola.Item.Id, ItemKey = $"adhoc:{granola.Item.Id}", Checked = true });
            adHoc.Verification!.IngredientId.Should().Be(granola.Item.IngredientId.Value);

            var state = await service.GetStateAsync(Household);
            state.Checks.Select(c => c.ItemKey).Should().BeEquivalentTo(new[] { "1", granola.Item.IngredientId.Value.ToString() });
            state.Items.Single().Checked.Should().BeTrue();

            var stale = f.Db.HouseholdShoppingChecks.Single(c => c.ItemKey == "1");
            stale.CheckedDate = DateTime.UtcNow.AddDays(-4);
            await f.Db.SaveChangesAsync();
            state = await service.GetStateAsync(Household);
            state.Checks.Select(c => c.ItemKey).Should().BeEquivalentTo(new[] { granola.Item.IngredientId.Value.ToString() });
            f.Db.HouseholdShoppingChecks.Single(c => c.ItemKey == "1").Checked.Should().BeFalse();

            await service.ClearChecksAsync(Household, Person);
            state = await service.GetStateAsync(Household);
            state.Checks.Should().BeEmpty();
            state.Items.Single().Checked.Should().BeFalse();
        }

        [Fact]
        public async Task Checks_RequireExactlyOneTarget_AndIgnoreItemsOfOtherHouseholds()
        {
            var f = await SeedAsync();
            var service = f.Service();
            var other = await service.AddItemAsync(OtherHousehold, Person, new HouseholdShoppingItemCreateModel { HouseholdId = OtherHousehold, Name = "Theirs" });

            await service.Invoking(s => s.SetCheckAsync(Household, Person, new ShoppingCheckRequestModel { HouseholdId = Household, Checked = true }))
                .Should().ThrowAsync<ArgumentException>();
            await service.Invoking(s => s.SetCheckAsync(Household, Person, new ShoppingCheckRequestModel { HouseholdId = Household, ItemKey = "1", ItemId = 1, Checked = true }))
                .Should().ThrowAsync<ArgumentException>();
            (await service.SetCheckAsync(Household, Person, new ShoppingCheckRequestModel { HouseholdId = Household, ItemId = other!.Item.Id, Checked = true })).Should().BeNull();
            f.Db.HouseholdShoppingItems.Single().Checked.Should().BeFalse();
        }

        [Fact]
        public async Task UpdateAndDelete_ChangeOnlyTheHouseholdsRow()
        {
            var f = await SeedAsync();
            var service = f.Service();
            var item = await service.AddItemAsync(Household, Person, new HouseholdShoppingItemCreateModel { HouseholdId = Household, Name = "Granola" });

            var updated = await service.UpdateItemAsync(item!.Item.Id, Person, new HouseholdShoppingItemUpdateModel { Name = "Granola clusters", Quantity = 1, Unit = "box", IngredientId = Yogurt });
            updated!.Should().BeEquivalentTo(new { Name = "Granola clusters", Quantity = 1m, Unit = "box", IngredientId = Yogurt, Department = "Dairy" });

            var cleared = await service.UpdateItemAsync(item.Item.Id, Person, new HouseholdShoppingItemUpdateModel());
            cleared!.Should().BeEquivalentTo(new { Name = "Granola clusters", Quantity = (decimal?)null, Unit = (string?)null, IngredientId = (long?)null });

            await service.Invoking(s => s.UpdateItemAsync(item.Item.Id, Person, new HouseholdShoppingItemUpdateModel { IngredientId = 999 }))
                .Should().ThrowAsync<ArgumentException>();
            (await service.UpdateItemAsync(999, Person, new HouseholdShoppingItemUpdateModel())).Should().BeNull();

            (await service.DeleteItemAsync(item.Item.Id, Person)).Should().BeTrue();
            (await service.DeleteItemAsync(item.Item.Id, Person)).Should().BeFalse();
            (await service.GetItemAsync(item.Item.Id)).Should().BeNull();
            (await service.GetStateAsync(Household)).Items.Should().BeEmpty();
            f.Db.HouseholdShoppingItems.IgnoreQueryFilters().Single().IsDeleted.Should().BeTrue();
        }
    }
}
