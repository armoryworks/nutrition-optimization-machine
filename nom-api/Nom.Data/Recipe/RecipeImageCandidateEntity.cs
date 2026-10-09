using System;

namespace Nom.Data.Recipe
{
    /// <summary>
    /// An openly licensed photo the external image finder (ops/recipe-image-finder.py) proposed
    /// for a published recipe. Candidates that passed every verification check arrive already
    /// attached; borderline ones wait here for an admin. Every row keeps the licence, credit and
    /// the finder's per-check evidence, so a decision can always be audited or reversed.
    /// See docs/architecture/image-sources.md.
    /// </summary>
    public class RecipeImageCandidateEntity : BaseEntity
    {
        public long RecipeId { get; set; }
        public virtual RecipeEntity? Recipe { get; set; }

        /// <summary>See <see cref="RecipeImageCandidateStatus"/>.</summary>
        public string Status { get; set; } = RecipeImageCandidateStatus.Pending;

        /// <summary>Source adapter key: wikimedia, openverse, pexels, pixabay, unsplash.</summary>
        public string SourceKey { get; set; } = string.Empty;
        public string SourceName { get; set; } = string.Empty;

        /// <summary>The source's own id for the photo; unique per recipe and source.</summary>
        public string SourceId { get; set; } = string.Empty;

        public string? Title { get; set; }
        public string? Author { get; set; }
        public string? AuthorUrl { get; set; }

        /// <summary>Licence label shown to readers ("CC BY 4.0").</summary>
        public string License { get; set; } = string.Empty;

        /// <summary>Licence family code (cc0, pd, by, by-nd, by-sa, pexels, pixabay, unsplash).</summary>
        public string LicenseCode { get; set; } = string.Empty;
        public string? LicenseUrl { get; set; }

        /// <summary>The photo's page at its source.</summary>
        public string? LandingUrl { get; set; }

        /// <summary>The remote image URL; for hotlink-only sources this is what the recipe displays.</summary>
        public string? ImageUrl { get; set; }

        /// <summary>True when the source requires hotlinking (Unsplash): nothing is rehosted.</summary>
        public bool Hotlink { get; set; }

        /// <summary>Unsplash download-tracking endpoint, pinged by the finder once the photo is used.</summary>
        public string? DownloadLocation { get; set; }
        public DateTime? DownloadTrackedAt { get; set; }

        /// <summary>Combined triangulation score (0..1) — for sorting the review queue.</summary>
        public decimal? Score { get; set; }

        /// <summary>The finder's per-check evidence (scores, caption, answers, reasons) as JSON.</summary>
        public string? ChecksJson { get; set; }

        public string? Batch { get; set; }

        /// <summary>Rehosted bytes awaiting review: a media-store path, or inline bytes when no store is configured.</summary>
        public string? FilePath { get; set; }
        public byte[]? FileData { get; set; }
        public string? ContentType { get; set; }

        /// <summary>The asset created when the candidate was attached (rehosted sources only).</summary>
        public long? RecipeAssetId { get; set; }

        public long? ReviewedByPersonId { get; set; }
        public DateTime? ReviewedAt { get; set; }
    }

    public static class RecipeImageCandidateStatus
    {
        public const string Pending = "pending";
        public const string Attached = "attached";
        public const string Approved = "approved";
        public const string Rejected = "rejected";
        public const string Superseded = "superseded";
        public const string Removed = "removed";
    }
}
