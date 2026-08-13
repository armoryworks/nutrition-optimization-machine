using Nom.Data.Reference;

namespace Nom.Data.Commerce
{
    /// <summary>
    /// A known price for a retail package at a store, as of a point in time.
    /// The authoritative price signal (from licensed APIs, partner feeds, or
    /// scraping); crowdsourced receipt data lives in PriceObservation instead.
    /// Maps to the 'commerce.StorePrice' table.
    /// </summary>
    public class StorePriceEntity : BaseEntity
    {
        public long GroceryStoreId { get; set; }
        public virtual GroceryStoreEntity? GroceryStore { get; set; }

        public long RetailPackagingId { get; set; }
        public virtual RetailPackagingEntity? RetailPackaging { get; set; }

        public decimal Price { get; set; }
        public string Currency { get; set; } = "USD";

        public DateTime AsOf { get; set; }

        /// <summary>Where this price came from: "api", "partner", "scrape", "manual".</summary>
        public string Source { get; set; } = "manual";
    }
}
