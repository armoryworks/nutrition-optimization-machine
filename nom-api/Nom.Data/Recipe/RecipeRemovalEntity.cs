using System;

namespace Nom.Data.Recipe
{
    /// <summary>
    /// The journal row of a recipe removed because it was taken from a source whose effective rights class is forbidden.
    /// Stage 1 (retire) takes the recipe, its lines, steps and stored documents off every surface by a soft delete stamped
    /// <see cref="RetiredAtUtc"/>, which its run's undo puts back. Stage 2 (purge) deletes the source's wording and copies for
    /// good; what stays is this row: the recipe id, the source, the address and the title, so the address is never taken again.
    /// </summary>
    public class RecipeRemovalEntity : BaseEntity
    {
        public long RecipeId { get; set; }

        /// <summary>The run (<see cref="RecipeRemovalRunEntity"/>) that retired the recipe.</summary>
        public long RunId { get; set; }

        public long ScrapingSourceId { get; set; }

        /// <summary>The source's host, lowercased, without a leading "www.".</summary>
        public string Host { get; set; } = string.Empty;

        /// <summary>The recipe's source address as it was stored.</summary>
        public string SourceUrl { get; set; } = string.Empty;

        /// <summary>The address as URL import and harvest compare it: host without "www.", path without a trailing slash, no query or fragment.</summary>
        public string UrlKey { get; set; } = string.Empty;

        /// <summary>The recipe's title when it was retired.</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>The source's effective rights class when the recipe was retired (<c>forbidden</c>).</summary>
        public string RightsClass { get; set; } = string.Empty;

        /// <summary>The recipe was already soft-deleted before it was retired; the undo leaves it deleted.</summary>
        public bool WasDeleted { get; set; }

        public DateTime RetiredAtUtc { get; set; }

        public long RetiredByPersonId { get; set; }

        public int LinesRetired { get; set; }

        public int StepsRetired { get; set; }

        public int DocumentsRetired { get; set; }

        /// <summary>While set, the retirement was undone and the recipe is back where it was.</summary>
        public DateTime? UndoneAtUtc { get; set; }

        public long? UndoneByPersonId { get; set; }

        /// <summary>While set, the source's wording and copies were deleted for good; the retirement can no longer be undone.</summary>
        public DateTime? PurgedAtUtc { get; set; }

        public long? PurgedByPersonId { get; set; }

        public long? PurgeRunId { get; set; }

        /// <summary>What the purge deleted or cleared, by table and column, with row counts (JSON).</summary>
        public string? PurgeSummary { get; set; }
    }
}
