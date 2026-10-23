using FluentAssertions;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services.Shopping
{
    public class GtinTests
    {
        [Theory]
        [InlineData("036000291452", "036000291452")]
        [InlineData("4006381333931", "4006381333931")]
        [InlineData("96385074", "96385074")]
        [InlineData("10614141000415", "10614141000415")]
        [InlineData("0 36000 29145 2", "036000291452")]
        [InlineData("4-006381-333931", "4006381333931")]
        public void ValidCodes_AreNormalisedToDigits(string raw, string expected)
        {
            Gtin.TryNormalize(raw, out var digits).Should().BeTrue();
            digits.Should().Be(expected);
        }

        [Theory]
        [InlineData("036000291453")]
        [InlineData("4006381333930")]
        [InlineData("12345")]
        [InlineData("1234567890")]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("not a barcode")]
        public void InvalidCodes_AreRefused(string? raw)
        {
            Gtin.TryNormalize(raw, out _).Should().BeFalse();
        }
    }
}
