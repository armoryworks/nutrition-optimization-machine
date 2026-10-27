using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nom.Api.Authentication;
using Nom.Data;
using Nom.Data.Person;
using Nom.Data.Plan;
using Xunit;

namespace Nom.Api.Tests.Authentication
{
    /// <summary>
    /// A sign-in token keeps the household claims it was minted with for up to a day. Leaving or being removed
    /// from a household must cut access on the next request, not at the next sign-in.
    /// </summary>
    public class MembershipClaimsTransformationTests
    {
        private static async Task<ApplicationDbContext> SeedAsync()
        {
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            db.Households.AddRange(
                new HouseholdEntity { Id = 1, Name = "Rivera", HouseholdGroupId = 1 },
                new HouseholdEntity { Id = 2, Name = "Left", HouseholdGroupId = 1 },
                new HouseholdEntity { Id = 3, Name = "Gone", HouseholdGroupId = 1, IsDeleted = true });
            db.Persons.Add(new PersonEntity { Id = 10, Name = "Liz", UserId = "u-liz" });
            db.HouseholdMembers.AddRange(
                new HouseholdMemberEntity { HouseholdId = 1, PersonId = 10, Role = "Admin", IsActive = true, CanManage = true },
                new HouseholdMemberEntity { HouseholdId = 2, PersonId = 10, Role = "Admin", IsActive = false, CanManage = true },
                new HouseholdMemberEntity { HouseholdId = 3, PersonId = 10, Role = "Member", IsActive = true });
            await db.SaveChangesAsync();
            return db;
        }

        private static ClaimsPrincipal TokenFor(long personId, params (string Type, long Id)[] memberships)
        {
            var identity = new ClaimsIdentity("Bearer");
            identity.AddClaim(new Claim("PersonId", personId.ToString()));
            identity.AddClaims(memberships.Select(m => new Claim(m.Type, m.Id.ToString())));
            return new ClaimsPrincipal(identity);
        }

        private static string[] Values(ClaimsPrincipal principal, string type) =>
            principal.FindAll(type).Select(c => c.Value).OrderBy(v => v).ToArray();

        [Fact]
        public async Task Claims_for_a_household_the_person_has_left_are_dropped()
        {
            var db = await SeedAsync();
            var token = TokenFor(10, ("HouseholdMember", 1), ("HouseholdMember", 2), ("can_manage_household", 2));

            var result = await new MembershipClaimsTransformation(db).TransformAsync(token);

            Values(result, "HouseholdMember").Should().Equal("1");
            Values(result, "can_manage_household").Should().Equal("1");
        }

        [Fact]
        public async Task Removing_the_membership_row_cuts_access_on_the_next_request()
        {
            var db = await SeedAsync();
            var transformation = new MembershipClaimsTransformation(db);
            var token = TokenFor(10, ("HouseholdMember", 1));
            (await transformation.TransformAsync(token)).HasClaim("HouseholdMember", "1").Should().BeTrue();

            db.HouseholdMembers.Remove(await db.HouseholdMembers.SingleAsync(hm => hm.HouseholdId == 1));
            await db.SaveChangesAsync();

            var result = await transformation.TransformAsync(token);
            result.FindAll("HouseholdMember").Should().BeEmpty();
            result.FindAll("can_manage_household").Should().BeEmpty();
        }

        [Fact]
        public async Task A_deleted_household_grants_nothing()
        {
            var db = await SeedAsync();

            var result = await new MembershipClaimsTransformation(db).TransformAsync(TokenFor(10, ("HouseholdMember", 3)));

            Values(result, "HouseholdMember").Should().NotContain("3");
        }

        [Fact]
        public async Task A_membership_granted_after_sign_in_applies_without_signing_in_again()
        {
            var db = await SeedAsync();
            db.Households.Add(new HouseholdEntity { Id = 4, Name = "New", HouseholdGroupId = 1 });
            db.HouseholdMembers.Add(new HouseholdMemberEntity { HouseholdId = 4, PersonId = 10, Role = "Member", IsActive = true });
            await db.SaveChangesAsync();

            var result = await new MembershipClaimsTransformation(db).TransformAsync(TokenFor(10, ("HouseholdMember", 1)));

            Values(result, "HouseholdMember").Should().Equal("1", "4");
        }

        [Fact]
        public async Task Other_claims_and_the_original_principal_are_left_alone()
        {
            var db = await SeedAsync();
            var token = TokenFor(10, ("HouseholdMember", 2));
            ((ClaimsIdentity)token.Identity!).AddClaim(new Claim("CanManageCuration", "true"));

            var result = await new MembershipClaimsTransformation(db).TransformAsync(token);

            result.HasClaim("CanManageCuration", "true").Should().BeTrue();
            result.FindFirst("PersonId")!.Value.Should().Be("10");
            token.HasClaim("HouseholdMember", "2").Should().BeTrue();
        }

        [Fact]
        public async Task An_anonymous_or_person_less_principal_is_returned_unchanged()
        {
            var db = await SeedAsync();
            var transformation = new MembershipClaimsTransformation(db);
            var anonymous = new ClaimsPrincipal(new ClaimsIdentity());
            var registering = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("HouseholdMember", "1") }, "Bearer"));

            (await transformation.TransformAsync(anonymous)).Should().BeSameAs(anonymous);
            (await transformation.TransformAsync(registering)).Should().BeSameAs(registering);
        }
    }
}
