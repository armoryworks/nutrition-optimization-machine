using Nom.Orch.Models.Commerce;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// Consumer-facing pricing operations: where-to-shop recommendations and
    /// receipt ingestion into crowdsourced price observations.
    /// </summary>
    public interface IPricingOrchestrationService
    {
        Task<ShopRecommendationModel> WhereToShopAsync(ShopQueryModel query);

        /// <summary>
        /// Parse a receipt image; if parsed with confidence, persist the line
        /// items as PriceObservations and return them. When no parser is wired,
        /// the result flags manual entry and nothing is persisted.
        /// </summary>
        Task<ReceiptParseResultModel> SubmitReceiptAsync(byte[] imageData, string contentType, long? contributorPersonId, string? postalCode);
    }
}
