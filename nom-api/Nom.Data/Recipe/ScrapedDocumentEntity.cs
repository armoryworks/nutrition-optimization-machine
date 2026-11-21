// File: nom-api/Nom.Data/Recipe/ScrapedDocumentEntity.cs

using System;

namespace Nom.Data.Recipe
{
    /// <summary>
    /// The source a recipe was built from, kept for provenance and so enrichment jobs can
    /// re-process without re-fetching the source site: the JSON-LD Recipe node of a scraped page,
    /// or the record the staged import or the source attach received.
    /// </summary>
    public class ScrapedDocumentEntity : BaseEntity
    {
        public long RecipeId { get; set; }
        public virtual RecipeEntity? Recipe { get; set; }

        public string SourceUrl { get; set; } = string.Empty;

        /// <summary>The source exactly as stored: for <see cref="ScrapedDocumentFormat.JsonLd"/> the schema.org Recipe node as published, otherwise the record as JSON.</summary>
        public string RawJsonLd { get; set; } = string.Empty;

        public DateTime FetchedAtUtc { get; set; }

        /// <summary>What <see cref="RawJsonLd"/> holds, one of <see cref="ScrapedDocumentFormat"/>.</summary>
        public string Format { get; set; } = ScrapedDocumentFormat.JsonLd;

        /// <summary>Lowercase hex SHA-256 of <see cref="RawJsonLd"/> as UTF-8; null on documents stored before it was kept.</summary>
        public string? ContentSha256 { get; set; }

        /// <summary>1 for the first document of its format on the recipe; a newer source of the same format takes the next number.</summary>
        public int Version { get; set; } = 1;
    }

    /// <summary>The formats a <see cref="ScrapedDocumentEntity"/> is stored in.</summary>
    public static class ScrapedDocumentFormat
    {
        /// <summary>The schema.org Recipe node of a scraped page.</summary>
        public const string JsonLd = "jsonld";

        /// <summary>The scraper's parsed record of a recipe with a source URL (web harvest, myplate).</summary>
        public const string ScraperRecipe = "scraper-recipe";

        /// <summary>The parsed record of a recipe with no source URL, extracted from a book.</summary>
        public const string StagedCookbook = "staged-cookbook";

        /// <summary>The journal and step copies of a facts-only recipe's prose that a retention reduced, held (soft-deleted) for its undo until purged.</summary>
        public const string HeldProse = "held-prose";
    }
}
