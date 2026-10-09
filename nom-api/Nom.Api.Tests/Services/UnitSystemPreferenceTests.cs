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
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Person;
using Nom.Orch.Services;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The unit-system preference lives with the profile attributes, but screens that save a
    /// profile without it (onboarding, household member edits) must not wipe it.
    /// </summary>
    public class UnitSystemPreferenceTests
    {
        private static (ApplicationDbContext Db, PersonOrchestrationService Svc) Setup()
        {
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var svc = new PersonOrchestrationService(db, Mock.Of<IHttpContextAccessor>(), Mock.Of<ICurrentUserService>(),
                Mock.Of<IPrivacyOrchestrationService>(), Mock.Of<IHouseholdOrchestrationService>(), NullLogger<PersonOrchestrationService>.Instance);
            return (db, svc);
        }

        private static async Task<long> PersonWithUnitSystemAsync(ApplicationDbContext db)
        {
            var person = new PersonEntity { Name = "Rivera" };
            db.Persons.Add(person);
            await db.SaveChangesAsync();
            db.PersonAttributes.AddRange(
                new PersonAttributeEntity { PersonId = person.Id, AttributeTypeId = PersonOrchestrationService.UnitSystemAttributeType, Value = "imperial" },
                new PersonAttributeEntity { PersonId = person.Id, AttributeTypeId = 11070, Value = "170" });
            await db.SaveChangesAsync();
            return person.Id;
        }

        [Fact]
        public async Task A_profile_save_without_the_unit_system_keeps_it()
        {
            var (db, svc) = Setup();
            var id = await PersonWithUnitSystemAsync(db);

            await svc.SaveProfileAsync(id, new SaveProfileRequest
            {
                Name = "Rivera",
                Attributes = new List<PersonAttributeRequest> { new() { AttributeTypeRefId = 11070, Value = "172" } },
            });

            var attrs = await db.PersonAttributes.Where(a => a.PersonId == id).ToListAsync();
            attrs.Select(a => (a.AttributeTypeId, a.Value)).Should().BeEquivalentTo(new[]
            {
                (PersonOrchestrationService.UnitSystemAttributeType, "imperial"),
                (11070L, "172"),
            });
        }

        [Fact]
        public async Task A_profile_save_with_the_unit_system_replaces_it()
        {
            var (db, svc) = Setup();
            var id = await PersonWithUnitSystemAsync(db);

            await svc.SaveProfileAsync(id, new SaveProfileRequest
            {
                Name = "Rivera",
                Attributes = new List<PersonAttributeRequest> { new() { AttributeTypeRefId = PersonOrchestrationService.UnitSystemAttributeType, Value = "metric" } },
            });

            (await db.PersonAttributes.Where(a => a.PersonId == id).Select(a => a.Value).ToListAsync()).Should().Equal("metric");
        }
    }
}
