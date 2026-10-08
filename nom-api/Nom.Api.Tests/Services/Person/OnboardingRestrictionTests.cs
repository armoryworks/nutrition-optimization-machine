using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nom.Data;
using Nom.Data.Person;
using Nom.Data.Plan;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Person;
using Nom.Orch.Services;
using Xunit;

namespace Nom.Api.Tests.Services.Person
{
    /// <summary>
    /// Restrictions saved on the Dietary step must survive finishing onboarding in a later session (UA-065).
    /// </summary>
    public class OnboardingRestrictionTests
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

        private static async Task<long> SeedPersonAsync(ApplicationDbContext context, params RestrictionEntity[] restrictions)
        {
            var person = new PersonEntity { Name = "Resumer", CreatedDate = DateTime.UtcNow };
            context.Persons.Add(person);
            await context.SaveChangesAsync();
            foreach (var r in restrictions)
            {
                r.PersonId = person.Id;
                context.Restrictions.Add(r);
            }
            await context.SaveChangesAsync();
            return person.Id;
        }

        private static OnboardingCompleteRequest RequestFor(long personId, List<RestrictionRequest> restrictions) => new()
        {
            PersonId = personId,
            PersonDetails = new PersonDetailsRequest { Id = personId, Name = "Resumer" },
            Attributes = new List<PersonAttributeRequest>(),
            Restrictions = restrictions,
        };

        [Fact]
        public async Task Complete_WithResumedRestrictions_KeepsThemInForce()
        {
            using var context = NewContext();
            var personId = await SeedPersonAsync(context,
                new RestrictionEntity { Name = "Celiac Disease", RestrictionTypeId = 1, CreatedDate = DateTime.UtcNow },
                new RestrictionEntity { Name = "Type 2 Diabetes", RestrictionTypeId = 1, CreatedDate = DateTime.UtcNow });
            var service = NewService(context);

            var state = await service.GetOnboardingStateAsync(personId);
            await service.CompleteOnboardingAsync(RequestFor(personId, state.Restrictions));

            context.Restrictions.Where(r => r.PersonId == personId && !r.IsDeleted).Select(r => r.Name)
                .Should().BeEquivalentTo("Celiac Disease", "Type 2 Diabetes");
        }

        [Fact]
        public async Task Complete_InOneSession_KeepsSavedRestrictionsWithoutPlanWideCopies()
        {
            using var context = NewContext();
            var personId = await SeedPersonAsync(context,
                new RestrictionEntity { Name = "Celiac Disease", RestrictionTypeId = 1, CreatedDate = DateTime.UtcNow });
            var service = NewService(context);

            await service.CompleteOnboardingAsync(RequestFor(personId, new List<RestrictionRequest>
            {
                new() { Name = "Celiac Disease", RestrictionTypeId = 1, AppliesToEntirePlan = true },
            }));

            context.Restrictions.Where(r => !r.IsDeleted)
                .Select(r => new { r.Name, r.PersonId })
                .Should().ContainSingle()
                .Which.PersonId.Should().Be(personId);
        }

        [Fact]
        public async Task Complete_KeepsLockedPersonLevelRestriction_WithoutDuplicating()
        {
            using var context = NewContext();
            var personId = await SeedPersonAsync(context,
                new RestrictionEntity { Name = "Peanut Allergy", RestrictionTypeId = 1, Locked = true, CreatedDate = DateTime.UtcNow });
            var service = NewService(context);

            var state = await service.GetOnboardingStateAsync(personId);
            await service.CompleteOnboardingAsync(RequestFor(personId, state.Restrictions));

            context.Restrictions.Where(r => r.Name == "Peanut Allergy").Should().ContainSingle()
                .Which.Locked.Should().BeTrue();
        }
    }
}
