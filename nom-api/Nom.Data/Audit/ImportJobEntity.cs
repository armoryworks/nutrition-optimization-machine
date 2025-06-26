// Nom.Data/Audit/ImportJobEntity.cs
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Nom.Data.Audit
{
    /// <summary>
    /// Represents a single, long-running data import job in the system.
    /// This entity tracks the status, progress, and outcomes of import operations.
    /// It implements IAuditableEntity to leverage the automatic auditing in ApplicationDbContext.
    /// </summary>
    [Table("ImportJob", Schema = "audit")]
    public class ImportJobEntity : BaseEntity // Assuming BaseEntity provides Id, CreatedDate, CreatedByPersonId, LastModifiedDate, LastModifiedByPersonId
    {
        /// <summary>
        /// A unique identifier for this specific import process, often used to track it
        /// across distributed systems or for API status queries.
        /// </summary>
        [Required]
        public Guid ProcessId { get; set; }

        /// <summary>
        /// A descriptive name for the import job (e.g., "Kaggle Recipe Import").
        /// </summary>
        [Required]
        [MaxLength(255)]
        public string JobName { get; set; } = string.Empty;

        /// <summary>
        /// The source from which the data is being imported (e.g., "Kaggle CSV", "External API").
        /// </summary>
        [MaxLength(255)]
        public string? Source { get; set; }

        /// <summary>
        /// The file path or endpoint URL of the data source.
        /// </summary>
        [MaxLength(1024)]
        public string? SourcePath { get; set; }

        /// <summary>
        /// The current status of the import job.
        /// </summary>
        [Required]
        public ImportStatusEnum Status { get; set; } = ImportStatusEnum.Queued;

        /// <summary>
        /// The total number of records/items expected or processed by the job.
        /// Can be null if the total is unknown.
        /// </summary>
        public int? TotalRecords { get; set; }

        /// <summary>
        /// The number of records/items successfully imported.
        /// </summary>
        public int ImportedCount { get; set; } = 0;

        /// <summary>
        /// The number of records/items skipped due to validation errors, duplicates, etc.
        /// </summary>
        public int SkippedCount { get; set; } = 0;

        /// <summary>
        /// The number of records/items that caused an unrecoverable error during processing.
        /// </summary>
        public int ErrorCount { get; set; } = 0;

        /// <summary>
        /// The timestamp when the job started processing.
        /// </summary>
        public DateTime? StartedAt { get; set; }

        /// <summary>
        /// The timestamp when the job completed (successfully, failed, or canceled).
        /// </summary>
        public DateTime? CompletedAt { get; set; }

        /// <summary>
        /// A summary message or last error message for the job.
        /// </summary>
        [MaxLength(2047)]
        public string? Message { get; set; }
    }
}
