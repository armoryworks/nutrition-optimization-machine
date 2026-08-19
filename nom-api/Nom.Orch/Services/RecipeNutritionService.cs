using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;

namespace Nom.Orch.Services
{
    /// <summary>
    /// Recipe nutrition = Σ(ingredient per-100 g amount × grams used) / servings.
    ///
    /// Grams per recipe ingredient come from the measurement's category:
    ///   Mass   → quantity × factor-to-grams
    ///   Volume → quantity × factor-to-ml, at 1 g/ml (no per-food density yet — a stated approximation)
    ///   Count  → quantity × factor × Ingredient.ReferenceServingGrams (skipped when unknown)
    /// Ingredients without nutrition or without a gram conversion contribute nothing; if
    /// nothing contributes, existing rows are left alone. Rows written here carry
    /// DateCalculated so a later hand-authored/seeded set (DateCalculated NULL) is respected.
    /// Values are per serving (Servings ?? RecipeServings ?? 1), matching the seed convention.
    /// </summary>
    public class RecipeNutritionService : IRecipeNutritionService
    {
        private const long CategoryMass = 1L;
        private const long CategoryVolume = 2L;
        private const long CategoryCount = 3L;

        private readonly ApplicationDbContext _db;
        private readonly ILogger<RecipeNutritionService> _logger;

        public RecipeNutritionService(ApplicationDbContext db, ILogger<RecipeNutritionService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<int> RecalculateForIngredientAsync(long ingredientId)
        {
            var recipeIds = await _db.RecipeIngredients
                .Where(ri => ri.IngredientId == ingredientId)
                .Select(ri => ri.RecipeId)
                .Distinct()
                .ToListAsync();

            var written = 0;
            foreach (var id in recipeIds)
                written += await RecalculateAsync(id);
            return written;
        }

        public async Task<int> RecalculateAsync(long recipeId)
        {
            var recipe = await _db.Recipes
                .Include(r => r.RecipeIngredients).ThenInclude(ri => ri.Measurement)
                .Include(r => r.RecipeIngredients).ThenInclude(ri => ri.Ingredient)
                    .ThenInclude(i => i.IngredientNutrients).ThenInclude(n => n.Nutrient)
                        .ThenInclude(n => n.DefaultMeasurement)
                .FirstOrDefaultAsync(r => r.Id == recipeId);
            if (recipe == null) return 0;

            var existing = await _db.RecipeNutrition.Where(n => n.RecipeId == recipeId).ToListAsync();
            if (existing.Any(n => n.DateCalculated == null))
            {
                // Seeded / hand-authored label — authoritative, do not derive over it.
                return 0;
            }

            var totals = new Dictionary<long, (decimal amount, string unit)>();
            var contributed = 0;
            foreach (var ri in recipe.RecipeIngredients ?? Enumerable.Empty<RecipeIngredientEntity>())
            {
                var ing = ri.Ingredient;
                if (ing?.IngredientNutrients == null || ing.IngredientNutrients.Count == 0) continue;
                var grams = GramsFor(ri);
                if (grams is not { } g || g <= 0) continue;

                contributed++;
                foreach (var n in ing.IngredientNutrients)
                {
                    var add = n.Amount * g / 100m;
                    var unit = n.Nutrient?.DefaultMeasurement?.Symbol ?? n.Measurement?.Symbol ?? string.Empty;
                    totals[n.NutrientId] = totals.TryGetValue(n.NutrientId, out var cur)
                        ? (cur.amount + add, cur.unit)
                        : (add, unit);
                }
            }

            if (contributed == 0)
            {
                _logger.LogDebug("Recipe {RecipeId}: no ingredient contributed nutrition; leaving label as-is", recipeId);
                return 0;
            }

            var servings = recipe.Servings is > 0 ? recipe.Servings.Value
                : recipe.RecipeServings is > 0 ? recipe.RecipeServings.Value
                : 1m;
            var now = DateTime.UtcNow;

            _db.RecipeNutrition.RemoveRange(existing);
            foreach (var (nutrientId, (amount, unit)) in totals)
            {
                _db.RecipeNutrition.Add(new RecipeNutritionEntity
                {
                    RecipeId = recipeId,
                    NutrientId = nutrientId,
                    Amount = Math.Round(amount / servings, 4),
                    Unit = unit,
                    DateCalculated = now,
                    CreatedDate = now,
                    LastModifiedDate = now
                });
            }
            await _db.SaveChangesAsync();
            _logger.LogInformation("Recipe {RecipeId}: derived {Count} nutrient rows from {Ingredients} ingredient(s)", recipeId, totals.Count, contributed);
            return totals.Count;
        }

        private static decimal? GramsFor(RecipeIngredientEntity ri)
        {
            var m = ri.Measurement;
            if (m == null) return null;
            var factor = m.BaseUnitConversionFactor ?? 1m;
            switch (m.MeasurementCategoryId)
            {
                case CategoryMass:
                    return ri.Quantity * factor;
                case CategoryVolume:
                    return ri.Quantity * factor; // ml ≈ g
                case CategoryCount:
                    var per = ri.Ingredient?.ReferenceServingGrams;
                    return per is > 0 ? ri.Quantity * factor * per.Value : null;
                default:
                    return null;
            }
        }
    }
}
