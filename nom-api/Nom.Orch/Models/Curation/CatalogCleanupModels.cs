using System.Collections.Generic;

namespace Nom.Orch.Models.Curation
{
    /// <summary>One planned fix for a residue catalog row.</summary>
    public class CatalogCleanupAction
    {
        public long IngredientId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string NormalizedName { get; set; } = string.Empty;
        /// <summary>"merge" into TargetId, or "rename" in place.</summary>
        public string Action { get; set; } = string.Empty;
        public long? TargetId { get; set; }
        public string? TargetName { get; set; }
        public int RecipeLinks { get; set; }
    }

    public class CatalogCleanupPreview
    {
        public int CatalogSize { get; set; }
        public int ResidueCount { get; set; }
        public int MergeCount { get; set; }
        public int RenameCount { get; set; }
        public List<CatalogCleanupAction> Samples { get; set; } = new();
    }

    public class CatalogCleanupResult
    {
        public int Renamed { get; set; }
        public int Merged { get; set; }
        public int LinksMoved { get; set; }
        public int LinksFolded { get; set; }
        /// <summary>Rows left alone because something beyond recipe links references them.</summary>
        public int SkippedReferenced { get; set; }
        public int Remaining { get; set; }
    }
}
