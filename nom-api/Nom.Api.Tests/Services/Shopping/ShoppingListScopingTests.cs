using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Plan;
using Nom.Data.Shopping;
using Nom.Orch.Models.Shopping;
using Nom.Orch.Services;
using Xunit;

namespace Nom.Api.Tests.Services.Shopping
{
    /// <summary>
    /// Tenant-scoping tests: shopping lists must only be visible to their author
    /// and to members of their household.
    /// </summary>
    public class ShoppingListScopingTests
    {
        private const long Author = 10;
        private const long HouseholdMate = 11;
        private const long Outsider = 12;
        private const long HouseholdId = 100;
        private const long OtherHouseholdId = 200;

        private static ApplicationDbContext NewContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            return new ApplicationDbContext(options);
        }

        private static async Task<long> SeedAsync(ApplicationDbContext context)
        {
            context.HouseholdMembers.Add(new HouseholdMemberEntity { PersonId = Author, HouseholdId = HouseholdId });
            context.HouseholdMembers.Add(new HouseholdMemberEntity { PersonId = HouseholdMate, HouseholdId = HouseholdId });
            context.HouseholdMembers.Add(new HouseholdMemberEntity { PersonId = Outsider, HouseholdId = OtherHouseholdId });

            var list = new ShoppingListEntity
            {
                Name = "Groceries",
                AuthorId = Author,
                HouseholdId = HouseholdId,
                CreatedDate = DateTime.UtcNow,
            };
            context.ShoppingLists.Add(list);
            await context.SaveChangesAsync();
            return list.Id;
        }

        [Fact]
        public async Task GetAll_ReturnsListsForAuthorAndHouseholdMate_ButNotOutsider()
        {
            using var context = NewContext();
            await SeedAsync(context);
            var service = new ShoppingListOrchestrationService(context);

            (await service.GetAllShoppingListsAsync(Author)).Should().HaveCount(1);
            (await service.GetAllShoppingListsAsync(HouseholdMate)).Should().HaveCount(1);
            (await service.GetAllShoppingListsAsync(Outsider)).Should().BeEmpty();
        }

        [Fact]
        public async Task Get_ReturnsNullForOutsider()
        {
            using var context = NewContext();
            var listId = await SeedAsync(context);
            var service = new ShoppingListOrchestrationService(context);

            (await service.GetShoppingListAsync(listId, HouseholdMate)).Should().NotBeNull();
            (await service.GetShoppingListAsync(listId, Outsider)).Should().BeNull();
        }

        [Fact]
        public async Task Update_DeniesOutsider_AndDeniesMovingIntoForeignHousehold()
        {
            using var context = NewContext();
            var listId = await SeedAsync(context);
            var service = new ShoppingListOrchestrationService(context);
            var update = new ShoppingListUpdateModel { Name = "Renamed", HouseholdId = HouseholdId };

            (await service.UpdateShoppingListAsync(listId, update, Outsider)).Should().BeNull();

            var foreignMove = new ShoppingListUpdateModel { Name = "Groceries", HouseholdId = OtherHouseholdId };
            (await service.UpdateShoppingListAsync(listId, foreignMove, Author)).Should().BeNull();

            var renamed = await service.UpdateShoppingListAsync(listId, update, Author);
            renamed.Should().NotBeNull();
            renamed!.Name.Should().Be("Renamed");
        }

        [Fact]
        public async Task Delete_DeniesOutsider_AllowsAuthor()
        {
            using var context = NewContext();
            var listId = await SeedAsync(context);
            var service = new ShoppingListOrchestrationService(context);

            (await service.DeleteShoppingListAsync(listId, Outsider)).Should().BeFalse();
            (await service.DeleteShoppingListAsync(listId, Author)).Should().BeTrue();
        }

        [Fact]
        public async Task AddItem_DeniesOutsider_AllowsHouseholdMate()
        {
            using var context = NewContext();
            var listId = await SeedAsync(context);
            var service = new ShoppingListOrchestrationService(context);
            var item = new ShoppingListItemCreateModel { ShoppingListId = listId, Name = "Milk" };

            (await service.AddItemAsync(item, Outsider)).Should().BeNull();
            (await service.AddItemAsync(item, HouseholdMate)).Should().NotBeNull();
        }

        [Fact]
        public async Task Create_ThrowsWhenTargetingForeignHousehold()
        {
            using var context = NewContext();
            await SeedAsync(context);
            var service = new ShoppingListOrchestrationService(context);
            var model = new ShoppingListCreateModel { Name = "Sneaky", HouseholdId = OtherHouseholdId };

            var act = () => service.CreateShoppingListAsync(model, Author);
            await act.Should().ThrowAsync<InvalidOperationException>();
        }
    }
}
