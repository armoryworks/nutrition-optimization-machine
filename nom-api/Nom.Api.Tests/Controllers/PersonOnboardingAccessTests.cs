using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Nom.Api.Controllers;
using Nom.Data;
using Nom.Data.Person;
using Nom.Data.Plan;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Person;
using Nom.Orch.Services;
using Xunit;

namespace Nom.Api.Tests.Controllers
{
    /// <summary>
    /// The edit-member dialog and the Member Policies panel read a member's profile and restrictions
    /// through GET /api/Person/{id}/onboarding, so it must admit exactly who the sibling profile,
    /// restrictions and macro-goal endpoints admit: the person themselves, or a caller holding a
    /// HouseholdMember claim for a household the person is an active member of (DA-002).
    /// </summary>
    public class PersonOnboardingAccessTests
    {
        private const long Household = 100;
        private const long OtherHousehold = 200;

        private sealed record Seeded(long Steward, long Dependent, long PlainMember, long Outsider, long FormerMember);

        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

        private static async Task<Seeded> SeedAsync(ApplicationDbContext context)
        {
            var steward = new PersonEntity { Name = "Steward", UserId = "u-steward", CreatedDate = DateTime.UtcNow };
            var dependent = new PersonEntity { Name = "Tobi", CreatedDate = DateTime.UtcNow };
            var plain = new PersonEntity { Name = "Plain", UserId = "u-plain", CreatedDate = DateTime.UtcNow };
            var outsider = new PersonEntity { Name = "Outsider", UserId = "u-outsider", CreatedDate = DateTime.UtcNow };
            var former = new PersonEntity { Name = "Former", CreatedDate = DateTime.UtcNow };
            context.Persons.AddRange(steward, dependent, plain, outsider, former);
            context.Households.AddRange(
                new HouseholdEntity { Id = Household, Name = "Ours" },
                new HouseholdEntity { Id = OtherHousehold, Name = "Theirs" });
            await context.SaveChangesAsync();

            context.HouseholdMembers.AddRange(
                new HouseholdMemberEntity { HouseholdId = Household, PersonId = steward.Id, CanManage = true, IsAdmin = true, CreatedDate = DateTime.UtcNow },
                new HouseholdMemberEntity { HouseholdId = Household, PersonId = dependent.Id, CreatedDate = DateTime.UtcNow },
                new HouseholdMemberEntity { HouseholdId = Household, PersonId = plain.Id, CreatedDate = DateTime.UtcNow },
                new HouseholdMemberEntity { HouseholdId = Household, PersonId = former.Id, IsActive = false, CreatedDate = DateTime.UtcNow },
                new HouseholdMemberEntity { HouseholdId = OtherHousehold, PersonId = outsider.Id, CanManage = true, CreatedDate = DateTime.UtcNow });
            context.Restrictions.AddRange(
                new RestrictionEntity { PersonId = dependent.Id, Name = "Peanut Allergy", RestrictionTypeId = 1, CreatedDate = DateTime.UtcNow },
                new RestrictionEntity { PersonId = dependent.Id, PlanId = 9999, Name = "Another Household's Plan Rule", RestrictionTypeId = 1, CreatedDate = DateTime.UtcNow });
            await context.SaveChangesAsync();

            return new Seeded(steward.Id, dependent.Id, plain.Id, outsider.Id, former.Id);
        }

        private static PersonController Controller(ApplicationDbContext context, long personId, params (string Type, long Household)[] claims)
        {
            var service = new PersonOrchestrationService(
                context,
                Mock.Of<IHttpContextAccessor>(),
                Mock.Of<ICurrentUserService>(),
                Mock.Of<IPrivacyOrchestrationService>(),
                Mock.Of<IHouseholdOrchestrationService>(),
                NullLogger<PersonOrchestrationService>.Instance);
            var all = new List<Claim> { new("PersonId", personId.ToString()) };
            all.AddRange(claims.Select(c => new Claim(c.Type, c.Household.ToString())));
            return new PersonController(service, Mock.Of<IMacroGoalOrchestrationService>())
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(all, "test")) },
                },
            };
        }

        private static (string, long) Member(long household) => ("HouseholdMember", household);
        private static (string, long) Manager(long household) => ("can_manage_household", household);

        [Fact]
        public async Task Self_CanReadTheirOwnOnboardingState()
        {
            using var context = NewContext();
            var seeded = await SeedAsync(context);

            var result = await Controller(context, seeded.Steward, Member(Household), Manager(Household)).GetOnboardingState(seeded.Steward);

            result.Should().BeOfType<OkObjectResult>()
                .Which.Value.Should().BeOfType<OnboardingStateResponse>()
                .Which.PersonId.Should().Be(seeded.Steward);
        }

        [Fact]
        public async Task SelfWithoutAnyHousehold_CanStillReadTheirOwnOnboardingState()
        {
            using var context = NewContext();
            var seeded = await SeedAsync(context);

            var result = await Controller(context, seeded.Outsider).GetOnboardingState(seeded.Outsider);

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task StewardOfTheSameHousehold_ReadsADependentsProfileAndRestrictions()
        {
            using var context = NewContext();
            var seeded = await SeedAsync(context);

            var result = await Controller(context, seeded.Steward, Member(Household), Manager(Household)).GetOnboardingState(seeded.Dependent);

            var state = result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<OnboardingStateResponse>().Subject;
            state.PersonDetails.Name.Should().Be("Tobi");
            state.Restrictions.Select(r => r.Name).Should().ContainSingle().Which.Should().Be("Peanut Allergy");
        }

        [Fact]
        public async Task MemberOfAnotherHousehold_IsForbidden_EvenWhenTheyManageTheirOwn()
        {
            using var context = NewContext();
            var seeded = await SeedAsync(context);

            var result = await Controller(context, seeded.Outsider, Member(OtherHousehold), Manager(OtherHousehold)).GetOnboardingState(seeded.Dependent);

            result.Should().BeOfType<ForbidResult>();
        }

        [Fact]
        public async Task NonStewardMemberOfTheSameHousehold_IsAdmitted_LikeTheSiblingEndpoints()
        {
            using var context = NewContext();
            var seeded = await SeedAsync(context);
            var controller = Controller(context, seeded.PlainMember, Member(Household));

            (await controller.GetOnboardingState(seeded.Dependent)).Should().BeOfType<OkObjectResult>();
            (await controller.GetPersonById(seeded.Dependent)).Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task PersonWhoLeftTheHousehold_IsForbidden()
        {
            using var context = NewContext();
            var seeded = await SeedAsync(context);

            var result = await Controller(context, seeded.Steward, Member(Household), Manager(Household)).GetOnboardingState(seeded.FormerMember);

            result.Should().BeOfType<ForbidResult>();
        }

        [Fact]
        public async Task EditDialogWrites_UseTheSameRule()
        {
            using var context = NewContext();
            var seeded = await SeedAsync(context);
            var outsider = Controller(context, seeded.Outsider, Member(OtherHousehold), Manager(OtherHousehold));
            var steward = Controller(context, seeded.Steward, Member(Household), Manager(Household));
            var profile = new SaveProfileRequest { Name = "Tobi", Attributes = new List<PersonAttributeRequest>(), HouseholdId = Household };
            var restrictions = new List<RestrictionRequest> { new() { Name = "Peanut Allergy", RestrictionTypeId = 1 } };

            (await outsider.SaveProfile(seeded.Dependent, profile)).Should().BeOfType<ForbidResult>();
            (await outsider.SaveRestrictions(seeded.Dependent, restrictions)).Should().BeOfType<ForbidResult>();
            (await steward.SaveRestrictions(seeded.Dependent, restrictions)).Should().BeOfType<NoContentResult>();
        }

        [Fact]
        public async Task Anonymous_GetsUnauthorized()
        {
            using var host = await new HostBuilder()
                .ConfigureWebHost(web => web
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddSingleton(Mock.Of<IPersonOrchestrationService>());
                        services.AddSingleton(Mock.Of<IMacroGoalOrchestrationService>());
                        services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, NoUserHandler>("Test", _ => { });
                        services.AddAuthorization();
                        services.AddControllers().AddApplicationPart(typeof(PersonController).Assembly);
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseAuthentication();
                        app.UseAuthorization();
                        app.UseEndpoints(endpoints => endpoints.MapControllers());
                    }))
                .StartAsync();

            var response = await host.GetTestClient().GetAsync("/api/Person/1/onboarding");

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        private sealed class NoUserHandler : AuthenticationHandler<AuthenticationSchemeOptions>
        {
            public NoUserHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
                : base(options, logger, encoder)
            {
            }

            protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());
        }
    }
}
