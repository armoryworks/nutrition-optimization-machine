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
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The deterministic recipe auto-approval job holds while the platform feature
    /// pause-auto-approve is on, so a data window can be undone without approvals made on the
    /// strength of a change that is taken back, and it resumes where it left off.
    /// </summary>
    public class RecipeAutoApprovePauseTests
    {
        [Fact]
        public async Task The_job_approves_nothing_while_paused_and_resumes_when_the_pause_is_turned_off()
        {
            var switches = new ConcurrentDictionary<string, bool>();
            switches[IPlatformFeatureService.Keys.AutoApprovePause] = true;
            var features = new Mock<IPlatformFeatureService>();
            features.Setup(f => f.IsEnabledAsync(It.IsAny<string>())).ReturnsAsync((string key) => switches.ContainsKey(key));
            var batches = 0;
            var curation = new Mock<IRecipeAutoCurationService>();
            curation.Setup(c => c.AutoApproveBatchAsync(It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    Interlocked.Increment(ref batches);
                    return new AutoApproveBatchResult(0, 0, 0);
                });
            var services = new ServiceCollection();
            services.AddScoped(_ => features.Object);
            services.AddScoped(_ => curation.Object);
            using var provider = services.BuildServiceProvider();
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Curation:AutoApprove:StartDelaySeconds"] = "0",
                ["Curation:AutoApprove:PauseCheckSeconds"] = "1",
                ["Curation:AutoApprove:SkipUnchanged"] = "false",
                ["Curation:AutoApprove:IdleMinutes"] = "1",
            }).Build();
            var job = new RecipeAutoApproveHostedService(provider.GetRequiredService<IServiceScopeFactory>(), configuration, NullLogger<RecipeAutoApproveHostedService>.Instance);

            await job.StartAsync(CancellationToken.None);
            try
            {
                await Task.Delay(1500);
                batches.Should().Be(0, "the pause holds the job before its first batch");

                switches.TryRemove(IPlatformFeatureService.Keys.AutoApprovePause, out _);
                var until = DateTime.UtcNow.AddSeconds(5);
                while (Volatile.Read(ref batches) == 0 && DateTime.UtcNow < until) await Task.Delay(100);
                batches.Should().BeGreaterThan(0, "the job resumes once the pause is off");
            }
            finally
            {
                await job.StopAsync(CancellationToken.None);
            }
        }

        [Fact]
        public async Task A_batch_held_at_its_save_is_asked_again_from_the_same_cursor_once_the_pause_is_off()
        {
            var switches = new ConcurrentDictionary<string, bool>();
            var features = new Mock<IPlatformFeatureService>();
            features.Setup(f => f.IsEnabledAsync(It.IsAny<string>())).ReturnsAsync((string key) => switches.ContainsKey(key));
            var cursors = new ConcurrentQueue<long>();
            var curation = new Mock<IRecipeAutoCurationService>();
            curation.Setup(c => c.AutoApproveBatchAsync(It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((long after, int _, CancellationToken _) =>
                {
                    cursors.Enqueue(after);
                    if (after == 0) return new AutoApproveBatchResult(1, 0, 40);
                    if (cursors.Count == 2)
                    {
                        switches[IPlatformFeatureService.Keys.AutoApprovePause] = true;
                        return new AutoApproveBatchResult(0, 0, after, Held: true);
                    }
                    return new AutoApproveBatchResult(0, 0, after);
                });
            var services = new ServiceCollection();
            services.AddScoped(_ => features.Object);
            services.AddScoped(_ => curation.Object);
            using var provider = services.BuildServiceProvider();
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Curation:AutoApprove:StartDelaySeconds"] = "0",
                ["Curation:AutoApprove:PauseCheckSeconds"] = "1",
                ["Curation:AutoApprove:PauseMilliseconds"] = "0",
                ["Curation:AutoApprove:SkipUnchanged"] = "false",
                ["Curation:AutoApprove:IdleMinutes"] = "1",
            }).Build();
            var job = new RecipeAutoApproveHostedService(provider.GetRequiredService<IServiceScopeFactory>(), configuration, NullLogger<RecipeAutoApproveHostedService>.Instance);

            await job.StartAsync(CancellationToken.None);
            try
            {
                var until = DateTime.UtcNow.AddSeconds(5);
                while (cursors.Count < 2 && DateTime.UtcNow < until) await Task.Delay(50);
                await Task.Delay(1500);
                cursors.Should().Equal(new long[] { 0, 40 }, "no batch runs while the pause is on");

                switches.TryRemove(IPlatformFeatureService.Keys.AutoApprovePause, out _);
                until = DateTime.UtcNow.AddSeconds(5);
                while (cursors.Count < 3 && DateTime.UtcNow < until) await Task.Delay(50);
                cursors.Should().StartWith(new long[] { 0, 40, 40 }, "the held batch is asked again from where it stood");
            }
            finally
            {
                await job.StopAsync(CancellationToken.None);
            }
        }
    }
}
