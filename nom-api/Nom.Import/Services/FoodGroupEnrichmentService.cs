using System;
using System.Collections.Generic;
using System.Linq;
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
    /// Batch enrichment of the ingredient catalog with food group + whole-food classification.
    /// Runs offline in Nom.Import (not in the nom-api request path). Deterministic keyword
    /// classification always runs; when an <see cref="IAiService"/> is provided (config-gated on
    /// the Ollama URL) the local model refines the food group and supplies the whole-food flag,
    /// validated against the known vocabulary so a hallucinated group is discarded.
    ///
    /// Placement note: this lives here — not in open-core nom-api and not in the nom-commerce
    /// overlay — because Nom.Import already owns the AI-enhancement infrastructure and this is an
    /// inherently batch operation. See docs/architecture/food-catalog-ingestion.md.
    /// </summary>
    public class FoodGroupEnrichmentService
    {
        private readonly ApplicationDbContext _db;
        private readonly IAiService? _ai;
        private readonly ILogger<FoodGroupEnrichmentService> _logger;
        private readonly int _batchSize;

        public FoodGroupEnrichmentService(
            ApplicationDbContext db,
            ILogger<FoodGroupEnrichmentService> logger,
            IAiService? ai = null,
            int batchSize = 50)
        {
            _db = db;
            _logger = logger;
            _ai = ai;
            _batchSize = Math.Max(1, batchSize);
        }

        /// <summary>
        /// Classifies ingredients. When <paramref name="overwrite"/> is false, only touches
        /// ingredients missing a food group (or, for the whole-food flag, missing that flag).
        /// Returns the number of ingredients updated.
        /// </summary>
        public async Task<int> EnrichAsync(bool overwrite, CancellationToken ct = default)
        {
            var query = _db.Ingredients.AsQueryable();
            if (!overwrite)
                query = query.Where(i => i.FoodGroupId == null || i.IsWholeFood == null);

            var ingredients = await query.ToListAsync(ct);
            _logger.LogInformation("Food-group enrichment: {Count} ingredients, AI {State}.",
                ingredients.Count, _ai == null ? "off" : "on");

            int updated = 0, processed = 0;
            foreach (var ing in ingredients)
            {
                ct.ThrowIfCancellationRequested();
                if (await EnrichOneAsync(ing, overwrite))
                    updated++;

                if (++processed % _batchSize == 0)
                    await _db.SaveChangesAsync(ct);
            }
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Food-group enrichment complete: {Updated} updated.", updated);
            return updated;
        }

        private async Task<bool> EnrichOneAsync(IngredientEntity ing, bool overwrite)
        {
            var name = ing.NameNormalized ?? ing.Name;
            long? group = FoodGroupHeuristics.ClassifyFoodGroup(name);
            bool? wholeFood = null;

            if (_ai != null && !string.IsNullOrWhiteSpace(name))
            {
                try
                {
                    var reply = await _ai.EnhanceIngredientAsync(BuildPrompt(name!));
                    var parsed = FoodEnrichmentParser.Parse(reply);
                    group = parsed.FoodGroupId ?? group; // AI refines; heuristic backs it up
                    wholeFood = parsed.IsWholeFood;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "AI enrichment failed for '{Name}'; using heuristic.", name);
                }
            }

            var changed = false;
            if (group.HasValue && (overwrite || ing.FoodGroupId == null) && ing.FoodGroupId != group)
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

        private static string BuildPrompt(string name) =>
            "You classify a food into a nutritional food group. Reply with ONLY compact JSON, no prose.\n" +
            "Schema: {\"food_group\": <one of: Vegetables, Fruits, Grains, Protein Foods, Dairy, " +
            "Fats/Oils, Legumes, Nuts/Seeds, Sweets/Snacks, Beverages>, \"whole_food\": <true if a " +
            "person would eat it directly as-is (an apple, a protein bar, yogurt), false if it is " +
            "normally only a recipe ingredient (flour, baking soda, raw spices)>}.\n" +
            $"Food: \"{name}\"";
    }
}
