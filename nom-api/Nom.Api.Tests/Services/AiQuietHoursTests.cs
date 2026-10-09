using System;
using System.Collections.Generic;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Nom.Api.Services;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The API's model lanes step aside for a nightly window so batch jobs can have the GPU.
    /// </summary>
    public class AiQuietHoursTests
    {
        private static IConfiguration Config(string window) => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Ai:QuietHours"] = window, ["Ai:QuietHoursTimeZone"] = "UTC" })
            .Build();

        [Fact]
        public void Inside_the_window_reports_the_time_left()
        {
            AiQuietHours.Remaining(Config("01:00-05:00"), new DateTimeOffset(2026, 10, 10, 3, 30, 0, TimeSpan.Zero))
                .Should().Be(TimeSpan.FromMinutes(90));
        }

        [Fact]
        public void Outside_the_window_or_disabled_is_null()
        {
            var noon = new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
            AiQuietHours.Remaining(Config("01:00-05:00"), noon).Should().BeNull();
            AiQuietHours.Remaining(Config(""), noon).Should().BeNull();
        }

        [Fact]
        public void A_window_may_wrap_midnight()
        {
            AiQuietHours.Remaining(Config("23:00-02:00"), new DateTimeOffset(2026, 10, 10, 23, 30, 0, TimeSpan.Zero))
                .Should().Be(TimeSpan.FromMinutes(150));
        }
    }
}
