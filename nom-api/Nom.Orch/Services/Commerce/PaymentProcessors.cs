using Microsoft.Extensions.Configuration;
using Nom.Orch.Interfaces;

namespace Nom.Orch.Services.Commerce
{
    /// <summary>
    /// Base scaffold: reads config to decide whether it's "configured", but
    /// never actually charges — CreateSplitChargeAsync returns NotConfigured
    /// until the real SDK integration and credentials land (D-060c). This lets
    /// the order lifecycle be built and tested end-to-end without moving money.
    /// </summary>
    public abstract class ScaffoldPaymentProcessor : IPaymentProcessor
    {
        protected readonly IConfiguration Config;
        protected ScaffoldPaymentProcessor(IConfiguration config) => Config = config;

        public abstract string Name { get; }

        // Configured only when a secret is present AND live mode is explicitly on.
        public bool IsConfigured =>
            !string.IsNullOrEmpty(Config[$"Payments:{Name}:ApiKey"]) &&
            Config.GetValue($"Payments:{Name}:LiveMode", false);

        public Task<PaymentResult> CreateSplitChargeAsync(PaymentRequest request)
        {
            // Scaffold: no real charge. When the concrete SDK is added, replace
            // this body; the interface and callers stay the same.
            return Task.FromResult(new PaymentResult
            {
                Success = false,
                NotConfigured = true,
                Error = $"{Name} payments are not enabled yet.",
            });
        }
    }

    /// <summary>Stripe Connect — the marketplace split-payout rail (scaffold).</summary>
    public class StripePaymentProcessor : ScaffoldPaymentProcessor
    {
        public StripePaymentProcessor(IConfiguration config) : base(config) { }
        public override string Name => "stripe";
    }

    /// <summary>Braintree/PayPal — consumer checkout (cards + PayPal + Venmo) (scaffold).</summary>
    public class BraintreePaymentProcessor : ScaffoldPaymentProcessor
    {
        public BraintreePaymentProcessor(IConfiguration config) : base(config) { }
        public override string Name => "braintree";
    }
}
