// File: Nom.Api/_Abstractions/_Core/ICacheService.cs

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nom.Api.Core
{
    /// <summary>
    /// Interface for cache service operations
    /// </summary>
    public interface ICacheService
    {
        /// <summary>
        /// Gets a value from cache
        /// </summary>
        /// <typeparam name="T">The type of the value</typeparam>
        /// <param name="key">The cache key</param>
        /// <returns>The cached value or null if not found</returns>
        Task<T?> GetAsync<T>(string key);

        /// <summary>
        /// Sets a value in cache
        /// </summary>
        /// <typeparam name="T">The type of the value</typeparam>
        /// <param name="key">The cache key</param>
        /// <param name="value">The value to cache</param>
        /// <param name="expiration">Optional expiration time</param>
        /// <returns>Task representing the async operation</returns>
        Task SetAsync<T>(string key, T value, TimeSpan? expiration = null);

        /// <summary>
        /// Removes a value from cache
        /// </summary>
        /// <param name="key">The cache key</param>
        /// <returns>Task representing the async operation</returns>
        Task RemoveAsync(string key);

        /// <summary>
        /// Removes multiple values from cache
        /// </summary>
        /// <param name="keys">The cache keys</param>
        /// <returns>Task representing the async operation</returns>
        Task RemoveAsync(IEnumerable<string> keys);

        /// <summary>
        /// Checks if a key exists in cache
        /// </summary>
        /// <param name="key">The cache key</param>
        /// <returns>True if the key exists</returns>
        Task<bool> ExistsAsync(string key);

        /// <summary>
        /// Gets or sets a value in cache
        /// </summary>
        /// <typeparam name="T">The type of the value</typeparam>
        /// <param name="key">The cache key</param>
        /// <param name="factory">The factory to create the value if not cached</param>
        /// <param name="expiration">Optional expiration time</param>
        /// <returns>The cached or newly created value</returns>
        Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null);

        /// <summary>
        /// Gets or sets a value in cache with a default value
        /// </summary>
        /// <typeparam name="T">The type of the value</typeparam>
        /// <param name="key">The cache key</param>
        /// <param name="defaultValue">The default value if not cached</param>
        /// <param name="expiration">Optional expiration time</param>
        /// <returns>The cached or default value</returns>
        Task<T> GetOrSetAsync<T>(string key, T defaultValue, TimeSpan? expiration = null);

        /// <summary>
        /// Increments a numeric value in cache
        /// </summary>
        /// <param name="key">The cache key</param>
        /// <param name="increment">The increment value</param>
        /// <returns>The new value</returns>
        Task<long> IncrementAsync(string key, long increment = 1);

        /// <summary>
        /// Decrements a numeric value in cache
        /// </summary>
        /// <param name="key">The cache key</param>
        /// <param name="decrement">The decrement value</param>
        /// <returns>The new value</returns>
        Task<long> DecrementAsync(string key, long decrement = 1);

        /// <summary>
        /// Sets the expiration for a key
        /// </summary>
        /// <param name="key">The cache key</param>
        /// <param name="expiration">The expiration time</param>
        /// <returns>Task representing the async operation</returns>
        Task SetExpirationAsync(string key, TimeSpan expiration);

        /// <summary>
        /// Gets the time to live for a key
        /// </summary>
        /// <param name="key">The cache key</param>
        /// <returns>The time to live or null if not found</returns>
        Task<TimeSpan?> GetTimeToLiveAsync(string key);

        /// <summary>
        /// Clears all cache entries
        /// </summary>
        /// <returns>Task representing the async operation</returns>
        Task ClearAsync();

        /// <summary>
        /// Gets cache statistics
        /// </summary>
        /// <returns>Cache statistics</returns>
        Task<CacheStatistics> GetStatisticsAsync();

        /// <summary>
        /// Gets all keys matching a pattern
        /// </summary>
        /// <param name="pattern">The pattern to match</param>
        /// <returns>Matching keys</returns>
        Task<IEnumerable<string>> GetKeysAsync(string pattern);

        /// <summary>
        /// Gets the size of the cache
        /// </summary>
        /// <returns>The number of items in cache</returns>
        Task<long> GetSizeAsync();
    }
}