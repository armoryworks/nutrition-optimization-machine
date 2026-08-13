using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Commerce;

namespace Nom.Orch.Services.Commerce
{
    /// <summary>
    /// Price source backed by the local StorePrice table (populated by whatever
    /// upstream sources exist — API, partner, scrape, or promoted observations).
    /// Returns the most recent price per (store, package) for stores whose
    /// postal code matches. External live sources implement IPriceSource
    /// separately and are composed in later.
    /// </summary>
    public class DbPriceSource : IPriceSource
    {
        private readonly ApplicationDbContext _db;
        public DbPriceSource(ApplicationDbContext db) => _db = db;

        public async Task<IReadOnlyList<PriceQuoteModel>> GetPricesAsync(string postalCode, IReadOnlyList<long> retailPackagingIds)
        {
            if (retailPackagingIds.Count == 0) return System.Array.Empty<PriceQuoteModel>();

            var rows = await _db.StorePrices
                .AsNoTracking()
                .Where(sp => retailPackagingIds.Contains(sp.RetailPackagingId)
                    && sp.GroceryStore!.PostalCode == postalCode)
                .Select(sp => new
                {
                    sp.GroceryStoreId,
                    StoreName = sp.GroceryStore!.Name,
                    sp.RetailPackagingId,
                    sp.Price,
                    sp.Currency,
                    sp.Source,
                    sp.AsOf,
                })
                .ToListAsync();

            // Latest price per (store, package).
            return rows
                .GroupBy(r => new { r.GroceryStoreId, r.RetailPackagingId })
                .Select(g => g.OrderByDescending(r => r.AsOf).First())
                .Select(r => new PriceQuoteModel
                {
                    GroceryStoreId = r.GroceryStoreId,
                    StoreName = r.StoreName,
                    RetailPackagingId = r.RetailPackagingId,
                    Price = r.Price,
                    Currency = r.Currency,
                    Source = r.Source,
                    AsOf = r.AsOf,
                })
                .ToList();
        }
    }

    /// <summary>Coupon source backed by the local Coupon table (currently valid only).</summary>
    public class DbCouponSource : ICouponSource
    {
        private readonly ApplicationDbContext _db;
        public DbCouponSource(ApplicationDbContext db) => _db = db;

        public async Task<IReadOnlyList<CouponMatchModel>> GetCouponsAsync(string? chain, IReadOnlyList<string> itemTexts)
        {
            var today = DateOnly.FromDateTime(System.DateTime.UtcNow);
            var candidates = await _db.Coupons
                .AsNoTracking()
                .Where(c => (c.ValidTo == null || c.ValidTo >= today)
                    && (c.ValidFrom == null || c.ValidFrom <= today)
                    && (chain == null || c.Chain == null || c.Chain == chain))
                .Select(c => new { c.Id, c.Title, c.ItemPattern, c.DiscountAmount, c.DiscountType })
                .ToListAsync();

            var lowerItems = itemTexts.Select(t => t.ToLowerInvariant()).ToList();
            return candidates
                .Where(c => lowerItems.Any(it => it.Contains(c.ItemPattern.ToLowerInvariant())))
                .Select(c => new CouponMatchModel
                {
                    CouponId = c.Id,
                    Title = c.Title,
                    ItemText = c.ItemPattern,
                    DiscountAmount = c.DiscountAmount,
                    DiscountType = c.DiscountType,
                })
                .ToList();
        }
    }

    /// <summary>
    /// Placeholder receipt parser: no vision provider is wired yet, so it asks
    /// the caller to enter the receipt manually. Replace with a vision-model
    /// implementation once a provider is chosen (see epic #24 / D-060d).
    /// </summary>
    public class ManualReceiptParser : IReceiptParser
    {
        public Task<ReceiptParseResultModel> ParseAsync(byte[] imageData, string contentType)
        {
            return Task.FromResult(new ReceiptParseResultModel
            {
                RequiresManualEntry = true,
                Confidence = 0m,
            });
        }
    }
}
