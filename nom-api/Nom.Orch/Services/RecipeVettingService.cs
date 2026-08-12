using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nom.Orch.Interfaces;
using Nom.Orch.UtilityInterfaces;

namespace Nom.Orch.Services
{
    /// <summary>
    /// Rules-based plausibility checks: realistic times and servings, minimum
    /// completeness, parseable quantities. Deliberately conservative — its job
    /// is to catch garbage and route "suspect" imports to admin review, not to
    /// judge culinary quality.
    /// </summary>
    public class RecipeVettingService : IRecipeVettingService
    {
        private const int MaxPlausibleActiveMinutes = 24 * 60;      // a day of prep/cook
        private const int MaxPlausibleTotalMinutes = 14 * 24 * 60;  // ferments, cures
        private const decimal MaxPlausibleServings = 200m;

        public Task<List<string>> VetAsync(ScraperRecipe recipe)
        {
            var issues = new List<string>();

            if (string.IsNullOrWhiteSpace(recipe.Name) || recipe.Name.Trim().Length < 3)
            {
                issues.Add("Recipe name is missing or implausibly short.");
            }

            if (recipe.Ingredients.Count < 2)
            {
                issues.Add($"Only {recipe.Ingredients.Count} ingredient line(s) — a usable recipe almost always has at least 2.");
            }

            if (recipe.Steps.Count < 2)
            {
                issues.Add($"Only {recipe.Steps.Count} instruction step(s) — likely an incomplete extraction.");
            }

            if (recipe.Steps.Any(s => s.Instruction.Trim().Length < 10))
            {
                issues.Add("One or more instruction steps are implausibly short.");
            }

            CheckDuration(issues, "Prep time", recipe.PrepTimeMinutes, MaxPlausibleActiveMinutes);
            CheckDuration(issues, "Cook time", recipe.CookTimeMinutes, MaxPlausibleActiveMinutes);
            CheckDuration(issues, "Total time", recipe.TotalTimeMinutes, MaxPlausibleTotalMinutes);

            if (recipe.PrepTimeMinutes is > 0 && recipe.CookTimeMinutes is > 0 && recipe.TotalTimeMinutes is > 0 &&
                recipe.TotalTimeMinutes < Math.Max(recipe.PrepTimeMinutes.Value, recipe.CookTimeMinutes.Value))
            {
                issues.Add($"Total time ({recipe.TotalTimeMinutes}m) is less than prep ({recipe.PrepTimeMinutes}m) or cook ({recipe.CookTimeMinutes}m) time.");
            }

            if (recipe.RecipeServings is { } servings && (servings <= 0 || servings > MaxPlausibleServings))
            {
                issues.Add($"Servings value of {servings} is outside the plausible range (1–{MaxPlausibleServings}).");
            }

            var unparsed = recipe.Ingredients.Count(i => i.Quantity == null);
            if (recipe.Ingredients.Count > 0 && unparsed > recipe.Ingredients.Count / 2)
            {
                issues.Add($"{unparsed} of {recipe.Ingredients.Count} ingredient lines have no parseable quantity — needs a human (or enrichment) pass.");
            }

            return Task.FromResult(issues);
        }

        private static void CheckDuration(List<string> issues, string label, int? minutes, int maxPlausible)
        {
            if (minutes is { } value && (value < 0 || value > maxPlausible))
            {
                issues.Add($"{label} of {value} minutes is outside the plausible range (0–{maxPlausible}).");
            }
        }
    }
}
