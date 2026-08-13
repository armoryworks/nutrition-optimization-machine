using Nom.Data.Person;
using Nom.Data.Plan;

namespace Nom.Data.Commerce
{
    /// <summary>
    /// A customer's order against a ServiceOffering (a shopper trip or a recipe
    /// prepackaging job). Tracks lifecycle and the money split without moving
    /// money — the actual charge/payout is deferred to the payment processor
    /// once that's live (D-060c). Maps to 'commerce.ServiceOrder'.
    /// </summary>
    public class ServiceOrderEntity : BaseEntity
    {
        public long ServiceOfferingId { get; set; }
        public virtual ServiceOfferingEntity? ServiceOffering { get; set; }

        public long CustomerPersonId { get; set; }
        public virtual PersonEntity? CustomerPerson { get; set; }

        public long? HouseholdId { get; set; }
        public virtual HouseholdEntity? Household { get; set; }

        /// <summary>Optional link to the shopping list (shopper) or recipe (prepackaging) being fulfilled.</summary>
        public long? ShoppingListId { get; set; }
        public long? RecipeId { get; set; }

        /// <summary>"quoted" | "accepted" | "in_progress" | "fulfilled" | "cancelled".</summary>
        public string Status { get; set; } = "quoted";

        public decimal QuotedTotal { get; set; }
        public decimal PlatformFee { get; set; }
        public decimal ProviderPayout { get; set; }
        public string Currency { get; set; } = "USD";

        /// <summary>Processor used once charged: "stripe" | "paypal" | "braintree" | null.</summary>
        public string? PaymentProcessor { get; set; }

        /// <summary>Processor's charge/intent id once money moves; null while scaffolded.</summary>
        public string? PaymentReference { get; set; }
    }
}
