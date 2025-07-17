namespace Nom.Import.Models
{
    /// <summary>
    /// Configuration settings for the import process.
    /// </summary>
    public class ImportConfig
    {
        /// <summary>
        /// Base path for FDC CSV files (e.g., "C:\Data\FDC" or "/home/user/data/fdc").
        /// </summary>
        public string FdcCsvBasePath { get; set; } = string.Empty;

        /// <summary>
        /// Base path for Recipe CSV files (e.g., "C:\Data\Recipes" or "/home/user/data/recipes").
        /// </summary>
        public string RecipeCsvBasePath { get; set; } = string.Empty; // NEW PROPERTY

        /// <summary>
        /// The size of batches for bulk operations.
        /// </summary>
        public int BatchSize { get; set; } = 100000;

        /// <summary>
        /// Default limit for records processed in debug mode.
        /// </summary>
        public int DefaultDebugLimit { get; set; } = 10000;

        /// <summary>
        /// The ID of the system person used for audit fields (CreatedByPersonId, LastModifiedByPersonId).
        /// </summary>
        public long SystemPersonId { get; set; } = 1L;

        /// <summary>
        /// The maximum degree of parallelism for processing CSV batches.
        /// Set to 1 for sequential processing.
        /// </summary>
        public int MaxParallelism { get; set; } = 4; // NEW PROPERTY: Default to 4 parallel tasks
    }
}
