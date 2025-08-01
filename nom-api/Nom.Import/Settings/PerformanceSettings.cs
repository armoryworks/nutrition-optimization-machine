// File: nom-api/Nom.Import/Settings/PerformanceSettings.cs

using System;

namespace Nom.Import.Settings
{
    /// <summary>
    /// Settings for performance and batch processing.
    /// </summary>
    public class PerformanceSettings
    {
        /// <summary>
        /// Number of records to process in each batch.
        /// </summary>
        public int BatchSize { get; set; } = 10000;

        /// <summary>
        /// Whether to use parallel processing.
        /// </summary>
        public bool UseParallelProcessing { get; set; } = true;

        /// <summary>
        /// Maximum degree of parallelism for parallel operations.
        /// </summary>
        public int MaxDegreeOfParallelism { get; set; } = Environment.ProcessorCount;

        /// <summary>
        /// Whether to create indexes after import.
        /// </summary>
        public bool CreateIndexesAfterImport { get; set; } = true;

        /// <summary>
        /// Whether to create materialized views.
        /// </summary>
        public bool CreateMaterializedViews { get; set; } = true;
    }
} 