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
        public string? Instructions { get; set; } // For recipes
        public string? RawIngredientsString { get; set; } // For recipes
        public string? SourceUrl { get; set; }
        public long AuthorId { get; set; }
    }
} 