using System;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Nom.Data;
using Nom.Orch;
using Nom.Orch.Interfaces;
using Nom.Orch.Services;
using Nom.Orch.Settings;
using Nom.Orch.UtilityInterfaces;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The rights engine resolves from the orchestration registrations, and the source lane, whose
    /// constructor gained the rights settings as an optional last argument, still resolves with them
    /// bound (the full suite does not start the API).
    /// </summary>
    public class SourceRightsRegistrationTests
    {
        [Fact]
        public void The_rights_engine_and_the_source_lane_resolve_from_the_container()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
            services.AddOrchestrationServices();
            services.AddScoped(_ => Mock.Of<IRecipeScraperClient>());
            services.Configure<SourceRightsSettings>(s => s.Enforce = true);
            using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            using var scope = provider.CreateScope();

            scope.ServiceProvider.GetRequiredService<ISourceRightsService>().Should().BeOfType<SourceRightsService>();
            scope.ServiceProvider.GetRequiredService<ISourceAdmissionService>().Should().BeOfType<SourceAdmissionService>();
        }
    }
}
