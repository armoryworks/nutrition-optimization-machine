namespace Nom.Orch.Models.Shopping
{
    /// <summary>
    /// Internal model for parsing AI JSON output.
    /// </summary>
    public class AiRetailPackagingSuggestion
    {
        public string IngredientPattern { get; set; } = string.Empty;
        public string PackageName { get; set; } = string.Empty;
        public decimal PackageSize { get; set; }
        public string PackageSizeUnit { get; set; } = string.Empty;
        public string SizeCategory { get; set; } = string.Empty;
    }
}
