using System.Collections.Generic;
using System;
using System.Text.Json.Serialization; // For JsonStringEnumConverter
using System.Collections.Concurrent; // NEW: For ConcurrentDictionary

namespace Nom.Import.Models
{
    /// <summary>
    /// Represents statistics for a specific import type (e.g., FDC_Foods_Import).
    /// </summary>
    public class ImportStats
    {
        /// <summary>
        /// The total number of records discovered in the source CSV file for this import type.
        /// </summary>
        public long TotalDiscovered { get; set; }

        /// <summary>
        /// The total number of records successfully imported into the database for this import type.
        /// </summary>
        public long TotalImported { get; set; }

        /// <summary>
        /// The total number of records skipped during the import process for this import type.
        /// </summary>
        public long TotalSkipped { get; set; }

        /// <summary>
        /// A dictionary containing unique reasons for skipping records and their respective counts.
        /// (e.g., "Empty FdcId": 15, "Ingredient not found": 200).
        /// Changed to ConcurrentDictionary for thread-safe updates.
        /// </summary>
        public ConcurrentDictionary<string, long> SkippedReasons { get; set; } = new ConcurrentDictionary<string, long>();
    }

    /// <summary>
    /// Represents the comprehensive report of an entire data import process.
    /// </summary>
    public class ImportReport
    {
        /// <summary>
        /// The UTC timestamp when this report was generated.
        /// </summary>
        public DateTime ReportGeneratedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// A dictionary containing detailed statistics for each type of import stage.
        /// Key: Stage Name (e.g., "FDC_Foods_Import", "FDC_Nutrients_Import").
        /// Value: ImportStats object for that stage.
        /// </summary>
        public Dictionary<string, ImportStats> TypeStats { get; set; } = new Dictionary<string, ImportStats>();

        /// <summary>
        /// A list of unique warning messages encountered during the import process.
        /// </summary>
        public List<string> UniqueWarnings { get; set; } = new List<string>();

        /// <summary>
        /// A list of unique error messages encountered during the import process.
        /// These are typically non-fatal errors that might affect a batch but not stop the entire process immediately.
        /// </summary>
        public List<string> UniqueErrors { get; set; } = new List<string>();

        /// <summary>
        /// A list of fatal/critical error messages that caused the overall import process to stop prematurely.
        /// </summary>
        public List<string> FatalErrors { get; set; } = new List<string>();
    }
}
