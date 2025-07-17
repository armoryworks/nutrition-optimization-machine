using Nom.Import.Models;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.IO;
using Microsoft.Extensions.Logging;
using System.Linq;
using System;

namespace Nom.Import.Data.Shared
{
    /// <summary>
    /// A thread-safe service for collecting and generating a comprehensive report
    /// of the data import process, including statistics, warnings, and errors.
    /// </summary>
    public class ImportReportGenerator
    {
        private readonly ConcurrentDictionary<string, ImportStats> _typeStats = new ConcurrentDictionary<string, ImportStats>();
        private readonly ConcurrentBag<string> _uniqueWarnings = new ConcurrentBag<string>();
        private readonly ConcurrentBag<string> _uniqueErrors = new ConcurrentBag<string>();
        private readonly ConcurrentBag<string> _fatalErrors = new ConcurrentBag<string>();
        private readonly ILogger<ImportReportGenerator> _logger;

        public ImportReportGenerator(ILogger<ImportReportGenerator> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Gets or creates the ImportStats object for a given import type in a thread-safe manner.
        /// </summary>
        /// <param name="importType">The name of the import stage (e.g., "FDC_Foods_Import").</param>
        /// <returns>The ImportStats object for the specified type.</returns>
        private ImportStats GetOrCreateStats(string importType)
        {
            return _typeStats.GetOrAdd(importType, _ => new ImportStats());
        }

        /// <summary>
        /// Records the total number of records discovered for a specific import type.
        /// </summary>
        /// <param name="importType">The name of the import stage.</param>
        /// <param name="count">The number of records discovered.</param>
        public void RecordDiscovered(string importType, long count)
        {
            GetOrCreateStats(importType).TotalDiscovered += count;
        }

        /// <summary>
        /// Records the number of records successfully imported for a specific import type.
        /// </summary>
        /// <param name="importType">The name of the import stage.</param>
        /// <param name="count">The number of records imported.</param>
        public void RecordImported(string importType, long count)
        {
            GetOrCreateStats(importType).TotalImported += count;
        }

        /// <summary>
        /// Records a skipped record for a specific import type and the reason for skipping.
        /// The reason is used to aggregate counts of similar skipped reasons.
        /// </summary>
        /// <param name="importType">The name of the import stage.</param>
        /// <param name="reason">The reason the record was skipped (e.g., "Empty FdcId").</param>
        public void RecordSkipped(string importType, string reason)
        {
            var stats = GetOrCreateStats(importType);
            stats.TotalSkipped++;
            stats.SkippedReasons.AddOrUpdate(reason, 1, (key, oldValue) => oldValue + 1);
        }

        /// <summary>
        /// Records a unique warning message.
        /// </summary>
        /// <param name="warningMessage">The warning message.</param>
        public void RecordWarning(string warningMessage)
        {
            // ConcurrentBag does not have Contains, so we use ToList() for checking uniqueness
            // This is a trade-off for simplicity; for extremely high volume, a ConcurrentHashSet would be better.
            if (!_uniqueWarnings.ToList().Contains(warningMessage))
            {
                _uniqueWarnings.Add(warningMessage);
            }
        }

        /// <summary>
        /// Records a unique error message.
        /// </summary>
        /// <param name="errorMessage">The error message.</param>
        public void RecordError(string errorMessage)
        {
            if (!_uniqueErrors.ToList().Contains(errorMessage))
            {
                _uniqueErrors.Add(errorMessage);
            }
        }

        /// <summary>
        /// Records a fatal error message that caused the import process to terminate.
        /// </summary>
        /// <param name="errorMessage">The fatal error message.</param>
        public void RecordFatalError(string errorMessage)
        {
            _fatalErrors.Add(errorMessage);
        }

        /// <summary>
        /// Generates the final import report and saves it to a JSON file.
        /// </summary>
        /// <param name="filePath">The full path where the report JSON file will be saved.</param>
        public async Task GenerateReportFileAsync(string filePath)
        {
            var report = new ImportReport
            {
                ReportGeneratedUtc = DateTime.UtcNow,
                TypeStats = new Dictionary<string, ImportStats>(_typeStats),
                UniqueWarnings = _uniqueWarnings.ToList(),
                UniqueErrors = _uniqueErrors.ToList(),
                FatalErrors = _fatalErrors.ToList()
            };

            var options = new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
            var jsonString = JsonSerializer.Serialize(report, options);

            try
            {
                await File.WriteAllTextAsync(filePath, jsonString);
                _logger.LogInformation("Import report generated successfully at: {FilePath}", filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write import report to file: {FilePath}", filePath);
            }
        }
    }
}
