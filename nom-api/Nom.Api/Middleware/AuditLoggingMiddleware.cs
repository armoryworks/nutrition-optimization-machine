using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Linq; // Added for .Select()

namespace Nom.Api.Middleware
{
    /// <summary>
    /// Audit logging middleware to track security events and user actions
    /// Provides comprehensive audit trail for compliance and security monitoring
    /// </summary>
    public class AuditLoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<AuditLoggingMiddleware> _logger;

        // Security-sensitive endpoints that require detailed logging
        private static readonly string[] SecuritySensitiveEndpoints = {
            "/api/auth/",
            "/api/users/",
            "/api/admin/",
            "/api/recipe/import",
            "/api/recipe/create",
            "/api/recipe/update",
            "/api/recipe/delete",
            "/api/household/",
            "/api/group/",
            "/api/admin/"
        };

        public AuditLoggingMiddleware(RequestDelegate next, ILogger<AuditLoggingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var stopwatch = Stopwatch.StartNew();
            var requestId = Guid.NewGuid().ToString();
            var timestamp = DateTime.UtcNow;

            // Capture request details
            var auditInfo = new AuditInfo
            {
                RequestId = requestId,
                Timestamp = timestamp,
                Method = context.Request.Method,
                Path = context.Request.Path.Value,
                QueryString = context.Request.QueryString.Value,
                UserAgent = context.Request.Headers["User-Agent"].ToString(),
                IpAddress = GetClientIpAddress(context),
                UserId = GetUserId(context),
                UserName = GetUserName(context),
                UserRoles = GetUserRoles(context),
                ContentType = context.Request.ContentType,
                ContentLength = context.Request.ContentLength
            };

            try
            {
                // Log request start
                LogAuditEvent("RequestStarted", auditInfo, null);

                // Capture request body for sensitive endpoints
                if (IsSecuritySensitiveEndpoint(auditInfo.Path))
                {
                    auditInfo.RequestBody = await CaptureRequestBody(context);
                }

                // Process the request
                await _next(context);

                // Update audit info with response details
                auditInfo.StatusCode = context.Response.StatusCode;
                auditInfo.ResponseTime = stopwatch.ElapsedMilliseconds;

                // Log request completion
                LogAuditEvent("RequestCompleted", auditInfo, null);

                // Log security events for specific status codes
                if (context.Response.StatusCode >= 400)
                {
                    LogSecurityEvent("RequestError", auditInfo, $"HTTP {context.Response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                // Update audit info with error details
                auditInfo.StatusCode = 500;
                auditInfo.ResponseTime = stopwatch.ElapsedMilliseconds;
                auditInfo.ErrorMessage = ex.Message;

                // Log security event for exceptions
                LogSecurityEvent("RequestException", auditInfo, ex.ToString());

                throw; // Re-throw to let other middleware handle it
            }
        }

        private string GetClientIpAddress(HttpContext context)
        {
            // Get the real IP address, considering proxies
            var forwardedHeader = context.Request.Headers["X-Forwarded-For"].ToString();
            if (!string.IsNullOrEmpty(forwardedHeader))
            {
                return forwardedHeader.Split(',')[0].Trim();
            }

            var realIpHeader = context.Request.Headers["X-Real-IP"].ToString();
            if (!string.IsNullOrEmpty(realIpHeader))
            {
                return realIpHeader;
            }

            return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        }

        private string? GetUserId(HttpContext context)
        {
            return context.User?.FindFirst("sub")?.Value;
        }

        private string? GetUserName(HttpContext context)
        {
            return context.User?.FindFirst("name")?.Value;
        }

        private string GetUserRoles(HttpContext context)
        {
            var roles = context.User?.FindAll("role")?.Select(c => c.Value) ?? Array.Empty<string>();
            return string.Join(",", roles);
        }

        private bool IsSecuritySensitiveEndpoint(string? path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            return Array.Exists(SecuritySensitiveEndpoints, endpoint => 
                path.StartsWith(endpoint, StringComparison.OrdinalIgnoreCase));
        }

        private async Task<string?> CaptureRequestBody(HttpContext context)
        {
            try
            {
                if (context.Request.ContentLength > 0 && context.Request.ContentLength < 10240) // Max 10KB
                {
                    context.Request.EnableBuffering();
                    context.Request.Body.Position = 0;

                    using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
                    var body = await reader.ReadToEndAsync();
                    context.Request.Body.Position = 0;

                    // Sanitize sensitive data
                    return SanitizeRequestBody(body);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to capture request body");
            }

            return null;
        }

        private string SanitizeRequestBody(string body)
        {
            // Remove or mask sensitive information
            var sanitized = body;

            // Mask passwords
            sanitized = System.Text.RegularExpressions.Regex.Replace(
                sanitized, 
                @"""password""\s*:\s*""[^""]*""", 
                @"""password"":""***""");

            // Mask tokens
            sanitized = System.Text.RegularExpressions.Regex.Replace(
                sanitized, 
                @"""token""\s*:\s*""[^""]*""", 
                @"""token"":""***""");

            // Mask API keys
            sanitized = System.Text.RegularExpressions.Regex.Replace(
                sanitized, 
                @"""apiKey""\s*:\s*""[^""]*""", 
                @"""apiKey"":""***""");

            return sanitized;
        }

        private void LogAuditEvent(string eventType, AuditInfo auditInfo, string? details)
        {
            var logMessage = new
            {
                EventType = eventType,
                RequestId = auditInfo.RequestId,
                Timestamp = auditInfo.Timestamp,
                Method = auditInfo.Method,
                Path = auditInfo.Path,
                StatusCode = auditInfo.StatusCode,
                ResponseTime = auditInfo.ResponseTime,
                UserId = auditInfo.UserId,
                UserName = auditInfo.UserName,
                UserRoles = auditInfo.UserRoles,
                IpAddress = auditInfo.IpAddress,
                UserAgent = auditInfo.UserAgent,
                Details = details
            };

            _logger.LogInformation("AUDIT: {@AuditEvent}", logMessage);
        }

        private void LogSecurityEvent(string eventType, AuditInfo auditInfo, string details)
        {
            var securityEvent = new
            {
                EventType = eventType,
                SecurityLevel = "HIGH",
                RequestId = auditInfo.RequestId,
                Timestamp = auditInfo.Timestamp,
                Method = auditInfo.Method,
                Path = auditInfo.Path,
                StatusCode = auditInfo.StatusCode,
                UserId = auditInfo.UserId,
                UserName = auditInfo.UserName,
                UserRoles = auditInfo.UserRoles,
                IpAddress = auditInfo.IpAddress,
                UserAgent = auditInfo.UserAgent,
                RequestBody = auditInfo.RequestBody,
                ErrorMessage = auditInfo.ErrorMessage,
                Details = details
            };

            _logger.LogWarning("SECURITY: {@SecurityEvent}", securityEvent);
        }

        private class AuditInfo
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
} 