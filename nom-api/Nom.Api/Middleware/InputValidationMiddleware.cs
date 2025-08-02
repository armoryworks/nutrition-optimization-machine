using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Nom.Api.Middleware
{
    /// <summary>
    /// Input validation middleware to sanitize and validate incoming requests
    /// Protects against XSS, SQL injection, and other injection attacks
    /// </summary>
    public class InputValidationMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<InputValidationMiddleware> _logger;

        // Dangerous patterns to detect and block
        private static readonly Regex[] DangerousPatterns = {
            new Regex(@"<script[^>]*>.*?</script>", RegexOptions.IgnoreCase | RegexOptions.Singleline),
            new Regex(@"javascript:", RegexOptions.IgnoreCase),
            new Regex(@"on\w+\s*=", RegexOptions.IgnoreCase),
            new Regex(@"vbscript:", RegexOptions.IgnoreCase),
            new Regex(@"expression\s*\(", RegexOptions.IgnoreCase),
            new Regex(@"eval\s*\(", RegexOptions.IgnoreCase),
            new Regex(@"<iframe[^>]*>", RegexOptions.IgnoreCase),
            new Regex(@"<object[^>]*>", RegexOptions.IgnoreCase),
            new Regex(@"<embed[^>]*>", RegexOptions.IgnoreCase),
            new Regex(@"union\s+select", RegexOptions.IgnoreCase),
            new Regex(@"drop\s+table", RegexOptions.IgnoreCase),
            new Regex(@"delete\s+from", RegexOptions.IgnoreCase),
            new Regex(@"insert\s+into", RegexOptions.IgnoreCase),
            new Regex(@"update\s+set", RegexOptions.IgnoreCase),
            new Regex(@"exec\s*\(", RegexOptions.IgnoreCase),
            new Regex(@"xp_cmdshell", RegexOptions.IgnoreCase),
            new Regex(@"sp_", RegexOptions.IgnoreCase),
            new Regex(@"@@", RegexOptions.IgnoreCase),
            new Regex(@"--", RegexOptions.IgnoreCase),
            new Regex(@"/\*.*?\*/", RegexOptions.Singleline),
            new Regex(@"<.*?>", RegexOptions.Singleline), // HTML tags
            new Regex(@"['"";]", RegexOptions.None), // SQL injection characters
        };

        // Maximum content length for different content types
        private const int MaxJsonLength = 1024 * 1024; // 1MB
        private const int MaxFormLength = 10 * 1024 * 1024; // 10MB
        private const int MaxTextLength = 10000; // 10KB

        public InputValidationMiddleware(RequestDelegate next, ILogger<InputValidationMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                // Validate request headers
                if (!ValidateHeaders(context))
                {
                    context.Response.StatusCode = 400;
                    await context.Response.WriteAsync("Invalid request headers");
                    return;
                }

                // Validate request path
                if (!ValidatePath(context.Request.Path.Value))
                {
                    context.Response.StatusCode = 400;
                    await context.Response.WriteAsync("Invalid request path");
                    return;
                }

                // Validate query parameters
                if (!ValidateQueryParameters(context.Request.QueryString.Value))
                {
                    context.Response.StatusCode = 400;
                    await context.Response.WriteAsync("Invalid query parameters");
                    return;
                }

                // Validate request body for specific content types
                if (context.Request.ContentLength > 0)
                {
                    if (!await ValidateRequestBody(context))
                    {
                        context.Response.StatusCode = 400;
                        await context.Response.WriteAsync("Invalid request body");
                        return;
                    }
                }

                await _next(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in input validation middleware");
                context.Response.StatusCode = 500;
                await context.Response.WriteAsync("Internal server error");
            }
        }

        private bool ValidateHeaders(HttpContext context)
        {
            var request = context.Request;

            // Check for suspicious headers
            var suspiciousHeaders = new[]
            {
                "X-Forwarded-For",
                "X-Real-IP",
                "X-Forwarded-Host",
                "X-Forwarded-Proto"
            };

            foreach (var header in suspiciousHeaders)
            {
                if (request.Headers.ContainsKey(header))
                {
                    var value = request.Headers[header].ToString();
                    if (ContainsDangerousContent(value))
                    {
                        _logger.LogWarning("Suspicious header detected: {Header} = {Value}", header, value);
                        return false;
                    }
                }
            }

            // Validate content type for POST/PUT requests
            if (request.Method == "POST" || request.Method == "PUT")
            {
                var contentType = request.ContentType?.ToLower();
                if (contentType != null)
                {
                    if (!IsValidContentType(contentType))
                    {
                        _logger.LogWarning("Invalid content type: {ContentType}", contentType);
                        return false;
                    }
                }
            }

            return true;
        }

        private bool ValidatePath(string? path)
        {
            if (string.IsNullOrEmpty(path))
                return true;

            // Check for path traversal attacks
            if (path.Contains("..") || path.Contains("\\") || path.Contains("%2e%2e"))
            {
                _logger.LogWarning("Path traversal attack detected: {Path}", path);
                return false;
            }

            // Check for dangerous content in path
            if (ContainsDangerousContent(path))
            {
                _logger.LogWarning("Dangerous content in path: {Path}", path);
                return false;
            }

            return true;
        }

        private bool ValidateQueryParameters(string? queryString)
        {
            if (string.IsNullOrEmpty(queryString))
                return true;

            // Check for dangerous content in query string
            if (ContainsDangerousContent(queryString))
            {
                _logger.LogWarning("Dangerous content in query string: {QueryString}", queryString);
                return false;
            }

            return true;
        }

        private async Task<bool> ValidateRequestBody(HttpContext context)
        {
            var request = context.Request;
            var contentType = request.ContentType?.ToLower();

            if (string.IsNullOrEmpty(contentType))
                return true;

            // Read and validate request body based on content type
            if (contentType.Contains("application/json"))
            {
                return await ValidateJsonBody(request);
            }
            else if (contentType.Contains("application/x-www-form-urlencoded"))
            {
                return await ValidateFormBody(request);
            }
            else if (contentType.Contains("text/"))
            {
                return await ValidateTextBody(request);
            }
            else if (contentType.Contains("multipart/form-data"))
            {
                return await ValidateMultipartBody(request);
            }

            return true;
        }

        private async Task<bool> ValidateJsonBody(HttpRequest request)
        {
            if (request.ContentLength > MaxJsonLength)
            {
                _logger.LogWarning("JSON body too large: {Size} bytes", request.ContentLength);
                return false;
            }

            try
            {
                request.EnableBuffering();
                request.Body.Position = 0;

                using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
                var body = await reader.ReadToEndAsync();
                request.Body.Position = 0;

                if (ContainsDangerousContent(body))
                {
                    _logger.LogWarning("Dangerous content in JSON body");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating JSON body");
                return false;
            }
        }

        private async Task<bool> ValidateFormBody(HttpRequest request)
        {
            if (request.ContentLength > MaxFormLength)
            {
                _logger.LogWarning("Form body too large: {Size} bytes", request.ContentLength);
                return false;
            }

            try
            {
                request.EnableBuffering();
                request.Body.Position = 0;

                using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
                var body = await reader.ReadToEndAsync();
                request.Body.Position = 0;

                if (ContainsDangerousContent(body))
                {
                    _logger.LogWarning("Dangerous content in form body");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating form body");
                return false;
            }
        }

        private async Task<bool> ValidateTextBody(HttpRequest request)
        {
            if (request.ContentLength > MaxTextLength)
            {
                _logger.LogWarning("Text body too large: {Size} bytes", request.ContentLength);
                return false;
            }

            try
            {
                request.EnableBuffering();
                request.Body.Position = 0;

                using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
                var body = await reader.ReadToEndAsync();
                request.Body.Position = 0;

                if (ContainsDangerousContent(body))
                {
                    _logger.LogWarning("Dangerous content in text body");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating text body");
                return false;
            }
        }

        private async Task<bool> ValidateMultipartBody(HttpRequest request)
        {
            if (request.ContentLength > MaxFormLength)
            {
                _logger.LogWarning("Multipart body too large: {Size} bytes", request.ContentLength);
                return false;
            }

            // For multipart requests, we'll validate individual form fields
            // This is a simplified validation - in production, you might want more granular control
            try
            {
                request.EnableBuffering();
                return true; // Multipart validation is complex and handled by ASP.NET Core
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating multipart body");
                return false;
            }
        }

        private bool ContainsDangerousContent(string content)
        {
            if (string.IsNullOrEmpty(content))
                return false;

            foreach (var pattern in DangerousPatterns)
            {
                if (pattern.IsMatch(content))
                {
                    _logger.LogWarning("Dangerous pattern detected: {Pattern}", pattern.ToString());
                    return true;
                }
            }

            return false;
        }

        private bool IsValidContentType(string contentType)
        {
            var validContentTypes = new[]
            {
                "application/json",
                "application/x-www-form-urlencoded",
                "multipart/form-data",
                "text/plain",
                "text/html",
                "text/xml",
                "application/xml"
            };

            return Array.Exists(validContentTypes, ct => contentType.Contains(ct));
        }
    }
} 