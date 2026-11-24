using System;
using System.Collections.Generic;
using System.Linq;
using Nom.Data.Recipe;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// The line-level guard of the rebuild and the start-up fill: a rule may never make a line's amount
    /// worse. An amount is read against the line's own source text: the line alone when the line states an
    /// amount, else the line and the method sentences naming its food or its wording. A stored amount is
    /// grounded when it stands there (A1, <see cref="ReprocessRuleAudit.GroundedIn"/>); a new amount when it
    /// stands there with a unit sane for the line (A3). A rebuilt row with no amount never replaces a stored
    /// row that has one, whatever the grounding says: it keeps a stored amount the line itself grounds and
    /// holds any other, one only a method sentence grounds too, since that sentence's number may be another
    /// food's.
    /// A grounded new amount replaces the stored one only where the stored one contradicts the source: it is
    /// not grounded; its unit is not sane ("1 teaspn. salt" stored as a piece); it is a count in pieces of a
    /// measure the source names for the food ("a large tablespoon of butter" stored as one piece), the new
    /// amount being that measure and the source not also stating the count as a count of the food; it is the
    /// first part of an amount the source states in the same measure ("a pound and a half", "1-1/4 cups");
    /// or it is empty on a line of a kind that takes an amount (an ingredient, a reference such as a sauce
    /// made by another recipe, or equipment such as boiling water; never a note, an instruction or an
    /// artefact). Every other difference keeps the stored amount and is held, with its reason, for the line
    /// judge. No model and no database.
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

        /// <summary>Why the guard gave its verdict; a hold's reason is stored with the hold (<see cref="LineGuardHolds"/>).</summary>
        public static class Reasons
        {
            public const string Same = "same";

            /// <summary>Kept: the stored amount is grounded and the rebuilt one is not grounded, or empty with the stored amount grounded in the line itself.</summary>
            public const string StoredGrounded = "stored-grounded";

            /// <summary>Replaced: the stored line had no amount and takes one.</summary>
            public const string Fill = "fill";

            /// <summary>Replaced: the stored amount does not stand in the line's own source text.</summary>
            public const string StoredUngrounded = "stored-ungrounded";

            /// <summary>Replaced: the stored amount's unit is not sane for the line (A3).</summary>
            public const string Unit = "unit";

            /// <summary>Replaced: the stored count in pieces is the number of a measure the source names for the food.</summary>
            public const string CountOfMeasure = "count-of-measure";

            /// <summary>Replaced: the stored amount is the first part of the amount the source states.</summary>
            public const string AddedPart = "added-part";

            /// <summary>Held: the rebuilt row has no amount and the stored amount is not grounded.</summary>
            public const string RebuiltEmpty = "rebuilt-empty";

            /// <summary>Held: the rebuilt row has no amount and the stored amount stands only in a method sentence, whose number may be another food's.</summary>
            public const string RebuiltEmptyMethod = "rebuilt-empty-method";

            /// <summary>Held: the rebuilt amount does not stand in the line's own source text, or its unit is not sane.</summary>
            public const string RebuiltUngrounded = "rebuilt-ungrounded";

            /// <summary>Held: the stored line has no amount and is a note, an instruction or an artefact.</summary>
            public const string Kind = "kind";

            /// <summary>Held: both amounts stand in the source and differ ("four or five oranges").</summary>
            public const string BothGrounded = "both-grounded";

            public static readonly string[] Held = { RebuiltEmpty, RebuiltEmptyMethod, RebuiltUngrounded, Kind, BothGrounded };
        }

        /// <summary>The guard's verdict on one line with its reason.</summary>
        public sealed record Decision(Verdict Verdict, string Reason);

        /// <summary>One amount as the guard reads it: the unit by measurement name, the line's kind, the food's name, its weight per item, and whether it came from the method.</summary>
        public sealed record Amount(decimal Quantity, string? Unit, string? Kind, string? Food, decimal? GramsPerItem, bool FromMethod = false);

        /// <summary>The verdict on replacing <paramref name="stored"/> with <paramref name="proposed"/> on the line <paramref name="rawLine"/>.</summary>
        public static Verdict Decide(string? rawLine, Amount stored, Amount proposed, IEnumerable<string>? method) =>
            Judge(rawLine, stored, proposed, method).Verdict;

        /// <summary>The verdict and its reason on replacing <paramref name="stored"/> with <paramref name="proposed"/> on the line <paramref name="rawLine"/>.</summary>
        public static Decision Judge(string? rawLine, Amount stored, Amount proposed, IEnumerable<string>? method)
        {
            if (SameAmount(stored, proposed)) return new Decision(Verdict.Same, Reasons.Same);
            var raw = rawLine ?? string.Empty;
            var steps = (method ?? Enumerable.Empty<string>()).ToList();
            var statesOwn = LineStates(raw);
            var storedFailures = Failures(raw, stored, steps, fromMethod: stored.Quantity > 0 && !statesOwn);
            var storedGrounded = stored.Quantity > 0 && !storedFailures.Contains(ReprocessRuleAudit.Grounded);

            if (proposed.Quantity <= 0)
            {
                if (!storedGrounded) return new Decision(Verdict.Hold, Reasons.RebuiltEmpty);
                return !Failures(raw, stored, steps, fromMethod: false).Contains(ReprocessRuleAudit.Grounded)
                    ? new Decision(Verdict.Keep, Reasons.StoredGrounded)
                    : new Decision(Verdict.Hold, Reasons.RebuiltEmptyMethod);
            }

            var proposedFromMethod = proposed.FromMethod && !statesOwn;
            if (!Grounded(raw, proposed, steps, proposedFromMethod))
            {
                return storedGrounded ? new Decision(Verdict.Keep, Reasons.StoredGrounded) : new Decision(Verdict.Hold, Reasons.RebuiltUngrounded);
            }

            if (stored.Quantity <= 0)
            {
                return TakesAmount(stored.Kind) ? new Decision(Verdict.Replace, Reasons.Fill) : new Decision(Verdict.Hold, Reasons.Kind);
            }

            if (!storedGrounded) return new Decision(Verdict.Replace, Reasons.StoredUngrounded);
            if (storedFailures.Contains(ReprocessRuleAudit.UnitSane)) return new Decision(Verdict.Replace, Reasons.Unit);

            var own = OwnTexts(raw, steps, proposed, proposedFromMethod);
            var named = new[] { stored.Food, proposed.Food, ReprocessRuleAudit.Wording(raw) };
            if (string.Equals(stored.Unit, IngredientLineParser.Piece, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(proposed.Unit, IngredientLineParser.Piece, StringComparison.OrdinalIgnoreCase)
                && ReprocessRuleAudit.MeasureCounts(stored.Quantity, own, named).Any(phrase => Reads(phrase, proposed))
                && !ReprocessRuleAudit.CountsFood(stored.Quantity, own, named))
            {
                return new Decision(Verdict.Replace, Reasons.CountOfMeasure);
            }

            if (string.Equals(stored.Unit, proposed.Unit, StringComparison.OrdinalIgnoreCase) && proposed.Quantity > stored.Quantity
                && (LineReads(raw, proposed.Quantity) || ReprocessRuleAudit.AddsPart(stored.Quantity, proposed.Quantity, own, named)))
            {
                return new Decision(Verdict.Replace, Reasons.AddedPart);
            }

            return new Decision(Verdict.Hold, Reasons.BothGrounded);
        }

        /// <summary>Whether the verdict lets the new amount stand.</summary>
        public static bool Takes(Verdict verdict) => verdict is Verdict.Same or Verdict.Replace;

        /// <summary>Whether a line of the kind may take an amount from the source: an ingredient, a line of no kind, a reference or equipment; never a note, an instruction or an artefact.</summary>
        public static bool TakesAmount(string? kind) =>
            kind is null or RecipeIngredientLineKind.Ingredient or RecipeIngredientLineKind.Reference or RecipeIngredientLineKind.Equipment;

        private static bool SameAmount(Amount a, Amount b) =>
            (a.Quantity <= 0 && b.Quantity <= 0) || (a.Quantity == b.Quantity && string.Equals(a.Unit, b.Unit, StringComparison.OrdinalIgnoreCase));

        private static string FirstPart(string raw) => raw.Split(FoldedLine.Separator, 2)[0];

        private static bool LineStates(string raw) => IngredientLineParser.Parse(FirstPart(raw)) != null || ReprocessRuleAudit.StatesAmount(raw);

        private static bool LineReads(string raw, decimal quantity) => IngredientLineParser.Parse(FirstPart(raw)) is { } read && read.Quantity == quantity;

        private static bool Reads(string phrase, Amount amount) =>
            IngredientLineParser.Parse(phrase) is { } read
            && string.Equals(read.MeasurementName, amount.Unit, StringComparison.OrdinalIgnoreCase)
            && Math.Abs(read.Quantity - amount.Quantity) <= 0.01m;

        private static List<string?> OwnTexts(string raw, IReadOnlyList<string> method, Amount amount, bool fromMethod)
        {
            var texts = new List<string?> { raw };
            if (fromMethod) texts.AddRange(ReprocessRuleAudit.MethodSentences(method, amount.Food, raw));
            return texts;
        }

        private static bool Grounded(string raw, Amount amount, IReadOnlyList<string> method, bool fromMethod)
        {
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
