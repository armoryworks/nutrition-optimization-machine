namespace Nom.Data.Commerce
{
    /// <summary>
    /// A physical grocery store the cheapest-store finder can price a basket
    /// against. Maps to the 'commerce.GroceryStore' table.
    /// </summary>
    public class GroceryStoreEntity : BaseEntity
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>Chain/banner, e.g. "Kroger", "Harmons", "Associated Foods". Null for independents.</summary>
        public string? Chain { get; set; }

        public string? AddressLine { get; set; }
        public string? City { get; set; }
        public string? Region { get; set; }
        public string? PostalCode { get; set; }

        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }

        /// <summary>External identifier from a partner/pricing source, if any.</summary>
        public string? ExternalId { get; set; }
    }
}
