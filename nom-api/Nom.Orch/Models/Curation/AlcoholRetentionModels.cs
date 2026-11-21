using System;
using System.Collections.Generic;

namespace Nom.Orch.Models.Curation
{
    /// <summary>What a retention run did, or would do on a dry run: the alcohol lines read, those whose stored retention changes, the recipes recalculated, counts by basis kind and a sample.</summary>
    public class AlcoholRetentionRunResult
    {
        public bool Applied { get; set; }
        public string Version { get; set; } = string.Empty;
        public DateTime StampUtc { get; set; }
        public int Lines { get; set; }
        public int Changed { get; set; }
        public int Recipes { get; set; }
        public int RecipesRecalculated { get; set; }

        /// <summary>Lines by the kind of their new basis (timed, up-to, flamed, hot-liquid, overnight, no-heat, no-steps).</summary>
        public Dictionary<string, int> ByKind { get; set; } = new();
        public List<AlcoholRetentionSample> Samples { get; set; } = new();
    }

    /// <summary>One line's retention as the run reads it, beside what it had.</summary>
    public class AlcoholRetentionSample
    {
        public long RecipeId { get; set; }
        public long IngredientId { get; set; }
        public int SortOrder { get; set; }
        public string Line { get; set; } = string.Empty;
        public decimal? OldFactor { get; set; }
        public string? OldBasis { get; set; }
        public decimal NewFactor { get; set; }
        public string NewBasis { get; set; } = string.Empty;
        public string? UsdaCode { get; set; }
    }

    /// <summary>What taking a retention run back did, or would do on a dry run.</summary>
    public class AlcoholRetentionUndoResult
    {
        public bool Applied { get; set; }

        /// <summary>AlcoholRetention journal entries at or after the given time.</summary>
        public int Entries { get; set; }

        /// <summary>Lines given back the retention they had before (on a dry run: that would be).</summary>
        public int Restored { get; set; }

        /// <summary>Journal entries whose line has since been read again, moved to another food or removed.</summary>
        public int Skipped { get; set; }

        /// <summary>Journal entries of a recipe rebuilt after them (<see cref="Nom.Orch.Services.RecipeReprocessService"/>): undo the rebuild first.</summary>
        public int SkippedRebuilt { get; set; }
        public int RecipesRecalculated { get; set; }
    }
}
