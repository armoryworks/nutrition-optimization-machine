using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Plan;
using Nom.Data.Recipe;
using Nom.Orch.Services;
using Xunit;

namespace Nom.Api.Tests.Services.MealPlan
{
    /// <summary>
    /// Redeeming the same household invite twice must converge on one membership,
    /// not error or duplicate (a real duplicate landed in prod — audit N-26).
    /// </summary>
    public class HouseholdJoinIdempotencyTests
    {
        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        private sealed class NoPolicy : Nom.Orch.Interfaces.IPolicyEnforcementService
        {
            public Task<bool> IsStewardAsync(long p, long h) => Task.FromResult(true);
            public Task<bool> IsFeatureGatedAsync(long p, long h, string k) => Task.FromResult(false);
            public Task<bool> IsFeatureGatedAnywhereAsync(long p, string k) => Task.FromResult(false);
            public Task<bool> IsCuratedOnlyAsync(long p, long h) => Task.FromResult(false);
            public Task<System.Collections.Generic.List<long>> GetHouseholdsPlanningRecipeAsync(long r) => Task.FromResult(new System.Collections.Generic.List<long>());
            public Task<System.Collections.Generic.List<long>> GetLockedIngredientIdsAsync(long h) => Task.FromResult(new System.Collections.Generic.List<long>());
        }

        [Fact]
        public async Task Second_redemption_returns_the_existing_membership()
        {
            using var db = NewContext();
            db.Households.Add(new HouseholdEntity { Id = 1, Name = "H", HouseholdGroupId = 1 });
            db.Persons.Add(new Nom.Data.Person.PersonEntity { Id = 10, Name = "Aaron" });
            db.HouseholdInviteTokens.Add(new HouseholdInviteTokenEntity { HouseholdId = 1, Token = "tok" });
            await db.SaveChangesAsync();
            var svc = new HouseholdOrchestrationService(db, new NoPolicy());

            var first = await svc.JoinHouseholdAsync("tok", 10);
            var second = await svc.JoinHouseholdAsync("tok", 10);

            second.Id.Should().Be(first.Id);
            (await db.HouseholdMembers.CountAsync(m => m.HouseholdId == 1 && m.PersonId == 10)).Should().Be(1);
        }

        [Fact]
        public async Task Rejoining_reactivates_an_inactive_membership()
        {
            using var db = NewContext();
            db.Households.Add(new HouseholdEntity { Id = 1, Name = "H", HouseholdGroupId = 1 });
            db.Persons.Add(new Nom.Data.Person.PersonEntity { Id = 10, Name = "Aaron" });
            db.HouseholdMembers.Add(new HouseholdMemberEntity { HouseholdId = 1, PersonId = 10, Role = "Member", IsActive = false });
            db.HouseholdInviteTokens.Add(new HouseholdInviteTokenEntity { HouseholdId = 1, Token = "tok" });
            await db.SaveChangesAsync();
            var svc = new HouseholdOrchestrationService(db, new NoPolicy());

            var result = await svc.JoinHouseholdAsync("tok", 10);

            result.IsActive.Should().BeTrue();
            (await db.HouseholdMembers.CountAsync(m => m.HouseholdId == 1 && m.PersonId == 10)).Should().Be(1);
        }
    }
}
