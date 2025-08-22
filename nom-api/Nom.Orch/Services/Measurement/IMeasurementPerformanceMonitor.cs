namespace Nom.Orch.Services.Measurement
{
    /// <summary>
    /// Interface for monitoring measurement system performance metrics
    /// </summary>
    public interface IMeasurementPerformanceMonitor
    {
        /// <summary>
        /// Records conversion operation timing
        /// </summary>
        void RecordConversionTime(long fromId, long toId, TimeSpan duration, bool fromCache);

        /// <summary>
        /// Records cache hit/miss statistics
        /// </summary>
        void RecordCacheHit(long fromId, long toId, bool isHit);

        /// <summary>
        /// Records database query timing
        /// </summary>
        void RecordQueryTime(string operation, TimeSpan duration);

        /// <summary>
        /// Gets performance statistics
        /// </summary>
        MeasurementPerformanceStats GetPerformanceStats();

        /// <summary>
        /// Resets performance statistics
        /// </summary>
        void ResetStats();
    }

    /// <summary>
    /// Performance statistics for the measurement system
    /// </summary>
    public class MeasurementPerformanceStats
    {
        public int TotalConversions { get; set; }
        public int CacheHits { get; set; }
        public int CacheMisses { get; set; }
        public double CacheHitRate => TotalConversions > 0 ? (double)CacheHits / TotalConversions * 100 : 0;
        public TimeSpan AverageConversionTime { get; set; }
        public TimeSpan AverageQueryTime { get; set; }
        public Dictionary<string, int> OperationCounts { get; set; } = new();
        public Dictionary<string, TimeSpan> OperationTimes { get; set; } = new();
    }
}

