// Nom.Orch/Models/Audit/ImportJobStatusResponse.cs
using System;
using Nom.Data.Audit; // For ImportStatusEnum

namespace Nom.Orch.Models.Audit // New namespace for orchestration-level audit models
{
    /// <summary>
    /// Represents the detailed status of an asynchronous import job,
    /// suitable for returning via an API endpoint.
    /// </summary>
    public class ImportJobStatusResponse
    {
        /// <summary>
        /// The unique identifier of the import process.
        /// </summary>
        public Guid ProcessId { get; set; }

        /// <summary>
        /// The name of the import job (e.g., "Kaggle Recipe Import").
        /// </summary>
        public string JobName { get; set; } = string.Empty;

        /// <summary>
        /// The current status of the import job (e.g., Queued, Running, Completed, Failed).
        /// </summary>
        public ImportStatusEnum Status { get; set; }

        /// <summary>
        /// A descriptive message providing more details about the job's current state or outcome.
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// The total number of records expected or processed (can be null if unknown).
        /// </summary>
        public int? TotalRecords { get; set; }

        /// <summary>
        /// The number of records successfully imported so far.
        /// </summary>
        public int ImportedCount { get; set; }

        /// <summary>
        /// The number of records skipped due to issues (e.g., duplicates, validation errors).
        /// </summary>
        public int SkippedCount { get; set; }

        /// <summary>
        /// The number of records that caused an error during processing.
        /// </summary>
        public int ErrorCount { get; set; }

        /// <summary>
        /// The UTC timestamp when the job was first created/queued.
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// The UTC timestamp when the job started processing.
        /// </summary>
        public DateTime? StartedAt { get; set; }

        /// <summary>
        /// The UTC timestamp when the job completed (successfully, failed, or canceled).
        /// </summary>
        public DateTime? CompletedAt { get; set; }

        /// <summary>
        /// The ID of the person who initiated or created this job.
        /// </summary>
        public long CreatedByPersonId { get; set; }
    }
}
