using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nom.Data;
using Nom.Data.Plan;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Orch.Models.Pantry;
using Nom.Orch.Services;
using Xunit;

namespace Nom.Api.Tests.Services.Shopping
{
    /// <summary>
    /// Pantry item statuses are resolved by name and created on first use — the old
    /// hardcoded ids (502/503/504) never existed in any seeded database, so every
    /// pantry insert violated FK_PantryItem_Reference_ItemStatusTypeId (audit N-27).
    /// </summary>
    public class PantryStatusResolutionTests
    {
        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        [Fact]
        public async Task Add_creates_the_In_Pantry_status_and_links_it_to_the_group()
        {
            using var db = NewContext();
            var household = new HouseholdEntity { Id = 1, Name = "H", HouseholdGroupId = 1 };
            db.Households.Add(household);
            db.HouseholdMembers.Add(new HouseholdMemberEntity { HouseholdId = 1, PersonId = 10, IsActive = true });
            var plan = new PlanEntity { Id = 1, Name = "P", AuthorId = 10 };
            db.Plans.Add(plan);
            household.Plans.Add(plan);
            db.Set<ReferenceGroupEntity>().Add(new ReferenceGroupEntity { Id = 5, Name = "Item Status Types" });
            db.Ingredients.Add(new IngredientEntity { Id = 100, Name = "Potato", CurationStatusId = 9003 });
            db.Set<Nom.Data.Measurement.BaseMeasurementEntity>().Add(new Nom.Data.Measurement.BaseMeasurementEntity { Id = 3, Name = "Piece", Symbol = "pc", MeasurementCategoryId = 3 });
            await db.SaveChangesAsync();

            var svc = new PantryOrchestrationService(db, NullLogger<PantryOrchestrationService>.Instance);
            var created = await svc.AddPantryItemAsync(new PantryItemCreateModel
            {
                HouseholdId = 1, IngredientId = 100, Quantity = 5, MeasurementId = 3,
            });

            created.Should().NotBeNull();
            var status = await db.Set<ReferenceEntity>().SingleAsync(r => r.Name == "In Pantry");
            (await db.PantryItems.SingleAsync()).ItemStatusTypeId.Should().Be(status.Id);
            (await db.Set<ReferenceGroupEntity>().Include(g => g.References).SingleAsync(g => g.Id == 5))
                .References!.Should().Contain(r => r.Name == "In Pantry");
        }
    }
}
