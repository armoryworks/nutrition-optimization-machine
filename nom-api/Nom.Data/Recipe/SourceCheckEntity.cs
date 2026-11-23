using System;

namespace Nom.Data.Recipe
{
    /// <summary>
    /// One source-check job of the scraper (POST /api/source-check/jobs) for a candidate or an
    /// approved source: the report it returned, stored whole as the evidence an approval rests on,
    /// and the single verdict NOM drew from it (<c>SourceAdmissionPolicy</c>). A job that was lost,
    /// failed or never started is stored too, with no verdict.
    /// </summary>
    public class SourceCheckEntity : BaseEntity
    {
        public const string Running = "running";
        public const string Completed = "completed";
        public const string Partial = "partial";
        public const string Failed = "failed";
        public const string Lost = "lost";

        public string Domain { get; set; } = string.Empty;

        /// <summary><c>candidate</c>, <c>recheck</c> (an approved source), <c>request</c> (a pending request) or <c>terms-only</c> (a terms-only check an admin started).</summary>
        public string Purpose { get; set; } = string.Empty;

        public long? SourceCandidateId { get; set; }

        public long? ScrapingSourceId { get; set; }

        /// <summary>
        /// The kind of check asked of the scraper: <c>terms-only</c> (sampleSize 0: robots.txt, the homepage and the terms pages,
        /// no recipe page) or <c>full</c> (20 sample recipe pages). Null on a row stored before the column: read from its purpose and report.
        /// </summary>
        public string? Mode { get; set; }

        /// <summary>The scraper's job id.</summary>
        public string? JobId { get; set; }

        /// <summary><c>queued</c> (a terms-only check waiting for the source lane), <c>running</c>, <c>completed</c>, <c>partial</c>, <c>failed</c> or <c>lost</c> (the job id read 404, or no answer by its deadline).</summary>
        public string Status { get; set; } = Running;

        public string? StopReason { get; set; }

        public string? Error { get; set; }

        public DateTime StartedUtc { get; set; }

        public DateTime? FinishedUtc { get; set; }

        /// <summary>The scraper's own verdict, as reported.</summary>
        public string? ScraperVerdict { get; set; }

        /// <summary>The scraper's rule version (<c>source-check/5</c>; earlier reports may hold <c>source-check/4</c> or <c>source-check/2</c>).</summary>
        public string? RuleVersion { get; set; }

        /// <summary>NOM's single verdict: <c>clear</c> or <c>not-clear</c>; null when no report came back.</summary>
        public string? Verdict { get; set; }

        /// <summary>Whether the verdict rests on a complete check under a rule version NOM knows; an inconclusive not-clear changes nothing but the display.</summary>
        public bool Conclusive { get; set; }

        /// <summary>NOM's policy version that drew the verdict.</summary>
        public string? PolicyVersion { get; set; }

        /// <summary>Every reason with its outcome and detail, the scraper's and NOM's own.</summary>
        public string? ReasonsJson { get; set; }

        /// <summary>The scraper's report, whole.</summary>
        public string? ReportJson { get; set; }
    }
}
