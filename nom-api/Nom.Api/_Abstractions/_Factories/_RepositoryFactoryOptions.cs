namespace Nom.Api._Abstractions._Factories
{
    /// <summary>
    /// Configuration options for repository factory
    /// </summary>
    public class _RepositoryFactoryOptions
    {
        /// <summary>
        /// Whether to enable caching for the repository
        /// </summary>
        public bool EnableCaching { get; set; } = true;

        /// <summary>
        /// Cache duration in seconds
        /// </summary>
        public int CacheDurationSeconds { get; set; } = 300;

        /// <summary>
        /// Whether to enable logging for the repository
        /// </summary>
        public bool EnableLogging { get; set; } = true;

        /// <summary>
        /// Whether to enable performance monitoring
        /// </summary>
        public bool EnablePerformanceMonitoring { get; set; } = true;

        /// <summary>
        /// Maximum number of concurrent operations
        /// </summary>
        public int MaxConcurrentOperations { get; set; } = 10;

        /// <summary>
        /// Whether to enable retry logic
        /// </summary>
        public bool EnableRetry { get; set; } = true;

        /// <summary>
        /// Maximum number of retry attempts
        /// </summary>
        public int MaxRetryAttempts { get; set; } = 3;

        /// <summary>
        /// Retry delay in milliseconds
        /// </summary>
        public int RetryDelayMs { get; set; } = 1000;
    }
} 