using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Nom.Data.Recipe;
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
        private const int MinMethodLengthForOneStep = 40;

        public Task<List<string>> VetAsync(ScraperRecipe recipe)
        {
            var issues = new List<string>();

            if (string.IsNullOrWhiteSpace(recipe.Name) || recipe.Name.Trim().Length < 3)
            {
                issues.Add("Recipe name is missing or implausibly short.");
            }

            var oneQuantifiedPublicDomainLine = recipe.PublicDomain && recipe.Ingredients.Count == 1 && recipe.Ingredients[0].Quantity > 0;
            if (recipe.Ingredients.Count < 2 && !oneQuantifiedPublicDomainLine)
            {
                issues.Add($"Only {recipe.Ingredients.Count} ingredient line(s) — a usable recipe almost always has at least 2.");
            }

            var methodLength = recipe.Steps.Sum(s => s.Instruction.Trim().Length);
            if (recipe.Steps.Count < 2 && methodLength < MinMethodLengthForOneStep)
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

            foreach (var problem in TextProblems(recipe))
            {
                issues.Add(problem);
            }

            var unparsed = recipe.Ingredients.Count(i => i.Quantity == null && !IsAcceptablyUnquantified(i.RawLine, i.LineKind));
            if (recipe.Ingredients.Count > 0 && unparsed > recipe.Ingredients.Count / 2)
            {
                issues.Add($"{unparsed} of {recipe.Ingredients.Count} ingredient lines have no parseable quantity — needs a human (or enrichment) pass.");
            }

            return Task.FromResult(issues);
        }

        /// <summary>
        /// Text that cannot be a recipe as written: markup or links left over from extraction,
        /// placeholder copy, steps with no words, digit soup or consonant runs from bad OCR. One
        /// issue per kind, naming the first step it was seen in. Pure string rules, no model.
        /// </summary>
        public static IEnumerable<string> TextProblems(ScraperRecipe recipe)
        {
            var texts = recipe.Steps.Select((s, i) => (Text: s.Instruction, Where: $"step {i + 1}"))
                .Prepend((Text: recipe.Description ?? string.Empty, Where: "description"))
                .Prepend((Text: recipe.Name, Where: "name"))
                .Where(t => !string.IsNullOrWhiteSpace(t.Text))
                .ToList();

            var checks = new (Func<(string Text, string Where), bool> Hit, string Message)[]
            {
                (t => Markup.IsMatch(t.Text), "Text still contains markup, entities or links ({0}) — extraction left residue behind."),
                (t => Placeholder.IsMatch(t.Text), "Text is placeholder copy ({0})."),
                (t => t.Where != "name" && !RealWord.IsMatch(t.Text), "Text has no real words ({0})."),
                (t => t.Text.Length > 40 && DigitShare(t.Text) > 0.3, "Text is mostly digits ({0})."),
                (t => ConsonantRun.IsMatch(t.Text), "Text has unreadable letter runs ({0}) — likely OCR garbage."),
            };
            foreach (var (hit, message) in checks)
            {
                var first = texts.FirstOrDefault(hit);
                if (first.Text != null) yield return string.Format(message, first.Where);
            }
        }

        private static double DigitShare(string text) => text.Count(char.IsDigit) / (double)text.Length;

        private static readonly Regex Markup = new(
            @"<[a-z!/][^>]*>|&(?:nbsp|amp|lt|gt|quot|#\d+|#x[0-9a-f]+);|https?://|\{\{|\}\}|\[\[|\]\]|\[nrt]\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex Placeholder = new(
            @"\b(?:lorem\s+ipsum|todo|tbd|placeholder|insert\s+\w+\s+here|coming\s+soon|untitled\s+recipe)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex RealWord = new(
            @"\p{L}{3,}",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex ConsonantRun = new(
            @"(?<![\p{L}\p{N}])[bcdfghjklmnpqrstvwxz]{5,}(?![\p{L}\p{N}])",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static bool IsAcceptablyUnquantified(string? rawLine, string? lineKind) =>
            RecipeIngredientLineKind.IsExempt(lineKind) || IsAcceptablyUnquantified(rawLine);

        public static bool IsAcceptablyUnquantified(string? rawLine) =>
            !string.IsNullOrWhiteSpace(rawLine)
            && (AcceptablyUnquantified.IsMatch(rawLine) || VagueAmount.IsMatch(rawLine) || BareSeasoning.IsMatch(rawLine));

        private static readonly Regex VagueAmount = new(
            @"^\s*(?:and\s+)?(?:a\s+(?:little|bit|few|handful|sprinkl(?:e|ing)|grating|squeeze|splash|drizzle|knob|dollop|dab|pinch|dash|touch|drop|few\s+grains|small\s+(?:piece|quantity|amount))|some|little|few|several)\b(?:\s+of)?\s+\p{L}",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private const string Seasonings = @"(?:salt|pepper|nutmeg|mace|cinnamon|cayenne|paprika|ginger|cloves|allspice|spices?|seasoning|herbs|parsley)";

        private static readonly Regex BareSeasoning = new(
            @"^\s*(?:(?:fine|kosher|sea|table|black|white|ground|freshly\s+ground|grated)\s+)*" + Seasonings + @"\s*[.,]?\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex AcceptablyUnquantified = new(
            @"\b(?:to\s+taste|as\s+(?:needed|required|desired)|if\s+(?:needed|desired)|(?:for|to)\s+(?:garnish(?:ing)?|serv(?:e|ing)|dust(?:ing)?|grease|greasing|frying|decorat(?:e|ing)|sprinkl(?:e|ing))|(?:a|one)\s+(?:pinch|dash)|salt\s+(?:and|&)\s+(?:black\s+)?pepper|optional)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static void CheckDuration(List<string> issues, string label, int? minutes, int maxPlausible)
        {
            if (minutes is { } value && (value < 0 || value > maxPlausible))
            {
                issues.Add($"{label} of {value} minutes is outside the plausible range (0–{maxPlausible}).");
            }
        }
    }
}
