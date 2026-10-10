using System;
using System.Linq;
using System.Text.RegularExpressions;
using Nom.Data.Recipe;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// The deterministic pre-pass of line classification: kinds that need no model because the line
    /// says so on its face. Returns null for anything that needs judgement.
    /// </summary>
    public static class RecipeLineClassifier
    {
        public const string DeterministicSource = "deterministic";

        public static string? Classify(string? rawLine)
        {
            if (string.IsNullOrWhiteSpace(rawLine) || !rawLine.Any(char.IsLetter)) return RecipeIngredientLineKind.Artefact;
            if (IsRepeatedMerge(rawLine)) return RecipeIngredientLineKind.Artefact;
            if (Heading.IsMatch(rawLine) || YieldNote.IsMatch(rawLine)) return RecipeIngredientLineKind.Note;
            if (Equipment.IsMatch(rawLine)) return RecipeIngredientLineKind.Equipment;
            return null;
        }

        private static bool IsRepeatedMerge(string rawLine)
        {
            var parts = rawLine.Split(" + ", StringSplitOptions.TrimEntries);
            if (parts.Length < 2 || rawLine.Any(char.IsDigit)) return false;
            var first = Normalize(parts[0]);
            return first.Length > 0 && parts.All(p => Normalize(p) == first);
        }

        private static string Normalize(string part) =>
            Regex.Replace(part.ToLowerInvariant(), @"[\W_]+", " ").Trim();

        private static readonly Regex Heading = new(
            @"^[\W_]*(?:for\s+(?:the\s+)?[^:\d]{1,50}|[^:\d]{0,40}\b(?:ingredients|garnish|filling|sauce|dressing|topping|icing|frosting|crust|pastry|dough|batter|marinade|glaze|stuffing)\b[^:\d]{0,20}):[\W_]*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex YieldNote = new(
            @"^[\W_]*(?:(?:serves|makes|yields?)\b.*|.*\b(?:servings?|portions?)[\W_]*)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex Equipment = new(
            @"^[\W_]*(?:(?:a|an|the|one)\s+)?(?:(?:large|small|deep|shallow|buttered|well[\s-]buttered|greased|clean|wet|hot|round|square|flat|fireproof|earthenware|china|copper|wooden|cake|pie|pudding|jelly|baking|frying|roasting|stew|sauce|white|kitchen)[\s-]+)*" +
            @"(?:bain[\s-]?marie|saucepans?|stew[\s-]?pans?|frying[\s-]?pans?|pans?|skillets?|moulds?|molds?|sieves?|tamm(?:y|ies)|cloths?|napkins?|skewers?|twine|string|basins?|tins?|ovens?|thermometers?|larding[\s-]needles?|jelly[\s-]bags?|paper)" +
            @"[\W_]*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
