using Nom.Orch.Interfaces;
using Nom.Orch.Models.Commerce;

namespace Nom.Orch.Services.Commerce
{
    /// <summary>
    /// Ranks stores for a basket by total price and coverage, using whatever
    /// IPriceSource provides. Deterministic and explainable; an LLM-backed
    /// IShopAdvisor can replace this to add nuance ("cheapest overall, though
    /// you'd pay more for produce"). Returns InsufficientData when no store has
    /// enough prices to compare — the expected state until price data exists.
    /// </summary>
    public class HeuristicShopAdvisor : IShopAdvisor
    {
        private const decimal MinCoverageToRecommend = 0.5m;

        private readonly IPriceSource _priceSource;
        public HeuristicShopAdvisor(IPriceSource priceSource) => _priceSource = priceSource;

        public async Task<ShopRecommendationModel> RecommendAsync(ShopQueryModel query)
        {
            var basket = query.Basket.Where(b => b.Quantity > 0).ToList();
            if (basket.Count == 0)
            {
                return new ShopRecommendationModel
                {
                    InsufficientData = true,
                    Explanation = "Your shopping list is empty.",
                };
            }

            var packageIds = basket.Select(b => b.RetailPackagingId).Distinct().ToList();
            var quotes = await _priceSource.GetPricesAsync(query.PostalCode, packageIds);

            if (quotes.Count == 0)
            {
                return new ShopRecommendationModel
                {
                    InsufficientData = true,
                    Explanation = $"No price data yet for stores near {query.PostalCode}. " +
                        "Prices build up as receipts are uploaded and pricing sources are connected.",
                };
            }

            var qtyByPackage = basket.ToDictionary(b => b.RetailPackagingId, b => b.Quantity);
            var itemsTotal = basket.Count;

            var stores = quotes
                .GroupBy(q => new { q.GroceryStoreId, q.StoreName })
                .Select(g =>
                {
                    var pricedPackages = g.Select(q => q.RetailPackagingId).Distinct().ToList();
                    var total = g.Sum(q => q.Price * qtyByPackage.GetValueOrDefault(q.RetailPackagingId, 1));
                    var itemsPriced = basket.Count(b => pricedPackages.Contains(b.RetailPackagingId));
                    return new StoreBasketModel
                    {
                        GroceryStoreId = g.Key.GroceryStoreId,
                        StoreName = g.Key.StoreName,
                        Total = decimal.Round(total, 2),
                        ItemsPriced = itemsPriced,
                        ItemsTotal = itemsTotal,
                        CoveragePct = itemsTotal == 0 ? 0 : decimal.Round((decimal)itemsPriced / itemsTotal, 3),
                    };
                })
                // Prefer well-covered baskets first, then cheapest.
                .OrderByDescending(s => s.CoveragePct >= MinCoverageToRecommend)
                .ThenBy(s => s.Total)
                .ToList();

            var best = stores.FirstOrDefault(s => s.CoveragePct >= MinCoverageToRecommend) ?? stores.First();

            var explanation = best.CoveragePct >= MinCoverageToRecommend
                ? $"{best.StoreName} is cheapest for this basket at {best.Total:C} " +
                  $"({best.ItemsPriced} of {best.ItemsTotal} items priced)."
                : $"Only partial price data is available near {query.PostalCode}; " +
                  $"{best.StoreName} covers {best.ItemsPriced} of {best.ItemsTotal} items so far.";

            return new ShopRecommendationModel
            {
                Stores = stores,
                Best = best,
                Explanation = explanation,
                InsufficientData = false,
            };
        }
    }
}
