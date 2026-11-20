using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;
using Nom.Api.Controllers;
using Nom.Api.Services;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using Nom.Orch.Models.Platform;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Controllers
{
    /// <summary>
    /// The pipeline page is for curation admins: it returns the cached counts with the quiet hours
    /// worked out for this moment and every model lane as the lane gate reports it, and its only
    /// write turns a known AI pause switch on or off.
    /// </summary>
    public class PipelineControllerTests
    {
        private const long Person = 10;

        private readonly Mock<IPipelineStatusService> _pipeline = new();
        private readonly Mock<IAiLaneStatus> _lanes = new();
        private readonly Mock<IPlatformFeatureService> _features = new();

        public PipelineControllerTests()
        {
            _pipeline.Setup(p => p.GetAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PipelineStatusModel { Recipes = new PipelineRecipeCounts { Approved = 22842 }, Ai = new PipelineAiStatus { QuietHours = "01:00-05:00" } });
            _lanes.Setup(l => l.GetLanes()).Returns(new[]
            {
                new AiLaneStatusModel { Lane = AiLanes.Link, State = AiLaneState.Paused, StateReason = "paused by the platform feature ai-pause-all" },
            });
            _lanes.Setup(l => l.GetHosts()).Returns(new[]
            {
                new AiHostStatusModel { Host = "http://ai:11434", Up = true, ModelsMissing = new[] { "qwen2.5:14b" } },
            });
            _features.Setup(f => f.IsEnabledAsync(It.IsAny<string>())).ReturnsAsync(false);
            _features.Setup(f => f.IsEnabledAsync(AiLanes.PauseAllKey)).ReturnsAsync(true);
            _features.Setup(f => f.SetAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<long>()))
                .ReturnsAsync((string key, bool on, long _) => new PlatformFeatureModel { Key = key, IsEnabled = on });
        }

        private PipelineController Controller(Dictionary<string, string?>? settings = null) =>
            new(_pipeline.Object, _lanes.Object, _features.Object,
                new ConfigurationBuilder().AddInMemoryCollection(settings ?? new Dictionary<string, string?> { ["Ai:QuietHours"] = "" }).Build())
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("PersonId", Person.ToString()) }, "test")),
                    },
                },
            };

        private static PipelineStatusResponse Body(ActionResult<PipelineStatusResponse> result) =>
            (PipelineStatusResponse)result.Result.Should().BeOfType<OkObjectResult>().Subject.Value!;

        [Fact]
        public void The_controller_is_for_curation_admins()
        {
            typeof(PipelineController).GetCustomAttribute<AuthorizeAttribute>()!.Policy.Should().Be("CanManageCuration");
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task GetStatus_returns_the_counts_and_passes_refresh_through(bool refresh)
        {
            var body = Body(await Controller().GetStatus(refresh));

            body.Status.Recipes.Approved.Should().Be(22842);
            _pipeline.Verify(p => p.GetAsync(refresh, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetStatus_reports_every_model_lane_from_the_gate_with_its_pause_switch()
        {
            var body = Body(await Controller().GetStatus());

            body.Gate.PauseAll.Should().BeTrue();
            body.Gate.Lanes.Select(l => l.Lane).Should().Equal(AiLanes.All.Select(l => l.Lane));
            body.Gate.Lanes.Single(l => l.Lane == AiLanes.Link).Status.State.Should().Be(AiLaneState.Paused);
            body.Gate.Lanes.Single(l => l.Lane == AiLanes.Repair).Status.State.Should().Be(AiLaneState.NotStarted);
            body.Gate.Hosts.Single().ModelsMissing.Should().Equal("qwen2.5:14b");
        }

        [Fact]
        public async Task Quiet_minutes_are_set_inside_the_quiet_hours_and_null_without_them()
        {
            var now = DateTime.UtcNow;
            var window = $"{now.AddHours(-1):HH:mm}-{now.AddHours(1):HH:mm}";

            var inside = Body(await Controller(new() { ["Ai:QuietHours"] = window, ["Ai:QuietHoursTimeZone"] = "UTC" }).GetStatus());
            var none = Body(await Controller().GetStatus());

            inside.Status.Ai.QuietMinutesLeft.Should().BeInRange(1, 61);
            none.Status.Ai.QuietMinutesLeft.Should().BeNull();
        }

        [Theory]
        [InlineData("all", "ai-pause-all")]
        [InlineData("link", "ai-pause-link")]
        [InlineData("Prose-Rewrite", "ai-pause-prose-rewrite")]
        public async Task SetPause_turns_a_known_pause_switch_as_the_caller(string lane, string key)
        {
            var result = await Controller().SetPause(lane, new SetPipelinePauseRequest { IsEnabled = true });

            result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<PipelineGateStatus>();
            _features.Verify(f => f.SetAsync(key, true, Person), Times.Once);
        }

        [Theory]
        [InlineData("brigade")]
        [InlineData("image-finder")]
        public async Task SetPause_refuses_any_other_switch(string lane)
        {
            var result = await Controller().SetPause(lane, new SetPipelinePauseRequest { IsEnabled = true });

            result.Result.Should().BeOfType<BadRequestObjectResult>();
            _features.Verify(f => f.SetAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<long>()), Times.Never);
        }
    }
}
