using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Platform;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Platform;

namespace Nom.Orch.Services
{
    public class PlatformFeatureService : IPlatformFeatureService
    {
        private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);

        private readonly ApplicationDbContext _db;
        private readonly IMemoryCache _cache;
        private readonly ILogger<PlatformFeatureService> _logger;

        public PlatformFeatureService(
            ApplicationDbContext db,
            IMemoryCache cache,
            ILogger<PlatformFeatureService> logger)
        {
            _db = db;
            _cache = cache;
            _logger = logger;
        }

        private static string CacheKey(string key) => $"platform-feature:{key.ToLowerInvariant()}";

        public async Task<bool> IsEnabledAsync(string key)
        {
            var cacheKey = CacheKey(key);
            if (_cache.TryGetValue<bool>(cacheKey, out var cached))
            {
                return cached;
            }

            // Unknown keys are OFF: a feature must be switched on deliberately,
            // and a missing row must never mean "enabled".
            var enabled = await _db.PlatformFeatures
                .AsNoTracking()
                .Where(f => f.Key == key && !f.IsDeleted)
                .Select(f => f.IsEnabled)
                .FirstOrDefaultAsync();

            _cache.Set(cacheKey, enabled, CacheFor);
            return enabled;
        }

        public async Task<List<PlatformFeatureModel>> ListAsync()
        {
            var features = await _db.PlatformFeatures
                .AsNoTracking()
                .Where(f => !f.IsDeleted)
                .OrderBy(f => f.Key)
                .ToListAsync();

            return features.Select(Map).ToList();
        }

        public async Task<PlatformFeatureModel> SetAsync(string key, bool isEnabled, long adminPersonId)
        {
            var normalized = key.Trim().ToLowerInvariant();
            var feature = await _db.PlatformFeatures.FirstOrDefaultAsync(f => f.Key == normalized && !f.IsDeleted);

            if (feature == null)
            {
                feature = new PlatformFeatureEntity
                {
                    Key = normalized,
                    IsEnabled = isEnabled,
                    CreatedDate = DateTime.UtcNow,
                    CreatedByPersonId = adminPersonId,
                };
                _db.PlatformFeatures.Add(feature);
            }
            else
            {
                feature.IsEnabled = isEnabled;
                feature.LastModifiedDate = DateTime.UtcNow;
                feature.LastModifiedByPersonId = adminPersonId;
            }

            await _db.SaveChangesAsync();

            // A toggle must take effect now, not in 30 seconds.
            _cache.Remove(CacheKey(normalized));

            _logger.LogInformation("Platform feature {Key} set to {State} by person {PersonId}",
                normalized, isEnabled ? "ON" : "OFF", adminPersonId);

            return Map(feature);
        }

        private static PlatformFeatureModel Map(PlatformFeatureEntity f) => new()
        {
            Key = f.Key,
            IsEnabled = f.IsEnabled,
            Description = f.Description,
            LastModifiedDate = f.LastModifiedDate,
        };
    }
}
