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

        // Well-known ItemStatusType reference IDs (must match seed data in db/seed.sql)
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
            var plan = await ResolvePlanForHouseholdAsync(model.HouseholdId);

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

        /// <summary>
        /// PantryItem.PlanId is a required FK, but nothing in the app populates the
        /// household ↔ plan link, so a household usually has no plan of its own.
        /// Resolve one instead of failing: the household's linked plan, else a plan
        /// any active member participates in (their registration default plan), else
        /// a lightweight pantry plan created here. Whatever is found is linked to the
        /// household so the next lookup is a single query.
        /// </summary>
        private async Task<Nom.Data.Plan.PlanEntity> ResolvePlanForHouseholdAsync(long householdId)
        {
            var household = await _context.Set<Nom.Data.Plan.HouseholdEntity>()
                .Include(h => h.Plans)
                .FirstOrDefaultAsync(h => h.Id == householdId)
                ?? throw new InvalidOperationException($"Household {householdId} was not found.");

            var linked = household.Plans.FirstOrDefault(p => !p.IsDeleted);
            if (linked != null)
                return linked;

            var memberPersonIds = await _context.HouseholdMembers
                .Where(m => m.HouseholdId == householdId && m.IsActive)
                .OrderByDescending(m => m.IsAdmin)
                .ThenBy(m => m.JoinedDate)
                .Select(m => m.PersonId)
                .ToListAsync();

            var plan = await _context.PlanParticipants
                .Where(pp => memberPersonIds.Contains(pp.PersonId) && !pp.Plan.IsDeleted)
                .OrderByDescending(pp => pp.IsAdmin)
                .ThenBy(pp => pp.PlanId)
                .Select(pp => pp.Plan)
                .FirstOrDefaultAsync();

            if (plan == null)
            {
                if (memberPersonIds.Count == 0)
                    throw new InvalidOperationException($"Household {householdId} has no active members, so no plan can be created for its pantry.");

                var now = DateTime.UtcNow;
                plan = new Nom.Data.Plan.PlanEntity
                {
                    Name = $"{household.Name} pantry",
                    Description = "Created automatically so pantry items could be recorded before a meal plan existed.",
                    StartDate = DateOnly.FromDateTime(now),
                    AuthorId = memberPersonIds[0],
                    CurationStatusId = 9000L, // NonCurated
                    Version = 1,
                    CreatedDate = now,
                    LastModifiedDate = now
                };
                _context.Plans.Add(plan);
                _logger.LogInformation("Household {HouseholdId} had no plan; created pantry plan for person {PersonId}", householdId, memberPersonIds[0]);
            }

            household.Plans.Add(plan);
            await _context.SaveChangesAsync();
            return plan;
        }

        public async Task<List<PantryItemResponseModel>> AddPantryItemsBatchAsync(List<PantryItemCreateModel> items)
        {
            if (items == null || items.Count == 0)
                return new List<PantryItemResponseModel>();

            // All items belong to the same household; resolve its plan once.
            var householdId = items[0].HouseholdId;
            var plan = await ResolvePlanForHouseholdAsync(householdId);
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
