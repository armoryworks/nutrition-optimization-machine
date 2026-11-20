using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Nom.Orch.Interfaces;
using Nom.Orch.Services;
using Xunit;

namespace Nom.Api.Tests.Services
{
    public class DishGroupSuggesterTests
    {
        [Theory]
        [InlineData("BEST Chewy Chocolate Chip Cookies", "chocolate chip cookies")]
        [InlineData("chocolate chip cookies", "chocolate chip cookies")]
        [InlineData("Grandma's Classic Banana Bread", "banana bread")]
        [InlineData("Easy One-Pot Butter Chicken", "butter chicken")]
        [InlineData("Baking Powder Biscuit II", "baking powder biscuit")]
        [InlineData("Fruit Rolls (Pin Wheel Biscuit)", "fruit rolls")]
        [InlineData("The Ultimate 30-Minute Weeknight Spaghetti", "spaghetti")]
        [InlineData("Creamy Garlic Chicken Recipe", "garlic chicken")]
        public void Normalize_strips_marketing_and_numbering(string input, string expected)
        {
            Assert.Equal(expected, HeuristicDishGroupSuggester.Normalize(input));
        }

        [Theory]
        [InlineData("My", null)]
        [InlineData("", null)]
        public void Normalize_rejects_empty_results(string input, string? expected)
        {
            Assert.Equal(expected, HeuristicDishGroupSuggester.Normalize(input));
        }

        [Theory]
        [InlineData("chocolate chip cookies", "chocolate-chip-cookies")]
        [InlineData("  Butter Chicken!  ", "butter-chicken")]
        public void Slugify_produces_url_safe_keys(string input, string expected)
        {
            Assert.Equal(expected, DishGroupService.Slugify(input));
        }

        private sealed class StubHandler : HttpMessageHandler
        {
            public System.Func<HttpResponseMessage> Respond { get; init; } = () => new HttpResponseMessage(HttpStatusCode.OK);

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromResult(Respond());
        }

        private static OllamaDishGroupSuggester Suggester(StubHandler handler) =>
            new(new HttpClient(handler),
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Ai:OllamaUrl"] = "http://model-host:11434" }).Build(),
                NullLogger<OllamaDishGroupSuggester>.Instance);

        private static List<DishGroupCandidate> Cookies() => new() { new DishGroupCandidate(1, "BEST Chewy Chocolate Chip Cookies", new List<string> { "flour" }) };

        [Fact]
        public async Task An_unreachable_model_host_leaves_the_batch_ungrouped_instead_of_using_the_heuristic()
        {
            var suggester = Suggester(new StubHandler { Respond = () => throw new HttpRequestException("connection refused") });

            await suggester.Invoking(s => s.SuggestAsync(Cookies())).Should().ThrowAsync<HttpRequestException>();
        }

        [Fact]
        public async Task An_unreadable_answer_still_falls_back_to_the_heuristic()
        {
            var suggester = Suggester(new StubHandler { Respond = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"response\":\"{}\"}") } });

            (await suggester.SuggestAsync(Cookies())).Should().Equal("chocolate chip cookies");
        }
    }
}
