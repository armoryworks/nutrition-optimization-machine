using System;

namespace Nom.Data.Recipe
{
    /// <summary>
    /// A domain discovery found (or an admin request named) that is not yet a scraping source. It
    /// holds the facts of its last source check and the single verdict NOM drew from them; only a
    /// candidate whose verdict is clear is offered for approval, and nothing is ever fetched from it
    /// beyond the scraper's check until an admin approves it.
    /// </summary>
    public class SourceCandidateEntity : BaseEntity
    {
        public const string Lead = "lead";
        public const string New = "new";
        public const string Clear = "clear";
        public const string NotClear = "not-clear";
        public const string Approved = "approved";
        public const string Rejected = "rejected";

        /// <summary>Host name, lowercased, without a leading "www.".</summary>
        public string Domain { get; set; } = string.Empty;

        /// <summary>The page that carried the recipe signal; null for a lead.</summary>
        public string? EvidenceUrl { get; set; }

        /// <summary>The seed it was found from, or "request" for an admin request.</summary>
        public string? FoundVia { get; set; }

        /// <summary><c>RecipeJsonLd</c> or <c>RecipeLinks</c>; null for a lead (found but never probed), which is never checked.</summary>
        public string? Signal { get; set; }

        public DateTime FirstSeenUtc { get; set; }

        public DateTime LastSeenUtc { get; set; }

        public int TimesSeen { get; set; }

        /// <summary><c>lead</c>, <c>new</c>, <c>clear</c>, <c>not-clear</c>, <c>approved</c> or <c>rejected</c>.</summary>
        public string State { get; set; } = New;

        /// <summary>The last source check of this domain (recipe.SourceCheck).</summary>
        public long? LastCheckId { get; set; }

        /// <summary>The scraping source an approval created or updated.</summary>
        public long? ScrapingSourceId { get; set; }
    }
}
