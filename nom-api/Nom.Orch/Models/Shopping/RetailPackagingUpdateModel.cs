namespace Nom.Orch.Models.Shopping
{
    public class RetailPackagingUpdateModel
    {
        public string? PackageName { get; set; }
        public decimal? PackageSize { get; set; }
        public string? PackageSizeUnit { get; set; }
        public string? SizeCategory { get; set; }
        public decimal? SizeInBaseUnits { get; set; }
        public bool? IsDefault { get; set; }
    }
}
