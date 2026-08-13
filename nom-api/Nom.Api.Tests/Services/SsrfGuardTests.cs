using System.Net;
using Nom.Orch.UtilityServices;
using Xunit;

namespace Nom.Api.Tests.Services
{
    public class SsrfGuardTests
    {
        [Theory]
        [InlineData("127.0.0.1")]        // loopback
        [InlineData("169.254.169.254")]  // cloud metadata (link-local)
        [InlineData("10.0.0.5")]         // private
        [InlineData("172.16.4.9")]       // private
        [InlineData("172.31.255.255")]   // private (upper edge)
        [InlineData("192.168.1.198")]    // private (the NOM box itself)
        [InlineData("100.64.0.1")]       // CGNAT
        [InlineData("0.0.0.0")]          // this-network
        [InlineData("::1")]              // IPv6 loopback
        [InlineData("fd00::1")]          // IPv6 unique-local
        [InlineData("fe80::1")]          // IPv6 link-local
        [InlineData("::ffff:127.0.0.1")] // IPv4-mapped loopback
        public void IsBlocked_rejects_nonpublic_addresses(string ip)
        {
            Assert.True(SsrfGuard.IsBlocked(IPAddress.Parse(ip)));
        }

        [Theory]
        [InlineData("1.1.1.1")]
        [InlineData("8.8.8.8")]
        [InlineData("140.82.113.4")]     // github
        [InlineData("2606:4700:4700::1111")] // public IPv6
        public void IsBlocked_allows_public_addresses(string ip)
        {
            Assert.False(SsrfGuard.IsBlocked(IPAddress.Parse(ip)));
        }

        [Theory]
        [InlineData("http://169.254.169.254/latest/meta-data/")]
        [InlineData("https://127.0.0.1/admin")]
        [InlineData("http://10.0.0.1/")]
        public void TryValidateUrl_rejects_internal_literals(string url)
        {
            Assert.False(SsrfGuard.TryValidateUrl(url, out _));
        }

        [Theory]
        [InlineData("ftp://example.com/x")]  // wrong scheme
        [InlineData("file:///etc/passwd")]
        [InlineData("not-a-url")]
        [InlineData("")]
        public void TryValidateUrl_rejects_bad_schemes_and_shapes(string url)
        {
            Assert.False(SsrfGuard.TryValidateUrl(url, out _));
        }

        [Theory]
        [InlineData("https://hooks.example.com/webhook")]
        [InlineData("http://example.org/notify")]
        public void TryValidateUrl_allows_public_https(string url)
        {
            Assert.True(SsrfGuard.TryValidateUrl(url, out _));
        }
    }
}
