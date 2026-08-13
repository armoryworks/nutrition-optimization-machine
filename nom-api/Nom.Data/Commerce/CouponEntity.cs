namespace Nom.Data.Commerce
{
    /// <summary>
    /// A store/chain coupon that can be matched against a shopping list.
    /// Maps to the 'commerce.Coupon' table.
    /// </summary>
    public class CouponEntity : BaseEntity
    {
        public long? GroceryStoreId { get; set; }
        public virtual GroceryStoreEntity? GroceryStore { get; set; }

        /// <summary>Chain/banner the coupon applies to when not store-specific.</summary>
        public string? Chain { get; set; }

        /// <summary>Human title, e.g. "$1 off any dairy".</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>Ingredient/product text pattern the coupon applies to.</summary>
        public string ItemPattern { get; set; } = string.Empty;

        /// <summary>Discount amount; interpreted per DiscountType.</summary>
        public decimal DiscountAmount { get; set; }

        /// <summary>"amount" (currency off) or "percent".</summary>
        public string DiscountType { get; set; } = "amount";

        public DateOnly? ValidFrom { get; set; }
        public DateOnly? ValidTo { get; set; }

        /// <summary>Where it came from: "partner", "scrape", "manual".</summary>
        public string Source { get; set; } = "manual";
    }
}
