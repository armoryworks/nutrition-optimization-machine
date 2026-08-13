namespace Nom.Data.Commerce
{
    /// <summary>
    /// A paid service a third party offers through NOM: shopper fulfillment
    /// (someone shops/delivers a list) or per-recipe prepackaging (a partner
    /// assembles the ingredient kit). Case-by-case pricing. Maps to
    /// 'commerce.ServiceOffering'. Scaffold — no money moves until payments
    /// clear (D-060c).
    /// </summary>
    public class ServiceOfferingEntity : BaseEntity
    {
        /// <summary>"shopper" | "prepackaging".</summary>
        public string ServiceType { get; set; } = string.Empty;

        public string ProviderName { get; set; } = string.Empty;

        /// <summary>Provider's payout account id with the processor (e.g. Stripe Connect account).</summary>
        public string? ProviderPayoutaccount { get; set; }

        /// <summary>Coverage area, e.g. a postal-code prefix or region.</summary>
        public string? CoverageArea { get; set; }

        /// <summary>"per-order" | "per-item" | "quote".</summary>
        public string PricingModel { get; set; } = "quote";

        public decimal? BasePrice { get; set; }

        /// <summary>Platform commission fraction (0..1) NOM retains on payout.</summary>
        public decimal CommissionRate { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
