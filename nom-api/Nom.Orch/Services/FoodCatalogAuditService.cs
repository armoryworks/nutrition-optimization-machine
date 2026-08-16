using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Nutrition;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;

namespace Nom.Orch.Services
{
    /// <summary>
    /// Deterministic quality audit of the ingredient catalog. Runs for free (no model, no network)
    /// and catches most real problems, so an automated reviewer only ever has to look at what
    /// survives this pass. Findings describe what is wrong and why; they never mutate the catalog.
    /// </summary>
    public class FoodCatalogAuditService : IFoodCatalogAuditService
    {
        private readonly ApplicationDbContext _context;

        public FoodCatalogAuditService(ApplicationDbContext context)
        {
            _context = context;
        }

        // Matches the nutrition display's name patterns so the audit sees what the app sees.
        private static readonly string[] CalorieNames = { "energy", "calories", "kcal" };
        private static readonly string[] ProteinNames = { "protein" };
        private static readonly string[] CarbNames = { "carbohydrate", "carbs" };
        private static readonly string[] FatNames = { "total lipid", "fat" };

        public async Task<FoodCatalogAuditResult> AuditAsync(string? source = null, int limit = 5000)
        {
            var query = _context.Ingredients
                .Include(i => i.IngredientNutrients).ThenInclude(n => n.Nutrient)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(source))
                query = query.Where(i => i.FdcDataType == source);

            var ingredients = await query.OrderBy(i => i.Id).Take(limit).ToListAsync();
            var validator = new FoodDataQualityValidator();
            var findings = new List<FoodCatalogFindingModel>();

            // Group statistics for outlier detection (needs the population first).
            var kcalByGroup = new Dictionary<long, List<decimal>>();
            var withMacros = new List<(Nom.Data.Recipe.IngredientEntity Ing, decimal? K, decimal? P, decimal? C, decimal? F)>();

            foreach (var ing in ingredients)
            {
                var k = Amount(ing, CalorieNames);
                var p = Amount(ing, ProteinNames);
                var c = Amount(ing, CarbNames);
                var f = Amount(ing, FatNames);
                withMacros.Add((ing, k, p, c, f));

                if (ing.FoodGroupId is { } g && k is { } kv)
                {
                    kcalByGroup.TryAdd(g, new List<decimal>());
                    kcalByGroup[g].Add(kv);
                }
            }

            var groupMedian = kcalByGroup.ToDictionary(
                kv => kv.Key,
                kv => Median(kv.Value));

            foreach (var (ing, k, p, c, f) in withMacros)
            {
                // 1. No nutrition at all — the food cannot contribute to a plan's totals.
                if (ing.IngredientNutrients.Count == 0)
                {
                    findings.Add(Finding(ing, "no_nutrition", "high",
                        "No nutrient rows — nutrition cannot be shown or totalled for this food."));
                    continue;
                }

                // 2. Physical plausibility, incl. the Atwater cross-check.
                var verdict = validator.Validate(new FoodQualityInput(ing.Name, k, p, c, f));
                if (!verdict.Accepted)
                {
                    findings.Add(Finding(ing, string.Join("+", verdict.Reasons),
                        verdict.Reasons.Any(r => r.Contains("impossible") || r.Contains("negative")) ? "high" : "medium",
                        $"Fails plausibility: {string.Join(", ", verdict.Reasons)}."));
                }

                // 3. Unclassified — invisible to food-group requirements.
                if (ing.FoodGroupId == null)
                {
                    findings.Add(Finding(ing, "unclassified", "low",
                        "No food group — this food cannot satisfy a household food-group requirement."));
                }
                // 4. Energy far from its group's typical value: likely a wrong group or a bad number.
                else if (k is { } kv && groupMedian.TryGetValue(ing.FoodGroupId.Value, out var med) && med > 0)
                {
                    var ratio = kv / med;
                    if (ratio > 6m || (ratio < 0.15m && kv > 0))
                    {
                        findings.Add(Finding(ing, "group_energy_outlier", "medium",
                            $"{kv:0} kcal/100 g is far from the {ing.FoodGroupId} group median of {med:0} — " +
                            "check the food group or the value."));
                    }
                }

                // 5. Unknown edibility — the standalone picker can't rank it.
                if (ing.IsWholeFood == null)
                {
                    findings.Add(Finding(ing, "edibility_unknown", "low",
                        "Not marked as a whole food or a cooking ingredient."));
                }
            }

            // 6. Near-duplicate names (normalized), which fragment search and shopping lists.
            var dupes = ingredients
                .GroupBy(i => Normalize(i.Name))
                .Where(g => g.Count() > 1)
                .SelectMany(g => g.Skip(1).Select(i => Finding(i, "duplicate_name", "medium",
                    $"Normalizes to the same name as ingredient {g.First().Id} (\"{g.First().Name}\").")));
            findings.AddRange(dupes);

            return new FoodCatalogAuditResult
            {
                Examined = ingredients.Count,
                Findings = findings.OrderByDescending(f => f.Severity == "high")
                                   .ThenByDescending(f => f.Severity == "medium")
                                   .ToList(),
            };
        }

        private static FoodCatalogFindingModel Finding(
            Nom.Data.Recipe.IngredientEntity ing, string code, string severity, string detail) =>
            new()
            {
                IngredientId = ing.Id,
                Name = ing.Name,
                FdcId = ing.FdcId,
                Source = ing.FdcDataType,
                Code = code,
                Severity = severity,
                Detail = detail,
            };

        private static decimal? Amount(Nom.Data.Recipe.IngredientEntity ing, string[] patterns) =>
            ing.IngredientNutrients
                .FirstOrDefault(n => n.Nutrient != null
                    && patterns.Any(p => n.Nutrient.Name.Contains(p, StringComparison.OrdinalIgnoreCase)))
                ?.Amount;

        private static decimal Median(List<decimal> values)
        {
            if (values.Count == 0) return 0m;
            var sorted = values.OrderBy(v => v).ToList();
            return sorted[sorted.Count / 2];
        }

        /// <summary>Lowercase, strip punctuation and collapse whitespace for duplicate detection.</summary>
        private static string Normalize(string name)
        {
            var chars = name.ToLowerInvariant()
                .Select(ch => char.IsLetterOrDigit(ch) ? ch : ' ');
            return string.Join(' ', new string(chars.ToArray())
                .Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }
    }
}
