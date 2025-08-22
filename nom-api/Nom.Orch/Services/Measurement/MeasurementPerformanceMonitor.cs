using Microsoft.Extensions.Logging;

namespace Nom.Orch.Services.Measurement
{
    /// <summary>
    /// Performance monitoring service for the measurement system
    /// </summary>
    public class MeasurementPerformanceMonitor : IMeasurementPerformanceMonitor
    {
        private readonly ILogger<MeasurementPerformanceMonitor> _logger;
        private readonly object _lock = new object();
        private readonly MeasurementPerformanceStats _stats = new();

        public MeasurementPerformanceMonitor(ILogger<MeasurementPerformanceMonitor> logger)
        {
            _logger = logger;
        }

        public void RecordConversionTime(long fromId, long toId, TimeSpan duration, bool fromCache)
        {
            lock (_lock)
            {
                _stats.TotalConversions++;
                _stats.AverageConversionTime = TimeSpan.FromTicks(
                    (_stats.AverageConversionTime.Ticks * (_stats.TotalConversions - 1) + duration.Ticks) / _stats.TotalConversions);

                if (fromCache)
                {
                    _stats.CacheHits++;
                }
                else
                {
                    _stats.CacheMisses++;
                }

                _logger.LogDebug("Conversion from {FromId} to {ToId} took {Duration} (Cache: {FromCache})", 
                    fromId, toId, duration, fromCache);
            }
        }

        public void RecordCacheHit(long fromId, long toId, bool isHit)
        {
            lock (_lock)
            {
                if (isHit)
                {
                    _stats.CacheHits++;
                }
                else
                {
                    _stats.CacheMisses++;
                }

                _logger.LogDebug("Cache {Result} for conversion from {FromId} to {ToId}", 
                    isHit ? "HIT" : "MISS", fromId, toId);
            }
        }

        public void RecordQueryTime(string operation, TimeSpan duration)
        {
            lock (_lock)
            {
                if (!_stats.OperationCounts.ContainsKey(operation))
                {
                    _stats.OperationCounts[operation] = 0;
                    _stats.OperationTimes[operation] = TimeSpan.Zero;
                }

                _stats.OperationCounts[operation]++;
                _stats.OperationTimes[operation] = TimeSpan.FromTicks(
                    (_stats.OperationTimes[operation].Ticks * (_stats.OperationCounts[operation] - 1) + duration.Ticks) / _stats.OperationCounts[operation]);

                // Calculate average query time only if we have operations
                if (_stats.OperationCounts.Values.Sum() > 0)
                {
                    _stats.AverageQueryTime = TimeSpan.FromTicks(
                        _stats.OperationTimes.Values.Sum(t => t.Ticks) / _stats.OperationCounts.Values.Sum());
                }

                _logger.LogDebug("Database operation '{Operation}' took {Duration}", operation, duration);
            }
        }

        public MeasurementPerformanceStats GetPerformanceStats()
        {
            lock (_lock)
            {
                // Return a copy to prevent external modification
                return new MeasurementPerformanceStats
                {
                    TotalConversions = _stats.TotalConversions,
                    CacheHits = _stats.CacheHits,
                    CacheMisses = _stats.CacheMisses,
                    AverageConversionTime = _stats.AverageConversionTime,
                    AverageQueryTime = _stats.AverageQueryTime,
                    OperationCounts = new Dictionary<string, int>(_stats.OperationCounts),
                    OperationTimes = new Dictionary<string, TimeSpan>(_stats.OperationTimes)
                };
            }
        }

        public void ResetStats()
        {
            lock (_lock)
            {
                _stats.TotalConversions = 0;
                _stats.CacheHits = 0;
                _stats.CacheMisses = 0;
                _stats.AverageConversionTime = TimeSpan.Zero;
                _stats.AverageQueryTime = TimeSpan.Zero;
                _stats.OperationCounts.Clear();
                _stats.OperationTimes.Clear();

                _logger.LogInformation("Measurement performance statistics reset");
            }
        }
    }
}
