using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nom.Data;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.UserManagement;
using Nom.Orch.Services;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// An API token's secret is shown once, at creation; listing tokens must never expose it again.
    /// </summary>
    public class ApiTokenTests
    {
        private static (ApplicationDbContext Db, UserManagementOrchestrationService Svc) Setup()
        {
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var userManager = new Mock<UserManager<IdentityUser>>(Mock.Of<IUserStore<IdentityUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
            var svc = new UserManagementOrchestrationService(userManager.Object, NullLogger<UserManagementOrchestrationService>.Instance, db, Mock.Of<IPersonOrchestrationService>());
            return (db, svc);
        }

        private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

        [Fact]
        public async Task Creating_a_token_returns_the_secret_once_and_stores_only_its_hash()
        {
            var (db, svc) = Setup();

            var created = await svc.CreateApiTokenAsync("u1", new CreateApiTokenRequestModel { Name = " ci-bot ", Description = " nightly export " });

            created.Token.Should().StartWith("nom_");
            created.Name.Should().Be("ci-bot");
            created.Description.Should().Be("nightly export");
            var row = await db.ApiTokens.SingleAsync();
            row.TokenHash.Should().NotBe(created.Token).And.NotContain(created.Token!);
            row.Description.Should().Be("nightly export");
        }

        [Fact]
        public async Task Listing_tokens_never_includes_the_secret()
        {
            var (_, svc) = Setup();
            var created = await svc.CreateApiTokenAsync("u1", new CreateApiTokenRequestModel { Name = "ci-bot", Description = "nightly" });

            var list = await svc.GetUserApiTokensAsync("u1");

            list.Should().ContainSingle();
            list[0].Token.Should().BeNull();
            list[0].Description.Should().Be("nightly");
            var json = JsonSerializer.Serialize(list, Web);
            json.Should().NotContain(created.Token!).And.NotContain("\"token\"");
        }

        [Fact]
        public async Task A_blank_description_is_stored_as_none()
        {
            var (db, svc) = Setup();

            await svc.CreateApiTokenAsync("u1", new CreateApiTokenRequestModel { Name = "ci-bot", Description = "   " });

            (await db.ApiTokens.SingleAsync()).Description.Should().BeNull();
        }

        [Fact]
        public async Task Revoking_hides_the_token_and_only_works_for_its_owner()
        {
            var (db, svc) = Setup();
            var mine = await svc.CreateApiTokenAsync("u1", new CreateApiTokenRequestModel { Name = "mine" });

            await svc.DeleteApiTokenAsync("u2", mine.Id);
            (await svc.GetUserApiTokensAsync("u1")).Should().ContainSingle();

            await svc.DeleteApiTokenAsync("u1", mine.Id);
            (await svc.GetUserApiTokensAsync("u1")).Should().BeEmpty();
            (await db.ApiTokens.SingleAsync()).IsActive.Should().BeFalse();
        }
    }
}
