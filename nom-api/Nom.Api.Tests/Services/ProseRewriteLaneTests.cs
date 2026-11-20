using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Nom.Api.Services;
using Nom.Data;
using Nom.Data.Recipe;
using Nom.Orch.Services;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The rewrite lane lifts the quarantine only on a rewrite that passes the fidelity check,
    /// journals every text it overwrites, and records no attempt when the model host is down.
    /// </summary>
    public class ProseRewriteLaneTests
    {
        private const string Source = """
            {"@type":"Recipe","description":"Crisp roast potatoes for a weeknight.",
             "recipeInstructions":[
               {"@type":"HowToStep","text":"Preheat the oven to 425 degrees F."},
               {"@type":"HowToStep","text":"Place the sheet in the oven and roast the potatoes for 25 to 30 minutes, until golden and crispy."}]}
            """;

        private const string Faithful = """
            {"description":"Weeknight potatoes, roasted until crisp.",
             "steps":["Heat the oven to 425 degrees F.","Roast the potatoes on their tray for 25-30 minutes so they turn crisp and golden."]}
            """;

        private const string LostTime = """
            {"description":"Weeknight potatoes, roasted until crisp.",
             "steps":["Heat the oven to 425 degrees F.","Roast the potatoes on their tray for about 25 minutes so they turn crisp and golden."]}
            """;

        private sealed class Model : HttpMessageHandler
        {
            private readonly Func<string> _answer;
            public Model(Func<string> answer) => _answer = answer;
            public List<string> Prompts { get; } = new();

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var body = await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken);
                Prompts.Add(body.GetProperty("prompt").GetString()!);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { response = _answer() }) };
            }
        }

        private sealed class Factory : IHttpClientFactory
        {
            private readonly HttpMessageHandler _handler;
            public Factory(HttpMessageHandler handler) => _handler = handler;
            public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
        }

        private static (ServiceProvider Provider, ProseRewriteHostedService Lane) NewLane(Model model, Dictionary<string, string?>? settings = null)
        {
            var name = Guid.NewGuid().ToString();
            var provider = new ServiceCollection()
                .AddDbContext<ApplicationDbContext>(o => o
                    .UseInMemoryDatabase(name)
                    .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)))
                .BuildServiceProvider();
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings ?? new()).Build();
            var gate = new AiLaneGate(provider.GetRequiredService<IServiceScopeFactory>(), configuration,
                new AiHostMonitor(new Factory(model), configuration, NullLogger<AiHostMonitor>.Instance), NullLogger<AiLaneGate>.Instance);
            var lane = new ProseRewriteHostedService(
                provider.GetRequiredService<IServiceScopeFactory>(), configuration, new Factory(model), NullLogger<ProseRewriteHostedService>.Instance, gate);
            return (provider, lane);
        }

        private static async Task<long> SeedAsync(ServiceProvider provider, string[] steps, DateTime? attempted = null)
        {
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var recipe = new RecipeEntity
            {
                Name = "Roast potatoes",
                Description = "Crisp roast potatoes for a weeknight.",
                ScrapedAtUtc = DateTime.UtcNow,
                LicenseStatus = RecipeLicenseStatus.Unknown,
                ContainsSourceProse = true,
                ProseRewriteAttemptedAt = attempted,
                CurationStatusId = (long)CurationStatusEnum.NonCurated,
                RecipeSteps = steps.Select((text, i) => new RecipeStepEntity { StepNumber = i + 1, Description = text }).ToList(),
            };
            db.Recipes.Add(recipe);
            await db.SaveChangesAsync();
            db.ScrapedDocuments.Add(new ScrapedDocumentEntity { RecipeId = recipe.Id, RawJsonLd = Source });
            await db.SaveChangesAsync();
            return recipe.Id;
        }

        private static readonly string[] SourceSteps =
        {
            "Preheat the oven to 425 degrees F.",
            "Place the sheet in the oven and roast the potatoes for 25 to 30 minutes, until golden and crispy.",
        };

        private static Task SweepAsync(ProseRewriteHostedService lane) =>
            lane.SweepAsync("http://model", "test-model", 25, 30, true, CancellationToken.None);

        [Fact]
        public async Task A_faithful_rewrite_clears_the_quarantine_and_journals_each_text()
        {
            var model = new Model(() => Faithful);
            var (provider, lane) = NewLane(model);
            var id = await SeedAsync(provider, SourceSteps);

            await SweepAsync(lane);

            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var recipe = await db.Recipes.Include(r => r.RecipeSteps).SingleAsync(r => r.Id == id);
            recipe.ContainsSourceProse.Should().BeFalse();
            recipe.RecipeSteps!.OrderBy(s => s.StepNumber).Last().Description.Should().StartWith("Roast the potatoes on their tray");
            var journal = await db.AuditLogEntries.Where(a => a.EntityId == id).ToListAsync();
            journal.Should().OnlyContain(a => a.ChangeType == ProseRecheckService.RewriteChangeType && a.EntityType == "Recipe" && a.NewValue!.StartsWith("[test-model] "));
            journal.Select(a => a.PropertyName).Should().BeEquivalentTo("Description", "RecipeStep:1", "RecipeStep:2", "ContainsSourceProse");
            journal.Single(a => a.PropertyName == "RecipeStep:2").OldValue.Should().Be(SourceSteps[1]);
        }

        [Fact]
        public async Task A_refused_rewrite_is_asked_again_with_the_reason()
        {
            var answers = new Queue<string>(new[] { LostTime, Faithful });
            var model = new Model(() => answers.Dequeue());
            var (provider, lane) = NewLane(model);
            var id = await SeedAsync(provider, SourceSteps);

            await SweepAsync(lane);

            model.Prompts.Should().HaveCount(2);
            model.Prompts[1].Should().EndWith("Your previous answer was refused because it dropped these numbers the original states: 30. Keep every amount, time and temperature exactly as the original gives it.");
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.Recipes.SingleAsync(r => r.Id == id)).ContainsSourceProse.Should().BeFalse();
        }

        [Fact]
        public async Task A_rewrite_that_drops_a_time_twice_stays_quarantined()
        {
            var model = new Model(() => LostTime);
            var (provider, lane) = NewLane(model);
            var id = await SeedAsync(provider, SourceSteps);

            await SweepAsync(lane);

            model.Prompts.Should().HaveCount(2);

            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var recipe = await db.Recipes.Include(r => r.RecipeSteps).SingleAsync(r => r.Id == id);
            recipe.ContainsSourceProse.Should().BeTrue();
            recipe.ProseRewriteAttemptedAt.Should().NotBeNull();
            recipe.RecipeSteps!.Select(s => s.Description).Should().BeEquivalentTo(SourceSteps);
            (await db.AuditLogEntries.CountAsync()).Should().Be(0);
        }

        [Fact]
        public async Task The_check_can_be_switched_off()
        {
            var model = new Model(() => LostTime);
            var (provider, lane) = NewLane(model);
            var id = await SeedAsync(provider, SourceSteps);

            await lane.SweepAsync("http://model", "test-model", 25, 30, false, CancellationToken.None);

            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.Recipes.SingleAsync(r => r.Id == id)).ContainsSourceProse.Should().BeFalse();
        }

        [Fact]
        public async Task A_recipe_put_back_by_the_recheck_is_rewritten_from_its_source()
        {
            var model = new Model(() => Faithful);
            var (provider, lane) = NewLane(model);
            await SeedAsync(provider, new[] { "Heat the oven to 425 degrees F.", "Roast the potatoes for about 25 minutes." });

            await SweepAsync(lane);

            model.Prompts.Should().ContainSingle().Which.Should().Contain("25 to 30 minutes");
        }

        [Fact]
        public async Task A_host_outage_records_no_attempt_and_ends_the_sweep()
        {
            var model = new Model(() => throw new HttpRequestException("connection refused"));
            var (provider, lane) = NewLane(model);
            var first = await SeedAsync(provider, SourceSteps);
            var second = await SeedAsync(provider, SourceSteps);

            var (_, outage) = await lane.SweepAsync("http://model", "test-model", 25, 30, true, CancellationToken.None);

            outage.Should().BeOfType<HttpRequestException>();
            model.Prompts.Should().HaveCount(1);
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.Recipes.Where(r => r.Id == first || r.Id == second).ToListAsync())
                .Should().OnlyContain(r => r.ContainsSourceProse && r.ProseRewriteAttemptedAt == null);
        }
    }
}
