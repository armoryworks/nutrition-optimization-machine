using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Shopping;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Pantry;

namespace Nom.Orch.Services
{
    public class PantryOrchestrationService : IPantryOrchestrationService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<PantryOrchestrationService> _logger;

        // Well-known ItemStatusType reference IDs (must match seed data in _CustomMigration.cs)
        private const long StatusInPantryId = 502L;
        private const long StatusUsedId = 503L;
        private const long StatusExpiredId = 504L;

        public PantryOrchestrationService(
            ApplicationDbContext context,
            ILogger<PantryOrchestrationService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<List<PantryItemResponseModel>> GetPantryItemsAsync(long householdId)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            var items = await _context.PantryItems
                .Include(p => p.Ingredient)
                .Include(p => p.Measurement)
                .Include(p => p.ItemStatusType)
                .Where(p => p.HouseholdId == householdId)
                .AsNoTracking()
                .ToListAsync();

            return items.Select(p => MapToResponse(p, today)).ToList();
        }

        public async Task<PantryItemResponseModel?> GetPantryItemAsync(long id)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            var item = await _context.PantryItems
                .Include(p => p.Ingredient)
                .Include(p => p.Measurement)
                .Include(p => p.ItemStatusType)
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == id);

            return item == null ? null : MapToResponse(item, today);
        }

        public async Task<PantryItemResponseModel> AddPantryItemAsync(PantryItemCreateModel model)
        {
            // Look up the household's first plan to satisfy the PlanId FK
            var plan = await _context.Set<Nom.Data.Plan.PlanEntity>()
                .Where(p => _context.Set<Nom.Data.Plan.HouseholdEntity>()
                    .Any(h => h.Id == model.HouseholdId && h.Plans.Any(hp => hp.Id == p.Id)))
                .FirstOrDefaultAsync();

            if (plan == null)
            {
                // If no plan exists yet, we need to find any plan associated with this household
                // via the Household → Plans navigation
                var household = await _context.Set<Nom.Data.Plan.HouseholdEntity>()
                    .Include(h => h.Plans)
                    .FirstOrDefaultAsync(h => h.Id == model.HouseholdId);

                if (household?.Plans.Any() != true)
                    throw new InvalidOperationException($"Household {model.HouseholdId} has no associated plans. Create a plan first.");

                plan = household.Plans.First();
            }

            var entity = new PantryItemEntity
            {
                HouseholdId = model.HouseholdId,
                PlanId = plan.Id,
                IngredientId = model.IngredientId,
                Quantity = model.Quantity,
                MeasurementId = model.MeasurementId,
                ItemStatusTypeId = StatusInPantryId,
                AcquisitionDate = model.AcquisitionDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
                ExpectedExpirationDate = model.ExpectedExpirationDate,
                SourceLocation = model.SourceLocation,
                Notes = model.Notes,
                CreatedDate = DateTime.UtcNow,
                LastModifiedDate = DateTime.UtcNow
            };

            _context.PantryItems.Add(entity);
            await _context.SaveChangesAsync();

            // Reload with includes
            var created = await _context.PantryItems
                .Include(p => p.Ingredient)
                .Include(p => p.Measurement)
                .Include(p => p.ItemStatusType)
                .FirstAsync(p => p.Id == entity.Id);

            return MapToResponse(created, DateOnly.FromDateTime(DateTime.UtcNow));
        }

        public async Task<List<PantryItemResponseModel>> AddPantryItemsBatchAsync(List<PantryItemCreateModel> items)
        {
            if (items == null || items.Count == 0)
                return new List<PantryItemResponseModel>();

            // Look up the household's plan once (all items belong to same household)
            var householdId = items[0].HouseholdId;
            var household = await _context.Set<Nom.Data.Plan.HouseholdEntity>()
                .Include(h => h.Plans)
                .FirstOrDefaultAsync(h => h.Id == householdId);

            if (household?.Plans.Any() != true)
                throw new InvalidOperationException($"Household {householdId} has no associated plans. Create a plan first.");

            var plan = household.Plans.First();
            var now = DateTime.UtcNow;
            var entities = new List<PantryItemEntity>();

            foreach (var model in items)
            {
                var entity = new PantryItemEntity
                {
                    HouseholdId = model.HouseholdId,
                    PlanId = plan.Id,
                    IngredientId = model.IngredientId,
                    Quantity = model.Quantity,
                    MeasurementId = model.MeasurementId,
                    ItemStatusTypeId = StatusInPantryId,
                    AcquisitionDate = model.AcquisitionDate ?? DateOnly.FromDateTime(now),
                    ExpectedExpirationDate = model.ExpectedExpirationDate,
                    SourceLocation = model.SourceLocation,
                    Notes = model.Notes,
                    CreatedDate = now,
                    LastModifiedDate = now
                };
                entities.Add(entity);
                _context.PantryItems.Add(entity);
            }

            await _context.SaveChangesAsync();

            // Reload all with includes
            var ids = entities.Select(e => e.Id).ToList();
            var created = await _context.PantryItems
                .Include(p => p.Ingredient)
                .Include(p => p.Measurement)
                .Include(p => p.ItemStatusType)
                .Where(p => ids.Contains(p.Id))
                .ToListAsync();

            var today = DateOnly.FromDateTime(now);
            return created.Select(p => MapToResponse(p, today)).ToList();
        }

        public async Task<PantryItemResponseModel?> UpdatePantryItemAsync(long id, PantryItemUpdateModel model)
        {
            var entity = await _context.PantryItems
                .Include(p => p.Ingredient)
                .Include(p => p.Measurement)
                .Include(p => p.ItemStatusType)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (entity == null) return null;

            if (model.Quantity.HasValue) entity.Quantity = model.Quantity.Value;
            if (model.MeasurementId.HasValue) entity.MeasurementId = model.MeasurementId.Value;
            if (model.ExpectedExpirationDate.HasValue) entity.ExpectedExpirationDate = model.ExpectedExpirationDate.Value;
            if (model.ItemStatusTypeId.HasValue) entity.ItemStatusTypeId = model.ItemStatusTypeId.Value;
            if (model.SourceLocation != null) entity.SourceLocation = model.SourceLocation;
            if (model.Notes != null) entity.Notes = model.Notes;

            entity.LastModifiedDate = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return MapToResponse(entity, DateOnly.FromDateTime(DateTime.UtcNow));
        }

        public async Task<bool> RemovePantryItemAsync(long id)
        {
            var entity = await _context.PantryItems.FindAsync(id);
            if (entity == null) return false;

            _context.PantryItems.Remove(entity);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<ShoppingNeedsResponseModel> GetShoppingNeedsAsync(long householdId, int daysAhead)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var endDate = today.AddDays(daysAhead);

            // 1. Get upcoming meal plans with recipes for this household
            var upcomingMeals = await _context.MealPlans
                .Include(mp => mp.Recipe)
                    .ThenInclude(r => r!.RecipeIngredients)
                        .ThenInclude(ri => ri.Measurement)
                            .ThenInclude(m => m!.Category)
                .Include(mp => mp.Recipe)
                    .ThenInclude(r => r!.RecipeIngredients)
                        .ThenInclude(ri => ri.Ingredient)
                .Where(mp => mp.HouseholdId == householdId
                    && mp.Date >= today
                    && mp.Date < endDate
                    && mp.RecipeId != null
                    && mp.CompletedDate == null) // Only uncompleted meals
                .AsNoTracking()
                .ToListAsync();

            // 2. Aggregate needed ingredients in base units
            // Key: ingredientId + measurement category name → total base units needed
            var neededMap = new Dictionary<(long ingredientId, string categoryName), NeededAccumulator>();

            foreach (var meal in upcomingMeals)
            {
                if (meal.Recipe?.RecipeIngredients == null) continue;

                foreach (var ri in meal.Recipe.RecipeIngredients)
                {
                    var categoryName = ri.Measurement?.Category?.Name ?? "Count";
                    var conversionFactor = ri.Measurement?.BaseUnitConversionFactor ?? 1m;
                    var baseQuantity = ri.Quantity * conversionFactor;

                    var key = (ri.IngredientId, categoryName);
                    if (neededMap.TryGetValue(key, out var acc))
                    {
                        acc.BaseQuantity += baseQuantity;
                    }
                    else
                    {
                        neededMap[key] = new NeededAccumulator
                        {
                            IngredientId = ri.IngredientId,
                            IngredientName = ri.Ingredient?.Name ?? "Unknown",
                            CategoryName = categoryName,
                            BaseQuantity = baseQuantity,
                            // Track a representative measurement for display
                            MeasurementId = ri.MeasurementId,
                            MeasurementName = ri.Measurement?.Name ?? "",
                            MeasurementSymbol = ri.Measurement?.Symbol ?? "",
                            ConversionFactor = conversionFactor
                        };
                    }
                }
            }

            // 3. Get active pantry items (In Pantry status, not expired)
            var pantryItems = await _context.PantryItems
                .Include(p => p.Measurement)
                    .ThenInclude(m => m.Category)
                .Where(p => p.HouseholdId == householdId
                    && p.ItemStatusTypeId == StatusInPantryId
                    && (p.ExpectedExpirationDate == null || p.ExpectedExpirationDate >= today))
                .AsNoTracking()
                .ToListAsync();

            // 4. Build pantry stock map in base units
            var pantryMap = new Dictionary<(long ingredientId, string categoryName), decimal>();

            foreach (var p in pantryItems)
            {
                var categoryName = p.Measurement?.Category?.Name ?? "Count";
                var conversionFactor = p.Measurement?.BaseUnitConversionFactor ?? 1m;
                var baseQuantity = p.Quantity * conversionFactor;

                var key = (p.IngredientId, categoryName);
                if (pantryMap.ContainsKey(key))
                    pantryMap[key] += baseQuantity;
                else
                    pantryMap[key] = baseQuantity;
            }

            // 5. Subtract pantry from needed → shopping needs
            var needs = new List<ShoppingNeedModel>();

            foreach (var kvp in neededMap)
            {
                var acc = kvp.Value;
                var onHandBase = pantryMap.GetValueOrDefault(kvp.Key, 0m);
                var toBuyBase = acc.BaseQuantity - onHandBase;

                if (toBuyBase <= 0) continue; // Fully covered by pantry

                // Convert back to the representative unit for display
                var conversionFactor = acc.ConversionFactor > 0 ? acc.ConversionFactor : 1m;

                needs.Add(new ShoppingNeedModel
                {
                    IngredientId = acc.IngredientId,
                    IngredientName = acc.IngredientName,
                    QuantityNeeded = Math.Round(acc.BaseQuantity / conversionFactor, 2),
                    QuantityOnHand = Math.Round(onHandBase / conversionFactor, 2),
                    QuantityToBuy = Math.Round(toBuyBase / conversionFactor, 2),
                    MeasurementId = acc.MeasurementId,
                    MeasurementName = acc.MeasurementName,
                    MeasurementSymbol = acc.MeasurementSymbol,
                    MeasurementCategory = acc.CategoryName
                });
            }

            return new ShoppingNeedsResponseModel
            {
                HouseholdId = householdId,
                DaysAhead = daysAhead,
                FromDate = today,
                ToDate = endDate,
                MealCount = upcomingMeals.Count,
                Needs = needs.OrderBy(n => n.IngredientName).ToList()
            };
        }

        public async Task<bool> DeductFromPantryAsync(long mealPlanId)
        {
            var mealPlan = await _context.MealPlans
                .Include(mp => mp.Recipe)
                    .ThenInclude(r => r!.RecipeIngredients)
                        .ThenInclude(ri => ri.Measurement)
                            .ThenInclude(m => m!.Category)
                .FirstOrDefaultAsync(mp => mp.Id == mealPlanId);

            if (mealPlan?.Recipe?.RecipeIngredients == null)
                return false;

            // Get pantry items for this household
            var pantryItems = await _context.PantryItems
                .Include(p => p.Measurement)
                    .ThenInclude(m => m.Category)
                .Where(p => p.HouseholdId == mealPlan.HouseholdId
                    && p.ItemStatusTypeId == StatusInPantryId)
                .ToListAsync();

            foreach (var ri in mealPlan.Recipe.RecipeIngredients)
            {
                var categoryName = ri.Measurement?.Category?.Name ?? "Count";
                var conversionFactor = ri.Measurement?.BaseUnitConversionFactor ?? 1m;
                var neededBase = ri.Quantity * conversionFactor;

                // Find matching pantry items (same ingredient, same measurement category)
                var matchingPantry = pantryItems
                    .Where(p => p.IngredientId == ri.IngredientId
                        && (p.Measurement?.Category?.Name ?? "Count") == categoryName)
                    .ToList();

                foreach (var pantryItem in matchingPantry)
                {
                    if (neededBase <= 0) break;

                    var pantryConversion = pantryItem.Measurement?.BaseUnitConversionFactor ?? 1m;
                    var pantryBase = pantryItem.Quantity * pantryConversion;

                    if (pantryBase <= neededBase)
                    {
                        // Use up entire pantry item
                        pantryItem.ItemStatusTypeId = StatusUsedId;
                        pantryItem.Quantity = 0;
                        pantryItem.LastModifiedDate = DateTime.UtcNow;
                        neededBase -= pantryBase;
                    }
                    else
                    {
                        // Partial deduction
                        var remainingBase = pantryBase - neededBase;
                        pantryItem.Quantity = remainingBase / pantryConversion;
                        pantryItem.LastModifiedDate = DateTime.UtcNow;
                        neededBase = 0;
                    }
                }
            }

            // Mark meal as completed
            mealPlan.CompletedDate = DateOnly.FromDateTime(DateTime.UtcNow);
            mealPlan.LastModifiedDate = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Meal plan {MealPlanId} completed, pantry deductions applied", mealPlanId);
            return true;
        }

        private static PantryItemResponseModel MapToResponse(PantryItemEntity entity, DateOnly today)
        {
            var isExpired = entity.ExpectedExpirationDate.HasValue && entity.ExpectedExpirationDate.Value < today;
            var isExpiringSoon = !isExpired
                && entity.ExpectedExpirationDate.HasValue
                && entity.ExpectedExpirationDate.Value <= today.AddDays(3);

            return new PantryItemResponseModel
            {
                Id = entity.Id,
                HouseholdId = entity.HouseholdId ?? 0,
                IngredientId = entity.IngredientId,
                IngredientName = entity.Ingredient?.Name ?? "",
                Quantity = entity.Quantity,
                MeasurementId = entity.MeasurementId,
                MeasurementName = entity.Measurement?.Name ?? "",
                MeasurementSymbol = entity.Measurement?.Symbol ?? "",
                ItemStatusTypeId = entity.ItemStatusTypeId,
                StatusName = entity.ItemStatusType?.Name ?? "",
                AcquisitionDate = entity.AcquisitionDate,
                ExpectedExpirationDate = entity.ExpectedExpirationDate,
                SourceLocation = entity.SourceLocation,
                Notes = entity.Notes,
                IsExpired = isExpired,
                IsExpiringSoon = isExpiringSoon,
                CreatedDate = entity.CreatedDate,
                LastModifiedDate = entity.LastModifiedDate
            };
        }

        private class NeededAccumulator
        {
            public long IngredientId { get; set; }
            public string IngredientName { get; set; } = string.Empty;
            public string CategoryName { get; set; } = string.Empty;
            public decimal BaseQuantity { get; set; }
            public long MeasurementId { get; set; }
            public string MeasurementName { get; set; } = string.Empty;
            public string MeasurementSymbol { get; set; } = string.Empty;
            public decimal ConversionFactor { get; set; }
        }
    }
}
