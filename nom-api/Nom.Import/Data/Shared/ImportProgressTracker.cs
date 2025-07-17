using System.Collections.Concurrent;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Nom.Import.Models;

namespace Nom.Import.Data.Shared
{
    /// <summary>
    /// Tracks the progress of various import stages and reports statistics to the ImportReportGenerator.
    /// This class is thread-safe.
    /// </summary>
    public class ImportProgressTracker
    {
        private readonly ConcurrentDictionary<string, long> _processedCounts = new ConcurrentDictionary<string, long>();
        private readonly ConcurrentDictionary<string, long> _totalCounts = new ConcurrentDictionary<string, long>();
        private readonly ILogger<ImportProgressTracker> _logger;
        private readonly ImportReportGenerator _reportGenerator; // Inject the report generator

        public ImportProgressTracker(ILogger<ImportProgressTracker> logger, ImportReportGenerator reportGenerator)
        {
            _logger = logger;
            _reportGenerator = reportGenerator; // Assign the injected generator
        }

        /// <summary>
        /// Gets the last recorded processed offset for a given stage.
        /// </summary>
        /// <param name="stageName">The name of the import stage.</param>
        /// <returns>The last processed record count for the stage.</returns>
        public long GetLastProcessedOffset(string stageName)
        {
            return _processedCounts.GetOrAdd(stageName, 0);
        }

        /// <summary>
        /// Sets the total number of records expected for a stage and records it as 'discovered'.
        /// </summary>
        /// <param name="stageName">The name of the import stage.</param>
        /// <param name="total">The total number of records to be processed.</param>
        public void SetTotalRecords(string stageName, long total)
        {
            _totalCounts.AddOrUpdate(stageName, total, (key, oldValue) => total);
            _reportGenerator.RecordDiscovered(stageName, total); // Report total discovered to the generator
            _logger.LogInformation("Total records for stage '{StageName}': {Total}", stageName, total);
        }

        /// <summary>
        /// Records the number of records successfully imported for a specific import type.
        /// </summary>
        /// <param name="stageName">The name of the import stage.</param>
        /// <param name="count">The number of records imported in this increment.</param>
        public void RecordImported(string stageName, long count) // Changed from async Task to void
        {
            _processedCounts.AddOrUpdate(stageName, count, (key, oldValue) => oldValue + count);
            _reportGenerator.RecordImported(stageName, count); // Report imported count to the generator
        }

        /// <summary>
        /// Records a skipped record with a specific reason to the report generator.
        /// </summary>
        /// <param name="stageName">The name of the import stage.</param>
        /// <param name="reason">The reason for skipping the record.</param>
        public void RecordSkipped(string stageName, string reason)
        {
            _reportGenerator.RecordSkipped(stageName, reason);
        }

        /// <summary>
        /// Updates the final processed count for a stage.
        /// </summary>
        /// <param name="stageName">The name of the import stage.</param>
        /// <param name="finalCount">The final total number of processed records.</param>
        public async Task UpdateProgressAsync(string stageName, long finalCount)
        {
            _processedCounts.AddOrUpdate(stageName, finalCount, (key, oldValue) => finalCount);
            _logger.LogInformation("Final processed count for stage '{StageName}': {ProcessedCount}", stageName, finalCount);
            await Task.CompletedTask;
        }
    }
}
