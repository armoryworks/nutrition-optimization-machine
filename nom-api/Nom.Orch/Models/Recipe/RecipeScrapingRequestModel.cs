using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Recipe
{
    /// <summary>
    /// Model for scraping a recipe from a URL
    /// </summary>
    public class RecipeScrapingRequestModel
    {
        [Required]
        [Url]
        public string Url { get; set; } = string.Empty;

        public bool ImportKeywordsAsTags { get; set; } = false;
        public bool StayInEditMode { get; set; } = false;
    }
} 