using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace Nom.Api.Middleware
{
    /// <summary>
    /// Rate limiting middleware to prevent API abuse and protect against brute force attacks
    /// </summary>
    public class RateLimitingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<RateLimitingMiddleware> _logger;
        private readonly IMemoryCache _cache;
        private readonly ConcurrentDictionary<string, RateLimitInfo> _rateLimitStore;

        // Rate limiting configuration
        private const int MaxRequestsPerMinute = 100;
        private const int MaxRequestsPerHour = 1000;
        private const int MaxRequestsPerDay = 10000;
        private const int BurstLimit = 20; // Max requests in a short burst

        public RateLimitingMiddleware(RequestDelegate next, ILogger<RateLimitingMiddleware> logger, IMemoryCache cache)
        {
            _next = next;
            _logger = logger;
            _cache = cache;
            _rateLimitStore = new ConcurrentDictionary<string, RateLimitInfo>();
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var clientId = GetClientIdentifier(context);
            var endpoint = context.Request.Path.Value ?? "/";

            // Skip rate limiting for certain endpoints
            if (ShouldSkipRateLimiting(endpoint))
            {
                await _next(context);
                return;
            }

            // Check rate limits
            if (!await CheckRateLimits(clientId, endpoint))
            {
                _logger.LogWarning("Rate limit exceeded for client {ClientId} on endpoint {Endpoint}", clientId, endpoint);
                context.Response.StatusCode = 429; // Too Many Requests
                context.Response.Headers.Add("Retry-After", "60"); // Retry after 1 minute
                await context.Response.WriteAsync("Rate limit exceeded. Please try again later.");
                return;
            }

            // Add rate limit headers to response
            context.Response.Headers.Add("X-RateLimit-Limit", MaxRequestsPerMinute.ToString());
            context.Response.Headers.Add("X-RateLimit-Remaining", GetRemainingRequests(clientId).ToString());
            context.Response.Headers.Add("X-RateLimit-Reset", GetResetTime().ToString());

            await _next(context);
        }

        private string GetClientIdentifier(HttpContext context)
        {
            // Use IP address as primary identifier
            var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            
            // If user is authenticated, include user ID for more granular rate limiting
            var userId = context.User?.FindFirst("sub")?.Value;
            if (!string.IsNullOrEmpty(userId))
            {
                return $"{ipAddress}:{userId}";
            }

            return ipAddress;
        }

        private bool ShouldSkipRateLimiting(string endpoint)
        {
            // Skip rate limiting for health checks and static files
            var skipEndpoints = new[]
            {
                "/health",
                "/favicon.ico",
                "/robots.txt",
                "/swagger",
                "/api/auth/token", // Allow authentication endpoints
                "/api/auth/register"
            };

            return Array.Exists(skipEndpoints, e => endpoint.StartsWith(e, StringComparison.OrdinalIgnoreCase));
        }

        private async Task<bool> CheckRateLimits(string clientId, string endpoint)
        {
            var now = DateTime.UtcNow;
            var cacheKey = $"rate_limit:{clientId}:{endpoint}";

            // Get or create rate limit info
            var rateLimitInfo = await _cache.GetOrCreateAsync(cacheKey, entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2);
                return Task.FromResult(new RateLimitInfo
                {
                    ClientId = clientId,
                    Endpoint = endpoint,
                    RequestCount = 0,
                    FirstRequestTime = now,
                    LastRequestTime = now,
                    BurstCount = 0,
                    BurstStartTime = now
                });
            });

            // Update rate limit info
            rateLimitInfo.RequestCount++;
            rateLimitInfo.LastRequestTime = now;

            // Check burst limit (requests in last 10 seconds)
            if (now.Subtract(rateLimitInfo.BurstStartTime).TotalSeconds > 10)
            {
                rateLimitInfo.BurstCount = 1;
                rateLimitInfo.BurstStartTime = now;
            }
            else
            {
                rateLimitInfo.BurstCount++;
            }

            // Check various rate limits
            if (rateLimitInfo.BurstCount > BurstLimit)
            {
                _logger.LogWarning("Burst limit exceeded for client {ClientId}", clientId);
                return false;
            }

            if (rateLimitInfo.RequestCount > MaxRequestsPerMinute)
            {
                _logger.LogWarning("Minute rate limit exceeded for client {ClientId}", clientId);
                return false;
            }

            // Check hourly limit
            var hourlyKey = $"rate_limit_hourly:{clientId}";
            var hourlyCount = await _cache.GetOrCreateAsync(hourlyKey, entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
                return Task.FromResult(0);
            });

            if (hourlyCount >= MaxRequestsPerHour)
            {
                _logger.LogWarning("Hourly rate limit exceeded for client {ClientId}", clientId);
                return false;
            }

            // Check daily limit
            var dailyKey = $"rate_limit_daily:{clientId}";
            var dailyCount = await _cache.GetOrCreateAsync(dailyKey, entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(1);
                return Task.FromResult(0);
            });

            if (dailyCount >= MaxRequestsPerDay)
            {
                _logger.LogWarning("Daily rate limit exceeded for client {ClientId}", clientId);
                return false;
            }

            // Update counters
            _cache.Set(hourlyKey, hourlyCount + 1, TimeSpan.FromHours(1));
            _cache.Set(dailyKey, dailyCount + 1, TimeSpan.FromDays(1));

            return true;
        }

        private int GetRemainingRequests(string clientId)
        {
            // Calculate remaining requests based on current usage
            return Math.Max(0, MaxRequestsPerMinute - GetCurrentRequestCount(clientId));
        }

        private int GetCurrentRequestCount(string clientId)
        {
            // Get current request count from cache
            var cacheKey = $"rate_limit:{clientId}";
            if (_cache.TryGetValue(cacheKey, out RateLimitInfo rateLimitInfo))
            {
                return rateLimitInfo.RequestCount;
            }
            return 0;
        }

        private DateTime GetResetTime()
        {
            // Reset time is the start of the next minute
            return DateTime.UtcNow.AddMinutes(1).Date.AddHours(DateTime.UtcNow.Hour).AddMinutes(DateTime.UtcNow.Minute + 1);
        }

        private class RateLimitInfo
        {
            public string ClientId { get; set; } = string.Empty;
            public string Endpoint { get; set; } = string.Empty;
            public int RequestCount { get; set; }
            public DateTime FirstRequestTime { get; set; }
            public DateTime LastRequestTime { get; set; }
            public int BurstCount { get; set; }
            public DateTime BurstStartTime { get; set; }
        }
    }
} 