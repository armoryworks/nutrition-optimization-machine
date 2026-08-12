using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Nom.Orch.UtilityInterfaces;

namespace Nom.Orch.UtilityServices
{
    /// <summary>
    /// Filesystem media storage rooted at Media:RootPath. Relative paths are
    /// canonicalized and verified to stay inside the root (defense against
    /// traversal if a stored path is ever tampered with).
    /// </summary>
    public class MediaStorageService : IMediaStorageService
    {
        private readonly string? _rootPath;
        private readonly ILogger<MediaStorageService> _logger;

        public MediaStorageService(IConfiguration configuration, ILogger<MediaStorageService> logger)
        {
            _logger = logger;
            var configured = configuration["Media:RootPath"];
            _rootPath = string.IsNullOrWhiteSpace(configured)
                ? null
                : Path.GetFullPath(configured);
        }

        public bool IsConfigured => _rootPath != null;

        public async Task<string> SaveAsync(string relativePath, byte[] data)
        {
            var fullPath = Resolve(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await File.WriteAllBytesAsync(fullPath, data);
            _logger.LogInformation("Stored media file {Path} ({Bytes} bytes)", relativePath, data.Length);
            return relativePath;
        }

        public async Task<byte[]?> ReadAsync(string relativePath)
        {
            var fullPath = Resolve(relativePath);
            return File.Exists(fullPath) ? await File.ReadAllBytesAsync(fullPath) : null;
        }

        public Task DeleteAsync(string relativePath)
        {
            var fullPath = Resolve(relativePath);
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }

            return Task.CompletedTask;
        }

        private string Resolve(string relativePath)
        {
            if (_rootPath == null)
            {
                throw new InvalidOperationException("Media storage is not configured (Media:RootPath).");
            }

            var fullPath = Path.GetFullPath(Path.Combine(_rootPath, relativePath));
            if (!fullPath.StartsWith(_rootPath + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Media path escapes the storage root.");
            }

            return fullPath;
        }
    }
}
