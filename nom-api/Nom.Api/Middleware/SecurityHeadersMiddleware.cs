using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace Nom.Api.Middleware
{
    /// <summary>
    /// Middleware that adds security headers to all HTTP responses
    /// </summary>
    public class SecurityHeadersMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<SecurityHeadersMiddleware> _logger;
        private readonly IConfiguration _configuration;

        public SecurityHeadersMiddleware(
            RequestDelegate next, 
            ILogger<SecurityHeadersMiddleware> logger,
            IConfiguration configuration)
        {
            _next = next;
            _logger = logger;
            _configuration = configuration;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Add security headers before processing the request
            AddSecurityHeaders(context);

            await _next(context);
        }

        private void AddSecurityHeaders(HttpContext context)
        {
            var headers = context.Response.Headers;

            // Prevent clickjacking attacks
            if (!headers.ContainsKey("X-Frame-Options"))
            {
                headers.Add("X-Frame-Options", "DENY");
            }

            // Prevent MIME type sniffing
            if (!headers.ContainsKey("X-Content-Type-Options"))
            {
                headers.Add("X-Content-Type-Options", "nosniff");
            }

            // Enable XSS protection (for older browsers)
            if (!headers.ContainsKey("X-XSS-Protection"))
            {
                headers.Add("X-XSS-Protection", "1; mode=block");
            }

            // Control referrer information
            if (!headers.ContainsKey("Referrer-Policy"))
            {
                headers.Add("Referrer-Policy", "strict-origin-when-cross-origin");
            }

            // Permissions Policy (formerly Feature Policy)
            if (!headers.ContainsKey("Permissions-Policy"))
            {
                headers.Add("Permissions-Policy", 
                    "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()");
            }

            // Content Security Policy
            if (!headers.ContainsKey("Content-Security-Policy"))
            {
                var csp = BuildContentSecurityPolicy();
                headers.Add("Content-Security-Policy", csp);
            }

            // Strict Transport Security (HSTS) - only add for HTTPS
            if (context.Request.IsHttps && !headers.ContainsKey("Strict-Transport-Security"))
            {
                var enableHsts = _configuration.GetValue<bool>("ENABLE_HSTS", true);
                if (enableHsts)
                {
                    headers.Add("Strict-Transport-Security", "max-age=31536000; includeSubDomains; preload");
                }
            }

            // Remove server header if present
            headers.Remove("Server");
            headers.Remove("X-Powered-By");
            headers.Remove("X-AspNet-Version");
            headers.Remove("X-AspNetCore-Version");

            _logger.LogDebug("Security headers added to response for {Path}", context.Request.Path);
        }

        private string BuildContentSecurityPolicy()
        {
            var cspBuilder = new System.Text.StringBuilder();
            
            // Default source
            cspBuilder.Append("default-src 'self'; ");
            
            // Script source - allow self and specific CDNs if needed
            cspBuilder.Append("script-src 'self' 'unsafe-inline' 'unsafe-eval' https://cdn.jsdelivr.net https://cdnjs.cloudflare.com; ");
            
            // Style source - allow self and inline styles (for Angular)
            cspBuilder.Append("style-src 'self' 'unsafe-inline' https://fonts.googleapis.com https://cdn.jsdelivr.net; ");
            
            // Image source - allow self, data URIs, and HTTPS
            cspBuilder.Append("img-src 'self' data: https: blob:; ");
            
            // Font source
            cspBuilder.Append("font-src 'self' https://fonts.gstatic.com data:; ");
            
            // Connect source - for API calls
            cspBuilder.Append("connect-src 'self' https: wss:; ");
            
            // Media source
            cspBuilder.Append("media-src 'self'; ");
            
            // Object source
            cspBuilder.Append("object-src 'none'; ");
            
            // Frame ancestors
            cspBuilder.Append("frame-ancestors 'none'; ");
            
            // Form action
            cspBuilder.Append("form-action 'self'; ");
            
            // Base URI
            cspBuilder.Append("base-uri 'self'; ");
            
            // Upgrade insecure requests
            cspBuilder.Append("upgrade-insecure-requests;");

            return cspBuilder.ToString();
        }
    }

    /// <summary>
    /// Extension methods for adding the SecurityHeadersMiddleware to the application pipeline
    /// </summary>
    public static class SecurityHeadersMiddlewareExtensions
    {
        public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<SecurityHeadersMiddleware>();
        }
    }
}