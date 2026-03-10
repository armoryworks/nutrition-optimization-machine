namespace Nom.Orch.Models.Shopping
{
    public class RetailPackagingCreateModel
    {
        public string IngredientPattern { get; set; } = string.Empty;
        public string PackageName { get; set; } = string.Empty;
        public decimal PackageSize { get; set; }
        public string PackageSizeUnit { get; set; } = string.Empty;
        public string SizeCategory { get; set; } = string.Empty;
        public decimal SizeInBaseUnits { get; set; }
        public bool IsDefault { get; set; } = true;
        public string Source { get; set; } = "manual";
    }
}
