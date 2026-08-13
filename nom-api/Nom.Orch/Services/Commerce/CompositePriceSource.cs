using Nom.Orch.Interfaces;
using Nom.Orch.Models.Commerce;

namespace Nom.Orch.Services.Commerce
{
    /// <summary>
    /// Merges quotes from every configured price source (per D-060: "all of the
    /// above" — DB/StorePrice now; licensed API, retailer partner feed, and
    /// scraping added as leaves later). For each (store, package) the freshest
    /// quote wins. New sources join by adding a leaf to the constructor list;
    /// consumers depend only on IPriceSource and never change.
    /// </summary>
    public class CompositePriceSource : IPriceSource
    {
        private readonly IReadOnlyList<IPriceSource> _sources;

        public CompositePriceSource(IEnumerable<IPriceSource> sources)
        {
            _sources = sources.ToList();
        }

        public async Task<IReadOnlyList<PriceQuoteModel>> GetPricesAsync(string postalCode, IReadOnlyList<long> retailPackagingIds)
        {
            var all = new List<PriceQuoteModel>();
            foreach (var source in _sources)
            {
                all.AddRange(await source.GetPricesAsync(postalCode, retailPackagingIds));
            }

            // Freshest quote per (store, package).
            return all
                .GroupBy(q => new { q.GroceryStoreId, q.RetailPackagingId })
                .Select(g => g.OrderByDescending(q => q.AsOf).First())
                .ToList();
        }
    }
}
