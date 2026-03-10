using System;
using System.Collections.Generic;

namespace Nom.Orch.Services.Measurement
{
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
