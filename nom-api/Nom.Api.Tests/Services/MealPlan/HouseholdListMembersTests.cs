using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Person;
using Nom.Data.Plan;
using Nom.Orch.Services;
using Xunit;

namespace Nom.Api.Tests.Services.MealPlan
{
    /// <summary>
    /// The household list feeds Compose's recipient picker and the plan's per-day
    /// member badges, so it must carry its members, not just a count (audit DA-003).
    /// </summary>
    public class HouseholdListMembersTests
    {
        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        private sealed class NoPolicy : Nom.Orch.Interfaces.IPolicyEnforcementService
        {
            public Task<bool> IsStewardAsync(long p, long h) => Task.FromResult(true);
            public Task<bool> IsFeatureGatedAsync(long p, long h, string k) => Task.FromResult(false);
            public Task<bool> IsFeatureGatedAnywhereAsync(long p, string k) => Task.FromResult(false);
            public Task<bool> IsCuratedOnlyAsync(long p, long h) => Task.FromResult(false);
            public Task<List<long>> GetHouseholdsPlanningRecipeAsync(long r) => Task.FromResult(new List<long>());
            public Task<List<long>> GetLockedIngredientIdsAsync(long h) => Task.FromResult(new List<long>());
        }

        private static async Task<ApplicationDbContext> SeedAsync()
        {
            var db = NewContext();
            db.Households.AddRange(
                new HouseholdEntity { Id = 1, Name = "Rivera", HouseholdGroupId = 1 },
                new HouseholdEntity { Id = 2, Name = "Other", HouseholdGroupId = 1 });
            db.Persons.AddRange(
                new PersonEntity { Id = 10, Name = "Liz", UserId = "u-liz", Email = "liz@example.com" },
                new PersonEntity { Id = 11, Name = "Sam", UserId = "u-sam", Email = "sam@example.com" },
                new PersonEntity { Id = 12, Name = "Diego" },
                new PersonEntity { Id = 13, Name = "Gone", UserId = "u-gone" },
                new PersonEntity { Id = 20, Name = "Stranger", UserId = "u-x" });
            db.HouseholdMembers.AddRange(
                new HouseholdMemberEntity { Id = 100, HouseholdId = 1, PersonId = 10, Role = "Admin", IsActive = true, IsAdmin = true },
                new HouseholdMemberEntity { Id = 101, HouseholdId = 1, PersonId = 11, Role = "Member", IsActive = true },
                new HouseholdMemberEntity { Id = 102, HouseholdId = 1, PersonId = 12, Role = "Member", IsActive = true },
                new HouseholdMemberEntity { Id = 103, HouseholdId = 1, PersonId = 13, Role = "Member", IsActive = false },
                new HouseholdMemberEntity { Id = 104, HouseholdId = 1, PersonId = 11, Role = "Member", IsActive = true },
                new HouseholdMemberEntity { Id = 200, HouseholdId = 2, PersonId = 20, Role = "Admin", IsActive = true });
            await db.SaveChangesAsync();
            return db;
        }

        [Fact]
        public async Task List_includes_each_active_member_once()
        {
            using var db = await SeedAsync();
            var svc = new HouseholdOrchestrationService(db, new NoPolicy());

            var list = await svc.GetHouseholdsForMemberAsync(new List<long> { 1 });

            var household = list.Should().ContainSingle().Subject;
            household.Members.Should().NotBeNull();
            household.Members!.Select(m => m.PersonId).Should().BeEquivalentTo(new[] { 10L, 11L, 12L });
            household.MemberCount.Should().Be(3);
        }

        [Fact]
        public async Task List_marks_who_has_an_account_and_who_is_a_steward()
        {
            using var db = await SeedAsync();
            var svc = new HouseholdOrchestrationService(db, new NoPolicy());

            var members = (await svc.GetHouseholdsForMemberAsync(new List<long> { 1 })).Single().Members!;

            var liz = members.Single(m => m.PersonId == 10);
            liz.PersonName.Should().Be("Liz");
            liz.HasAccount.Should().BeTrue();
            liz.IsSteward.Should().BeTrue();
            members.Single(m => m.PersonId == 11).HasAccount.Should().BeTrue();
            members.Single(m => m.PersonId == 12).HasAccount.Should().BeFalse();
        }

        [Fact]
        public async Task List_does_not_expose_member_emails()
        {
            using var db = await SeedAsync();
            var svc = new HouseholdOrchestrationService(db, new NoPolicy());

            var members = (await svc.GetHouseholdsForMemberAsync(new List<long> { 1 })).Single().Members!;

            members.Should().OnlyContain(m => m.PersonEmail == null);
        }

        [Fact]
        public async Task List_keeps_each_households_members_to_itself()
        {
            using var db = await SeedAsync();
            var svc = new HouseholdOrchestrationService(db, new NoPolicy());

            var list = await svc.GetHouseholdsForMemberAsync(new List<long> { 1, 2 });

            list.Single(h => h.Id == 2).Members!.Select(m => m.PersonId).Should().Equal(20L);
            list.Single(h => h.Id == 1).Members!.Should().NotContain(m => m.PersonId == 20);
        }

        [Fact]
        public async Task Detail_marks_dependents_without_an_account()
        {
            using var db = await SeedAsync();
            var svc = new HouseholdOrchestrationService(db, new NoPolicy());

            var detail = await svc.GetHouseholdAsync(1);

            detail!.Members!.Single(m => m.PersonId == 12).HasAccount.Should().BeFalse();
            detail.Members!.Single(m => m.PersonId == 11).HasAccount.Should().BeTrue();
        }
    }
}
