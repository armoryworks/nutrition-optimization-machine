using System;
using System.Collections.Generic;

namespace Nom.Orch.Models.Recipe
{
    /// <summary>The line guard's open holds counted by reason, in all and per writer, with the resolved ones by resolution.</summary>
    public class LineGuardHoldReport
    {
        public int Open { get; set; }
        public Dictionary<string, int> OpenByReason { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, Dictionary<string, int>> OpenByOrigin { get; set; } = new(StringComparer.Ordinal);
        public int Resolved { get; set; }
        public Dictionary<string, int> ResolvedByResolution { get; set; } = new(StringComparer.Ordinal);
    }

    /// <summary>One hold: the stored row and the rebuilt row, the source line and the reason.</summary>
    public class LineGuardHoldRow
    {
        public long Id { get; set; }
        public long RecipeId { get; set; }
        public long IngredientId { get; set; }
        public int SortOrder { get; set; }
        public string Origin { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public string RuleVersion { get; set; } = string.Empty;
        public string RawLine { get; set; } = string.Empty;
        public string? StoredFood { get; set; }
        public decimal StoredQuantity { get; set; }
        public string? StoredUnit { get; set; }
        public string? StoredKind { get; set; }
        public long? RebuiltIngredientId { get; set; }
        public string? RebuiltFood { get; set; }
        public decimal RebuiltQuantity { get; set; }
        public string? RebuiltUnit { get; set; }
        public string? RebuiltKind { get; set; }
        public bool RebuiltFromMethod { get; set; }
        public DateTime HeldAt { get; set; }
        public DateTime? ResolvedAt { get; set; }
        public string? Resolution { get; set; }
    }
}
