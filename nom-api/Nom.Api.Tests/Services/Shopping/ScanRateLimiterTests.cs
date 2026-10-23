using System;
using FluentAssertions;
using Nom.Orch.UtilityServices;
using Xunit;

namespace Nom.Api.Tests.Services.Shopping
{
    public class ScanRateLimiterTests
    {
        [Fact]
        public void Allows_the_limit_per_key_and_then_refuses_until_the_window_passes()
        {
            var limiter = new ScanRateLimiter();
            for (var i = 0; i < 3; i++) limiter.TryAcquire("a", 3, TimeSpan.FromHours(1)).Should().BeTrue();
            limiter.TryAcquire("a", 3, TimeSpan.FromHours(1)).Should().BeFalse();
            limiter.TryAcquire("b", 3, TimeSpan.FromHours(1)).Should().BeTrue();
            limiter.TryAcquire("a", 3, TimeSpan.Zero).Should().BeTrue();
        }
    }
}
