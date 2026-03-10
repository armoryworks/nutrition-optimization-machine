using System;

namespace Nom.Api.Middleware
{
    internal class AuditInfo
    {
        public string RequestId { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string Method { get; set; } = string.Empty;
        public string? Path { get; set; }
        public string? QueryString { get; set; }
        public string? RequestBody { get; set; }
        public string? UserAgent { get; set; }
        public string? IpAddress { get; set; }
        public string? UserId { get; set; }
        public string? UserName { get; set; }
        public string UserRoles { get; set; } = string.Empty;
        public string? ContentType { get; set; }
        public long? ContentLength { get; set; }
        public int StatusCode { get; set; }
        public long ResponseTime { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
