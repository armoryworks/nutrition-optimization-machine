using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Nutrition;
using Nom.Data.Recipe;

namespace Nom.Import.Services
{
    /// <summary>
    /// Batch enrichment of the ingredient catalog with food group + whole-food classification,
    /// from the food's stored USDA category only (<see cref="CatalogAdmissionRule.GroupFor"/> and
    /// <see cref="CatalogAdmissionRule.EdibleFor"/>). A food without a stored category, and every
    /// hand-made food, is left as it is: a wrong group is worse than none, so neither a name
    /// keyword nor a model sets a group. Runs offline in Nom.Import (not in the nom-api request
    /// path). See docs/architecture/food-catalog-ingestion.md.
    /// </summary>
    public class FoodGroupEnrichmentService
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<FoodGroupEnrichmentService> _logger;
        private readonly int _batchSize;

        public FoodGroupEnrichmentService(
            ApplicationDbContext db,
            ILogger<FoodGroupEnrichmentService> logger,
            int batchSize = 50)
        {
            _db = db;
            _logger = logger;
            _batchSize = Math.Max(1, batchSize);
        }

        /// <summary>
        /// Classifies ingredients that carry a USDA category. When <paramref name="overwrite"/> is
        /// false, only touches ingredients missing a food group (or, for the whole-food flag,
        /// missing that flag); with it, the group is set to what the category gives, none included.
        /// Returns the number of ingredients updated.
        /// </summary>
        public async Task<int> EnrichAsync(bool overwrite, CancellationToken ct = default)
        {
            var query = _db.Ingredients.Where(i => i.FdcCategoryId != null || i.FdcCategory != null);
            if (!overwrite)
                query = query.Where(i => i.FoodGroupId == null || i.IsWholeFood == null);

            var ingredients = await query.ToListAsync(ct);
            _logger.LogInformation("Food-group enrichment: {Count} ingredients with a USDA category.", ingredients.Count);

            int updated = 0, processed = 0;
            foreach (var ing in ingredients)
            {
                ct.ThrowIfCancellationRequested();
                if (EnrichOne(ing, overwrite))
                    updated++;

                if (++processed % _batchSize == 0)
                    await _db.SaveChangesAsync(ct);
            }
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Food-group enrichment complete: {Updated} updated.", updated);
            return updated;
        }

        private static bool EnrichOne(IngredientEntity ing, bool overwrite)
        {
            var group = CatalogAdmissionRule.GroupFor(ing.FdcDataType, ing.FdcCategoryId, ing.FdcCategory);
            var wholeFood = CatalogAdmissionRule.EdibleFor(ing.FdcDataType, ing.FdcCategoryId, ing.FdcCategory);

            var changed = false;
            if ((overwrite || (ing.FoodGroupId == null && group.HasValue)) && ing.FoodGroupId != group)
            {
                ing.FoodGroupId = group;
                changed = true;
            }
            if (wholeFood.HasValue && (overwrite || ing.IsWholeFood == null) && ing.IsWholeFood != wholeFood)
            {
                ing.IsWholeFood = wholeFood;
                changed = true;
            }
            if (changed)
                ing.LastModifiedDate = DateTime.UtcNow;
            return changed;
        }
    }
}
