using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Services;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// What the catalog cleanup plans to do with wordings that are a food plus a measure or a
    /// preparation. Names here are real prod rows.
    /// </summary>
    public class CatalogCleanupPlanTests
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

        private static IngredientEntity Wording(string name) => new() { Name = name, CurationStatusId = 9000 };

        [Fact]
        public async Task A_wording_merges_into_the_curated_food_its_bare_name_means()
        {
            using var db = NewContext();
            var onion = new IngredientEntity { Name = "Onion", FdcId = "170000", FdcDataType = "sr_legacy_food", CurationStatusId = 9003 };
            var flour = new IngredientEntity { Name = "Flour", FdcId = "168894", FdcDataType = "sr_legacy_food", CurationStatusId = 9003 };
            var pepper = new IngredientEntity { Name = "Pepper", FdcId = "170931", FdcDataType = "sr_legacy_food", CurationStatusId = 9003 };
            db.Ingredients.AddRange(onion, flour, pepper,
                Wording("onion"), Wording("small onions"), Wording("chopped onion"), Wording("tablespoonful of flour"),
                Wording("peppers"), Wording("dried onion"), Wording("gill of cedraty essence"));
            await db.SaveChangesAsync();

            var preview = await new CatalogCleanupService(db, new NoUser()).PreviewAsync(500);

            var plan = preview.Samples.ToDictionary(a => a.Name, a => a);
            foreach (var name in new[] { "onion", "small onions", "chopped onion" })
            {
                plan[name].Action.Should().Be("merge");
                plan[name].TargetId.Should().Be(onion.Id, $"\"{name}\" is the curated onion, not the uncurated wording");
            }
            plan["tablespoonful of flour"].TargetId.Should().Be(flour.Id);
            plan["gill of cedraty essence"].Action.Should().Be("rename");
            plan["gill of cedraty essence"].NormalizedName.Should().Be("cedraty essence");
            plan.Should().NotContainKey("peppers", "peppers are the vegetable and Pepper is the spice");
            plan.Should().NotContainKey("dried onion", "dried changes what the food is");
        }

        [Fact]
        public async Task A_chain_of_wordings_ends_on_one_row_and_never_on_a_row_that_is_itself_merged_away()
        {
            using var db = NewContext();
            db.Ingredients.AddRange(Wording("sago"), Wording("sagos"), Wording("washed sagos"));
            await db.SaveChangesAsync();
            var head = await db.Ingredients.SingleAsync(i => i.Name == "sago");

            var preview = await new CatalogCleanupService(db, new NoUser()).PreviewAsync(500);

            preview.Samples.Should().HaveCount(2);
            preview.Samples.Should().OnlyContain(a => a.Action == "merge" && a.TargetId == head.Id);
        }
    }
}
