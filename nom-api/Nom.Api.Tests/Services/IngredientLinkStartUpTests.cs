using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nom.Api.Services;
using Nom.Orch.Interfaces;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The link lane's start-up step re-points staples and applies certain and verified links only
    /// once the lane gate lets it through, so a paused link lane applies nothing after a restart.
    /// </summary>
    public class IngredientLinkStartUpTests
    {
        private sealed class Rig : IDisposable
        {
            public required ServiceProvider Provider { get; init; }
            public required IngredientLinkHostedService Lane { get; init; }
            public required Mock<IIngredientLinkService> Links { get; init; }
            public required ConcurrentDictionary<string, bool> Switches { get; init; }

            public void Dispose() => Provider.Dispose();
        }

        private static Rig NewRig()
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ai:QuietHours"] = "",
                ["Ai:Gate:PollSeconds"] = "1",
            }).Build();
            var switches = new ConcurrentDictionary<string, bool>();
            var features = new Mock<IPlatformFeatureService>();
            features.Setup(f => f.IsEnabledAsync(It.IsAny<string>())).ReturnsAsync((string key) => switches.ContainsKey(key));
            var links = new Mock<IIngredientLinkService>();
            links.Setup(l => l.ApplyStapleDefaultsToPendingAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);
            links.Setup(l => l.PendingDeterministicAsync(It.IsAny<int>(), It.IsAny<IReadOnlyCollection<long>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<long>());
            links.Setup(l => l.VerifiedAllowanceAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new LinkApplyAllowance(0, Array.Empty<string>()));

            var services = new ServiceCollection();
            services.AddScoped(_ => features.Object);
            services.AddScoped(_ => links.Object);
            var provider = services.BuildServiceProvider();
            var scopes = provider.GetRequiredService<IServiceScopeFactory>();
            var gate = new AiLaneGate(scopes, configuration,
                new AiHostMonitor(new Mock<System.Net.Http.IHttpClientFactory>().Object, configuration, NullLogger<AiHostMonitor>.Instance),
                NullLogger<AiLaneGate>.Instance);
            var lane = new IngredientLinkHostedService(scopes, configuration, NullLogger<IngredientLinkHostedService>.Instance, gate);
            return new Rig { Provider = provider, Lane = lane, Links = links, Switches = switches };
        }

        private static async Task<bool> StartUpWithinAsync(Rig rig, int milliseconds = 1500)
        {
            using var cts = new CancellationTokenSource(milliseconds);
            return await rig.Lane.StartUpAsync(autoApply: true, autoApplyVerified: true, cts.Token);
        }

        [Theory]
        [InlineData(AiLanes.PauseAllKey)]
        [InlineData("ai-pause-link")]
        public async Task A_paused_link_lane_applies_nothing_at_start_up(string pauseSwitch)
        {
            using var rig = NewRig();
            rig.Switches[pauseSwitch] = true;

            (await StartUpWithinAsync(rig)).Should().BeFalse();

            rig.Links.Verify(l => l.ApplyStapleDefaultsToPendingAsync(It.IsAny<CancellationToken>()), Times.Never);
            rig.Links.Verify(l => l.PendingDeterministicAsync(It.IsAny<int>(), It.IsAny<IReadOnlyCollection<long>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
            rig.Links.Verify(l => l.VerifiedAllowanceAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task A_running_link_lane_re_points_staples_and_applies_certain_and_verified_links_at_start_up()
        {
            using var rig = NewRig();

            (await StartUpWithinAsync(rig)).Should().BeTrue();

            rig.Links.Verify(l => l.ApplyStapleDefaultsToPendingAsync(It.IsAny<CancellationToken>()), Times.Once);
            rig.Links.Verify(l => l.PendingDeterministicAsync(It.IsAny<int>(), It.IsAny<IReadOnlyCollection<long>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
            rig.Links.Verify(l => l.VerifiedAllowanceAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Turning_the_pause_off_lets_the_start_up_step_run()
        {
            using var rig = NewRig();
            rig.Switches["ai-pause-link"] = true;

            var start = StartUpWithinAsync(rig, 5000);
            await Task.Delay(300);
            rig.Links.Verify(l => l.ApplyStapleDefaultsToPendingAsync(It.IsAny<CancellationToken>()), Times.Never);
            rig.Switches.Clear();

            (await start).Should().BeTrue();
            rig.Links.Verify(l => l.ApplyStapleDefaultsToPendingAsync(It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
