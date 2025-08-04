// File: Nom.Api/_Abstractions/_Core/ICacheService.cs

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nom.Api.Core
{

    /// <summary>
    /// Cache statistics
    /// </summary>
    public class CacheStatistics
    {
        /// <summary>
        /// Gets the total number of cache hits
        /// </summary>
        public long TotalHits { get; set; }

        /// <summary>
        /// Gets the total number of cache misses
        /// </summary>
        public long TotalMisses { get; set; }

        /// <summary>
        /// Gets the hit rate (hits / (hits + misses))
        /// </summary>
        public double HitRate => TotalHits + TotalMisses > 0 ? (double)TotalHits / (TotalHits + TotalMisses) : 0;

        /// <summary>
        /// Gets the total number of cache entries
        /// </summary>
        public long TotalEntries { get; set; }

        /// <summary>
        /// Gets the total memory usage in bytes
        /// </summary>
        public long TotalMemoryUsage { get; set; }

        /// <summary>
        /// Gets the average time to retrieve items in milliseconds
        /// </summary>
        public double AverageRetrievalTimeMs { get; set; }

        /// <summary>
        /// Gets the last cache access time
        /// </summary>
        public DateTime? LastAccessTime { get; set; }

        /// <summary>
        /// Gets the cache eviction count
        /// </summary>
        public long EvictionCount { get; set; }

        /// <summary>
        /// Gets the cache expiration count
        /// </summary>
        public long ExpirationCount { get; set; }
    }
}