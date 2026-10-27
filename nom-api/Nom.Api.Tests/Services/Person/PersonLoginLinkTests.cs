using System;
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
    /// A person's link to a login is set when the account is created and never through the person
    /// endpoints, which any household member can reach; nor can those endpoints delete someone who has a login.
    /// </summary>
    public class PersonLoginLinkTests
    {
        private static async Task<(ApplicationDbContext Db, PersonOrchestrationService Svc)> SetupAsync()
        {
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            db.Persons.AddRange(
                new PersonEntity { Id = 10, Name = "Liz", UserId = "u-liz" },
                new PersonEntity { Id = 12, Name = "Diego" });
            await db.SaveChangesAsync();
            var svc = new PersonOrchestrationService(db, Mock.Of<IHttpContextAccessor>(), Mock.Of<ICurrentUserService>(),
                Mock.Of<IPrivacyOrchestrationService>(), Mock.Of<IHouseholdOrchestrationService>(), NullLogger<PersonOrchestrationService>.Instance);
            return (db, svc);
        }

        [Fact]
        public async Task Updating_a_person_renames_them_and_leaves_their_login_alone()
        {
            var (db, svc) = await SetupAsync();

            var updated = await svc.UpdatePersonAsync(new UpdatePersonRequest { Id = 10, Name = "Liz R" });

            updated.Name.Should().Be("Liz R");
            (await db.Persons.AsNoTracking().SingleAsync(p => p.Id == 10)).UserId.Should().Be("u-liz");
        }

        [Fact]
        public async Task A_person_with_a_login_cannot_be_deleted_here()
        {
            var (db, svc) = await SetupAsync();

            var act = () => svc.DeletePersonAsync(10);

            await act.Should().ThrowAsync<InvalidOperationException>();
            (await db.Persons.AnyAsync(p => p.Id == 10)).Should().BeTrue();
        }

        [Fact]
        public async Task A_dependent_without_a_login_can_still_be_deleted()
        {
            var (db, svc) = await SetupAsync();

            (await svc.DeletePersonAsync(12)).Should().BeTrue();
            (await db.Persons.AnyAsync(p => p.Id == 12)).Should().BeFalse();
        }
    }
}
