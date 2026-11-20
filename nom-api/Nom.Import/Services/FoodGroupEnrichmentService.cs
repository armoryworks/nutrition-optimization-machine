using System;
using System.Globalization;
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
        /// Every change is journaled as <see cref="CatalogAdmissionRule.GroupChangeType"/> under one
        /// stamp, so `--admit --undo &lt;stamp&gt;` takes the run back. Returns the number of
        /// ingredients updated.
        /// </summary>
        public async Task<int> EnrichAsync(bool overwrite, CancellationToken ct = default)
        {
            var query = _db.Ingredients.Where(i => i.FdcCategoryId != null || i.FdcCategory != null);
            if (!overwrite)
                query = query.Where(i => i.FoodGroupId == null || i.IsWholeFood == null);

            var ingredients = await query.ToListAsync(ct);
            _logger.LogInformation("Food-group enrichment: {Count} ingredients with a USDA category.", ingredients.Count);

            var stamp = new DateTime(DateTime.UtcNow.Ticks / 10 * 10, DateTimeKind.Utc);
            int updated = 0, processed = 0;
            foreach (var ing in ingredients)
            {
                ct.ThrowIfCancellationRequested();
                if (EnrichOne(ing, overwrite, stamp))
                    updated++;

                if (++processed % _batchSize == 0)
                    await _db.SaveChangesAsync(ct);
            }
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Food-group enrichment {Stamp:O} complete: {Updated} updated, journaled under that stamp for --admit --undo.", stamp, updated);
            return updated;
        }

        private bool EnrichOne(IngredientEntity ing, bool overwrite, DateTime stamp)
        {
            var group = CatalogAdmissionRule.GroupFor(ing.FdcDataType, ing.FdcCategoryId, ing.FdcCategory);
            var wholeFood = CatalogAdmissionRule.EdibleFor(ing.FdcDataType, ing.FdcCategoryId, ing.FdcCategory);

            var changed = false;
            if ((overwrite || (ing.FoodGroupId == null && group.HasValue)) && ing.FoodGroupId != group)
            {
                _db.AuditLogEntries.Add(CatalogAdmissionRule.GroupJournal(ing.Id, nameof(ing.FoodGroupId),
                    ing.FoodGroupId?.ToString(CultureInfo.InvariantCulture), group?.ToString(CultureInfo.InvariantCulture), SystemConstants.SystemPersonId, stamp));
                ing.FoodGroupId = group;
                changed = true;
            }
            if (wholeFood.HasValue && (overwrite || ing.IsWholeFood == null) && ing.IsWholeFood != wholeFood)
            {
                _db.AuditLogEntries.Add(CatalogAdmissionRule.GroupJournal(ing.Id, nameof(ing.IsWholeFood),
                    ing.IsWholeFood?.ToString(), wholeFood.ToString(), SystemConstants.SystemPersonId, stamp));
                ing.IsWholeFood = wholeFood;
                changed = true;
            }
            if (changed)
                ing.LastModifiedDate = stamp;
            return changed;
        }
    }
}
