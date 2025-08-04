using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Recipe
{
    /// <summary>
    /// Individual item for bulk recipe scraping
    /// </summary>
    public class RecipeBulkScrapingItemModel
    {
        [Required]
        [Url]
        public string Url { get; set; } = string.Empty;

        public List<string>? Tags { get; set; }
        public List<string>? Categories { get; set; }
    }
} 