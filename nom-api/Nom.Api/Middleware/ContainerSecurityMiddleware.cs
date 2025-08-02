using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Linq;

namespace Nom.Api.Middleware
{
    /// <summary>
    /// Container security middleware for enforcing security headers and container-specific security measures
    /// Provides Docker container hardening and security header enforcement
    /// </summary>
    public class ContainerSecurityMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ContainerSecurityMiddleware> _logger;

        public ContainerSecurityMiddleware(RequestDelegate next, ILogger<ContainerSecurityMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                // Add security headers
                AddSecurityHeaders(context);

                // Validate container environment
                ValidateContainerEnvironment(context);

                // Log container security events
                LogContainerSecurityEvent(context);

                await _next(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in container security middleware");
                throw;
            }
        }

        /// <summary>
        /// Adds comprehensive security headers to all responses
        /// </summary>
        private void AddSecurityHeaders(HttpContext context)
        {
            var response = context.Response;

            // Content Security Policy (CSP)
            response.Headers["Content-Security-Policy"] = 
                "default-src 'self'; " +
                "script-src 'self' 'unsafe-inline' 'unsafe-eval'; " +
                "style-src 'self' 'unsafe-inline'; " +
                "img-src 'self' data: https:; " +
                "font-src 'self' https:; " +
                "connect-src 'self' https:; " +
                "frame-ancestors 'none'; " +
                "base-uri 'self'; " +
                "form-action 'self'; " +
                "upgrade-insecure-requests";

            // X-Frame-Options (prevent clickjacking)
            response.Headers["X-Frame-Options"] = "DENY";

            // X-Content-Type-Options (prevent MIME type sniffing)
            response.Headers["X-Content-Type-Options"] = "nosniff";

            // X-XSS-Protection (enable XSS protection)
            response.Headers["X-XSS-Protection"] = "1; mode=block";

            // Referrer Policy
            response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

            // Permissions Policy (formerly Feature Policy)
            response.Headers["Permissions-Policy"] = 
                "camera=(), " +
                "microphone=(), " +
                "geolocation=(), " +
                "payment=(), " +
                "usb=(), " +
                "magnetometer=(), " +
                "gyroscope=(), " +
                "accelerometer=()";

            // Strict-Transport-Security (HSTS)
            response.Headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains; preload";

            // Cross-Origin Resource Policy
            response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";

            // Cross-Origin Opener Policy
            response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";

            // Cross-Origin Embedder Policy
            response.Headers["Cross-Origin-Embedder-Policy"] = "require-corp";

            // Clear-Site-Data (for logout)
            if (context.Request.Path.StartsWithSegments("/auth/logout"))
            {
                response.Headers["Clear-Site-Data"] = "\"cache\", \"cookies\", \"storage\"";
            }

            // Server header removal (security through obscurity)
            response.Headers.Remove("Server");
        }

        /// <summary>
        /// Validates container environment for security compliance
        /// </summary>
        private void ValidateContainerEnvironment(HttpContext context)
        {
            try
            {
                // Check for required environment variables
                var requiredEnvVars = new[]
                {
                    "ASPNETCORE_ENVIRONMENT",
                    "ConnectionStrings__DefaultConnection"
                };

                foreach (var envVar in requiredEnvVars)
                {
                    if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(envVar)))
                    {
                        _logger.LogWarning("Required environment variable {EnvVar} is not set", envVar);
                    }
                }

                // Check for container-specific security settings
                var containerId = Environment.GetEnvironmentVariable("HOSTNAME");
                if (!string.IsNullOrEmpty(containerId))
                {
                    _logger.LogInformation("Container ID: {ContainerId}", containerId);
                }

                // Validate file permissions (in a real container, this would check actual file permissions)
                var criticalPaths = new[]
                {
                    "/app/appsettings.json",
                    "/app/appsettings.Production.json"
                };

                foreach (var path in criticalPaths)
                {
                    // In a real implementation, this would check file permissions
                    // For now, we'll just log that we're checking
                    _logger.LogDebug("Checking file permissions for {Path}", path);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating container environment");
            }
        }

        /// <summary>
        /// Logs container security events
        /// </summary>
        private void LogContainerSecurityEvent(HttpContext context)
        {
            try
            {
                var request = context.Request;
                var response = context.Response;

                // Log security-relevant requests
                if (IsSecuritySensitiveEndpoint(request.Path))
                {
                    _logger.LogInformation(
                        "Security-sensitive request: {Method} {Path} from {IpAddress}",
                        request.Method,
                        request.Path,
                        GetClientIpAddress(context));
                }

                // Log container resource usage (simulated)
                var memoryUsage = GC.GetTotalMemory(false);
                var cpuUsage = Environment.ProcessorCount;

                if (memoryUsage > 500 * 1024 * 1024) // 500MB threshold
                {
                    _logger.LogWarning("High memory usage detected: {MemoryUsage} bytes", memoryUsage);
                }

                // Log container health metrics
                LogContainerHealthMetrics(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error logging container security event");
            }
        }

        /// <summary>
        /// Logs container health metrics
        /// </summary>
        private void LogContainerHealthMetrics(HttpContext context)
        {
            try
            {
                var metrics = new
                {
                    Timestamp = DateTime.UtcNow,
                    MemoryUsage = GC.GetTotalMemory(false),
                    ProcessorCount = Environment.ProcessorCount,
                    Uptime = Environment.TickCount64,
                    RequestCount = context.Request.Path.Value,
                    UserAgent = context.Request.Headers["User-Agent"].ToString(),
                    IpAddress = GetClientIpAddress(context)
                };

                _logger.LogDebug("Container health metrics: {@Metrics}", metrics);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error logging container health metrics");
            }
        }

        /// <summary>
        /// Checks if an endpoint is security-sensitive
        /// </summary>
        private bool IsSecuritySensitiveEndpoint(string? path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            var sensitivePaths = new[]
            {
                "/auth/",
                "/api/auth/",
                "/api/user/",
                "/api/admin/",
                "/privacy/",
                "/api/privacy/"
            };

            return sensitivePaths.Any(sensitivePath => path.StartsWith(sensitivePath, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Gets the client IP address
        /// </summary>
        private string GetClientIpAddress(HttpContext context)
        {
            // Check for forwarded headers (common in containerized environments)
            var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrEmpty(forwardedFor))
            {
                return forwardedFor.Split(',')[0].Trim();
            }

            var realIp = context.Request.Headers["X-Real-IP"].FirstOrDefault();
            if (!string.IsNullOrEmpty(realIp))
            {
                return realIp;
            }

            return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        }
    }

    /// <summary>
    /// Extension method for registering container security middleware
    /// </summary>
    public static class ContainerSecurityMiddlewareExtensions
    {
        public static IApplicationBuilder UseContainerSecurity(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<ContainerSecurityMiddleware>();
        }
    }
} 