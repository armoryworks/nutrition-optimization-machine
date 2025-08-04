using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Recipe
{
    /// <summary>
    /// Model for bulk recipe scraping
    /// </summary>
    public class RecipeBulkScrapingRequestModel
    {
        [Required]
        public List<RecipeBulkScrapingItemModel> Imports { get; set; } = new();
    }
} 