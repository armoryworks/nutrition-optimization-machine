using Nom.Data.Person;

namespace Nom.Data.Commerce
{
    /// <summary>
    /// A crowdsourced price sighting extracted from a user-uploaded receipt.
    /// Lower-trust than StorePrice (free-text item, per-user, unverified) but
    /// improves the price history with every upload. Feeds the "X is usually
    /// cheapest" trend and, once matched to an ingredient/package, StorePrice.
    /// Maps to the 'commerce.PriceObservation' table.
    /// </summary>
    public class PriceObservationEntity : BaseEntity
    {
        /// <summary>Store as read off the receipt (may not resolve to a GroceryStore row).</summary>
        public long? GroceryStoreId { get; set; }
        public virtual GroceryStoreEntity? GroceryStore { get; set; }

        public string? StoreNameRaw { get; set; }
        public string? PostalCode { get; set; }

        /// <summary>Item text as printed on the receipt.</summary>
        public string ItemText { get; set; } = string.Empty;

        /// <summary>Best-effort match to a canonical ingredient (null until resolved).</summary>
        public long? IngredientId { get; set; }

        public decimal Price { get; set; }
        public string Currency { get; set; } = "USD";

        public DateOnly? PurchasedOn { get; set; }

        /// <summary>Who contributed it (attribution + de-dup).</summary>
        public long? ContributorPersonId { get; set; }

        /// <summary>Parser confidence 0..1; low-confidence rows await manual confirmation.</summary>
        public decimal Confidence { get; set; }
    }
}
