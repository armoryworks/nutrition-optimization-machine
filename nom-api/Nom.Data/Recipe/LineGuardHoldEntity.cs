using System;

namespace Nom.Data.Recipe
{
    /// <summary>
    /// A line whose stored amount the line guard kept although a rule read another (a hold): the stored row
    /// and the rebuilt row side by side, the source line and why it was held, so the line judge and a later
    /// rule can work the queue. A hold writes nothing to the recipe; the stored row stays as it was. One open
    /// hold per line and writer; a writer that reads the line again updates its hold, and resolves it when the
    /// line is no longer held.
    /// </summary>
    public class LineGuardHoldEntity : BaseEntity
    {
        public long RecipeId { get; set; }

        /// <summary>The stored row's food.</summary>
        public long IngredientId { get; set; }

        /// <summary>The stored row's position.</summary>
        public int SortOrder { get; set; }

        /// <summary>The writer that held the line, one of <see cref="LineGuardHoldOrigin"/>.</summary>
        public string Origin { get; set; } = LineGuardHoldOrigin.Rebuild;

        /// <summary>Why the line was held, one of the guard's hold reasons.</summary>
        public string Reason { get; set; } = string.Empty;

        /// <summary>The version of the line rules that held it.</summary>
        public string RuleVersion { get; set; } = string.Empty;

        /// <summary>The source line the amounts are read from.</summary>
        public string RawLine { get; set; } = string.Empty;

        public string? StoredFood { get; set; }
        public decimal StoredQuantity { get; set; }
        public string? StoredUnit { get; set; }
        public string? StoredKind { get; set; }

        /// <summary>The rebuilt row's food; null for a new food the rebuild would make.</summary>
        public long? RebuiltIngredientId { get; set; }

        public string? RebuiltFood { get; set; }
        public decimal RebuiltQuantity { get; set; }
        public string? RebuiltUnit { get; set; }
        public string? RebuiltKind { get; set; }

        /// <summary>Whether the rebuilt amount was read from the method rather than the line.</summary>
        public bool RebuiltFromMethod { get; set; }

        public DateTime HeldAt { get; set; }

        /// <summary>When the hold was worked; null while it waits.</summary>
        public DateTime? ResolvedAt { get; set; }

        /// <summary>How it was worked: a writer's "released" when it no longer holds the line, "rebuilt" when a rebuild replaced the recipe's lines, "undone" when the rebuild that held it was undone, or what the worker that resolved it records.</summary>
        public string? Resolution { get; set; }
    }

    /// <summary>The writers of <see cref="LineGuardHoldEntity.Origin"/>.</summary>
    public static class LineGuardHoldOrigin
    {
        public const string Rebuild = "rebuild";
        public const string StartupFill = "startup-fill";
    }
}
