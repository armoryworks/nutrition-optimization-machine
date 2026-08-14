using System.Collections.Generic;
using Nom.Api.Services;
using Nom.Orch.UtilityInterfaces;
using Xunit;

namespace Nom.Api.Tests.Services
{
    public class SourceDiscoveryAutoApproveTests
    {
        private const string HttpsUrl = "https://example.com/recipes/pancakes";

        private static ScraperResult CleanProbe() => new()
        {
            Success = true,
            Recipe = new ScraperRecipe
            {
                Name = "Classic Buttermilk Pancakes",
                RawJsonLd = "{\"@type\":\"Recipe\"}",
                Ingredients = { new ScraperIngredient { RawLine = "2 cups flour", Name = "flour" } },
                Steps = { new ScraperStep { Order = 1, Instruction = "Whisk and cook." } },
            },
        };

        [Fact]
        public void Clean_probe_over_https_is_obviously_fine()
        {
            Assert.True(SourceDiscoveryHostedService.IsObviouslyFine(HttpsUrl, CleanProbe(), new List<string>()));
        }

        [Fact]
        public void Failed_scrape_is_not_fine()
        {
            var probe = new ScraperResult { Success = false, FailureReason = "RobotsDisallowed" };
            Assert.False(SourceDiscoveryHostedService.IsObviouslyFine(HttpsUrl, probe, new List<string>()));
        }

        [Fact]
        public void Http_evidence_url_is_not_fine()
        {
            Assert.False(SourceDiscoveryHostedService.IsObviouslyFine(
                "http://example.com/recipes/pancakes", CleanProbe(), new List<string>()));
        }

        [Fact]
        public void Missing_raw_json_ld_is_not_fine()
        {
            var probe = CleanProbe();
            probe.Recipe!.RawJsonLd = null;
            Assert.False(SourceDiscoveryHostedService.IsObviouslyFine(HttpsUrl, probe, new List<string>()));
        }

        [Fact]
        public void Empty_ingredients_or_steps_is_not_fine()
        {
            var noIngredients = CleanProbe();
            noIngredients.Recipe!.Ingredients.Clear();
            Assert.False(SourceDiscoveryHostedService.IsObviouslyFine(HttpsUrl, noIngredients, new List<string>()));

            var noSteps = CleanProbe();
            noSteps.Recipe!.Steps.Clear();
            Assert.False(SourceDiscoveryHostedService.IsObviouslyFine(HttpsUrl, noSteps, new List<string>()));
        }

        [Fact]
        public void Any_vetting_issue_is_not_fine()
        {
            var issues = new List<string> { "Cook time of 100000 minutes is implausible" };
            Assert.False(SourceDiscoveryHostedService.IsObviouslyFine(HttpsUrl, CleanProbe(), issues));
        }

        [Fact]
        public void Success_without_recipe_payload_is_not_fine()
        {
            var probe = new ScraperResult { Success = true, Recipe = null };
            Assert.False(SourceDiscoveryHostedService.IsObviouslyFine(HttpsUrl, probe, new List<string>()));
        }
    }
}
