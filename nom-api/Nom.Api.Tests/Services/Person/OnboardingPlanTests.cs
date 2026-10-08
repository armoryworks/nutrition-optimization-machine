using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nom.Data;
using Nom.Data.Person;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Person;
using Nom.Orch.Services;
using Xunit;

namespace Nom.Api.Tests.Services.Person
{
    /// <summary>
    /// The plan entered on the onboarding Plan step is the plan that gets created (UA-074).
    /// </summary>
    public class OnboardingPlanTests
    {
        private static ApplicationDbContext NewContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            return new ApplicationDbContext(options);
        }

        private static PersonOrchestrationService NewService(ApplicationDbContext context) =>
            new(
                context,
                Mock.Of<IHttpContextAccessor>(),
                Mock.Of<ICurrentUserService>(),
                Mock.Of<IPrivacyOrchestrationService>(),
                Mock.Of<IHouseholdOrchestrationService>(),
                NullLogger<PersonOrchestrationService>.Instance);

        private static async Task<long> SeedPersonAsync(ApplicationDbContext context)
        {
            var person = new PersonEntity { Name = "Pat", CreatedDate = DateTime.UtcNow };
            context.Persons.Add(person);
            await context.SaveChangesAsync();
            return person.Id;
        }

        [Fact]
        public async Task Complete_WithPlanDetails_CreatesPlanWithThem()
        {
            using var context = NewContext();
            var personId = await SeedPersonAsync(context);

            await NewService(context).CompleteOnboardingAsync(new OnboardingCompleteRequest
            {
                PersonId = personId,
                PersonDetails = new PersonDetailsRequest { Id = personId, Name = "Pat" },
                PlanName = "  Pat GF + steady carbs ",
                PlanDescription = "Gluten-free, even carbs",
                PlanStartDate = new DateOnly(2026, 10, 8),
                PlanEndDate = new DateOnly(2026, 10, 20),
            });

            var plan = context.Plans.Single(p => p.AuthorId == personId);
            plan.Name.Should().Be("Pat GF + steady carbs");
            plan.Description.Should().Be("Gluten-free, even carbs");
            plan.StartDate.Should().Be(new DateOnly(2026, 10, 8));
            plan.EndDate.Should().Be(new DateOnly(2026, 10, 20));
        }

        [Fact]
        public async Task Complete_WithoutPlanDetails_CreatesDefaultPlanStartingToday()
        {
            using var context = NewContext();
            var personId = await SeedPersonAsync(context);

            await NewService(context).CompleteOnboardingAsync(new OnboardingCompleteRequest
            {
                PersonId = personId,
                PersonDetails = new PersonDetailsRequest { Id = personId, Name = "Pat" },
            });

            var plan = context.Plans.Single(p => p.AuthorId == personId);
            plan.Name.Should().Be("Pat's Default Plan");
            plan.StartDate.Should().Be(DateOnly.FromDateTime(DateTime.Today));
            plan.EndDate.Should().BeNull();
        }
    }
}
