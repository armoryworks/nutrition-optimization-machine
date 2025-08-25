using System.Collections.Generic;

namespace Nom.Orch.UtilityInterfaces
{
    /// <summary>
    /// Scraped recipe data structure
    /// </summary>
    public class ScrapedRecipeData
    {
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public List<string> Ingredients { get; set; } = new List<string>();
        public List<string> Instructions { get; set; } = new List<string>();
        public string PrepTime { get; set; } = string.Empty;
        public string CookTime { get; set; } = string.Empty;
        public string TotalTime { get; set; } = string.Empty;
        public string Yield { get; set; } = string.Empty;
        public string SourceUrl { get; set; } = string.Empty;
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
    }
}



