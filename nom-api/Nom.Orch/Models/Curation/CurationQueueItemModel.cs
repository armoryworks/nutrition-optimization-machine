using System;

namespace Nom.Orch.Models.Curation
{
    public class CurationQueueItemModel
    {
        public long Id { get; set; }
        public string EntityType { get; set; } = string.Empty; // "Recipe" or "Ingredient"
        public string Name { get; set; } = string.Empty;
        public string AuthorName { get; set; } = string.Empty;
        public DateTime DateSubmitted { get; set; }
        public string? Description { get; set; }
        public string? SourceUrl { get; set; }
        public long AuthorId { get; set; }

        /// <summary>Curation status name ("PendingCuration" or "RequiresRevision").</summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>Plausibility problems recorded by import vetting, newline-separated.</summary>
        public string? VettingIssues { get; set; }

        /// <summary>True while the recipe still contains the source's verbatim prose.</summary>
        public bool ContainsSourceProse { get; set; }

        /// <summary>Source hero image, for side-by-side review only — never published.</summary>
        public string? SourceImageUrl { get; set; }
    }
} 