using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Commerce;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Commerce;

namespace Nom.Orch.Services
{
    public class PricingOrchestrationService : IPricingOrchestrationService
    {
        private const decimal AutoPersistConfidence = 0.6m;

        private readonly ApplicationDbContext _context;
        private readonly IShopAdvisor _shopAdvisor;
        private readonly IReceiptParser _receiptParser;
        private readonly ILogger<PricingOrchestrationService> _logger;

        public PricingOrchestrationService(
            ApplicationDbContext context,
            IShopAdvisor shopAdvisor,
            IReceiptParser receiptParser,
            ILogger<PricingOrchestrationService> logger)
        {
            _context = context;
            _shopAdvisor = shopAdvisor;
            _receiptParser = receiptParser;
            _logger = logger;
        }

        public Task<ShopRecommendationModel> WhereToShopAsync(ShopQueryModel query) =>
            _shopAdvisor.RecommendAsync(query);

        public async Task<ReceiptParseResultModel> SubmitReceiptAsync(
            byte[] imageData, string contentType, long? contributorPersonId, string? postalCode)
        {
            var parsed = await _receiptParser.ParseAsync(imageData, contentType);

            // Only persist confident, non-manual parses; low-confidence results
            // go back to the user for confirmation before entering the price pool.
            if (!parsed.RequiresManualEntry && parsed.Confidence >= AutoPersistConfidence && parsed.Lines.Count > 0)
            {
                foreach (var line in parsed.Lines)
                {
                    _context.PriceObservations.Add(new PriceObservationEntity
                    {
                        StoreNameRaw = parsed.StoreNameRaw,
                        PostalCode = postalCode ?? parsed.PostalCode,
                        ItemText = line.ItemText,
                        Price = line.Price,
                        PurchasedOn = parsed.PurchasedOn,
                        ContributorPersonId = contributorPersonId,
                        Confidence = parsed.Confidence,
                    });
                }
                await _context.SaveChangesAsync();
                _logger.LogInformation("Persisted {Count} price observations from a receipt", parsed.Lines.Count);
            }

            return parsed;
        }
    }
}
