using System;
using System.Collections.Generic;
using System.Linq;
using Nom.Data.Recipe;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// The line-level guard of the rebuild and the start-up fill: a rule may never make a line's amount
    /// worse. A stored amount is grounded when it stands in the source (A1, <see cref="ReprocessRuleAudit.GroundedIn"/>
    /// over the line and the method sentences naming the food). A new amount is grounded when it stands in the
    /// line, or for a method fill in a method sentence naming the food, with a unit sane for the line (A3); no
    /// amount is grounded unless the line opens with one. A stored amount contradicts the source when it is not
    /// grounded, when its unit is not sane and the new amount is not empty ("1 teaspn. salt" stored as a piece),
    /// when it is a smaller amount in the same measure than the one the line itself states (the part "a pint and
    /// a half" or "1-1/4 cups" adds to), and an empty stored ingredient line beside a new amount. The new amount
    /// replaces the stored one only where the stored one contradicts the source and the new one is grounded. A
    /// grounded stored amount is kept against a new one that is empty or not grounded; every other difference
    /// keeps the stored amount and is held for the line judge. No model and no database.
    /// </summary>
    public static class LineAmountGuard
    {
        public enum Verdict
        {
            Same,
            Keep,
            Replace,
            Hold,
        }

        /// <summary>One amount as the guard reads it: the unit by measurement name, the line's kind, the food's name, its weight per item, and whether it came from the method.</summary>
        public sealed record Amount(decimal Quantity, string? Unit, string? Kind, string? Food, decimal? GramsPerItem, bool FromMethod = false);

        /// <summary>The verdict on replacing <paramref name="stored"/> with <paramref name="proposed"/> on the line <paramref name="rawLine"/>.</summary>
        public static Verdict Decide(string? rawLine, Amount stored, Amount proposed, IEnumerable<string>? method)
        {
            if (SameAmount(stored, proposed)) return Verdict.Same;
            var raw = rawLine ?? string.Empty;
            var steps = (method ?? Enumerable.Empty<string>()).ToList();
            var storedFailures = Failures(raw, stored, steps, fromMethod: stored.Quantity > 0);
            var storedGrounded = stored.Quantity > 0 && !storedFailures.Contains(ReprocessRuleAudit.Grounded);
            var grounded = Grounded(raw, proposed, steps, proposed.FromMethod);

            if (grounded && Contradicted(raw, stored, storedGrounded, storedFailures.Contains(ReprocessRuleAudit.UnitSane), proposed)) return Verdict.Replace;
            if (stored.Quantity > 0 && storedGrounded && (proposed.Quantity <= 0 || !grounded)) return Verdict.Keep;
            return Verdict.Hold;
        }

        /// <summary>Whether the verdict lets the new amount stand.</summary>
        public static bool Takes(Verdict verdict) => verdict is Verdict.Same or Verdict.Replace;

        private static bool SameAmount(Amount a, Amount b) =>
            (a.Quantity <= 0 && b.Quantity <= 0) || (a.Quantity == b.Quantity && string.Equals(a.Unit, b.Unit, StringComparison.OrdinalIgnoreCase));

        private static bool IsIngredient(string? kind) => kind is null or RecipeIngredientLineKind.Ingredient;

        private static bool Contradicted(string raw, Amount stored, bool storedGrounded, bool storedUnitInsane, Amount proposed)
        {
            if (stored.Quantity <= 0) return IsIngredient(stored.Kind) && proposed.Quantity > 0;
            if (!storedGrounded) return true;
            if (storedUnitInsane && proposed.Quantity > 0) return true;
            return !proposed.FromMethod && string.Equals(stored.Unit, proposed.Unit, StringComparison.OrdinalIgnoreCase)
                && proposed.Quantity > stored.Quantity && IngredientLineParser.Parse(raw.Split(FoldedLine.Separator, 2)[0]) is { } read
                && read.Quantity == proposed.Quantity;
        }

        private static bool Grounded(string raw, Amount amount, IReadOnlyList<string> method, bool fromMethod)
        {
            if (amount.Quantity <= 0) return !(IsIngredient(amount.Kind) && ReprocessRuleAudit.StatesAmount(raw));
            var failures = Failures(raw, amount, method, fromMethod);
            return !failures.Contains(ReprocessRuleAudit.Grounded) && !failures.Contains(ReprocessRuleAudit.UnitSane);
        }

        private static IReadOnlyList<string> Failures(string raw, Amount amount, IReadOnlyList<string> method, bool fromMethod) =>
            amount.Quantity <= 0
                ? Array.Empty<string>()
                : ReprocessRuleAudit.Failures(
                    new ReprocessRuleAudit.Line(raw, amount.Food, amount.Quantity, amount.Unit, amount.Kind, fromMethod, false, amount.GramsPerItem), method);
    }
}
