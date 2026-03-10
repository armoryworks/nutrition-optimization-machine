using System;

namespace Nom.Api.Middleware
{
    internal class RateLimitInfo
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
