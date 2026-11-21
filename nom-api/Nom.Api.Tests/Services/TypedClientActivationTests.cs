using System.Collections.Generic;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nom.Orch.Interfaces;
using Nom.Orch.Services;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// A typed client with a second constructor for the lanes is still built by the HTTP client factory
    /// from its configuration constructor, so a controller that takes it does not answer 400.
    /// </summary>
    public class TypedClientActivationTests
    {
        [Fact]
        public void The_link_verifier_is_built_as_a_typed_client()
        {
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build());
            services.AddHttpClient<IIngredientLinkVerifier, OllamaIngredientLinkVerifier>();
            using var provider = services.BuildServiceProvider();

            provider.GetRequiredService<IIngredientLinkVerifier>().Should().BeOfType<OllamaIngredientLinkVerifier>();
        }
    }
}
