namespace Nom.Orch.Models.Recipe
{
    /// <summary>
    /// Scraped step model
    /// </summary>
    public class ScrapedStepModel
    {
        public int? Order { get; set; }
        public string Instruction { get; set; } = string.Empty;
        public string? Image { get; set; }
    }
}