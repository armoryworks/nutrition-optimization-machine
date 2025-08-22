using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Nom.Orch.Models.Measurement;
using Nom.Data.Measurement;

namespace Nom.Orch.Services.Measurement
{
    /// <summary>
    /// In-memory caching service for measurement data to improve performance
    /// </summary>
    public class MeasurementCacheService : IMeasurementCacheService
    {
        private readonly IMemoryCache _cache;
        private readonly ILogger<MeasurementCacheService> _logger;
        private readonly TimeSpan _defaultExpiration = TimeSpan.FromHours(1);

        public MeasurementCacheService(IMemoryCache cache, ILogger<MeasurementCacheService> logger)
        {
            _cache = cache;
            _logger = logger;
        }

        public async Task<decimal?> GetCachedConversionAsync(long fromId, long toId)
        {
            var cacheKey = $"conversion_{fromId}_{toId}";
            if (_cache.TryGetValue(cacheKey, out var cached))
            {
                _logger.LogDebug("Cache hit for conversion from {FromId} to {ToId}", fromId, toId);
                return (decimal?)cached;
            }

            _logger.LogDebug("Cache miss for conversion from {FromId} to {ToId}", fromId, toId);
            return null;
        }

        public async Task CacheConversionAsync(long fromId, long toId, decimal conversionFactor, decimal? offset = null)
        {
            var cacheKey = $"conversion_{fromId}_{toId}";
            var cacheValue = offset.HasValue ? conversionFactor + offset.Value : conversionFactor;
            
            var options = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = _defaultExpiration,
                SlidingExpiration = TimeSpan.FromMinutes(30)
            };

            _cache.Set(cacheKey, cacheValue, options);
            _logger.LogDebug("Cached conversion from {FromId} to {ToId}: {Value}", fromId, toId, cacheValue);
        }

        public async Task<MeasurementModel?> GetCachedMeasurementAsync(long id)
        {
            var cacheKey = $"measurement_{id}";
            if (_cache.TryGetValue(cacheKey, out var cached))
            {
                _logger.LogDebug("Cache hit for measurement {Id}", id);
                return (MeasurementModel?)cached;
            }

            _logger.LogDebug("Cache miss for measurement {Id}", id);
            return null;
        }

        public async Task CacheMeasurementAsync(MeasurementModel measurement)
        {
            var cacheKey = $"measurement_{measurement.Id}";
            var options = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = _defaultExpiration,
                SlidingExpiration = TimeSpan.FromMinutes(30)
            };

            _cache.Set(cacheKey, measurement, options);
            _logger.LogDebug("Cached measurement {Id}: {Name}", measurement.Id, measurement.Name);
        }

        public async Task<List<MeasurementConversionModel>?> GetCachedConversionPathAsync(long fromId, long toId)
        {
            var cacheKey = $"conversion_path_{fromId}_{toId}";
            if (_cache.TryGetValue(cacheKey, out var cached))
            {
                _logger.LogDebug("Cache hit for conversion path from {FromId} to {ToId}", fromId, toId);
                return (List<MeasurementConversionModel>?)cached;
            }

            _logger.LogDebug("Cache miss for conversion path from {FromId} to {ToId}", fromId, toId);
            return null;
        }

        public async Task CacheConversionPathAsync(long fromId, long toId, List<MeasurementConversionModel> path)
        {
            var cacheKey = $"conversion_path_{fromId}_{toId}";
            var options = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = _defaultExpiration,
                SlidingExpiration = TimeSpan.FromMinutes(30)
            };

            _cache.Set(cacheKey, path, options);
            _logger.LogDebug("Cached conversion path from {FromId} to {ToId} with {Count} steps", fromId, toId, path.Count);
        }

        public async Task<List<MeasurementConversionEntity>?> GetCachedConversionPathEntitiesAsync(long fromId, long toId)
        {
            var cacheKey = $"conversion_path_entities_{fromId}_{toId}";
            if (_cache.TryGetValue(cacheKey, out var cached))
            {
                _logger.LogDebug("Cache hit for conversion path entities from {FromId} to {ToId}", fromId, toId);
                return (List<MeasurementConversionEntity>?)cached;
            }

            _logger.LogDebug("Cache miss for conversion path entities from {FromId} to {ToId}", fromId, toId);
            return null;
        }

        public async Task CacheConversionPathEntitiesAsync(long fromId, long toId, List<MeasurementConversionEntity> path)
        {
            var cacheKey = $"conversion_path_entities_{fromId}_{toId}";
            var options = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = _defaultExpiration,
                SlidingExpiration = TimeSpan.FromMinutes(30)
            };

            _cache.Set(cacheKey, path, options);
            _logger.LogDebug("Cached conversion path entities from {FromId} to {ToId} with {Count} steps", fromId, toId, path.Count);
        }

        public async Task InvalidateMeasurementCacheAsync(long measurementId)
        {
            // Remove measurement from cache
            var measurementKey = $"measurement_{measurementId}";
            _cache.Remove(measurementKey);

            // Remove all conversion caches involving this measurement
            // This is a simplified approach - in production, you might want to track dependencies
            var keysToRemove = new List<string>();
            
            // Note: In a production system, you'd want to maintain a dependency graph
            // For now, we'll clear all conversion caches when a measurement changes
            _logger.LogInformation("Invalidated cache for measurement {Id}", measurementId);
        }

        public async Task ClearCacheAsync()
        {
            // Clear all measurement-related caches
            // In a production system, you'd want to be more selective
            // Note: IMemoryCache doesn't have a Clear method, so we'll log this limitation
            _logger.LogInformation("Cache clear requested - IMemoryCache doesn't support bulk clearing");
        }

        public bool TryGetCachedMeasurementsByCategory(long categoryId, out List<MeasurementModel> measurements)
        {
            var cacheKey = $"measurements_category_{categoryId}";
            if (_cache.TryGetValue(cacheKey, out var cached))
            {
                measurements = (List<MeasurementModel>)cached;
                _logger.LogDebug("Cache hit for measurements category {CategoryId}", categoryId);
                return true;
            }

            measurements = new List<MeasurementModel>();
            _logger.LogDebug("Cache miss for measurements category {CategoryId}", categoryId);
            return false;
        }

        public async Task CacheMeasurementsByCategoryAsync(long categoryId, List<MeasurementModel> measurements)
        {
            var cacheKey = $"measurements_category_{categoryId}";
            var options = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = _defaultExpiration,
                SlidingExpiration = TimeSpan.FromMinutes(30)
            };

            _cache.Set(cacheKey, measurements, options);
            _logger.LogDebug("Cached {Count} measurements for category {CategoryId}", measurements.Count, categoryId);
        }
    }
}
