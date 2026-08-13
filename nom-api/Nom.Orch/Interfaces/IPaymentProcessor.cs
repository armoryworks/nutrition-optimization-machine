namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// Abstracts a payment processor so the marketplace isn't locked to one
    /// vendor. Per D-060c the concrete integrations (Stripe Connect for
    /// split-payouts; Braintree/PayPal for consumer checkout) are NOT live —
    /// no money moves until the PCI/tax/CPA review and partner terms clear.
    /// Every method here is scaffolded to report "not configured" until then.
    /// </summary>
    public interface IPaymentProcessor
    {
        /// <summary>Processor key: "stripe" | "paypal" | "braintree".</summary>
        string Name { get; }

        /// <summary>True once real credentials/config are present (never in scaffold).</summary>
        bool IsConfigured { get; }

        /// <summary>
        /// Create a split charge: customer pays <paramref name="total"/>, the
        /// platform retains <paramref name="platformFee"/>, the provider account
        /// receives the rest. Returns a processor reference id.
        /// </summary>
        Task<PaymentResult> CreateSplitChargeAsync(PaymentRequest request);
    }

    public class PaymentRequest
    {
        public decimal Total { get; set; }
        public decimal PlatformFee { get; set; }
        public string Currency { get; set; } = "USD";
        public string? ProviderPayoutAccount { get; set; }
        public long ServiceOrderId { get; set; }
    }

    public class PaymentResult
    {
        public bool Success { get; set; }
        public string? Reference { get; set; }
        /// <summary>Set when the processor isn't wired yet (scaffold state).</summary>
        public bool NotConfigured { get; set; }
        public string? Error { get; set; }
    }
}
