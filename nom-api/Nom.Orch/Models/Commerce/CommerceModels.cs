namespace Nom.Orch.Models.Commerce
{
    public class PriceQuoteModel
    {
        public long GroceryStoreId { get; set; }
        public string StoreName { get; set; } = string.Empty;
        public long RetailPackagingId { get; set; }
        public decimal Price { get; set; }
        public string Currency { get; set; } = "USD";
        /// <summary>"api" | "partner" | "scrape" | "observation" | "manual".</summary>
        public string Source { get; set; } = "manual";
        public DateTime AsOf { get; set; }
    }

    public class CouponMatchModel
    {
        public long CouponId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string ItemText { get; set; } = string.Empty;
        public decimal DiscountAmount { get; set; }
        public string DiscountType { get; set; } = "amount";
    }

    public class ReceiptLineModel
    {
        public string ItemText { get; set; } = string.Empty;
        public decimal Price { get; set; }
    }

    public class ReceiptParseResultModel
    {
        public string? StoreNameRaw { get; set; }
        public string? PostalCode { get; set; }
        public DateOnly? PurchasedOn { get; set; }
        public List<ReceiptLineModel> Lines { get; set; } = new();
        /// <summary>0..1 overall confidence; low means the caller should confirm before persisting.</summary>
        public decimal Confidence { get; set; }
        /// <summary>True when no real parser is wired yet (manual-entry fallback).</summary>
        public bool RequiresManualEntry { get; set; }
    }

    public class BasketItemModel
    {
        public long RetailPackagingId { get; set; }
        public int Quantity { get; set; } = 1;
    }

    public class ShopQueryModel
    {
        public long HouseholdId { get; set; }
        public string PostalCode { get; set; } = string.Empty;
        /// <summary>Retail packages to buy — computed client-side from the shopping list.</summary>
        public List<BasketItemModel> Basket { get; set; } = new();
    }

    public class StoreBasketModel
    {
        public long GroceryStoreId { get; set; }
        public string StoreName { get; set; } = string.Empty;
        public string? Chain { get; set; }
        public decimal Total { get; set; }
        /// <summary>Fraction of basket items this store had a price for (0..1).</summary>
        public decimal CoveragePct { get; set; }
        public int ItemsPriced { get; set; }
        public int ItemsTotal { get; set; }
    }

    public class ShopRecommendationModel
    {
        public List<StoreBasketModel> Stores { get; set; } = new();
        public StoreBasketModel? Best { get; set; }
        /// <summary>Human explanation of the recommendation.</summary>
        public string Explanation { get; set; } = string.Empty;
        /// <summary>True when there isn't enough price data yet to recommend.</summary>
        public bool InsufficientData { get; set; }
    }
}
