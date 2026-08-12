// File: nom-api/Nom.Data/Recipe/ScrapedDocumentEntity.cs

using System;

namespace Nom.Data.Recipe
{
    /// <summary>
    /// The raw structured data (JSON-LD Recipe node) a scraped recipe was built
    /// from, kept for provenance and so enrichment jobs can re-process without
    /// re-fetching the source site.
    /// </summary>
    public class ScrapedDocumentEntity : BaseEntity
    {
        public long RecipeId { get; set; }
        public virtual RecipeEntity? Recipe { get; set; }

        public string SourceUrl { get; set; } = string.Empty;

        /// <summary>The schema.org Recipe node exactly as published.</summary>
        public string RawJsonLd { get; set; } = string.Empty;

        public DateTime FetchedAtUtc { get; set; }
    }
}
