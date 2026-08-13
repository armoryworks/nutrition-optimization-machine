using Nom.Orch.Models.Commerce;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// Supplies grocery prices for stores near a postal code. Implementations
    /// are swappable and unordered: a licensed pricing API, a retailer partner
    /// feed, intermittent scraping, or the DB of crowdsourced observations.
    /// The DB-backed default ships now; external sources are added later
    /// without touching consumers.
    /// </summary>
    public interface IPriceSource
    {
        /// <summary>Best-known prices for the given retail packages near a postal code.</summary>
        Task<IReadOnlyList<PriceQuoteModel>> GetPricesAsync(string postalCode, IReadOnlyList<long> retailPackagingIds);
    }

    /// <summary>Supplies coupons applicable to a shopping list. Swappable like IPriceSource.</summary>
    public interface ICouponSource
    {
        Task<IReadOnlyList<CouponMatchModel>> GetCouponsAsync(string? chain, IReadOnlyList<string> itemTexts);
    }

    /// <summary>
    /// Extracts store + line-item prices from a receipt image. The heuristic
    /// default returns low-confidence rows for manual confirmation; a vision
    /// model implementation replaces it once a provider is chosen.
    /// </summary>
    public interface IReceiptParser
    {
        Task<ReceiptParseResultModel> ParseAsync(byte[] imageData, string contentType);
    }

    /// <summary>
    /// Recommends where to shop for a basket and explains the pick. The
    /// heuristic default ranks by total price + coverage; an LLM implementation
    /// can add reasoning ("cheapest for your basket, though milk is $0.40 more").
    /// </summary>
    public interface IShopAdvisor
    {
        Task<ShopRecommendationModel> RecommendAsync(ShopQueryModel query);
    }
}
