using System;
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
using Nom.Orch.Models.Curation;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The prose re-check job withholds what fails only when a full run is due, holds while
    /// pause-prose-recheck is on, and is off until an operator turns it on.
    /// </summary>
    public class ProseRecheckJobTests
    {
        private sealed record Harness(ProseRecheckHostedService Job, Mock<IProseRecheckService> Recheck, Dictionary<string, bool> Switches, ServiceProvider Provider);

        private static Harness Build(string? due, bool enabled = true, Func<Dictionary<string, bool>, Func<CancellationToken, Task<bool>>?, Task<ProseRecheckResult>>? run = null)
        {
            var switches = new Dictionary<string, bool>();
            var features = new Mock<IPlatformFeatureService>();
            features.Setup(f => f.IsEnabledAsync(It.IsAny<string>())).ReturnsAsync((string key) => switches.GetValueOrDefault(key));
            var recheck = new Mock<IProseRecheckService>();
            recheck.Setup(r => r.DueAsync(It.IsAny<TimeSpan>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(due);
            recheck.Setup(r => r.RecheckAsync(true, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Func<CancellationToken, Task<bool>>?>(), It.IsAny<CancellationToken>()))
                .Returns((bool _, int _, int _, Func<CancellationToken, Task<bool>>? hold, CancellationToken _) =>
                    run?.Invoke(switches, hold) ?? Task.FromResult(new ProseRecheckResult { Applied = true, Withheld = 3, RunRecorded = true }));
            var services = new ServiceCollection();
            services.AddScoped(_ => features.Object);
            services.AddScoped(_ => recheck.Object);
            var provider = services.BuildServiceProvider();
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ProseRecheck:Enabled"] = enabled ? "true" : "false",
                ["ProseRecheck:StartDelaySeconds"] = "0",
            }).Build();
            var job = new ProseRecheckHostedService(provider.GetRequiredService<IServiceScopeFactory>(), configuration, NullLogger<ProseRecheckHostedService>.Instance);
            return new Harness(job, recheck, switches, provider);
        }

        [Fact]
        public async Task A_due_run_withholds_every_failure_in_one_apply()
        {
            var h = Build("no full run recorded");
            using var _ = h.Provider;

            (await h.Job.RunOnceAsync(TimeSpan.FromDays(7), false, CancellationToken.None)).Should().BeFalse();

            h.Recheck.Verify(r => r.RecheckAsync(true, It.Is<int>(n => n >= 100000), It.IsAny<int>(), It.IsAny<Func<CancellationToken, Task<bool>>?>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task A_run_already_going_asks_the_pause_switch_before_each_batch_and_reports_paused_when_held()
        {
            var answers = new List<bool>();
            var h = Build("weekly", run: async (switches, hold) =>
            {
                hold.Should().NotBeNull();
                answers.Add(await hold!(CancellationToken.None));
                switches[IPlatformFeatureService.Keys.ProseRecheckPause] = true;
                answers.Add(await hold(CancellationToken.None));
                return new ProseRecheckResult { Applied = true, Withheld = 500, Remaining = 20000, Held = true };
            });
            using var _ = h.Provider;

            (await h.Job.RunOnceAsync(TimeSpan.FromDays(7), false, CancellationToken.None)).Should().BeTrue("the job reports itself paused");

            answers.Should().Equal(false, true);
        }

        [Fact]
        public async Task Nothing_runs_when_no_run_is_due()
        {
            var h = Build(null);
            using var _ = h.Provider;

            await h.Job.RunOnceAsync(TimeSpan.FromDays(7), false, CancellationToken.None);

            h.Recheck.Verify(r => r.RecheckAsync(It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Func<CancellationToken, Task<bool>>?>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task The_pause_switch_holds_the_job()
        {
            var h = Build("weekly");
            using var _ = h.Provider;
            h.Switches[IPlatformFeatureService.Keys.ProseRecheckPause] = true;

            (await h.Job.RunOnceAsync(TimeSpan.FromDays(7), false, CancellationToken.None)).Should().BeTrue();

            h.Recheck.Verify(r => r.DueAsync(It.IsAny<TimeSpan>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
            h.Recheck.Verify(r => r.RecheckAsync(It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Func<CancellationToken, Task<bool>>?>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task The_job_is_off_until_it_is_turned_on()
        {
            var h = Build("no full run recorded", enabled: false);
            using var _ = h.Provider;

            await h.Job.StartAsync(CancellationToken.None);
            await Task.Delay(300);
            await h.Job.StopAsync(CancellationToken.None);

            h.Recheck.Verify(r => r.DueAsync(It.IsAny<TimeSpan>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }
}
