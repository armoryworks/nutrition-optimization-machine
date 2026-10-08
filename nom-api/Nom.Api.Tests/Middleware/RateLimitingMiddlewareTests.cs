using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Nom.Api.Middleware;
using Xunit;

namespace Nom.Api.Tests.Middleware
{
    public class RateLimitingMiddlewareTests
    {
        private static DefaultHttpContext Request(string userId, string path)
        {
            var context = new DefaultHttpContext();
            context.Request.Method = "GET";
            context.Request.Path = path;
            context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", userId) }, "test"));
            return context;
        }

        [Fact]
        public async Task TypeaheadPaceIsNotThrottled()
        {
            var middleware = new RateLimitingMiddleware(_ => Task.CompletedTask,
                NullLogger<RateLimitingMiddleware>.Instance, new MemoryCache(new MemoryCacheOptions()));

            for (var i = 0; i < 30; i++)
            {
                var context = Request("user-a", "/api/Ingredients/search");
                await middleware.InvokeAsync(context);
                Assert.Equal(200, context.Response.StatusCode);
            }
        }

        [Fact]
        public async Task BurstOverflowReturns429WithRetryAfterWithinTheBurstWindow()
        {
            var middleware = new RateLimitingMiddleware(_ => Task.CompletedTask,
                NullLogger<RateLimitingMiddleware>.Instance, new MemoryCache(new MemoryCacheOptions()));

            DefaultHttpContext context = Request("user-a", "/api/Household");
            for (var i = 0; i < 51; i++)
            {
                context = Request("user-a", "/api/Household");
                await middleware.InvokeAsync(context);
            }

            Assert.Equal(429, context.Response.StatusCode);
            var retryAfter = int.Parse(context.Response.Headers.RetryAfter.ToString());
            Assert.InRange(retryAfter, 1, 10);

            var otherUser = Request("user-b", "/api/Household");
            await middleware.InvokeAsync(otherUser);
            Assert.Equal(200, otherUser.Response.StatusCode);
        }
    }
}
