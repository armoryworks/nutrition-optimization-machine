using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Recipe
{
    /// <summary>
    /// Model for scraping recipe from HTML or JSON data
    /// </summary>
    public class RecipeScrapingDataRequestModel
    {
        [Required]
        public string Data { get; set; } = string.Empty;

        public bool ImportKeywordsAsTags { get; set; } = false;
        public bool StayInEditMode { get; set; } = false;
    }
} 