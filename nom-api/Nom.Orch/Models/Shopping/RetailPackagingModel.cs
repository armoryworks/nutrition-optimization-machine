namespace Nom.Orch.Models.Shopping
{
    public class RetailPackagingResponseModel
    {
        public long Id { get; set; }
        public string IngredientPattern { get; set; } = string.Empty;
        public string PackageName { get; set; } = string.Empty;
        public decimal PackageSize { get; set; }
        public string PackageSizeUnit { get; set; } = string.Empty;
        public string SizeCategory { get; set; } = string.Empty;
        public decimal SizeInBaseUnits { get; set; }
        public bool IsDefault { get; set; }
        public string Source { get; set; } = string.Empty;
    }

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

    public class RetailPackagingUpdateModel
    {
        public string? PackageName { get; set; }
        public decimal? PackageSize { get; set; }
        public string? PackageSizeUnit { get; set; }
        public string? SizeCategory { get; set; }
        public decimal? SizeInBaseUnits { get; set; }
        public bool? IsDefault { get; set; }
    }

    public class RetailPackagingLookupRequest
    {
        public List<string> IngredientNames { get; set; } = new();
    }

    public class RetailPackagingLookupResponse
    {
        public List<RetailPackagingResponseModel> Results { get; set; } = new();
        public List<string> NotFound { get; set; } = new();
        public bool AiLookupPerformed { get; set; }
    }

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
