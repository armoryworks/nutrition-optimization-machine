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

        /// <summary>Hosts sent in the job's excludeDomains: hosts NOM holds as forbidden, permission-needed, rejected or unclassed, which neither pass fetches.</summary>
        public int ExcludeSent { get; set; }

        /// <summary>Held hosts that were not sent because the list would pass the scraper's 5,000 entries (never a forbidden or permission-needed host); they take their turn in later runs.</summary>
        public int ExcludeLeftOut { get; set; }

        /// <summary>When NOM first held the last rejected or unclassed host this run sent while hosts were left out; the next such run goes on from the host after it.</summary>
        public DateTime? ExcludeCursorSince { get; set; }

        /// <summary>The last rejected or unclassed host this run sent while hosts were left out; the next such run goes on from the host after it.</summary>
        public string? ExcludeCursorHost { get; set; }

        /// <summary>Seeds NOM excludes as well: held back before the job, or reported back by the scraper as excluded; a fault on NOM's side, shown to an admin and never rotated.</summary>
        public string? SeedsExcluded { get; set; }

        /// <summary>How many excluded hosts the seeds link to (nothing was requested from them); for the admin's view only, never a candidate.</summary>
        public int CandidatesExcluded { get; set; }

        /// <summary>The scraper's candidatesExcluded as it came back (domain, the seed that linked it, the list entry that excluded it).</summary>
        public string? CandidatesExcludedJson { get; set; }

        /// <summary>What the exclusion list did not cover as it should: held hosts left out over the limit, seeds held back, excluded hosts the scraper probed anyway.</summary>
        public string? ExcludeNote { get; set; }

        /// <summary>How many sites refused the run (the scraper's refusingHosts); each is held from later runs until an admin looks.</summary>
        public int RefusingHosts { get; set; }

        /// <summary>The scraper's refusingHosts as it came back (domain, the refusing URL, its HTTP status, whether it served a bot challenge).</summary>
        public string? RefusingHostsJson { get; set; }
    }
}
