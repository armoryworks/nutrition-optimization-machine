using System;

namespace Nom.Data.Recipe
{
    /// <summary>
    /// One discovery run through the scraper's budgeted job (POST /api/discover/jobs). A run whose
    /// job was lost, failed or never answered is stored as failed with what it had found so far, so
    /// a broken lane never reads as a lane with nothing to find.
    /// </summary>
    public class SourceDiscoveryRunEntity : BaseEntity
    {
        public const string Running = "running";
        public const string Completed = "completed";
        public const string Partial = "partial";
        public const string Failed = "failed";
        public const string Skipped = "skipped";

        public DateTime StartedUtc { get; set; }

        public DateTime? FinishedUtc { get; set; }

        public string? JobId { get; set; }

        /// <summary>The seeds sent, in order, comma-separated.</summary>
        public string Seeds { get; set; } = string.Empty;

        /// <summary><c>running</c>, <c>completed</c>, <c>partial</c>, <c>failed</c> or <c>skipped</c> (another discovery job was running).</summary>
        public string Status { get; set; } = Running;

        public string? StopReason { get; set; }

        public string? Error { get; set; }

        public int Candidates { get; set; }

        public int Leads { get; set; }

        public string? SeedsVisited { get; set; }

        public string? SeedsNotVisited { get; set; }

        /// <summary>Seeds the scraper refused as search engines or platforms; shown to an admin.</summary>
        public string? SeedsRefused { get; set; }

        public int PagesFetched { get; set; }
    }
}
