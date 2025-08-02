using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Nom.Api.Middleware
{
    /// <summary>
    /// File upload security middleware to validate and secure file uploads
    /// Protects against malicious file uploads and enforces file type restrictions
    /// </summary>
    public class FileUploadSecurityMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<FileUploadSecurityMiddleware> _logger;

        // Allowed file extensions for different content types
        private static readonly Dictionary<string, string[]> AllowedExtensions = new()
        {
            ["image"] = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".svg" },
            ["document"] = new[] { ".pdf", ".txt", ".md", ".doc", ".docx" },
            ["archive"] = new[] { ".zip", ".rar", ".7z", ".tar", ".gz" },
            ["recipe"] = new[] { ".json", ".xml", ".yaml", ".yml" }
        };

        // Maximum file sizes (in bytes)
        private const long MaxImageSize = 10 * 1024 * 1024; // 10MB
        private const long MaxDocumentSize = 50 * 1024 * 1024; // 50MB
        private const long MaxArchiveSize = 100 * 1024 * 1024; // 100MB
        private const long MaxRecipeSize = 1 * 1024 * 1024; // 1MB

        // Dangerous file signatures (magic bytes)
        private static readonly byte[][] DangerousSignatures = {
            new byte[] { 0x4D, 0x5A }, // .exe
            new byte[] { 0x7F, 0x45, 0x4C, 0x46 }, // ELF executable
            new byte[] { 0xFE, 0xED, 0xFA, 0xCE }, // Mach-O executable
            new byte[] { 0xFE, 0xED, 0xFA, 0xCF }, // Mach-O executable (reverse)
            new byte[] { 0xCA, 0xFE, 0xBA, 0xBE }, // Java class file
            new byte[] { 0x50, 0x4B, 0x03, 0x04 }, // ZIP (but we allow this for archives)
            new byte[] { 0x50, 0x4B, 0x05, 0x06 }, // ZIP
            new byte[] { 0x50, 0x4B, 0x07, 0x08 }, // ZIP
        };

        public FileUploadSecurityMiddleware(RequestDelegate next, ILogger<FileUploadSecurityMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                // Check if this is a file upload request
                if (IsFileUploadRequest(context.Request))
                {
                    if (!await ValidateFileUpload(context))
                    {
                        context.Response.StatusCode = 400;
                        await context.Response.WriteAsync("Invalid file upload");
                        return;
                    }
                }

                await _next(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in file upload security middleware");
                context.Response.StatusCode = 500;
                await context.Response.WriteAsync("Internal server error");
            }
        }

        private bool IsFileUploadRequest(HttpRequest request)
        {
            // Check if this is a multipart form data request
            var contentType = request.ContentType?.ToLower();
            return contentType != null && contentType.Contains("multipart/form-data");
        }

        private async Task<bool> ValidateFileUpload(HttpContext context)
        {
            var request = context.Request;

            // Check content length
            if (request.ContentLength > MaxArchiveSize)
            {
                _logger.LogWarning("File upload too large: {Size} bytes", request.ContentLength);
                return false;
            }

            // Enable buffering to read the request body
            request.EnableBuffering();

            try
            {
                // Read the request body to analyze file content
                request.Body.Position = 0;
                var buffer = new byte[Math.Min(request.ContentLength ?? 0, 1024)]; // Read first 1KB
                await request.Body.ReadAsync(buffer, 0, buffer.Length);
                request.Body.Position = 0;

                // Check for dangerous file signatures
                if (ContainsDangerousSignature(buffer))
                {
                    _logger.LogWarning("Dangerous file signature detected");
                    return false;
                }

                // Validate file extensions and content types
                if (!await ValidateFileContent(context, buffer))
                {
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating file upload");
                return false;
            }
        }

        private bool ContainsDangerousSignature(byte[] buffer)
        {
            foreach (var signature in DangerousSignatures)
            {
                if (buffer.Length >= signature.Length)
                {
                    bool matches = true;
                    for (int i = 0; i < signature.Length; i++)
                    {
                        if (buffer[i] != signature[i])
                        {
                            matches = false;
                            break;
                        }
                    }
                    if (matches)
                    {
                        _logger.LogWarning("Dangerous file signature detected: {Signature}", 
                            BitConverter.ToString(signature));
                        return true;
                    }
                }
            }
            return false;
        }

        private async Task<bool> ValidateFileContent(HttpContext context, byte[] buffer)
        {
            var request = context.Request;

            // Get form data to analyze file uploads
            if (!request.HasFormContentType)
            {
                return true; // Not a form upload
            }

            var form = await request.ReadFormAsync();
            
            foreach (var file in form.Files)
            {
                if (!await ValidateIndividualFile(file, buffer))
                {
                    return false;
                }
            }

            return true;
        }

        private async Task<bool> ValidateIndividualFile(IFormFile file, byte[] buffer)
        {
            // Check file name
            if (string.IsNullOrEmpty(file.FileName))
            {
                _logger.LogWarning("File has no name");
                return false;
            }

            // Check for path traversal in filename
            if (file.FileName.Contains("..") || file.FileName.Contains("\\") || file.FileName.Contains("/"))
            {
                _logger.LogWarning("Path traversal detected in filename: {FileName}", file.FileName);
                return false;
            }

            // Get file extension
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (string.IsNullOrEmpty(extension))
            {
                _logger.LogWarning("File has no extension: {FileName}", file.FileName);
                return false;
            }

            // Check file size based on type
            if (!ValidateFileSize(file, extension))
            {
                return false;
            }

            // Check if extension is allowed
            if (!IsAllowedExtension(extension))
            {
                _logger.LogWarning("File extension not allowed: {Extension}", extension);
                return false;
            }

            // Validate file content type
            if (!ValidateContentType(file, extension))
            {
                return false;
            }

            // Additional validation for specific file types
            if (!await ValidateFileTypeSpecific(file, extension))
            {
                return false;
            }

            return true;
        }

        private bool ValidateFileSize(IFormFile file, string extension)
        {
            var maxSize = GetMaxFileSize(extension);
            
            if (file.Length > maxSize)
            {
                _logger.LogWarning("File too large: {FileName} ({Size} bytes, max {MaxSize} bytes)", 
                    file.FileName, file.Length, maxSize);
                return false;
            }

            return true;
        }

        private long GetMaxFileSize(string extension)
        {
            if (IsImageExtension(extension))
                return MaxImageSize;
            if (IsDocumentExtension(extension))
                return MaxDocumentSize;
            if (IsArchiveExtension(extension))
                return MaxArchiveSize;
            if (IsRecipeExtension(extension))
                return MaxRecipeSize;
            
            return MaxDocumentSize; // Default
        }

        private bool IsAllowedExtension(string extension)
        {
            return AllowedExtensions.Values.Any(extensions => extensions.Contains(extension));
        }

        private bool ValidateContentType(IFormFile file, string extension)
        {
            var contentType = file.ContentType?.ToLowerInvariant();
            
            if (string.IsNullOrEmpty(contentType))
            {
                _logger.LogWarning("File has no content type: {FileName}", file.FileName);
                return false;
            }

            // Validate content type matches extension
            if (IsImageExtension(extension) && !contentType.StartsWith("image/"))
            {
                _logger.LogWarning("Content type mismatch for image: {ContentType}", contentType);
                return false;
            }

            if (IsDocumentExtension(extension) && !contentType.StartsWith("application/") && 
                !contentType.StartsWith("text/"))
            {
                _logger.LogWarning("Content type mismatch for document: {ContentType}", contentType);
                return false;
            }

            return true;
        }

        private async Task<bool> ValidateFileTypeSpecific(IFormFile file, string extension)
        {
            // Additional validation for specific file types
            if (IsImageExtension(extension))
            {
                return await ValidateImageFile(file);
            }
            else if (IsArchiveExtension(extension))
            {
                return await ValidateArchiveFile(file);
            }
            else if (IsRecipeExtension(extension))
            {
                return await ValidateRecipeFile(file);
            }

            return true;
        }

        private async Task<bool> ValidateImageFile(IFormFile file)
        {
            try
            {
                // Read first few bytes to check image signature
                using var stream = file.OpenReadStream();
                var buffer = new byte[8];
                await stream.ReadAsync(buffer, 0, buffer.Length);

                // Check for common image signatures
                var signatures = new[]
                {
                    new byte[] { 0xFF, 0xD8, 0xFF }, // JPEG
                    new byte[] { 0x89, 0x50, 0x4E, 0x47 }, // PNG
                    new byte[] { 0x47, 0x49, 0x46 }, // GIF
                    new byte[] { 0x42, 0x4D }, // BMP
                };

                foreach (var signature in signatures)
                {
                    if (StartsWithSignature(buffer, signature))
                    {
                        return true;
                    }
                }

                _logger.LogWarning("Invalid image file signature: {FileName}", file.FileName);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating image file: {FileName}", file.FileName);
                return false;
            }
        }

        private async Task<bool> ValidateArchiveFile(IFormFile file)
        {
            try
            {
                // For archives, we'll do basic validation
                // In production, you might want to scan the archive contents
                using var stream = file.OpenReadStream();
                var buffer = new byte[4];
                await stream.ReadAsync(buffer, 0, buffer.Length);

                // Check for ZIP signature
                var zipSignature = new byte[] { 0x50, 0x4B, 0x03, 0x04 };
                if (StartsWithSignature(buffer, zipSignature))
                {
                    return true;
                }

                _logger.LogWarning("Invalid archive file signature: {FileName}", file.FileName);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating archive file: {FileName}", file.FileName);
                return false;
            }
        }

        private async Task<bool> ValidateRecipeFile(IFormFile file)
        {
            try
            {
                // For recipe files, validate JSON/XML structure
                using var stream = file.OpenReadStream();
                using var reader = new StreamReader(stream);
                var content = await reader.ReadToEndAsync();

                // Basic validation - in production, you'd want more thorough validation
                if (file.FileName.EndsWith(".json"))
                {
                    return IsValidJson(content);
                }
                else if (file.FileName.EndsWith(".xml"))
                {
                    return IsValidXml(content);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating recipe file: {FileName}", file.FileName);
                return false;
            }
        }

        private bool StartsWithSignature(byte[] buffer, byte[] signature)
        {
            if (buffer.Length < signature.Length)
                return false;

            for (int i = 0; i < signature.Length; i++)
            {
                if (buffer[i] != signature[i])
                    return false;
            }
            return true;
        }

        private bool IsValidJson(string content)
        {
            try
            {
                System.Text.Json.JsonDocument.Parse(content);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool IsValidXml(string content)
        {
            try
            {
                var doc = new System.Xml.XmlDocument();
                doc.LoadXml(content);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool IsImageExtension(string extension)
        {
            return AllowedExtensions["image"].Contains(extension);
        }

        private bool IsDocumentExtension(string extension)
        {
            return AllowedExtensions["document"].Contains(extension);
        }

        private bool IsArchiveExtension(string extension)
        {
            return AllowedExtensions["archive"].Contains(extension);
        }

        private bool IsRecipeExtension(string extension)
        {
            return AllowedExtensions["recipe"].Contains(extension);
        }
    }
} 