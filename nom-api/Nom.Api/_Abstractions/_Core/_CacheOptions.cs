// File: Nom.Api/_Abstractions/_Core/_CacheOptions.cs

using System;

namespace Nom.Api._Abstractions._Core
{
    /// <summary>
    /// Configuration options for cache service
    /// </summary>
    public class _CacheOptions
    {
        /// <summary>
        /// Gets or sets the default expiration time for cache entries
        /// </summary>
        public TimeSpan DefaultExpiration { get; set; } = TimeSpan.FromMinutes(30);

        /// <summary>
        /// Gets or sets the maximum number of cache entries
        /// </summary>
        public int MaxEntries { get; set; } = 10000;

        /// <summary>
        /// Gets or sets the maximum memory usage in bytes
        /// </summary>
        public long MaxMemoryUsage { get; set; } = 100 * 1024 * 1024; // 100MB

        /// <summary>
        /// Gets or sets whether to enable cache statistics
        /// </summary>
        public bool EnableStatistics { get; set; } = true;

        /// <summary>
        /// Gets or sets whether to enable cache logging
        /// </summary>
        public bool EnableLogging { get; set; } = true;

        /// <summary>
        /// Gets or sets the cleanup interval for expired entries
        /// </summary>
        public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Gets or sets whether to enable compression
        /// </summary>
        public bool EnableCompression { get; set; } = false;

        /// <summary>
        /// Gets or sets the compression threshold in bytes
        /// </summary>
        public int CompressionThreshold { get; set; } = 1024; // 1KB

        /// <summary>
        /// Gets or sets whether to enable cache warming
        /// </summary>
        public bool EnableCacheWarming { get; set; } = false;

        /// <summary>
        /// Gets or sets the cache warming interval
        /// </summary>
        public TimeSpan CacheWarmingInterval { get; set; } = TimeSpan.FromHours(1);

        /// <summary>
        /// Gets or sets whether to enable cache persistence
        /// </summary>
        public bool EnablePersistence { get; set; } = false;

        /// <summary>
        /// Gets or sets the persistence file path
        /// </summary>
        public string PersistenceFilePath { get; set; } = "cache.dat";

        /// <summary>
        /// Gets or sets the persistence interval
        /// </summary>
        public TimeSpan PersistenceInterval { get; set; } = TimeSpan.FromMinutes(10);

        /// <summary>
        /// Gets or sets whether to enable cache eviction
        /// </summary>
        public bool EnableEviction { get; set; } = true;

        /// <summary>
        /// Gets or sets the eviction policy
        /// </summary>
        public _CacheEvictionPolicy EvictionPolicy { get; set; } = _CacheEvictionPolicy.LeastRecentlyUsed;

        /// <summary>
        /// Gets or sets the eviction threshold percentage
        /// </summary>
        public double EvictionThreshold { get; set; } = 0.8; // 80%

        /// <summary>
        /// Gets or sets whether to enable cache partitioning
        /// </summary>
        public bool EnablePartitioning { get; set; } = false;

        /// <summary>
        /// Gets or sets the number of cache partitions
        /// </summary>
        public int PartitionCount { get; set; } = 4;

        /// <summary>
        /// Gets or sets whether to enable cache monitoring
        /// </summary>
        public bool EnableMonitoring { get; set; } = true;

        /// <summary>
        /// Gets or sets the monitoring interval
        /// </summary>
        public TimeSpan MonitoringInterval { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>
        /// Gets or sets whether to enable cache health checks
        /// </summary>
        public bool EnableHealthChecks { get; set; } = true;

        /// <summary>
        /// Gets or sets the health check interval
        /// </summary>
        public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Gets or sets the cache key prefix
        /// </summary>
        public string KeyPrefix { get; set; } = "nom_cache_";

        /// <summary>
        /// Gets or sets whether to enable cache key validation
        /// </summary>
        public bool EnableKeyValidation { get; set; } = true;

        /// <summary>
        /// Gets or sets the maximum key length
        /// </summary>
        public int MaxKeyLength { get; set; } = 250;

        /// <summary>
        /// Gets or sets whether to enable cache value validation
        /// </summary>
        public bool EnableValueValidation { get; set; } = true;

        /// <summary>
        /// Gets or sets the maximum value size in bytes
        /// </summary>
        public int MaxValueSize { get; set; } = 1024 * 1024; // 1MB

        /// <summary>
        /// Gets or sets whether to enable cache serialization validation
        /// </summary>
        public bool EnableSerializationValidation { get; set; } = true;

        /// <summary>
        /// Gets or sets the serialization timeout in milliseconds
        /// </summary>
        public int SerializationTimeoutMs { get; set; } = 5000;

        /// <summary>
        /// Gets or sets whether to enable cache concurrency control
        /// </summary>
        public bool EnableConcurrencyControl { get; set; } = true;

        /// <summary>
        /// Gets or sets the maximum concurrent operations
        /// </summary>
        public int MaxConcurrentOperations { get; set; } = 100;

        /// <summary>
        /// Gets or sets the operation timeout in milliseconds
        /// </summary>
        public int OperationTimeoutMs { get; set; } = 10000;

        /// <summary>
        /// Gets or sets whether to enable cache retry logic
        /// </summary>
        public bool EnableRetryLogic { get; set; } = true;

        /// <summary>
        /// Gets or sets the maximum retry attempts
        /// </summary>
        public int MaxRetryAttempts { get; set; } = 3;

        /// <summary>
        /// Gets or sets the retry delay in milliseconds
        /// </summary>
        public int RetryDelayMs { get; set; } = 100;

        /// <summary>
        /// Gets or sets whether to enable cache circuit breaker
        /// </summary>
        public bool EnableCircuitBreaker { get; set; } = true;

        /// <summary>
        /// Gets or sets the circuit breaker failure threshold
        /// </summary>
        public int CircuitBreakerFailureThreshold { get; set; } = 5;

        /// <summary>
        /// Gets or sets the circuit breaker reset timeout in milliseconds
        /// </summary>
        public int CircuitBreakerResetTimeoutMs { get; set; } = 60000; // 1 minute
    }

    /// <summary>
    /// Cache eviction policy
    /// </summary>
    public enum _CacheEvictionPolicy
    {
        /// <summary>
        /// Least recently used
        /// </summary>
        LeastRecentlyUsed,

        /// <summary>
        /// Least frequently used
        /// </summary>
        LeastFrequentlyUsed,

        /// <summary>
        /// First in, first out
        /// </summary>
        FirstInFirstOut,

        /// <summary>
        /// Random
        /// </summary>
        Random,

        /// <summary>
        /// Time-based
        /// </summary>
        TimeBased
    }
} 