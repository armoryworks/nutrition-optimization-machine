// File: Nom.Api/_Abstractions/_Core/_BaseCacheService.cs

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Nom.Api._Abstractions._Core
{
    /// <summary>
    /// Base cache service implementation
    /// </summary>
    public abstract class _BaseCacheService : _ICacheService
    {
        protected readonly ILogger<_BaseCacheService> _logger;
        protected readonly _CacheOptions _options;
        protected readonly ConcurrentDictionary<string, _CacheEntry> _cache;
        protected readonly _CacheStatistics _statistics;

        protected _BaseCacheService(ILogger<_BaseCacheService> logger, IOptions<_CacheOptions> options)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _options = options?.Value ?? new _CacheOptions();
            _cache = new ConcurrentDictionary<string, _CacheEntry>();
            _statistics = new _CacheStatistics();
        }

        public virtual async Task<T?> GetAsync<T>(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                _logger.LogWarning("Attempted to get cache with null or empty key");
                return default;
            }

            var startTime = DateTime.UtcNow;

            try
            {
                if (_cache.TryGetValue(key, out var entry))
                {
                    if (entry.IsExpired)
                    {
                        _cache.TryRemove(key, out _);
                        _statistics.TotalMisses++;
                        _statistics.ExpirationCount++;
                        _logger.LogDebug("Cache entry expired for key: {Key}", key);
                        return default;
                    }

                    _statistics.TotalHits++;
                    _statistics.LastAccessTime = DateTime.UtcNow;

                    var retrievalTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
                    _statistics.AverageRetrievalTimeMs = (_statistics.AverageRetrievalTimeMs + retrievalTime) / 2;

                    _logger.LogDebug("Cache hit for key: {Key}", key);
                    return Deserialize<T>(entry.Value);
                }

                _statistics.TotalMisses++;
                _logger.LogDebug("Cache miss for key: {Key}", key);
                return default;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting cache value for key: {Key}", key);
                return default;
            }
        }

        public virtual async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                _logger.LogWarning("Attempted to set cache with null or empty key");
                return;
            }

            try
            {
                var serializedValue = Serialize(value);
                var entry = new _CacheEntry
                {
                    Value = serializedValue,
                    ExpirationTime = expiration.HasValue ? DateTime.UtcNow.Add(expiration.Value) : null,
                    CreatedTime = DateTime.UtcNow,
                    LastAccessedTime = DateTime.UtcNow
                };

                _cache.AddOrUpdate(key, entry, (k, v) => entry);
                _statistics.TotalEntries = _cache.Count;
                _statistics.LastAccessTime = DateTime.UtcNow;

                _logger.LogDebug("Set cache value for key: {Key}", key);
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting cache value for key: {Key}", key);
            }
        }

        public virtual async Task RemoveAsync(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                _logger.LogWarning("Attempted to remove cache with null or empty key");
                return;
            }

            try
            {
                if (_cache.TryRemove(key, out _))
                {
                    _statistics.TotalEntries = _cache.Count;
                    _logger.LogDebug("Removed cache entry for key: {Key}", key);
                }

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing cache entry for key: {Key}", key);
            }
        }

        public virtual async Task RemoveAsync(IEnumerable<string> keys)
        {
            if (keys == null)
            {
                _logger.LogWarning("Attempted to remove cache with null keys");
                return;
            }

            try
            {
                var removedCount = 0;
                foreach (var key in keys)
                {
                    if (_cache.TryRemove(key, out _))
                    {
                        removedCount++;
                    }
                }

                _statistics.TotalEntries = _cache.Count;
                _logger.LogDebug("Removed {Count} cache entries", removedCount);
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing cache entries");
            }
        }

        public virtual async Task<bool> ExistsAsync(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            try
            {
                if (_cache.TryGetValue(key, out var entry))
                {
                    if (entry.IsExpired)
                    {
                        _cache.TryRemove(key, out _);
                        _statistics.ExpirationCount++;
                        return false;
                    }

                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking cache existence for key: {Key}", key);
                return false;
            }
        }

        public virtual async Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null)
        {
            var cachedValue = await GetAsync<T>(key);
            if (cachedValue != null)
            {
                return cachedValue;
            }

            var value = await factory();
            await SetAsync(key, value, expiration);
            return value;
        }

        public virtual async Task<T> GetOrSetAsync<T>(string key, T defaultValue, TimeSpan? expiration = null)
        {
            var cachedValue = await GetAsync<T>(key);
            if (cachedValue != null)
            {
                return cachedValue;
            }

            await SetAsync(key, defaultValue, expiration);
            return defaultValue;
        }

        public virtual async Task<long> IncrementAsync(string key, long increment = 1)
        {
            var currentValue = await GetAsync<long>(key);
            var newValue = currentValue + increment;
            await SetAsync(key, newValue);
            return newValue;
        }

        public virtual async Task<long> DecrementAsync(string key, long decrement = 1)
        {
            var currentValue = await GetAsync<long>(key);
            var newValue = currentValue - decrement;
            await SetAsync(key, newValue);
            return newValue;
        }

        public virtual async Task SetExpirationAsync(string key, TimeSpan expiration)
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                entry.ExpirationTime = DateTime.UtcNow.Add(expiration);
                await Task.CompletedTask;
            }
        }

        public virtual async Task<TimeSpan?> GetTimeToLiveAsync(string key)
        {
            if (_cache.TryGetValue(key, out var entry) && entry.ExpirationTime.HasValue)
            {
                var ttl = entry.ExpirationTime.Value - DateTime.UtcNow;
                return ttl > TimeSpan.Zero ? ttl : null;
            }

            return null;
        }

        public virtual async Task ClearAsync()
        {
            try
            {
                _cache.Clear();
                _statistics.TotalEntries = 0;
                _logger.LogInformation("Cleared all cache entries");
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing cache");
            }
        }

        public virtual async Task<_CacheStatistics> GetStatisticsAsync()
        {
            return await Task.FromResult(new _CacheStatistics
            {
                TotalHits = _statistics.TotalHits,
                TotalMisses = _statistics.TotalMisses,
                TotalEntries = _statistics.TotalEntries,
                TotalMemoryUsage = _statistics.TotalMemoryUsage,
                AverageRetrievalTimeMs = _statistics.AverageRetrievalTimeMs,
                LastAccessTime = _statistics.LastAccessTime,
                EvictionCount = _statistics.EvictionCount,
                ExpirationCount = _statistics.ExpirationCount
            });
        }

        public virtual async Task<IEnumerable<string>> GetKeysAsync(string pattern)
        {
            try
            {
                var keys = _cache.Keys.Where(key => 
                    string.IsNullOrEmpty(pattern) || key.Contains(pattern, StringComparison.OrdinalIgnoreCase));
                return await Task.FromResult(keys);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting cache keys for pattern: {Pattern}", pattern);
                return Enumerable.Empty<string>();
            }
        }

        public virtual async Task<long> GetSizeAsync()
        {
            return await Task.FromResult((long)_cache.Count);
        }

        /// <summary>
        /// Serializes an object to a string
        /// </summary>
        /// <typeparam name="T">The type of the object</typeparam>
        /// <param name="value">The value to serialize</param>
        /// <returns>The serialized string</returns>
        protected virtual string Serialize<T>(T value)
        {
            try
            {
                return JsonSerializer.Serialize(value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error serializing value of type {Type}", typeof(T).Name);
                return string.Empty;
            }
        }

        /// <summary>
        /// Deserializes a string to an object
        /// </summary>
        /// <typeparam name="T">The type of the object</typeparam>
        /// <param name="value">The serialized string</param>
        /// <returns>The deserialized object</returns>
        protected virtual T? Deserialize<T>(string value)
        {
            try
            {
                if (string.IsNullOrEmpty(value))
                {
                    return default;
                }

                return JsonSerializer.Deserialize<T>(value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deserializing value to type {Type}", typeof(T).Name);
                return default;
            }
        }

        /// <summary>
        /// Performs cache cleanup
        /// </summary>
        protected virtual void CleanupExpiredEntries()
        {
            try
            {
                var expiredKeys = _cache
                    .Where(kvp => kvp.Value.IsExpired)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in expiredKeys)
                {
                    _cache.TryRemove(key, out _);
                    _statistics.ExpirationCount++;
                }

                _statistics.TotalEntries = _cache.Count;

                if (expiredKeys.Count > 0)
                {
                    _logger.LogDebug("Cleaned up {Count} expired cache entries", expiredKeys.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up expired cache entries");
            }
        }
    }

    /// <summary>
    /// Cache entry
    /// </summary>
    internal class _CacheEntry
    {
        public string Value { get; set; } = string.Empty;
        public DateTime? ExpirationTime { get; set; }
        public DateTime CreatedTime { get; set; }
        public DateTime LastAccessedTime { get; set; }

        public bool IsExpired => ExpirationTime.HasValue && DateTime.UtcNow > ExpirationTime.Value;
    }
} 