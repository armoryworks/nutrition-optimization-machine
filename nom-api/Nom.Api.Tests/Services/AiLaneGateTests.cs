using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Nom.Api.Services;
using Nom.Data;
using Nom.Data.Audit;
using Nom.Orch.Interfaces;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// Every model lane passes one gate before each batch: a pause switch an admin can flip, the
    /// quiet hours, a breaker shared by every lane that calls a dead host, and a daily cap counted
    /// from the lane's own rows. The gate only ever stops work.
    /// </summary>
    public class AiLaneGateTests
    {
        private sealed class FakeHandler : HttpMessageHandler
        {
            public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.OK);
            public int Calls { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Calls++;
                return Task.FromResult(Respond(request));
            }
        }

        private sealed class FakeClientFactory : IHttpClientFactory
        {
            public FakeHandler Probe { get; } = new();
            public HttpClient CreateClient(string name) => new(Probe, disposeHandler: false);
        }

        private sealed class Rig : IDisposable
        {
            public required ServiceProvider Provider { get; init; }
            public required AiLaneGate Gate { get; init; }
            public required AiHostMonitor Monitor { get; init; }
            public required FakeClientFactory Clients { get; init; }

            public async Task WithDbAsync(Func<ApplicationDbContext, Task> work)
            {
                using var scope = Provider.CreateScope();
                await work(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
            }

            public HttpClient Client(string lane, FakeHandler inner) =>
                new(new AiLaneHttpHandler(Monitor, lane) { InnerHandler = inner }) { BaseAddress = new Uri("http://model-host:11434") };

            public void Dispose() => Provider.Dispose();
        }

        private static Rig NewRig(Dictionary<string, string?>? settings = null, bool databaseDown = false)
        {
            var values = new Dictionary<string, string?> { ["Ai:QuietHours"] = "", ["Ai:Gate:PollSeconds"] = "1" };
            foreach (var (k, v) in settings ?? new()) values[k] = v;
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
            var dbName = Guid.NewGuid().ToString();
            var services = new ServiceCollection();
            services.AddMemoryCache();
            services.AddLogging();
            if (databaseDown) services.AddScoped<ApplicationDbContext>(_ => throw new InvalidOperationException("the database is restarting"));
            else services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(dbName));
            services.AddScoped<IPlatformFeatureService, PlatformFeatureService>();
            var provider = services.BuildServiceProvider();
            var clients = new FakeClientFactory();
            var monitor = new AiHostMonitor(clients, configuration, NullLogger<AiHostMonitor>.Instance);
            var gate = new AiLaneGate(provider.GetRequiredService<IServiceScopeFactory>(), configuration, monitor, NullLogger<AiLaneGate>.Instance);
            return new Rig { Provider = provider, Gate = gate, Monitor = monitor, Clients = clients };
        }

        private static async Task<bool> PassesWithinAsync(AiLaneGate gate, string lane, int milliseconds = 300)
        {
            using var cts = new CancellationTokenSource(milliseconds);
            return await gate.WaitAsync(lane, cts.Token);
        }

        private static AiLaneStatusModel Status(AiLaneGate gate, string lane) => gate.GetLanes().Single(l => l.Lane == lane);

        [Fact]
        public async Task With_no_switches_and_no_caps_every_lane_runs_as_before()
        {
            using var rig = NewRig();

            foreach (var (lane, _) in AiLanes.All)
            {
                (await PassesWithinAsync(rig.Gate, lane)).Should().BeTrue(lane);
            }

            rig.Gate.GetLanes().Select(l => l.Lane).Should().Equal(AiLanes.All.Select(l => l.Lane));
            Status(rig.Gate, AiLanes.Link).Should().Match<AiLaneStatusModel>(s => s.State == AiLaneState.Running && s.DailyCap == null);
        }

        [Fact]
        public async Task A_database_error_while_counting_the_daily_cap_does_not_escape_the_gate()
        {
            using var rig = NewRig(new Dictionary<string, string?> { [$"Ai:Lanes:{AiLanes.Repair}:MaxItemsPerDay"] = "5" }, databaseDown: true);

            (await PassesWithinAsync(rig.Gate, AiLanes.Repair)).Should().BeTrue("nothing has been done in memory either");
            rig.Gate.Passed(AiLanes.Repair, 5, idle: false);
            (await PassesWithinAsync(rig.Gate, AiLanes.Repair)).Should().BeFalse("the in-memory count still holds the cap");
            Status(rig.Gate, AiLanes.Repair).State.Should().Be(AiLaneState.AtCap);
        }

        [Fact]
        public async Task A_lane_switch_pauses_that_lane_only_and_the_all_switch_pauses_every_lane()
        {
            using var rig = NewRig();
            await rig.WithDbAsync(async db =>
            {
                await new PlatformFeatureService(db, rig.Provider.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(), NullLogger<PlatformFeatureService>.Instance)
                    .SetAsync(AiLanes.PauseKey(AiLanes.Repair), true, 4);
            });

            (await PassesWithinAsync(rig.Gate, AiLanes.Repair)).Should().BeFalse();
            (await PassesWithinAsync(rig.Gate, AiLanes.Link)).Should().BeTrue();
            Status(rig.Gate, AiLanes.Repair).Should().Match<AiLaneStatusModel>(s =>
                s.State == AiLaneState.Paused && s.StateReason!.Contains("ai-pause-repair"));

            await rig.WithDbAsync(async db =>
            {
                await new PlatformFeatureService(db, rig.Provider.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(), NullLogger<PlatformFeatureService>.Instance)
                    .SetAsync(AiLanes.PauseAllKey, true, 4);
            });
            (await PassesWithinAsync(rig.Gate, AiLanes.Link)).Should().BeFalse();
        }

        [Fact]
        public async Task A_daily_cap_counts_the_lanes_own_rows_of_the_last_24_hours()
        {
            using var rig = NewRig(new() { [$"Ai:Lanes:{AiLanes.Repair}:MaxItemsPerDay"] = "2" });
            await rig.WithDbAsync(async db =>
            {
                db.AuditLogEntries.AddRange(
                    Attempt(1, DateTime.UtcNow.AddHours(-2)),
                    Attempt(2, DateTime.UtcNow.AddHours(-30)));
                await db.SaveChangesAsync();
            });

            (await PassesWithinAsync(rig.Gate, AiLanes.Repair)).Should().BeTrue("one attempt in 24 h is under the cap of 2");

            await rig.WithDbAsync(async db =>
            {
                db.AuditLogEntries.Add(Attempt(3, DateTime.UtcNow.AddMinutes(-5)));
                await db.SaveChangesAsync();
            });
            rig.Gate.Passed(AiLanes.Repair, 1, false);

            (await PassesWithinAsync(rig.Gate, AiLanes.Repair)).Should().BeFalse();
            Status(rig.Gate, AiLanes.Repair).Should().Match<AiLaneStatusModel>(s =>
                s.State == AiLaneState.AtCap && s.DailyCap == 2 && s.ItemsToday == 2 && s.ItemsTodayFromLedger);
        }

        [Fact]
        public async Task A_lane_without_a_ledger_is_capped_by_what_it_reported()
        {
            using var rig = NewRig(new() { [$"Ai:Lanes:{AiLanes.DishGrouping}:MaxItemsPerDay"] = "50" });

            (await PassesWithinAsync(rig.Gate, AiLanes.DishGrouping)).Should().BeTrue();
            rig.Gate.Passed(AiLanes.DishGrouping, 50, false);

            (await PassesWithinAsync(rig.Gate, AiLanes.DishGrouping)).Should().BeFalse();
            Status(rig.Gate, AiLanes.DishGrouping).Should().Match<AiLaneStatusModel>(s => s.ItemsToday == 50 && !s.ItemsTodayFromLedger);
        }

        [Fact]
        public async Task Three_failed_calls_mark_the_host_down_for_every_lane_and_later_calls_never_leave_the_api()
        {
            using var rig = NewRig();
            var dead = new FakeHandler { Respond = _ => throw new HttpRequestException("connection refused") };
            using var link = rig.Client(AiLanes.Link, dead);

            for (var i = 0; i < 3; i++)
            {
                await link.Invoking(c => c.PostAsync("/api/generate", JsonBody("qwen2.5:14b-instruct"))).Should().ThrowAsync<HttpRequestException>();
            }
            await link.Invoking(c => c.PostAsync("/api/generate", JsonBody("qwen2.5:14b-instruct"))).Should().ThrowAsync<HttpRequestException>();

            dead.Calls.Should().Be(3, "the fourth call is refused before it leaves the API");
            rig.Gate.GetHosts().Should().ContainSingle().Which.Should().Match<AiHostStatusModel>(h =>
                !h.Up && h.ModelsAsked.Contains("qwen2.5:14b-instruct"));
            rig.Clients.Probe.Respond = _ => throw new HttpRequestException("connection refused");
            (await PassesWithinAsync(rig.Gate, AiLanes.Link)).Should().BeFalse();
            Status(rig.Gate, AiLanes.Link).Should().Match<AiLaneStatusModel>(s =>
                s.State == AiLaneState.HostDown && s.ModelCalls == 3 && s.ModelCallErrors == 3 && s.Model == "qwen2.5:14b-instruct");
        }

        [Fact]
        public async Task A_host_comes_back_only_when_its_probe_lists_every_model_the_lanes_ask_for()
        {
            using var rig = NewRig(new() { ["Ai:Breaker:ProbeMinutes"] = "1" });
            var host = new FakeHandler { Respond = _ => new HttpResponseMessage(HttpStatusCode.NotFound) };
            using var repair = rig.Client(AiLanes.Repair, host);
            for (var i = 0; i < 3; i++) await repair.PostAsync("/api/generate", JsonBody("qwen2.5:14b"));

            rig.Clients.Probe.Respond = _ => Tags("qwen2.5:14b-instruct", "nomic-embed-text:latest");
            (await rig.Monitor.AnswersAsync(AiLanes.Repair)).Should().BeFalse("the lane asks for a tag the host does not have");
            rig.Gate.GetHosts().Single().ModelsMissing.Should().Equal("qwen2.5:14b");

            rig.Clients.Probe.Respond = _ => Tags("qwen2.5:14b", "qwen2.5:14b-instruct");
            (await rig.Monitor.AnswersAsync(AiLanes.Repair)).Should().BeTrue();
            rig.Gate.GetHosts().Single().Up.Should().BeTrue();
            (await PassesWithinAsync(rig.Gate, AiLanes.Repair)).Should().BeTrue();
        }

        [Fact]
        public async Task A_success_resets_the_count_of_failures_in_a_row()
        {
            using var rig = NewRig();
            var calls = 0;
            var flaky = new FakeHandler { Respond = _ => ++calls % 2 == 0 ? new HttpResponseMessage(HttpStatusCode.OK) : new HttpResponseMessage(HttpStatusCode.BadGateway) };
            using var tools = rig.Client(AiLanes.ToolTagging, flaky);

            for (var i = 0; i < 6; i++) await tools.PostAsync("/api/generate", JsonBody("m"));

            rig.Gate.GetHosts().Single().Up.Should().BeTrue();
            flaky.Calls.Should().Be(6);
            Status(rig.Gate, AiLanes.ToolTagging).Should().Match<AiLaneStatusModel>(s => s.ModelCalls == 6 && s.ModelCallErrors == 3 && s.LatencyMedianMs != null);
        }

        [Fact]
        public void Passes_and_failures_are_kept_for_the_status_page()
        {
            using var rig = NewRig();

            rig.Gate.Passed(AiLanes.ProseRewrite, 25, false);
            rig.Gate.Failed(AiLanes.ProseRewrite, new HttpRequestException("connection refused"));
            rig.Gate.Disabled(AiLanes.ToolTagging, "ToolTagging:Enabled=false");

            Status(rig.Gate, AiLanes.ProseRewrite).Should().Match<AiLaneStatusModel>(s =>
                s.State == AiLaneState.Failing && s.LastPassItems == 25 && s.ItemsToday == 25 && s.LastError!.Contains("connection refused"));
            Status(rig.Gate, AiLanes.ToolTagging).State.Should().Be(AiLaneState.Disabled);
            Status(rig.Gate, AiLanes.DishGrouping).State.Should().Be(AiLaneState.NotStarted);
        }

        private static AuditLogEntryEntity Attempt(long recipeId, DateTime at) => new()
        {
            EntityType = "Recipe",
            EntityId = recipeId,
            ChangeType = RecipeAiRepairService.AttemptChangeType,
            NewValue = "v3 (clean)",
            Timestamp = at,
            ChangedByPersonId = 1,
        };

        private static StringContent JsonBody(string model) =>
            new($"{{\"model\":\"{model}\",\"prompt\":\"x\",\"stream\":false}}", System.Text.Encoding.UTF8, "application/json");

        private static HttpResponseMessage Tags(params string[] names) => new(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"models\":[" + string.Join(",", names.Select(n => $"{{\"name\":\"{n}\"}}")) + "]}"),
        };
    }
}
