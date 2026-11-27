using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Nom.Data;
using Nom.Data.Person;
using Nom.Data.Plan;
using Nom.Data.Platform;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services.MealPlan
{
    /// <summary>
    /// Plan item ck21: the second reader's shadow lane reads the approved pool first and then the recipes waiting for
    /// approval, with both models of the pair, stores each reading with the model, the prompt, text and store signatures and
    /// journals it; it never reads a recipe again while its text and the signatures are unchanged, keeps an unreadable reply
    /// as unreadable, reports its progress with the hours left, and the stored gate reads what it stored.
    /// </summary>
    public class DietSecondReaderLaneTests
    {
        private const long CeliacRef = 60800, PersonId = 82, HouseholdId = 9;
        private const string Method = "Boil the rice in a large pot of water until it is tender.";

        private sealed class Host : HttpMessageHandler
        {
            public List<(string Model, string Body)> Calls { get; } = new();

            public Func<string, string, string> Answer { get; set; } = (_, _) => "free";

            public bool Garbled { get; set; }

            public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

            public bool Down { get; set; }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                var model = doc.RootElement.GetProperty("model").GetString()!;
                var body = doc.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;
                Calls.Add((model, body));
                if (Down) throw new HttpRequestException("AI host is marked down; the call waits for its next health probe");
                if (Status != HttpStatusCode.OK) return new HttpResponseMessage(Status);
                var content = Garbled ? "not json" : JsonSerializer.Serialize(new Dictionary<string, object> { ["unlisted"] = Array.Empty<string>(), ["complete"] = true }
                    .Concat(OllamaDietSecondReader.Types.Select(t => new KeyValuePair<string, object>(t.Key, new { food = "", answer = Answer(model, t.Key) })))
                    .ToDictionary(p => p.Key, p => p.Value));
                var reply = JsonSerializer.Serialize(new { message = new { role = "assistant", content } });
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(reply, Encoding.UTF8, "application/json") };
            }
        }

        private sealed record Kitchen(long Approved, long Waiting, long Private, long Rice);

        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
                new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        private static RecipeEntity Recipe(string name, long food, long status, RecipeVisibilityEnum visibility) => new()
        {
            Name = name,
            AuthorId = PersonId,
            Visibility = visibility,
            CurationStatusId = status,
            LicenseStatus = RecipeLicenseStatus.PublicDomain,
            ScrapedAtUtc = DateTime.UtcNow,
            RecipeIngredients = new List<RecipeIngredientEntity> { new() { IngredientId = food, RawLine = "1 cup rice", Quantity = 1m, MeasurementId = 1 } },
            RecipeSteps = new List<RecipeStepEntity> { new() { StepNumber = 1, Summary = "Boil", Description = Method } },
        };

        private static async Task<Kitchen> SeedAsync(ApplicationDbContext db)
        {
            db.Set<ReferenceEntity>().Add(new ReferenceEntity { Id = CeliacRef, Name = "Celiac Disease" });
            db.HouseholdMembers.Add(new HouseholdMemberEntity { HouseholdId = HouseholdId, PersonId = PersonId, IsActive = true });
            db.Restrictions.Add(new RestrictionEntity { PersonId = PersonId, Name = "Celiac Disease", RestrictionTypeId = CeliacRef });
            var rice = new IngredientEntity { Name = "Rice", CurationStatusId = (long)CurationStatusEnum.Curated };
            db.Ingredients.Add(rice);
            await db.SaveChangesAsync();
            var waiting = Recipe("Rice Water", rice.Id, (long)CurationStatusEnum.NonCurated, RecipeVisibilityEnum.Public);
            var approved = Recipe("Boiled Rice", rice.Id, (long)CurationStatusEnum.Curated, RecipeVisibilityEnum.Public);
            var hidden = Recipe("My Rice", rice.Id, (long)CurationStatusEnum.NonCurated, RecipeVisibilityEnum.Private);
            db.Recipes.AddRange(waiting, approved, hidden);
            await db.SaveChangesAsync();
            await DefaultRestrictionCriteria.EnsureAsync(db);
            db.ChangeTracker.Clear();
            return new Kitchen(approved.Id, waiting.Id, hidden.Id, rice.Id);
        }

        private static DietSecondReaderLaneService Lane(ApplicationDbContext db, Host host, double retryHours = 24, Dictionary<string, string?>? settings = null) =>
            new(db, new HttpClient(host), new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DietSecondReader:OllamaUrl"] = "http://model.invalid:11434",
                ["DietSecondReader:RetryUnreadableHours"] = retryHours.ToString(System.Globalization.CultureInfo.InvariantCulture),
            }.Concat(settings ?? new()).ToDictionary(p => p.Key, p => p.Value)).Build());

        [Fact]
        public async Task The_lane_reads_the_approved_pool_first_then_the_waiting_recipes_stores_and_journals_each_reading_and_never_reads_an_unchanged_recipe_again()
        {
            using var db = NewContext();
            var k = await SeedAsync(db);
            var host = new Host();

            var first = await Lane(db, host).RunBatchAsync(1);
            first.Should().Be(new DietSecondBatch(1, 2, 0));
            host.Calls.Select(c => c.Model).Should().Equal(DietSecondGate.Pair, "one call per model of the pair");
            host.Calls[0].Body.Should().Contain("Title: Boiled Rice", "the approved pool is read before the recipes waiting for approval, whatever their ids");

            var rest = await Lane(db, host).RunBatchAsync(10);
            rest.Recipes.Should().Be(1, "the waiting recipe; the private one is outside the scope");
            var rows = await db.RecipeDietReadings.OrderBy(r => r.RecipeId).ThenBy(r => r.Model).ToListAsync();
            rows.Select(r => r.RecipeId).Distinct().Should().BeEquivalentTo(new[] { k.Approved, k.Waiting });
            rows.Should().OnlyContain(r => r.PromptSignature == OllamaDietSecondReader.Signature && r.StoreVersion == DietSecondGate.Version && r.Readable && r.Verdicts == "fffffffffff"
                && r.TextSignature == DietSecondGate.TextSignature(r.RecipeId == k.Approved ? "Boiled Rice" : "Rice Water", new[] { "1 cup rice" }, new[] { Method }));
            (await db.LaneDecisions.CountAsync(d => d.Lane == AiLanes.DietSecondReader && d.Mode == Nom.Data.Curation.LaneMode.Shadow && !d.Writes)).Should().Be(4, "every reading is journaled");

            host.Calls.Clear();
            (await Lane(db, host).RunBatchAsync(10)).Recipes.Should().Be(0, "nothing changed, so nothing is read again");
            host.Calls.Should().BeEmpty();

            var step = await db.RecipeSteps.SingleAsync(s => s.RecipeId == k.Approved);
            step.Description = "Boil the rice in milk until it is tender and thick.";
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            (await Lane(db, host).RunBatchAsync(10)).Should().Be(new DietSecondBatch(1, 2, 0), "a changed method is read again by both models");
            host.Calls.Should().OnlyContain(c => c.Body.Contains("Boil the rice in milk until it is tender and thick."));
        }

        [Fact]
        public async Task An_unreadable_reply_is_kept_as_unreadable_and_read_again_only_after_the_retry_time_and_a_pause_stops_the_batch()
        {
            using var db = NewContext();
            await SeedAsync(db);
            var host = new Host { Garbled = true };

            (await Lane(db, host).RunBatchAsync(10)).Should().Be(new DietSecondBatch(2, 4, 4));
            (await db.RecipeDietReadings.ToListAsync()).Should().OnlyContain(r => !r.Readable && r.Verdicts.Length == 0);
            (await Lane(db, host).RunBatchAsync(10)).Recipes.Should().Be(0, "an unreadable reply is read again only after the retry time");
            host.Garbled = false;
            (await Lane(db, host, retryHours: 0).RunBatchAsync(10)).Should().Be(new DietSecondBatch(2, 4, 0));

            using var other = NewContext();
            await SeedAsync(other);
            (await Lane(other, new Host()).RunBatchAsync(10, () => Task.FromResult(true))).Recipes.Should().Be(0, "a pause stops the batch before the next recipe");
        }

        [Fact]
        public async Task The_progress_counts_the_scope_and_the_hours_left_and_the_stored_gate_reads_what_the_lane_stored()
        {
            using var db = NewContext();
            var k = await SeedAsync(db);
            var before = await Lane(db, new Host()).ProgressAsync();
            before.Should().Match<DietSecondProgress>(p => p.Approved == 1 && p.Waiting == 1 && p.InScope == 2 && p.ReadByPair == 0 && !p.GateOn);
            before.HoursLeft.Should().BeApproximately(Math.Round(2 * 22.1 / 3600, 1) + Math.Round(2 * 13.1 / 3600, 1), 0.001, "ck20's seconds per recipe until the lane has measured its own");

            var host = new Host { Answer = (model, key) => key == "celiac" && model == DietSecondGate.Pair[1] ? "may" : "free" };
            await Lane(db, host).RunBatchAsync(10);
            var after = await Lane(db, host).ProgressAsync();
            after.Should().Match<DietSecondProgress>(p => p.ReadByPair == 2 && p.ApprovedReadByPair == 1 && p.HoursLeft == 0);
            after.PerModel.Should().OnlyContain(m => m.Current == 2 && m.Left == 0);

            db.PlatformFeatures.Add(new PlatformFeatureEntity { Key = DietSecondGate.FeatureKey, IsEnabled = true });
            await db.SaveChangesAsync();
            await new DietGateStoreBuilder().RunAsync(db);
            var sets = await db.DietGateSets.Where(s => s.RestrictionTypeId == CeliacRef).Select(s => s.Id).ToListAsync();
            var flags = await db.DietRecipeFlags.Where(f => sets.Contains(f.SetId)).ToListAsync();
            flags.Where(f => f.RecipeId == k.Approved).Should().OnlyContain(f => f.Reason == DietFlagReason.SecondReader).And.NotBeEmpty(
                "the lane's text signature is the one the build computes, so the stored readings are current and phi4's may holds the recipe");
            flags.Should().NotContain(f => f.Reason == DietFlagReason.SecondUnread && f.RecipeId != k.Private);
        }

        [Fact]
        public async Task The_lane_reads_only_the_waiting_recipes_the_sweep_could_approve_once_read_under_the_configured_image_rule()
        {
            using var db = NewContext();
            var k = await SeedAsync(db);
            var unmeasured = Recipe("Rice Mush", k.Rice, (long)CurationStatusEnum.NonCurated, RecipeVisibilityEnum.Public);
            unmeasured.RecipeIngredients!.Single().Quantity = 0m;
            unmeasured.RecipeIngredients!.Single().RawLine = "rice";
            var unlicensed = Recipe("Rice Porridge", k.Rice, (long)CurationStatusEnum.NonCurated, RecipeVisibilityEnum.Public);
            unlicensed.LicenseStatus = RecipeLicenseStatus.Unknown;
            unlicensed.RecipeIngredients!.Add(new RecipeIngredientEntity { IngredientId = k.Rice, RawLine = "2 cups rice", Quantity = 2m, MeasurementId = 1, SortOrder = 1 });
            db.Recipes.AddRange(unmeasured, unlicensed);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            (await Lane(db, new Host()).ScopeAsync()).Waiting.Should().Equal(new[] { k.Waiting },
                "a recipe with an unmeasured line waits for the line, not for the reader, and one of unknown licence without an image of its own is not examined while RequireOwnImage holds");
            var host = new Host();
            (await Lane(db, host).RunBatchAsync(10)).Recipes.Should().Be(2);
            host.Calls.Should().NotContain(c => c.Body.Contains("Rice Mush") || c.Body.Contains("Rice Porridge"));

            var waived = new Dictionary<string, string?> { ["Curation:AutoApprove:RequireOwnImage"] = "false" };
            (await Lane(db, host, settings: waived).ProgressAsync()).Waiting.Should().Be(2, "with the image rule waived the sweep can approve the unlicensed recipe, so the lane reads it");
            host.Calls.Clear();
            (await Lane(db, host, settings: waived).RunBatchAsync(10)).Recipes.Should().Be(1);
            host.Calls.Should().OnlyContain(c => c.Body.Contains("Title: Rice Porridge"));
        }

        [Fact]
        public async Task A_host_that_is_down_or_fails_stops_the_batch_and_stores_nothing_while_a_refused_request_is_unreadable()
        {
            using var db = NewContext();
            await SeedAsync(db);

            var down = new Host { Down = true };
            await FluentActions.Awaiting(() => Lane(db, down).RunBatchAsync(10)).Should().ThrowAsync<HttpRequestException>();
            down.Calls.Should().HaveCount(1, "the batch stops at the first fault");

            var failing = new Host { Status = HttpStatusCode.InternalServerError };
            await FluentActions.Awaiting(() => Lane(db, failing).RunBatchAsync(10)).Should().ThrowAsync<HttpRequestException>();
            (await db.RecipeDietReadings.CountAsync()).Should().Be(0, "a host fault is not a reply, so no recipe is kept as unreadable for the retry time");
            (await db.LaneDecisions.CountAsync()).Should().Be(0);

            (await Lane(db, new Host()).RunBatchAsync(10)).Should().Be(new DietSecondBatch(2, 4, 0), "once the host is back every recipe is read at once");

            using var other = NewContext();
            await SeedAsync(other);
            (await Lane(other, new Host { Status = HttpStatusCode.BadRequest }).RunBatchAsync(10)).Should().Be(new DietSecondBatch(2, 4, 4),
                "a request the host refuses is the recipe's own, so it is kept as unreadable and the lane goes on");
        }
    }
}
